using Compressarr.Core.Logging;
using Compressarr.Core.Orchestration;

namespace Compressarr.Web;

public sealed record LogLineEntry(string Text, string Severity);

public sealed record RunStateSnapshot(
    bool IsRunning,
    string? LaneDisplayName,
    bool CurrentLaneIsResumed,
    string? FileName,
    string? PresetName,
    int FileIndex,
    int FileTotal,
    double? ProgressPercent,
    double? ProgressFps,
    string? ProgressEta,
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
    private bool _currentLaneIsResumed;
    private string? _fileName;
    private string? _presetName;
    private int _fileIndex;
    private int _fileTotal;
    private double? _progressPercent;
    private double? _progressFps;
    private string? _progressEta;

    private readonly Dictionary<string, string> _laneDisplayNamesById = new();
    private readonly Dictionary<string, bool> _laneIsResumedById = new();

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
            _currentLaneIsResumed = false;
            _fileName = null;
            _presetName = null;
            _fileIndex = 0;
            _fileTotal = 0;
            _progressPercent = null;
            _progressFps = null;
            _progressEta = null;
        }
    }

    public void LaneStarted(string laneId, string laneDisplayName, bool isResumed)
    {
        lock (_lock)
        {
            _laneId = laneId;
            _laneDisplayName = laneDisplayName;
            _currentLaneIsResumed = isResumed;
            _laneDisplayNamesById[laneId] = laneDisplayName;
            _laneIsResumedById[laneId] = isResumed;
        }
    }

    public void FileStarted(string laneId, int index, int total, string fileName, string? presetName)
    {
        lock (_lock)
        {
            _laneId = laneId;
            _laneDisplayNamesById.TryGetValue(laneId, out _laneDisplayName);
            _laneIsResumedById.TryGetValue(laneId, out _currentLaneIsResumed);
            _fileName = fileName;
            _presetName = presetName;
            _fileIndex = index;
            _fileTotal = total;
            _progressPercent = null;
            _progressFps = null;
            _progressEta = null;
        }
    }

    public void FileProgress(string laneId, double percent, double? fps, string? eta)
    {
        lock (_lock)
        {
            _progressPercent = percent;
            _progressFps = fps;
            _progressEta = eta;
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
            _presetName = null;
            _fileIndex = 0;
            _fileTotal = 0;
            _progressPercent = null;
            _progressFps = null;
            _progressEta = null;
        }
    }

    public RunStateSnapshot GetSnapshot()
    {
        lock (_lock)
        {
            return new RunStateSnapshot(_isRunning, _laneDisplayName, _currentLaneIsResumed, _fileName, _presetName, _fileIndex, _fileTotal, _progressPercent, _progressFps, _progressEta, _recentLines.ToList());
        }
    }
}
