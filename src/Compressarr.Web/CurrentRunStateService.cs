using Compressarr.Core.Config;
using Compressarr.Core.Logging;
using Compressarr.Core.Orchestration;
using Compressarr.Core.Reporting;

namespace Compressarr.Web;

public sealed record LogLineEntry(string Text, string Severity);

// LaneIsResumedById: per-lane "did this lane have real incomplete work when its most recent
// pass began" - recorded by LaneStarted and, unlike a single "current lane" flag, kept around
// (keyed by lane id) even after that lane stops being the active one (pass finished, Stop
// Monitoring, or a different lane started) - see ComputeUpNext's use of it for why a lane that
// stops being "current" must still be able to answer this correctly.
public sealed record RunStateSnapshot(
    bool IsRunning,
    string? LaneDisplayName,
    IReadOnlyDictionary<string, bool> LaneIsResumedById,
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
///
/// Also tracks a throughput rate (GB of source video per minute) for the queue-ETA feature -
/// GetRateGbPerMinute resolves, per preset, from the best available signal: a completed sample
/// from THIS run, an in-progress estimate from whatever's encoding right now (once it's been
/// running long enough - roughly a minute or two - to trust; long individual encodes shouldn't
/// leave the ETA stuck on a stale seed for however long they take), a rough historical seed
/// parsed from a handful of recent reports (see IHistoryThroughputEstimator - a deliberate
/// placeholder, not a rigorous average), then finally a global blend of whatever's available
/// across every preset, in that order.
/// </summary>
public sealed class CurrentRunStateService : IRunProgressReporter
{
    private const int MaxRecentLines = 200;

    // Below this, an in-progress file's own elapsed time is too short/noisy to trust as a real
    // rate estimate - matches the "should update in a minute or two" expectation without reacting
    // to a jumpy 5%-complete reading a few seconds into an encode.
    private static readonly TimeSpan MinInProgressSampleAge = TimeSpan.FromMinutes(1);

    private readonly object _lock = new();
    private readonly Queue<LogLineEntry> _recentLines = new();
    private readonly IConfigStore _configStore;
    private readonly IPathExpander _pathExpander;
    private readonly IHistoryThroughputEstimator _historyEstimator;

    private bool _isRunning;
    private string? _laneId;
    private string? _laneDisplayName;
    private string? _fileName;
    private string? _presetName;
    private int _fileIndex;
    private int _fileTotal;
    private double? _progressPercent;
    private double? _progressFps;
    private string? _progressEta;

    private DateTime? _currentFileStartTime;
    private double _currentFileSizeGb;

    private readonly Dictionary<string, string> _laneDisplayNamesById = new();
    private readonly Dictionary<string, bool> _laneIsResumedById = new();

    // Live samples accumulated this run only - reset on RunStarted, since a rate from a much
    // earlier session isn't necessarily still representative (different hardware load, etc.).
    private readonly Dictionary<string, (double Gb, double Minutes)> _liveByPreset = new(StringComparer.OrdinalIgnoreCase);

    private HistoricalThroughput? _historicalSeed;

    public CurrentRunStateService(IRunLogger logger, IConfigStore configStore, IPathExpander pathExpander, IHistoryThroughputEstimator historyEstimator)
    {
        logger.LineWritten += OnLineWritten;
        _configStore = configStore;
        _pathExpander = pathExpander;
        _historyEstimator = historyEstimator;
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
            _presetName = null;
            _fileIndex = 0;
            _fileTotal = 0;
            _progressPercent = null;
            _progressFps = null;
            _progressEta = null;
            _currentFileStartTime = null;
            _currentFileSizeGb = 0;
            _liveByPreset.Clear();
        }
    }

    public void LaneStarted(string laneId, string laneDisplayName, bool isResumed)
    {
        lock (_lock)
        {
            _laneId = laneId;
            _laneDisplayName = laneDisplayName;
            _laneDisplayNamesById[laneId] = laneDisplayName;
            _laneIsResumedById[laneId] = isResumed;
        }
    }

    public void FileStarted(string laneId, int index, int total, string fileName, string? presetName, double sizeGb)
    {
        lock (_lock)
        {
            _laneId = laneId;
            _laneDisplayNamesById.TryGetValue(laneId, out _laneDisplayName);
            _fileName = fileName;
            _presetName = presetName;
            _fileIndex = index;
            _fileTotal = total;
            _progressPercent = null;
            _progressFps = null;
            _progressEta = null;
            _currentFileStartTime = DateTime.Now;
            _currentFileSizeGb = sizeGb;
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

    public void FileThroughputSample(string? presetName, double gb, TimeSpan duration)
    {
        if (string.IsNullOrWhiteSpace(presetName) || gb <= 0) return;

        var minutes = duration.TotalMinutes;
        if (minutes <= 0) return;

        lock (_lock)
        {
            var existing = _liveByPreset.TryGetValue(presetName, out var value) ? value : (0, 0);
            _liveByPreset[presetName] = (existing.Item1 + gb, existing.Item2 + minutes);
        }
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
            _currentFileStartTime = null;
            _currentFileSizeGb = 0;
        }
    }

    /// <summary>Resolves the best available GB/minute throughput rate for the given preset (or
    /// the best available rate overall if presetName is null/unknown), used by the queue-ETA
    /// feature. Returns null only when nothing at all is available yet - a genuinely fresh
    /// install with no report history and no live samples this run.</summary>
    public double? GetRateGbPerMinute(string? presetName)
    {
        lock (_lock)
        {
            if (presetName is not null && _liveByPreset.TryGetValue(presetName, out var live) && live.Minutes > 0)
            {
                return live.Gb / live.Minutes;
            }

            if (presetName is not null && string.Equals(_presetName, presetName, StringComparison.OrdinalIgnoreCase)
                && _currentFileStartTime is DateTime startTime && _currentFileSizeGb > 0 && _progressPercent is > 0)
            {
                var elapsed = DateTime.Now - startTime;
                if (elapsed >= MinInProgressSampleAge)
                {
                    var gbSoFar = _currentFileSizeGb * (_progressPercent.Value / 100.0);
                    var rate = gbSoFar / elapsed.TotalMinutes;
                    if (rate > 0) return rate;
                }
            }

            var seed = GetOrLoadHistoricalSeed();
            if (presetName is not null && seed.RateGbPerMinuteByPreset.TryGetValue(presetName, out var seededRate))
            {
                return seededRate;
            }

            if (_liveByPreset.Count > 0)
            {
                var totalGb = _liveByPreset.Values.Sum(v => v.Gb);
                var totalMinutes = _liveByPreset.Values.Sum(v => v.Minutes);
                if (totalMinutes > 0) return totalGb / totalMinutes;
            }

            return seed.GlobalRateGbPerMinute;
        }
    }

    // Computed once and cached - re-parsing report files on every ~1.5s status poll would be
    // wasteful, and this is only ever meant to be a rough starting placeholder anyway (live data
    // takes over within a run regardless). A Report Path setting change won't be reflected until
    // the app restarts - an accepted limitation for a placeholder seed, not worth the complexity
    // of invalidating this on every settings save.
    private HistoricalThroughput GetOrLoadHistoricalSeed()
    {
        if (_historicalSeed is not null) return _historicalSeed;

        var config = _configStore.Load(AppPaths.GetConfigFilePath());
        var reportPath = _pathExpander.Expand(config.Report.ReportPath);
        _historicalSeed = _historyEstimator.Estimate(reportPath);
        return _historicalSeed;
    }

    public RunStateSnapshot GetSnapshot()
    {
        lock (_lock)
        {
            return new RunStateSnapshot(_isRunning, _laneDisplayName, new Dictionary<string, bool>(_laneIsResumedById), _fileName, _presetName, _fileIndex, _fileTotal, _progressPercent, _progressFps, _progressEta, _recentLines.ToList());
        }
    }
}
