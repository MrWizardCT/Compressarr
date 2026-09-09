namespace Compressarr.Core.Logging;

public enum LogSeverity
{
    Info,
    Error
}

/// <summary>
/// Dual-target logging: a per-run summary log file, plus a LineWritten event a UI host (Desktop's
/// live log panel, or a future web monitor) can subscribe to for real-time streaming — replacing
/// v1's console Write-Host target, which has no equivalent in a GUI app. Ported from
/// Write-CompressarrLog/Write-CompressarrFileStart/Write-CompressarrFileComplete.
/// </summary>
public interface IRunLogger
{
    event Action<string, LogSeverity>? LineWritten;

    /// <summary>Creates a fresh summary log file at logFilePath/Compressarr_&lt;timestamp&gt;_Summary.txt
    /// and returns its full path. Resets HasLoggedError to false.</summary>
    string Initialize(string logFilePath, string timestamp);

    /// <summary>True once any Log call this run (since the last Initialize) has used
    /// LogSeverity.Error - lets a caller tell a quiet, uneventful "0 files, nothing wrong" pass
    /// apart from one that hit a real problem despite also processing 0 files (a misconfigured
    /// lane, a missing HandBrakeCLI, etc), so only the former's log file gets discarded rather
    /// than kept forever.</summary>
    bool HasLoggedError { get; }

    void Log(string message, LogSeverity severity = LogSeverity.Info);

    /// <summary>Logs a standing/recurring problem (a missing HandBrakeCLI path, a misconfigured
    /// lane, a broken FileBot path, etc) at Error the first time - and again at Error any time the
    /// message actually changes - but at Info for an unchanged repeat. Real gap found live: a
    /// condition like this gets re-checked and re-logged on literally every poll for as long as it
    /// stays broken, and every one of those was logging at Error, which (combined with "don't keep
    /// an empty/error-free pass's log") meant a kept log file every single poll for the whole
    /// outage - the same file-proliferation problem the empty-pass fix was meant to solve, just
    /// triggered by a standing error instead of an empty pass. This state persists across polls
    /// (NOT reset by Initialize) but not across an app restart - a restart is itself a reasonable
    /// "report it again once" moment, same reasoning the update-check fix uses.
    /// `key` scopes the dedup (e.g. per-lane, per-service) - use a stable identifier distinct from
    /// any other condition that might coincidentally produce the same message text.</summary>
    void LogProblem(string key, string message);

    /// <summary>Clears LogProblem's "already reported" memory for this key - call once a condition
    /// previously flagged via LogProblem is confirmed resolved, so a LATER recurrence of the exact
    /// same problem is treated as new again instead of silently staying "already known" forever.</summary>
    void ClearProblem(string key);

    void FileStart(string laneDisplayName, int index, int total, string fileName, double sizeGb, string contentType, string preset);

    void FileComplete(string fileName, double beginSizeGb, double endSizeGb, TimeSpan duration, bool success, string? detailLogFile);
}
