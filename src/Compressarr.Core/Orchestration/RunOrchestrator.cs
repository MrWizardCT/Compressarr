using Compressarr.Core.Config;
using Compressarr.Core.Conversion;
using Compressarr.Core.Logging;
using Compressarr.Core.Notifications;
using Compressarr.Core.Presets;
using Compressarr.Core.Reporting;
using Compressarr.Core.Routing;

namespace Compressarr.Core.Orchestration;

public sealed class RunResult
{
    public required ReportModel Report { get; init; }
    public required string ReportFilePath { get; init; }
    public required int TotalFiles { get; init; }
}

public interface IRunOrchestrator
{
    /// <summary>One full pass: validate HandBrakeCLI/presets paths, process every enabled lane
    /// with configured input, purge old logs/reports by retention, record history + increment
    /// the run counter (only if files were processed), run the optional post-exec command, build
    /// the HTML report, and fire a notification. Ported from Invoke-CompressarrRun. Returns null
    /// if HandBrakeCLI or presets.json can't be found (the whole run aborts, matching v1).</summary>
    Task<RunResult?> RunOnceAsync(CompressarrConfig config);
}

public sealed class RunOrchestrator : IRunOrchestrator
{
    private readonly IPathExpander _pathExpander;
    private readonly IHandBrakePresetService _presets;
    private readonly IConversionOrchestrator _conversionOrchestrator;
    private readonly IResumeStateStore _resumeStore;
    private readonly IRunLogger _logger;
    private readonly IRunHistoryStore _historyStore;
    private readonly IHistoryRollupCalculator _rollupCalculator;
    private readonly IHtmlReportGenerator _reportGenerator;
    private readonly IReportLauncher _reportLauncher;
    private readonly INotificationService _notifications;
    private readonly ITrashService _trash;

    public RunOrchestrator(
        IPathExpander pathExpander,
        IHandBrakePresetService presets,
        IConversionOrchestrator conversionOrchestrator,
        IResumeStateStore resumeStore,
        IRunLogger logger,
        IRunHistoryStore historyStore,
        IHistoryRollupCalculator rollupCalculator,
        IHtmlReportGenerator reportGenerator,
        IReportLauncher reportLauncher,
        INotificationService notifications,
        ITrashService trash)
    {
        _pathExpander = pathExpander;
        _presets = presets;
        _conversionOrchestrator = conversionOrchestrator;
        _resumeStore = resumeStore;
        _logger = logger;
        _historyStore = historyStore;
        _rollupCalculator = rollupCalculator;
        _reportGenerator = reportGenerator;
        _reportLauncher = reportLauncher;
        _notifications = notifications;
        _trash = trash;
    }

