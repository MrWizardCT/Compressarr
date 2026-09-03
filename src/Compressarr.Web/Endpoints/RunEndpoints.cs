using Microsoft.AspNetCore.Http;
using Compressarr.Core.Config;
using Compressarr.Core.Conversion;
using Compressarr.Core.Diagnostics;
using Compressarr.Core.Orchestration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace Compressarr.Web.Endpoints;

public sealed record UpNextItem(string LaneDisplayName, string FileName, double SizeGb, string? Preset, bool IsResumed);

public static class RunEndpoints
{
    private const long BytesPerGb = 1024L * 1024L * 1024L;

    /// <summary>Computes the files waiting to be compressed across every enabled lane, for the
    /// Monitor page's "Up Next" section - mirrors ConversionOrchestrator.ProcessLaneAsync's own
    /// "resume-state Pending entries if any exist, else a fresh scan" logic so the list matches
    /// what will actually get picked up next, not an independent re-scan. Skips whichever file is
    /// currently being converted (already shown in "Current status").</summary>
    private static List<UpNextItem> ComputeUpNext(
        CompressarrConfig config,
        IPathExpander pathExpander,
        IVideoFileScanner scanner,
        IResumeStateStore resumeStore,
        RunStateSnapshot currentRun)
    {
        var items = new List<UpNextItem>();
        var resumeFilePath = Path.Combine(AppPaths.GetAppDataDirectory(), "compressarr.resume.json");
        var resumeState = resumeStore.Load(resumeFilePath);

        foreach (var lane in config.Lanes)
        {
            if (!lane.Enabled) continue;

            var inputPath = pathExpander.Expand(lane.Input);
            if (string.IsNullOrWhiteSpace(inputPath) || !Directory.Exists(inputPath)) continue;

            var pending = resumeState.Where(e => e.LaneId == lane.Id && e.Status == ResumeStatus.Pending).ToList();
            var files = pending.Count > 0
                ? pending.Select(p => new FileInfo(p.FullName)).Where(f => f.Exists).ToList()
                : scanner.FindVideoFiles(inputPath, config.Processing.VidTypes, config.Processing.MinSizeBytes, config.Processing.Limit).ToList();

            // pending.Count > 0 is the right "new vs. resumed" signal for a lane this pass hasn't
            // reached yet - but once a lane actually starts, a clean start writes its own fresh
            // Pending entries as bookkeeping before converting anything, which would otherwise
            // make every file in it look "resumed" a moment later even though nothing was ever
            // interrupted. For whichever lane is currently running, trust the flag captured before
            // that mutation happened (RunOrchestrator, via CurrentRunStateService) instead.
            var isCurrentLane = currentRun.IsRunning && string.Equals(lane.DisplayName, currentRun.LaneDisplayName, StringComparison.Ordinal);
            var isResumed = isCurrentLane ? currentRun.CurrentLaneIsResumed : pending.Count > 0;

            foreach (var file in files)
            {
                if (string.Equals(lane.DisplayName, currentRun.LaneDisplayName, StringComparison.Ordinal) &&
                    string.Equals(file.Name, currentRun.FileName, StringComparison.Ordinal))
                {
                    continue;
                }

                var preset = ContentClassifier.IsTvFile(file.Name) ? lane.TvPreset : lane.MoviePreset;
                var sizeGb = Math.Round(file.Length / (double)BytesPerGb, 3);
                items.Add(new UpNextItem(lane.DisplayName, file.Name, sizeGb, preset, isResumed));
            }
        }

        return items;
    }

    public static void MapRunEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/run/once", async (IConfigStore configStore, IRunOrchestrator runOrchestrator) =>
        {
            var config = configStore.Load(AppPaths.GetConfigFilePath());
            var result = await runOrchestrator.RunOnceAsync(config);
            return result is null
                ? Results.BadRequest(new { message = "Run aborted - check the HandBrakeCLI and presets.json paths." })
                : Results.Json(new { totalFiles = result.TotalFiles });
        });

        app.MapPost("/api/run/start", (IConfigStore configStore, IRunLoopController loopController) =>
        {
            var config = configStore.Load(AppPaths.GetConfigFilePath());
            loopController.Start(config, TimeSpan.FromSeconds(Math.Max(5, config.Repeat.PollIntervalSeconds)));
            return Results.Ok();
        });

        app.MapPost("/api/run/stop", async (IRunLoopController loopController) =>
        {
            await loopController.StopAsync();
            return Results.Ok();
        });

        app.MapPost("/api/run/abort", (IRunLoopController loopController) =>
        {
            loopController.Abort();
            return Results.Ok();
        });

        app.MapPost("/api/run/trigger-now", (IRunLoopController loopController) =>
        {
            var triggered = loopController.TriggerNow();
            return Results.Json(new { triggered });
        });

        app.MapGet("/api/run/status", async (
            IRunLoopController loopController,
            CurrentRunStateService runState,
            ICpuUsageSampler cpuSampler,
            IConfigStore configStore,
            IPathExpander pathExpander,
            IVideoFileScanner scanner,
            IResumeStateStore resumeStore) =>
        {
            var snapshot = runState.GetSnapshot();
            var cpu = await cpuSampler.SampleAsync();

            var nextRunUtc = loopController.NextRunUtc;
            var secondsUntilNextRun = nextRunUtc is null
                ? (int?)null
                : Math.Max(0, (int)Math.Ceiling((nextRunUtc.Value - DateTimeOffset.UtcNow).TotalSeconds));

            var config = configStore.Load(AppPaths.GetConfigFilePath());
            var upNext = ComputeUpNext(config, pathExpander, scanner, resumeStore, snapshot);

            return Results.Json(new
            {
                isMonitoring = loopController.IsRunning,
                isStopping = loopController.IsStopping,
                isRunning = snapshot.IsRunning,
                laneDisplayName = snapshot.LaneDisplayName,
                fileName = snapshot.FileName,
                fileIndex = snapshot.FileIndex,
                fileTotal = snapshot.FileTotal,
                progressPercent = snapshot.ProgressPercent,
                progressFps = snapshot.ProgressFps,
                progressEta = snapshot.ProgressEta,
                recentLogLines = snapshot.RecentLogLines,
                cpuUsagePercent = cpu,
                secondsUntilNextRun,
                upNext
            });
        });
    }
}
