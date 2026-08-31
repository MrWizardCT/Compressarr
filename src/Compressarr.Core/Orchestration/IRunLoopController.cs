using Compressarr.Core.Config;
using Compressarr.Core.Logging;

namespace Compressarr.Core.Orchestration;

/// <summary>
/// Continuous "monitor mode" loop: repeatedly calls IRunOrchestrator.RunOnceAsync on an
/// interval until stopped. Lives in Core (not an ASP.NET Core BackgroundService) so it's
/// host-agnostic and testable without a web test host - both the tray icon and a web page call
/// into the same singleton instance, so either surface reflects the other's Start/Stop state.
///
/// Known v2.0.0 limitation: there is no CancellationToken anywhere in the conversion pipeline
/// (HandBrakeProcessRunner.Run blocks on Process.WaitForExit()), so StopAsync can only mean
/// "don't start another pass; let the current one finish" - it cannot abort a mid-flight encode.
/// </summary>
public interface IRunLoopController
{
    bool IsRunning { get; }

    /// <summary>True from the moment StopAsync is called until it actually finishes (the
    /// in-flight pass has to finish naturally first, since there's no CancellationToken reaching
    /// HandBrakeCLI itself). This is the single source of truth both the web UI and the tray icon
    /// read/subscribe to, so a stop requested from either surface is reflected on both -
    /// StopMenuHeader/RunningChanged alone can't do that, since each surface's own "click"
    /// handling only knows about itself.</summary>
    bool IsStopping { get; }

    /// <summary>UTC time the next pass is scheduled to start, while the loop is idle between
    /// passes (waiting out pollInterval). Null while a pass is actively running, or when the
    /// loop isn't started - a countdown UI should treat null as "no countdown to show".</summary>
    DateTimeOffset? NextRunUtc { get; }

    /// <summary>No-op if already running (idempotent - a second Start doesn't spawn a second loop).</summary>
    void Start(CompressarrConfig config, TimeSpan pollInterval);

    /// <summary>Stops the loop from starting another pass and waits for any in-flight pass to
    /// finish naturally. No-op if not running.</summary>
    Task StopAsync();

    /// <summary>Immediately kills whatever HandBrakeCLI process is currently running (via
    /// IActiveRunController) and stops the loop from starting another pass - "stop everything
    /// right now", as opposed to StopAsync's "finish this pass, then stop". Does not wait for the
    /// aborted pass to fully unwind; IsRunning/RunningChanged reflect that shortly afterward.
    /// No-op if not running.</summary>
    void Abort();

    /// <summary>Cuts short the wait between passes and starts the next pass immediately -
    /// "Run Now" on the web UI. Returns false (no-op) if the loop isn't currently idle between
    /// passes - not running at all, or already mid-pass - since there's no countdown to skip in
    /// either case.</summary>
    bool TriggerNow();

    event Action<bool>? RunningChanged;

    /// <summary>Fires true when StopAsync begins, false once it actually finishes.</summary>
    event Action<bool>? StoppingChanged;
}

public sealed class RunLoopController : IRunLoopController, IDisposable
{
    private readonly IRunOrchestrator _runOrchestrator;
    private readonly IRunLogger _logger;
    private readonly IActiveRunController _activeRunController;
    private readonly TimeProvider _timeProvider;

    private readonly object _lock = new();
    private CancellationTokenSource? _cts;
    private Task? _loopTask;
    private DateTimeOffset? _nextRunUtc;
    private TaskCompletionSource? _triggerNowTcs;
    private bool _isStopping;

    public RunLoopController(IRunOrchestrator runOrchestrator, IRunLogger logger, IActiveRunController activeRunController)
        : this(runOrchestrator, logger, activeRunController, TimeProvider.System)
    {
    }

    internal RunLoopController(IRunOrchestrator runOrchestrator, IRunLogger logger, IActiveRunController activeRunController, TimeProvider timeProvider)
    {
        _runOrchestrator = runOrchestrator;
        _logger = logger;
        _activeRunController = activeRunController;
        _timeProvider = timeProvider;
    }

    public bool IsRunning
    {
        get { lock (_lock) { return _loopTask is { IsCompleted: false }; } }
    }

    public bool IsStopping
    {
        get { lock (_lock) return _isStopping; }
    }

