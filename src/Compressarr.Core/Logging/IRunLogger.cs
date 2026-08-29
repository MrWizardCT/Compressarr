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
    /// and returns its full path.</summary>
    string Initialize(string logFilePath, string timestamp);

    void Log(string message, LogSeverity severity = LogSeverity.Info);

    void FileStart(string laneDisplayName, int index, int total, string fileName, double sizeGb, string contentType, string preset);

    void FileComplete(string fileName, double beginSizeGb, double endSizeGb, TimeSpan duration, bool success, string? detailLogFile);
}
