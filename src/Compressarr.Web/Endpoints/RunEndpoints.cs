using Microsoft.AspNetCore.Http;
using Compressarr.Core.Config;
using Compressarr.Core.Conversion;
using Compressarr.Core.Diagnostics;
using Compressarr.Core.Orchestration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace Compressarr.Web.Endpoints;

public sealed record UpNextItem(string LaneId, string LaneDisplayName, string FileName, double SizeGb, string? Preset, bool IsResumed, bool IsError, bool IsSkipped, bool IsCustomPreset);

public sealed record RemoveErrorQueueEntryRequest(string LaneId, string FileName);
public sealed record RemoveQueueEntryRequest(string LaneId, string FileName);
public sealed record SkipQueueEntryRequest(string LaneId, string FileName, bool Skipped);
public sealed record ReorderQueueRequest(string LaneId, List<string> OrderedFileNames);
public sealed record PresetOverrideRequest(string LaneId, string FileName, string? Preset);

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
            var pendingByPath = pending.ToDictionary(p => p.FullName, StringComparer.OrdinalIgnoreCase);

            // Same Order-based sort ConversionOrchestrator applies when it actually picks files up
            // - the queue display matches what would actually get processed next, not just an
            // unordered list.
            var pendingFiles = pending.OrderBy(p => p.Order ?? int.MaxValue).Select(p => new FileInfo(p.FullName)).Where(f => f.Exists);
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

                pendingByPath.TryGetValue(file.FullName, out var entry);

                // A freshly-scanned file with no Pending entry yet is always "New", regardless of
                // whatever this lane's own overall pass-resumed state is - it was never tracked
                // before this very poll.
                var isResumed = entry is not null && laneIsResumed;
                var isSkipped = entry?.Skipped ?? false;
                var hasOverride = !string.IsNullOrWhiteSpace(entry?.PresetOverride);
                var preset = hasOverride ? entry!.PresetOverride : (ContentClassifier.IsTvFile(file.Name) ? lane.TvPreset : lane.MoviePreset);
                var sizeGb = Math.Round(file.Length / (double)BytesPerGb, 3);
                items.Add(new UpNextItem(lane.Id, lane.DisplayName, file.Name, sizeGb, preset, isResumed, IsError: false, isSkipped, hasOverride));
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
                items.Add(new UpNextItem(lane.Id, lane.DisplayName, fileInfo.Name, sizeGb, preset, IsResumed: false, IsError: true, IsSkipped: false, IsCustomPreset: false));
            }
        }

        return items;
    }

    /// <summary>Finds the Pending resume entry for fileName in this lane, or creates one (Status
    /// Pending) if the file is real but wasn't tracked yet - e.g. a freshly-scanned file the user
    /// reorders/skips/overrides the preset on before ConversionOrchestrator's own next pass would
    /// have gotten around to tracking it. Returns null only if fileName doesn't resolve to a real
    /// file directly inside the lane's Input folder (a stale request for a file that's since been
    /// moved/deleted).</summary>
    private static ResumeEntry? FindOrCreatePendingEntry(List<ResumeEntry> resumeState, string laneId, string inputPath, string fileName)
    {
        var existing = resumeState.FirstOrDefault(e =>
            e.LaneId == laneId &&
            e.Status == ResumeStatus.Pending &&
            string.Equals(Path.GetFileName(e.FullName), fileName, StringComparison.Ordinal));
        if (existing is not null) return existing;

        var fullPath = Path.Combine(inputPath, fileName);
        if (!File.Exists(fullPath)) return null;

        var created = new ResumeEntry { LaneId = laneId, FullName = fullPath, Status = ResumeStatus.Pending };
        resumeState.Add(created);
        return created;
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

        // Drag-to-reorder on the Monitor page's In Queue list - sets Order sequentially for every
        // file in the given order, for that lane only. A file with no resume entry yet (freshly
        // scanned, never touched before) gets one created here (Status Pending) so the order
        // actually sticks - reusing the exact same path/lane-matching FindOrCreatePendingEntry the
        // skip and preset-override endpoints below use.
        app.MapPost("/api/run/queue/reorder", (ReorderQueueRequest request, IConfigStore configStore, IPathExpander pathExpander, IResumeStateStore resumeStore) =>
        {
            var config = configStore.Load(AppPaths.GetConfigFilePath());
            var lane = config.Lanes.FirstOrDefault(l => l.Id == request.LaneId);
            if (lane is null) return Results.NotFound();

            var inputPath = pathExpander.Expand(lane.Input);
            var resumeFilePath = AppPaths.GetResumeFilePath();
            var resumeState = resumeStore.Load(resumeFilePath);

            for (var i = 0; i < request.OrderedFileNames.Count; i++)
            {
                var entry = FindOrCreatePendingEntry(resumeState, lane.Id, inputPath, request.OrderedFileNames[i]);
                if (entry is not null) entry.Order = i;
            }

            resumeStore.Save(resumeState, resumeFilePath);
            return Results.Ok();
        });

        // "Skip" from the queue's 3-dot menu - the entry stays visible (dimmed) but
        // ConversionOrchestrator excludes it from what actually gets encoded. Persists until
        // toggled back off from the same menu, not a true one-shot skip (see ResumeEntry.Skipped).
        app.MapPost("/api/run/queue/skip", (SkipQueueEntryRequest request, IConfigStore configStore, IPathExpander pathExpander, IResumeStateStore resumeStore) =>
        {
            var config = configStore.Load(AppPaths.GetConfigFilePath());
            var lane = config.Lanes.FirstOrDefault(l => l.Id == request.LaneId);
            if (lane is null) return Results.NotFound();

            var inputPath = pathExpander.Expand(lane.Input);
            var resumeFilePath = AppPaths.GetResumeFilePath();
            var resumeState = resumeStore.Load(resumeFilePath);

            var entry = FindOrCreatePendingEntry(resumeState, lane.Id, inputPath, request.FileName);
            if (entry is null) return Results.NotFound();

            entry.Skipped = request.Skipped;
            resumeStore.Save(resumeState, resumeFilePath);
            return Results.Ok();
        });

        // "Remove from queue" for a regular (non-Error) queue item - clears its tracked Pending
        // entry, same "file stays on disk, may resurface on a future scan" semantics as
        // remove-error above. Unlike skip/reorder/preset-override, this never creates an entry
        // first - there's nothing meaningful to remove for a file that was never tracked, and it
        // would just be re-scanned back into view on the very next poll regardless.
        app.MapPost("/api/run/queue/remove", (RemoveQueueEntryRequest request, IResumeStateStore resumeStore) =>
        {
            var resumeFilePath = AppPaths.GetResumeFilePath();
            var resumeState = resumeStore.Load(resumeFilePath);
            var removed = resumeState.RemoveAll(e =>
                e.LaneId == request.LaneId &&
                e.Status == ResumeStatus.Pending &&
                string.Equals(Path.GetFileName(e.FullName), request.FileName, StringComparison.Ordinal));

            if (removed > 0) resumeStore.Save(resumeState, resumeFilePath);
            return Results.Json(new { removed });
        });

        // Per-file preset override, set by clicking the preset name on a queue row - overrides the
        // lane's TvPreset/MoviePreset for this one file only. Passing null/empty Preset clears the
        // override back to the lane default.
        app.MapPost("/api/run/queue/preset-override", (PresetOverrideRequest request, IConfigStore configStore, IPathExpander pathExpander, IResumeStateStore resumeStore) =>
        {
            var config = configStore.Load(AppPaths.GetConfigFilePath());
            var lane = config.Lanes.FirstOrDefault(l => l.Id == request.LaneId);
            if (lane is null) return Results.NotFound();

            var inputPath = pathExpander.Expand(lane.Input);
            var resumeFilePath = AppPaths.GetResumeFilePath();
            var resumeState = resumeStore.Load(resumeFilePath);

            var entry = FindOrCreatePendingEntry(resumeState, lane.Id, inputPath, request.FileName);
            if (entry is null) return Results.NotFound();

            entry.PresetOverride = string.IsNullOrWhiteSpace(request.Preset) ? null : request.Preset;
            resumeStore.Save(resumeState, resumeFilePath);
            return Results.Ok();
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