    public async Task<RunResult?> RunOnceAsync(CompressarrConfig config)
    {
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        var beginTime = DateTime.Now;

        var logFilePath = _pathExpander.Expand(config.Logging.LogFilePath);
        var reportPath = _pathExpander.Expand(config.Report.ReportPath);
        _logger.Initialize(logFilePath, timestamp);

        _logger.Log($"Compressarr - run started {timestamp}");
        _logger.Log(new string('-', 80));

        var hbloc = _pathExpander.Expand(config.HandBrake.CliPath);
        if (!_pathExpander.PathExists(config.HandBrake.CliPath))
        {
            _logger.Log($"HandBrakeCLI.exe not found at {hbloc}. Download it from https://handbrake.fr/downloads2.php", LogSeverity.Error);
            return null;
        }

        var presetsPath = _pathExpander.Expand(config.HandBrake.PresetsPath);
        if (!_pathExpander.PathExists(config.HandBrake.PresetsPath))
        {
            _logger.Log($"HandBrake presets file not found at {presetsPath}", LogSeverity.Error);
            return null;
        }

        var resumeFilePath = Path.Combine(AppPaths.GetAppDataDirectory(), "compressarr.resume.json");
        var resumeState = _resumeStore.Load(resumeFilePath);
        if (resumeState.Count > 0)
        {
            _logger.Log($"Resuming previous incomplete run ({resumeState.Count} file(s) tracked).");
        }

        var laneResults = new Dictionary<string, IReadOnlyList<ConversionResult>>();
        foreach (var lane in config.Lanes)
        {
            if (!lane.Enabled)
            {
                _logger.Log($"Skipping lane [{lane.DisplayName}] - lane is disabled.");
                continue;
            }

            if (string.IsNullOrWhiteSpace(lane.Input)) continue;

            if (string.IsNullOrWhiteSpace(lane.TvPreset) && string.IsNullOrWhiteSpace(lane.MoviePreset))
            {
                _logger.Log($"Skipping lane [{lane.DisplayName}] - no TV or Movie preset configured.", LogSeverity.Error);
                continue;
            }
            if (!string.IsNullOrWhiteSpace(lane.TvPreset) && !_presets.PresetExists(lane.TvPreset, presetsPath))
            {
                _logger.Log($"Lane [{lane.DisplayName}] - TV preset '{lane.TvPreset}' not found in presets.json. TV episodes in this lane will be skipped.", LogSeverity.Error);
            }
            if (!string.IsNullOrWhiteSpace(lane.MoviePreset) && !_presets.PresetExists(lane.MoviePreset, presetsPath))
            {
                _logger.Log($"Lane [{lane.DisplayName}] - Movie preset '{lane.MoviePreset}' not found in presets.json. Movies in this lane will be skipped.", LogSeverity.Error);
            }

            _logger.Log($"\nScanning lane [{lane.DisplayName}] - {_pathExpander.Expand(lane.Input)}");
            var results = await _conversionOrchestrator.ProcessLaneAsync(lane, config, logFilePath, timestamp, resumeState, resumeFilePath);
            laneResults[lane.Id] = results;
        }

        var stillOutstanding = resumeState.Any(e => e.Status != ResumeStatus.Completed);
        if (!stillOutstanding && File.Exists(resumeFilePath))
        {
            File.Delete(resumeFilePath);
        }

        RetentionCleaner.CleanUp(_trash, logFilePath, new[] { ".log", ".txt" }, config.Logging.RetentionDays, "log");
        RetentionCleaner.CleanUp(_trash, reportPath, new[] { ".html" }, config.Logging.RetentionDays, "report");

        var endTime = DateTime.Now;
        var runTime = endTime - beginTime;
        if (runTime < TimeSpan.Zero) runTime = runTime.Negate();

        var allResults = laneResults.Values.SelectMany(r => r).ToList();
        var totalFiles = allResults.Count;

        var runCountPath = Path.Combine(AppPaths.GetAppDataDirectory(), "compressarr.runcount.json");
        if (totalFiles > 0)
        {
            var totalBeg = allResults.Sum(r => r.BeginSizeGb);
            var totalEnd = allResults.Sum(r => r.EndSizeGb);
            _historyStore.AppendRun(logFilePath, new RunHistoryRecord(
                endTime.Year, endTime.Month, endTime.Day, totalBeg, totalEnd, totalFiles,
                runTime.Hours, runTime.Minutes, runTime.Seconds));

            // Only a pass that actually processed files counts as a "run" - an empty scan
            // (including every quiet monitor-mode poll) never moves this counter.
            _historyStore.IncrementRunCount(runCountPath);
        }

        var postExecCmd = _pathExpander.Expand(config.PostExec.Cmd);
        if (!string.IsNullOrWhiteSpace(postExecCmd) && File.Exists(postExecCmd))
        {
            _logger.Log($"\nRunning post-execution command: {postExecCmd} {config.PostExec.Args}");
            using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(postExecCmd, config.PostExec.Args) { UseShellExecute = false });
            process?.WaitForExit();
        }

        _logger.Log($"\nCompressarr run completed. {totalFiles} file(s) processed in {runTime.Hours}h {runTime.Minutes}m {runTime.Seconds}s.");

        var lanesById = config.Lanes.ToDictionary(l => l.Id);
        var (today, thisMonth, thisYear) = _rollupCalculator.Calculate(logFilePath);

        var reportModel = new ReportModel
        {
            GeneratedAt = endTime,
            Lanes = laneResults.Select(kv => new LaneReportSection
            {
                LaneDisplayName = lanesById.TryGetValue(kv.Key, out var lane) ? lane.DisplayName : kv.Key,
                Results = kv.Value
            }).ToList(),
            Today = today,
            ThisMonth = thisMonth,
            ThisYear = thisYear
        };

        Directory.CreateDirectory(reportPath);
        var reportFilePath = Path.Combine(reportPath, $"Compressarr_{timestamp}_Report.html");
        var html = _reportGenerator.Generate(reportModel, logoPngBytes: null);
        File.WriteAllText(reportFilePath, html);

        var shouldOpen = config.Report.OpenAfterRun switch
        {
            OpenReportMode.Always => true,
            OpenReportMode.OnError => reportModel.ErrorCount > 0,
            _ => false
        };
        if (shouldOpen)
        {
            _reportLauncher.Open(reportFilePath);
        }

        // Fires independent of OpenAfterRun (which can be Never/OnError-with-no-errors, in which
        // case the report never opens on its own and this is the only completion signal the user
        // gets), but skipped entirely for an empty pass so idle polling never spams a notification.
        if (totalFiles > 0)
        {
            _notifications.Notify(
                "Compressarr",
                $"{totalFiles} file(s) processed in {runTime.Hours}h {runTime.Minutes}m {runTime.Seconds}s.",
                reportFilePath);
        }

        return new RunResult
        {
            Report = reportModel,
            ReportFilePath = reportFilePath,
            TotalFiles = totalFiles
        };
    }
}
