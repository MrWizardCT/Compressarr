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

    /// <summary>True if any file in this pass failed in a way that looked like the volume being
    /// out of space. The monitoring loop stops itself when this is true, rather than retrying the
    /// same doomed encode again on the next poll interval.</summary>
    public bool DiskFull { get; init; }
}

public interface IRunOrchestrator
{
    /// <summary>One full pass: validate HandBrakeCLI/presets paths, prepare every enabled lane with
    /// configured input, then process pending files in one global, cross-lane priority order (not
    /// lane-by-lane) - a file's explicit drag-set Order always wins regardless of which lane it's
    /// in, so a not-yet-started file from any lane can be prioritized ahead of any other lane's.
    /// Purges old logs/reports by retention, records history + increments the run counter (only if
    /// files were processed), runs the optional post-exec command, builds the HTML report, and
    /// fires a notification. Ported from Invoke-CompressarrRun. Returns null if HandBrakeCLI or
    /// presets.json can't be found (the whole run aborts, matching v1).
    ///
    /// stopToken is a graceful "Stop Monitoring" signal, distinct from Abort's hard-kill token -
    /// checked before every file (across every lane, not just between lanes), so the file actively
    /// encoding right now always finishes before this pass unwinds, rather than being killed
    /// mid-encode.</summary>
    Task<RunResult?> RunOnceAsync(CompressarrConfig config, CancellationToken stopToken = default);
}

public sealed class RunOrchestrator : IRunOrchestrator
{
    private readonly IPathExpander _pathExpander;
    private readonly IHandBrakePresetService _presets;
    private readonly IConversionOrchestrator _conversionOrchestrator;
    private readonly IMetadataService _metadata;
    private readonly IResumeStateStore _resumeStore;
    private readonly IRunLogger _logger;
    private readonly IRunHistoryStore _historyStore;
    private readonly IHistoryRollupCalculator _rollupCalculator;
    private readonly IHtmlReportGenerator _reportGenerator;
    private readonly IReportLauncher _reportLauncher;
    private readonly INotificationService _notifications;
    private readonly INotificationDispatcher _notificationDispatcher;
    private readonly ITrashService _trash;
    private readonly IRunProgressReporter _progress;
    private readonly IActiveRunController _activeRunController;

    public RunOrchestrator(
        IPathExpander pathExpander,
        IHandBrakePresetService presets,
        IConversionOrchestrator conversionOrchestrator,
        IMetadataService metadata,
        IResumeStateStore resumeStore,
        IRunLogger logger,
        IRunHistoryStore historyStore,
        IHistoryRollupCalculator rollupCalculator,
        IHtmlReportGenerator reportGenerator,
        IReportLauncher reportLauncher,
        INotificationService notifications,
        INotificationDispatcher notificationDispatcher,
        ITrashService trash,
        IRunProgressReporter progress,
        IActiveRunController activeRunController)
    {
        _pathExpander = pathExpander;
        _presets = presets;
        _conversionOrchestrator = conversionOrchestrator;
        _metadata = metadata;
        _resumeStore = resumeStore;
        _logger = logger;
        _historyStore = historyStore;
        _rollupCalculator = rollupCalculator;
        _reportGenerator = reportGenerator;
        _reportLauncher = reportLauncher;
        _notifications = notifications;
        _notificationDispatcher = notificationDispatcher;
        _trash = trash;
        _progress = progress;
        _activeRunController = activeRunController;
    }

    public async Task<RunResult?> RunOnceAsync(CompressarrConfig config, CancellationToken stopToken = default)
    {
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        var beginTime = DateTime.Now;

        var logFilePath = _pathExpander.Expand(config.Logging.LogFilePath);
        var reportPath = _pathExpander.Expand(config.Report.ReportPath);
        _logger.Initialize(logFilePath, timestamp);

        _logger.Log($"Compressarr - run started {timestamp}");
        _logger.Log(new string('-', 80));
        _progress.RunStarted(timestamp);

        var token = _activeRunController.Begin();
        try
        {
            return await RunOnceCoreAsync(config, timestamp, beginTime, logFilePath, reportPath, token, stopToken);
        }
        finally
        {
            _activeRunController.End();
        }
    }

