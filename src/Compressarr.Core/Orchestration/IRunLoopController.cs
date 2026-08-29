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

    /// <summary>No-op if already running (idempotent - a second Start doesn't spawn a second loop).</summary>
    void Start(CompressarrConfig config, TimeSpan pollInterval);

    /// <summary>Stops the loop from starting another pass and waits for any in-flight pass to
    /// finish naturally. No-op if not running.</summary>
    Task StopAsync();

    event Action<bool>? RunningChanged;
}

public sealed class RunLoopController : IRunLoopController, IDisposable
{
    private readonly IRunOrchestrator _runOrchestrator;
    private readonly IRunLogger _logger;
    private readonly TimeProvider _timeProvider;

    private readonly object _lock = new();
    private CancellationTokenSource? _cts;
    private Task? _loopTask;

    public RunLoopController(IRunOrchestrator runOrchestrator, IRunLogger logger)
        : this(runOrchestrator, logger, TimeProvider.System)
    {
    }

    internal RunLoopController(IRunOrchestrator runOrchestrator, IRunLogger logger, TimeProvider timeProvider)
    {
        _runOrchestrator = runOrchestrator;
        _logger = logger;
        _timeProvider = timeProvider;
    }

    public bool IsRunning
    {
        get { lock (_lock) { return _loopTask is { IsCompleted: false }; } }
    }

    public event Action<bool>? RunningChanged;

    public void Start(CompressarrConfig config, TimeSpan pollInterval)
    {
        lock (_lock)
        {
            if (_loopTask is { IsCompleted: false }) return;

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

        cts.Cancel();
        if (loopTask is not null)
        {
            try { await loopTask; } catch { /* LoopAsync swallows its own exceptions; this is belt-and-braces */ }
        }

        RunningChanged?.Invoke(false);
    }

    private async Task LoopAsync(CompressarrConfig config, TimeSpan pollInterval, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                await _runOrchestrator.RunOnceAsync(config);
            }
            catch (Exception ex)
            {
                // A failed pass must not kill the monitor loop - log and keep polling.
                _logger.Log($"Monitor-mode pass failed: {ex.Message}", LogSeverity.Error);
            }

            try
            {
                await Task.Delay(pollInterval, _timeProvider, token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    public void Dispose() => _cts?.Dispose();
}
