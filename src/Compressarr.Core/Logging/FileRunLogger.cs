using System.Globalization;

namespace Compressarr.Core.Logging;

public sealed class FileRunLogger : IRunLogger
{
    private string? _summaryLogFile;

    public event Action<string, LogSeverity>? LineWritten;

    public string Initialize(string logFilePath, string timestamp)
    {
        if (string.IsNullOrWhiteSpace(logFilePath))
        {
            throw new InvalidOperationException("Compressarr: Log folder is not configured.");
        }

        Directory.CreateDirectory(logFilePath);

        var logName = $"Compressarr_{timestamp}_Summary.txt";
        _summaryLogFile = Path.Combine(logFilePath, logName);
        if (File.Exists(_summaryLogFile)) File.Delete(_summaryLogFile);

        return _summaryLogFile;
    }

    public void Log(string message, LogSeverity severity = LogSeverity.Info)
    {
        if (_summaryLogFile is not null)
        {
            File.AppendAllText(_summaryLogFile, message + Environment.NewLine);
        }

        LineWritten?.Invoke(message, severity);
    }

    public void FileStart(string laneDisplayName, int index, int total, string fileName, double sizeGb, string contentType, string preset)
    {
        var rule = new string('-', 80);
        Log("");
        Log(rule);
        Log($"[{laneDisplayName}] File {index} of {total}");
        Log("  Name   : " + fileName);
        Log("  Size   : " + sizeGb.ToString("N3", CultureInfo.InvariantCulture) + " GB");
        Log("  Type   : " + contentType);
        Log("  Preset : " + preset);
        Log(rule);
    }

    public void FileComplete(string fileName, double beginSizeGb, double endSizeGb, TimeSpan duration, bool success, string? detailLogFile)
    {
        if (success)
        {
            var savings = Math.Round(beginSizeGb - endSizeGb, 3);
            var pct = beginSizeGb > 0 ? Math.Round(100 - (endSizeGb / beginSizeGb) * 100, 1) : 0;
            Log("  Completed : " + fileName);
            Log("  End size  : " + endSizeGb.ToString("N3", CultureInfo.InvariantCulture) + " GB    Saved: " +
                savings.ToString("N3", CultureInfo.InvariantCulture) + $" GB ({pct}%)");
            Log($"  Duration  : {duration.Hours}h {duration.Minutes}m {duration.Seconds}s");
        }
        else
        {
            Log("  FAILED    : " + fileName, LogSeverity.Error);
            Log("  Detail log: " + detailLogFile, LogSeverity.Error);
        }
        Log(new string('-', 80));
        Log("");
    }
}