    private async Task<RunResult?> RunOnceCoreAsync(CompressarrConfig config, string timestamp, DateTime beginTime, string logFilePath, string reportPath, CancellationToken token, CancellationToken stopToken)
    {
        var hbloc = _pathExpander.Expand(config.HandBrake.CliPath);
        if (!_pathExpander.PathExists(config.HandBrake.CliPath))
        {
            _logger.Log($"HandBrakeCLI.exe not found at {hbloc}. Download it from https://handbrake.fr/downloads2.php", LogSeverity.Error);
            _progress.RunCompleted(0);
            return null;
        }

        var presetsPath = _pathExpander.Expand(config.HandBrake.PresetsPath);
        if (!_pathExpander.PathExists(config.HandBrake.PresetsPath))
        {
            _logger.Log($"HandBrake presets file not found at {presetsPath}", LogSeverity.Error);
            _progress.RunCompleted(0);
            return null;
        }

        var resumeFilePath = AppPaths.GetResumeFilePath();
        var resumeState = _resumeStore.Load(resumeFilePath);
        if (resumeState.Count > 0)
        {
            _logger.Log($"Resuming previous incomplete run ({resumeState.Count} file(s) tracked).");
        }

        _metadata.Enabled = config.Processing.ClearTitleMetadata;

        var laneResults = new Dictionary<string, List<ConversionResult>>();
        try
        {
            // Phase 1: per-lane prep, unchanged validation/logging - every enabled, valid lane gets
            // scanned and seeded up front, before any file from any lane starts converting. Lanes
            // that don't pass validation are simply never added to laneContexts/laneOrderIndex, so
            // Phase 2's cross-lane picking query below naturally never considers their entries.
            var laneContexts = new Dictionary<string, LaneProcessingContext>();
            var laneOrderIndex = new Dictionary<string, int>();
            var configLaneIndex = 0;
            foreach (var lane in config.Lanes)
            {
                token.ThrowIfCancellationRequested();
                stopToken.ThrowIfCancellationRequested();

                if (!lane.Enabled)
                {
                    _logger.Log($"Skipping lane [{lane.DisplayName}] - lane is disabled.");
                    configLaneIndex++;
                    continue;
                }

                if (string.IsNullOrWhiteSpace(lane.Input)) { configLaneIndex++; continue; }

                if (string.IsNullOrWhiteSpace(lane.TvPreset) && string.IsNullOrWhiteSpace(lane.MoviePreset))
                {
                    _logger.Log($"Skipping lane [{lane.DisplayName}] - no TV or Movie preset configured.", LogSeverity.Error);
                    configLaneIndex++;
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
                // Captured from resumeState before PrepareLane touches it - a lane starting clean
                // writes its own fresh Pending entries for bookkeeping before converting anything,
                // which would otherwise make it look "resumed" a moment later even though nothing
                // was ever interrupted.
                var laneIsResumed = resumeState.Any(e => e.LaneId == lane.Id && e.Status == ResumeStatus.Pending);
                _progress.LaneStarted(lane.Id, lane.DisplayName, laneIsResumed);

                var context = _conversionOrchestrator.PrepareLane(lane, config, resumeState, resumeFilePath);
                configLaneIndex++;
                if (context is null) continue;

                laneContexts[lane.Id] = context;
                laneOrderIndex[lane.Id] = configLaneIndex - 1;
                laneResults[lane.Id] = new List<ConversionResult>();
            }

            // Phase 2: one global loop across every prepared lane, picking whichever eligible entry
            // is highest priority regardless of which lane it belongs to - lower explicit Order
            // first, then (for entries nobody has ever dragged) each lane's own position in
            // config.Lanes, then that file's natural scan order within its own lane. This is the
            // same three-level tie-break RunEndpoints.ComputeUpNext computes independently for the
            // Monitor page's own queue display, so the two can never disagree about "what's next."
            while (true)
            {
                token.ThrowIfCancellationRequested();
                // Graceful Stop Monitoring - checked before every file, across every lane, so a
                // stop requested while other lanes still have queued files doesn't go on to start
                // an entirely different file's worth of work before honoring it.
                stopToken.ThrowIfCancellationRequested();

                _conversionOrchestrator.RefreshResumeState(resumeState, resumeFilePath);

                var next = resumeState
                    .Where(e => laneContexts.ContainsKey(e.LaneId) && e.Status == ResumeStatus.Pending && !e.Skipped && !e.Removed)
                    .OrderBy(e => e.Order ?? int.MaxValue)
                    .ThenBy(e => laneOrderIndex[e.LaneId])
                    .ThenBy(e => laneContexts[e.LaneId].NaturalOrderIndex.TryGetValue(e.FullName, out var idx) ? idx : int.MaxValue)
                    .FirstOrDefault(e => File.Exists(e.FullName));

                if (next is null) break;

                var context = laneContexts[next.LaneId];
                var result = await _conversionOrchestrator.ProcessOneFileAsync(context, next, logFilePath, timestamp, resumeState, resumeFilePath, token);
                laneResults[next.LaneId].Add(result);
            }
        }
        catch (OperationCanceledException)
        {
            // token (Abort) kills the in-flight encode; stopToken (Stop Monitoring) only ever
            // stops this pass between files/lanes, letting whatever was already encoding finish -
            // worth distinguishing here since "aborted"/Error severity would be actively
            // misleading for the latter, much more common, entirely-expected case.
            if (token.IsCancellationRequested)
            {
                _logger.Log("\nRun aborted by user.", LogSeverity.Error);
            }
            else
            {
                _logger.Log("\nMonitoring stopped - remaining queued files will run on the next pass.");
            }
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

        var runCountPath = AppPaths.GetRunCountFilePath();
        var reportFileName = $"Compressarr_{timestamp}_Report.html";
        // 0 means "this pass found nothing to do" - the report shows a plain "Run:" label
        // instead of "Run #N:" for one of those, matching v1.1.
        var runNumber = 0;
        if (totalFiles > 0)
        {
            var totalBeg = allResults.Sum(r => r.BeginSizeGb);
            var totalEnd = allResults.Sum(r => r.EndSizeGb);
            // Mirrors ReportModel.ErrorCount/WarningCount - computed here too since the history
            // record is written before reportModel exists below.
            var errorCount = allResults.Count(r => !r.Success);
            var warningCount = allResults.Count(r => r.Success && !string.IsNullOrEmpty(r.PostProcessWarning));
            // The run's own permanent number is "how many runs came before it, plus one" - read
            // before IncrementRunCount below so the record and the counter agree on the same value.
            runNumber = _historyStore.GetRunCount(runCountPath) + 1;
            _historyStore.AppendRun(logFilePath, new RunHistoryRecord(
                endTime.Year, endTime.Month, endTime.Day, totalBeg, totalEnd, totalFiles,
                runTime.Hours, runTime.Minutes, runTime.Seconds,
                RunNumber: runNumber, ReportFileName: reportFileName,
                ErrorCount: errorCount, WarningCount: warningCount));

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

        _logger.Log(totalFiles == 0
            ? "\nCompressarr run completed. No new files to process."
            : $"\nCompressarr run completed. {totalFiles} file(s) processed in {runTime.Hours}h {runTime.Minutes}m {runTime.Seconds}s.");

        var (today, thisMonth, thisYear) = _rollupCalculator.Calculate(logFilePath);

        var reportModel = new ReportModel
        {
            GeneratedAt = endTime,
            RunTime = runTime,
            RunNumber = runNumber,
            // Every configured lane gets a section - matching v1.1, a lane that wasn't touched
            // this pass (disabled, or enabled but nothing to do) still shows up with a "No files
            // processed." placeholder rather than being silently absent from the report.
            Lanes = config.Lanes.Select(lane => new LaneReportSection
            {
                LaneDisplayName = lane.DisplayName,
                Results = laneResults.TryGetValue(lane.Id, out var results) ? results : Array.Empty<ConversionResult>()
            }).ToList(),
            Today = today,
            ThisMonth = thisMonth,
            ThisYear = thisYear
        };

        Directory.CreateDirectory(reportPath);
        var reportFilePath = Path.Combine(reportPath, reportFileName);
        var html = _reportGenerator.Generate(reportModel);
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
            if (config.Notifications.ToastEnabled)
            {
                var toastBeg = allResults.Sum(r => r.BeginSizeGb);
                var toastEnd = allResults.Sum(r => r.EndSizeGb);

                _notifications.NotifyRunComplete(
                    new RunCompletionSummary(totalFiles, toastBeg, toastEnd, runTime),
                    reportFilePath);
            }

            // Separate from the toast above - toast is a local-OS-only channel, this is the
            // pluggable list of external destinations (webhook, and later Discord/Slack/etc)
            // configured on the Notifications page. Outcome/counts come straight off reportModel,
            // already built above, rather than recomputing anything.
            var outcome = reportModel.ErrorCount > 0 ? NotificationOutcome.Error
                : reportModel.WarningCount > 0 ? NotificationOutcome.Warning
                : NotificationOutcome.Success;
            var notifyEvent = new NotificationEvent(
                outcome,
                Title: $"Compressarr run #{runNumber}",
                Body: $"{totalFiles} file(s) processed, {reportModel.TotalBeforeGb - reportModel.TotalAfterGb:0.##} GB saved.",
                totalFiles,
                SavedGb: reportModel.TotalBeforeGb - reportModel.TotalAfterGb,
                runTime,
                reportFilePath);
            await _notificationDispatcher.DispatchAsync(config.Notifications, notifyEvent);
        }

        _progress.RunCompleted(totalFiles);

        return new RunResult
        {
            Report = reportModel,
            ReportFilePath = reportFilePath,
            TotalFiles = totalFiles,
            DiskFull = allResults.Any(r => r.DiskFull)
        };
    }
}
