namespace Compressarr.Core.Orchestration;

/// <summary>
/// Tracks the CancellationTokenSource for whichever RunOnceAsync pass is currently in flight -
/// the single chokepoint both a manual "Run Once" and the monitor loop pass through, so an Abort
/// request from either surface reaches whichever one is actually running. RunOrchestrator calls
/// Begin() at the start of every pass and End() in a finally block; a caller wanting to abort
/// (RunLoopController.Abort, the /api/run/abort endpoint) just calls Abort() without needing to
/// know which path started the run.
/// </summary>
public interface IActiveRunController
{
    bool IsRunning { get; }

    /// <summary>Starts tracking a new pass, returning the token it should observe for
    /// cancellation. Replaces any previous token - RunOnceAsync is not expected to overlap with
    /// itself (same single-active-run assumption CurrentRunStateService already makes).</summary>
    CancellationToken Begin();

    /// <summary>Stops tracking the current pass. Safe to call even if nothing is running.</summary>
    void End();

    /// <summary>Cancels the currently tracked pass, if any - propagates down into
    /// ProcessLaneAsync/HandBrakeProcessRunner, which kills the in-flight HandBrakeCLI process.
    /// No-op if nothing is running.</summary>
    void Abort();
}

public sealed class ActiveRunController : IActiveRunController
{
    private readonly object _lock = new();
    private CancellationTokenSource? _cts;

    public bool IsRunning
    {
        get { lock (_lock) return _cts is not null; }
    }

    public CancellationToken Begin()
    {
        lock (_lock)
        {
            _cts?.Dispose();
            _cts = new CancellationTokenSource();
            return _cts.Token;
        }
    }

    public void End()
    {
        lock (_lock)
        {
            _cts?.Dispose();
            _cts = null;
        }
    }

    public void Abort()
    {
        lock (_lock)
        {
            _cts?.Cancel();
        }
    }
}
