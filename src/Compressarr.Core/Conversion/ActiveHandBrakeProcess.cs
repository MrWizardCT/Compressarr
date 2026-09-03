using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Compressarr.Core.Conversion;

/// <summary>
/// Tracks whichever HandBrakeCLI process is currently encoding, so a Pause/Resume request from
/// the web UI can reach it - mirrors IActiveRunController's "single chokepoint" shape, but at the
/// single-file process level rather than the whole multi-file pass. HandBrakeProcessRunner calls
/// Register()/Unregister() bracketing each process it starts, mixed-audience interface like
/// IActiveRunController.Begin()/End() (internal bookkeeping) alongside Pause()/Resume() (called
/// externally, from RunEndpoints).
///
/// Pause/Resume use real OS-level process suspension (NtSuspendProcess/NtResumeProcess), not
/// HandBrakeCLI's own interactive "p"/"r" keypress - that keypress detection needs a real
/// attached console, which conflicts with running the process hidden
/// (ProcessStartInfo.CreateNoWindow) the way Compressarr always does. Confirmed live against a
/// real conversion: writing "p" to a redirected stdin pipe did nothing (encode kept progressing
/// while "paused"). OS-level suspension is the same mechanism Task Manager's/Process Explorer's
/// own "Suspend" does - it freezes every thread in the process, safe to resume mid-encode since
/// nothing is killed or loses state.</summary>
public interface IActiveHandBrakeProcess
{
    bool IsRunning { get; }
    bool IsPaused { get; }

    /// <summary>Starts tracking a freshly-started HandBrakeCLI process. Replaces any previous
    /// one - RunAsync is not expected to overlap with itself.</summary>
    void Register(Process process);

    /// <summary>Stops tracking the current process. Safe to call even if nothing is registered.</summary>
    void Unregister();

    /// <summary>No-op if nothing is running, already paused, or not on Windows (this mechanism is
    /// Windows-only).</summary>
    void Pause();

    /// <summary>No-op if nothing is running or it isn't currently paused.</summary>
    void Resume();
}

public sealed class ActiveHandBrakeProcess : IActiveHandBrakeProcess
{
    // Undocumented but long-stable NT native APIs - the same mechanism Task Manager's and Process
    // Explorer's own "Suspend Process" use, and the standard technique for this in .NET since
    // there's no managed API for a true whole-process suspend (only per-thread, which is more
    // fragile to get right for a process whose thread count Compressarr doesn't control).
    [DllImport("ntdll.dll")]
    private static extern int NtSuspendProcess(IntPtr processHandle);

    [DllImport("ntdll.dll")]
    private static extern int NtResumeProcess(IntPtr processHandle);

    private readonly object _lock = new();
    private Process? _process;
    private bool _isPaused;

    public bool IsRunning
    {
        get
        {
            lock (_lock)
            {
                try { return _process is not null && !_process.HasExited; }
                catch (InvalidOperationException) { return false; } // process object not yet started/already disposed
            }
        }
    }

    public bool IsPaused { get { lock (_lock) return _isPaused; } }

    public void Register(Process process)
    {
        lock (_lock)
        {
            _process = process;
            _isPaused = false;
        }
    }

    public void Unregister()
    {
        lock (_lock)
        {
            _process = null;
            _isPaused = false;
        }
    }

    public void Pause()
    {
        lock (_lock)
        {
            if (_process is null || _isPaused || !OperatingSystem.IsWindows()) return;
            try
            {
                if (_process.HasExited) return;
                NtSuspendProcess(_process.Handle);
                _isPaused = true;
            }
            catch (InvalidOperationException)
            {
                // Process exited between the HasExited check and suspending it - nothing to pause.
            }
        }
    }

    public void Resume()
    {
        lock (_lock)
        {
            if (_process is null || !_isPaused) return;
            try
            {
                if (!_process.HasExited) NtResumeProcess(_process.Handle);
            }
            catch (InvalidOperationException)
            {
                // Process exited while suspended - nothing left to resume, just clear the flag.
            }
            finally
            {
                _isPaused = false;
            }
        }
    }
}
