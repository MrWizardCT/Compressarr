using Compressarr.Core.Logging;
using Compressarr.Core.Orchestration;

namespace Compressarr.Web;

public sealed record LogLineEntry(string Text, string Severity);

public sealed record RunStateSnapshot(
    bool IsRunning,
    string? LaneDisplayName,
    string? FileName,
    int FileIndex,
    int FileTotal,
    IReadOnlyList<LogLineEntry> RecentLogLines);

/// <summary>
/// Web's IRunProgressReporter implementation - tracks in-memory state for GET /api/run/status to
/// read, and buffers recent log lines (IRunLogger.LineWritten has no replay of its own) so a
/// browser tab opened mid-run isn't blind. Thread-safe: progress calls arrive from whatever
/// thread RunOrchestrator/ConversionOrchestrator run on, while status requests arrive
/// concurrently on Kestrel's thread pool.
/// </summary>
public sealed class CurrentRunStateService : IRunProgressReporter
{
    private const int MaxRecentLines = 200;

    private readonly object _lock = new();
    private readonly Queue<LogLineEntry> _recentLines = new();

    private bool _isRunning;
    private string? _laneId;
    private string? _laneDisplayName;
    private string? _fileName;
    private int _fileIndex;
    private int _fileTotal;

    private readonly Dictionary<string, string> _laneDisplayNamesById = new();

    public CurrentRunStateService(IRunLogger logger)
    {
        logger.LineWritten += OnLineWritten;
    }

    private void OnLineWritten(string text, LogSeverity severity)
    {
        lock (_lock)
        {
            _recentLines.Enqueue(new LogLineEntry(text, severity.ToString()));
            while (_recentLines.Count > MaxRecentLines) _recentLines.Dequeue();
        }
    }

    public void RunStarted(string timestamp)
    {
        lock (_lock)
        {
            _isRunning = true;
            _laneId = null;
            _laneDisplayName = null;
            _fileName = null;
            _fileIndex = 0;
            _fileTotal = 0;
        }
    }

    public void LaneStarted(string laneId, string laneDisplayName)
    {
        lock (_lock)
        {
            _laneId = laneId;
            _laneDisplayName = laneDisplayName;
            _laneDisplayNamesById[laneId] = laneDisplayName;
        }
    }

    public void FileStarted(string laneId, int index, int total, string fileName)
    {
        lock (_lock)
        {
            _laneId = laneId;
            _laneDisplayNamesById.TryGetValue(laneId, out _laneDisplayName);
            _fileName = fileName;
            _fileIndex = index;
            _fileTotal = total;
        }
    }

    public void FileCompleted(string laneId, string fileName, bool success)
    {
        // Current file/index stays visible until the next FileStarted/RunCompleted - nothing to
        // update here beyond what FileStarted already tracks.
    }

    public void RunCompleted(int totalFiles)
    {
        lock (_lock)
        {
            _isRunning = false;
            _fileName = null;
            _fileIndex = 0;
            _fileTotal = 0;
        }
    }

    public RunStateSnapshot GetSnapshot()
    {
        lock (_lock)
        {
            return new RunStateSnapshot(_isRunning, _laneDisplayName, _fileName, _fileIndex, _fileTotal, _recentLines.ToList());
        }
    }
}
