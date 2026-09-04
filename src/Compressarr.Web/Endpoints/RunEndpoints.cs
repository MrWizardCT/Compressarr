using Microsoft.AspNetCore.Http;
using Compressarr.Core.Config;
using Compressarr.Core.Conversion;
using Compressarr.Core.Diagnostics;
using Compressarr.Core.Orchestration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace Compressarr.Web.Endpoints;

public sealed record UpNextItem(string LaneId, string LaneDisplayName, string FileName, double SizeGb, string? Preset, bool IsResumed, bool IsError);

public sealed record RemoveErrorQueueEntryRequest(string LaneId, string FileName);

public static class RunEndpoints
{
    private const long BytesPerGb = 1024L * 1024L * 1024L;

    /// <summary>Computes the files waiting to be compressed across every enabled lane, for the
    /// Monitor page's "Up Next" section - mirrors ConversionOrchestrator.ProcessLaneAsync's own
    /// "resume-state Pending entries if any exist, else a fresh scan" logic so the list matches
    /// what will actually get picked up next, not an independent re-scan. Skips whichever file is
    /// currently being converted (already shown in "Current status").
    ///
    /// Always does a live Input scan for every lane, not just ones with no Pending entries yet -
    /// this is what lets a file dropped into the *currently-running* lane's Input folder show up
    /// here on the very next poll instead of only after that lane's whole pass finishes. Files
    /// already known via a Pending resume entry are deduplicated against the scan by full path.
    ///
    /// Error entries are surfaced too (their own bucket, IsError=true) purely for visibility - they
    /// are never fed into ConversionOrchestrator's own Pending filter, so listing them here doesn't
    /// change what gets processed.</summary>
    private static List<UpNextItem> ComputeUpNext(
        CompressarrConfig config,
        IPathExpander pathExpander,
        IVideoFileScanner scanner,
        IResumeStateStore resumeStore,
        RunStateSnapshot currentRun)
    {
        var items = new List<UpNextItem>();
        var resumeState = resumeStore.Load(AppPaths.GetResumeFilePath());

        foreach (var lane in config.Lanes)
        {
            if (!lane.Enabled) continue;

            var inputPath = pathExpander.Expand(lane.Input);
            if (string.IsNullOrWhiteSpace(inputPath) || !Directory.Exists(inputPath)) continue;

            var pending = resumeState.Where(e => e.LaneId == lane.Id && e.Status == ResumeStatus.Pending).ToList();
            var pendingPaths = pending.Select(p => p.FullName).ToHashSet(StringComparer.OrdinalIgnoreCase);

            var pendingFiles = pending.Select(p => new FileInfo(p.FullName)).Where(f => f.Exists);
            var freshlyScanned = scanner.FindVideoFiles(inputPath, config.Processing.VidTypes, config.Processing.MinSizeBytes, config.Processing.Limit)
                .Where(f => !pendingPaths.Contains(f.FullName));
            var files = pendingFiles.Concat(freshlyScanned).ToList();

            // pending.Count > 0 is the right "new vs. resumed" signal for a lane this pass hasn't
            // reached yet - but once a lane actually starts, a clean start writes its own fresh
            // Pending entries as bookkeeping before converting anything, which would otherwise
            // make every file in it look "resumed" a moment later even though nothing was ever
            // interrupted. For whichever lane is currently running, trust the flag captured before
            // that mutation happened (RunOrchestrator, via CurrentRunStateService) instead.
            var isCurrentLane = currentRun.IsRunning && string.Equals(lane.DisplayName, currentRun.LaneDisplayName, StringComparison.Ordinal);
            var laneIsResumed = isCurrentLane ? currentRun.CurrentLaneIsResumed : pending.Count > 0;

            foreach (var file in files)
            {
                if (string.Equals(lane.DisplayName, currentRun.LaneDisplayName, StringComparison.Ordinal) &&
                    string.Equals(file.Name, currentRun.FileName, StringComparison.Ordinal))
                {
                    continue;
                }

                // A freshly-scanned file with no Pending entry yet is always "New", regardless of
                // whatever this lane's own overall pass-resumed state is - it was never tracked
                // before this very poll.
                var isResumed = pendingPaths.Contains(file.FullName) && laneIsResumed;

                var preset = ContentClassifier.IsTvFile(file.Name) ? lane.TvPreset : lane.MoviePreset;
                var sizeGb = Math.Round(file.Length / (double)BytesPerGb, 3);
                items.Add(new UpNextItem(lane.Id, lane.DisplayName, file.Name, sizeGb, preset, isResumed, IsError: false));
            }

            var errorEntries = resumeState.Where(e => e.LaneId == lane.Id && e.Status == ResumeStatus.Error).ToList();
            foreach (var entry in errorEntries)
            {
                var fileInfo = new FileInfo(entry.FullName);
                // A dead Error entry (source file gone) is cleaned up automatically by
                // ConversionOrchestrator the next time this lane's pass actually runs - just skip
                // showing a ghost row for it here in the meantime, rather than duplicating that
                // cleanup in a read-only status endpoint.
                if (!fileInfo.Exists) continue;

                var preset = ContentClassifier.IsTvFile(fileInfo.Name) ? lane.TvPreset : lane.MoviePreset;
                var sizeGb = Math.Round(fileInfo.Length / (double)BytesPerGb, 3);
                items.Add(new UpNextItem(lane.Id, lane.DisplayName, fileInfo.Name, sizeGb, preset, IsResumed: false, IsError: true));
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

        // Clears a single Error-status resume entry, surfaced on the In Queue list's Error badge -
        // the file itself is left untouched on disk, only its tracking entry is dropped so it stops
        // showing up here. A later fresh scan of the lane's Input folder can pick the file back up
        // as a brand-new Pending entry, same as any other file that was never tracked before.
        app.MapPost("/api/run/queue/remove-error", (RemoveErrorQueueEntryRequest request, IResumeStateStore resumeStore) =>
        {
            var resumeFilePath = AppPaths.GetResumeFilePath();
            var resumeState = resumeStore.Load(resumeFilePath);
            var removed = resumeState.RemoveAll(e =>
                e.LaneId == request.LaneId &&
                e.Status == ResumeStatus.Error &&
                string.Equals(Path.GetFileName(e.FullName), request.FileName, StringComparison.Ordinal));

            if (removed > 0) resumeStore.Save(resumeState, resumeFilePath);
            return Results.Json(new { removed });
        });

        app.MapPost("/api/run/pause", (IActiveHandBrakeProcess activeProcess) =>
        {
            activeProcess.Pause();
            return Results.Ok();
        });

        app.MapPost("/api/run/resume", (IActiveHandBrakeProcess activeProcess) =>
        {
            activeProcess.Resume();
            return Results.Ok();
        });

        app.MapGet("/api/run/status", async (
            IRunLoopController loopController,
            CurrentRunStateService runState,
            ICpuUsageSampler cpuSampler,
            IConfigStore configStore,
            IPathExpander pathExpander,
            IVideoFileScanner scanner,
            IResumeStateStore resumeStore,
            IActiveHandBrakeProcess activeProcess) =>
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
                isPaused = activeProcess.IsPaused,
                laneDisplayName = snapshot.LaneDisplayName,
                fileName = snapshot.FileName,
                presetName = snapshot.PresetName,
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