    public DateTimeOffset? NextRunUtc
    {
        get { lock (_lock) return _nextRunUtc; }
    }

    public event Action<bool>? RunningChanged;
    public event Action<bool>? StoppingChanged;

    public void Start(CompressarrConfig config, TimeSpan pollInterval)
    {
        lock (_lock)
        {
            if (_loopTask is { IsCompleted: false }) return;

            // Logged synchronously, before the loop task is even spawned, so the log window
            // always reflects "monitoring started" before any real conversion output can appear -
            // the loop's first pass can begin running on the thread pool essentially immediately.
            _logger.Log("Monitoring started.");
            _nextRunUtc = null;
            _cts = new CancellationTokenSource();
            _loopTask = LoopAsync(config, pollInterval, _cts.Token);
        }

        RunningChanged?.Invoke(true);
    }

    public async Task StopAsync()
    {
        CancellationTokenSource? cts;
        Task? loopTask;
        lock (_lock)
        {
            cts = _cts;
            loopTask = _loopTask;
        }

        if (cts is null) return;

        lock (_lock) { _isStopping = true; }
        StoppingChanged?.Invoke(true);

        cts.Cancel();
        if (loopTask is not null)
        {
            try { await loopTask; } catch { /* LoopAsync swallows its own exceptions; this is belt-and-braces */ }
        }

        lock (_lock) { _isStopping = false; }
        _logger.Log("Monitoring stopped.");
        RunningChanged?.Invoke(false);
        StoppingChanged?.Invoke(false);
    }

    public void Abort()
    {
        // Kills whatever HandBrakeCLI process the active pass owns, regardless of whether that
        // pass was started by this loop or by a manual "Run Once" - IActiveRunController tracks
        // whichever RunOnceAsync call is actually in flight.
        _activeRunController.Abort();

        CancellationTokenSource? cts;
        Task? loopTask;
        lock (_lock)
        {
            cts = _cts;
            loopTask = _loopTask;
        }

        if (cts is null) return;

        _logger.Log("Aborted by user.", LogSeverity.Error);
        cts.Cancel();

        // Deliberately not awaited here - Abort must return immediately. The loop task unwinds
        // on its own (the active pass is being killed too, so this is fast) and the continuation
        // still raises RunningChanged for listeners like the tray icon that don't poll for state.
        if (loopTask is not null)
        {
            _ = loopTask.ContinueWith(_ => RunningChanged?.Invoke(false), TaskScheduler.Default);
        }
    }

    public bool TriggerNow()
    {
        lock (_lock)
        {
            // Null means either not running at all, or a pass is currently mid-flight (LoopAsync
            // clears it before calling RunOnceAsync and only creates it once idle) - either way
            // there's no countdown to cut short.
            if (_triggerNowTcs is null) return false;

            _logger.Log("Run Now requested - skipping the rest of the countdown.");
            _triggerNowTcs.TrySetResult();
            return true;
        }
    }

    private async Task LoopAsync(CompressarrConfig config, TimeSpan pollInterval, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            lock (_lock) { _nextRunUtc = null; _triggerNowTcs = null; }

            try
            {
                await _runOrchestrator.RunOnceAsync(config);
            }
            catch (Exception ex)
            {
                // A failed pass must not kill the monitor loop - log and keep polling.
                _logger.Log($"Monitor-mode pass failed: {ex.Message}", LogSeverity.Error);
            }

            if (token.IsCancellationRequested) break;

            var triggerNowTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_lock)
            {
                _nextRunUtc = _timeProvider.GetUtcNow() + pollInterval;
                _triggerNowTcs = triggerNowTcs;
            }

            // Cancelling token (Stop/Abort) must still interrupt the wait the same way it always
            // did - registering it against the same TCS the Run Now button completes means one
            // await covers both "time's up" and "either external reason to stop waiting now".
            using var cancelRegistration = token.Register(() => triggerNowTcs.TrySetCanceled());
            var delayTask = Task.Delay(pollInterval, _timeProvider, token);
            await Task.WhenAny(delayTask, triggerNowTcs.Task);

            if (token.IsCancellationRequested) break;
        }

        lock (_lock) { _nextRunUtc = null; _triggerNowTcs = null; }
    }

    public void Dispose() => _cts?.Dispose();
}
