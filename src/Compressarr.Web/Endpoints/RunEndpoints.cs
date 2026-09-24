using Microsoft.AspNetCore.Http;
using Compressarr.Core.Config;
using Compressarr.Core.Conversion;
using Compressarr.Core.Diagnostics;
using Compressarr.Core.Orchestration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace Compressarr.Web.Endpoints;

// FullName (the file's real absolute path) is what every queue-mutation request identifies a row
// by - FileName (the bare leaf name, kept for display only) is NOT unique: lanes intentionally
// support recursive Input folders, so two different files can legitimately share the same leaf
// name in different subfolders (e.g. two different shows' own "episode.mkv"). Code-review finding:
// matching on FileName alone let a skip/remove/preset-override/reorder request silently target the
// wrong one of two same-named files, or fail ambiguously.
public sealed record UpNextItem(string LaneId, string LaneDisplayName, string FileName, string FullName, double SizeGb, string? Preset, bool IsResumed, bool IsError, bool IsSkipped, bool IsCustomPreset, bool IsFileBotUnmatched);

public sealed record RemoveErrorQueueEntryRequest(string LaneId, string FullName);
public sealed record RemoveQueueEntryRequest(string LaneId, string FullName);
public sealed record SkipQueueEntryRequest(string LaneId, string FullName, bool Skipped);
public sealed record ReorderQueueItem(string LaneId, string FullName);
public sealed record ReorderQueueRequest(List<ReorderQueueItem> Items);
public sealed record PresetOverrideRequest(string LaneId, string FullName, string? Preset);
public sealed record ReassignLaneRequest(string LaneId, string FullName, string NewLaneId);

public static class RunEndpoints
{
    private const long BytesPerGb = 1024L * 1024L * 1024L;

    /// <summary>Computes the files waiting to be compressed across every enabled lane, for the
    /// Monitor page's "Up Next" section - mirrors ConversionOrchestrator's own PrepareLane/global
    /// picking logic so the list matches what will actually get picked up next, not an independent
    /// re-scan. Skips whichever file is currently being converted (already shown in "Current
    /// status").
    ///
    /// Always does a live Input scan for every lane, not just ones with no Pending entries yet -
    /// this is what lets a file dropped into the *currently-running* lane's Input folder show up
    /// here on the very next poll instead of only after that lane's whole pass finishes. Files
    /// already known via a Pending resume entry are deduplicated against the scan by full path.
    ///
    /// One combined, cross-lane sort at the end - not per-lane blocks concatenated in config.Lanes
    /// order - using the identical three-level tie-break RunOrchestrator's own global picking loop
    /// uses (explicit Order, then each file's own lane's position in config.Lanes, then that file's
    /// natural scan order within its own lane), so the displayed queue and the actual processing
    /// order can never disagree. Error entries are surfaced too (their own bucket, IsError=true,
    /// appended after every lane's Pending items) purely for visibility - they are never fed into
    /// the engine's own Pending filter, so listing them here doesn't change what gets processed.</summary>
    private static List<UpNextItem> ComputeUpNext(
        CompressarrConfig config,
        IPathExpander pathExpander,
        IVideoFileScanner scanner,
        IResumeStateStore resumeStore,
        RunStateSnapshot currentRun)
    {
        var resumeState = resumeStore.Load(AppPaths.GetResumeFilePath());

        var candidates = new List<(UpNextItem Item, int LaneOrderIndex, int? Order, int NaturalIndex)>();
        var errorItems = new List<UpNextItem>();

        var laneOrderIndex = 0;
        foreach (var lane in config.Lanes)
        {
            if (!lane.Enabled) { laneOrderIndex++; continue; }

            var inputPath = pathExpander.Expand(lane.Input);
            if (string.IsNullOrWhiteSpace(inputPath) || !Directory.Exists(inputPath)) { laneOrderIndex++; continue; }

            var pending = resumeState.Where(e => e.LaneId == lane.Id && e.Status == ResumeStatus.Pending).ToList();
            var pendingByPath = pending.ToDictionary(p => p.FullName, StringComparer.OrdinalIgnoreCase);

            // Excludes every path already tracked under ANY status for this lane, not just
            // Pending - otherwise a file that just became Error (e.g. Abort mid-encode) or
            // MoveFailed shows up twice: once here as a "fresh" untracked file, and again from its
            // own real entry (the Error bucket below, or invisible-but-real for MoveFailed).
            var trackedPaths = resumeState.Where(e => e.LaneId == lane.Id).Select(e => e.FullName).ToHashSet(StringComparer.OrdinalIgnoreCase);

            // A file physically still sitting in this lane's own Input folder, but reassigned (the
            // Monitor page's "Move to lane" queue control) to track under a DIFFERENT lane's
            // LaneId, is that other lane's item to show now - excluding it here keeps this display
            // in sync with ConversionOrchestrator.PrepareLaneAsync's own identical guard, so a
            // reassigned file is never shown twice (once under its new lane, once "rediscovered" as
            // new under the old one).
            var reassignedElsewhere = resumeState
                .Where(e => e.LaneId != lane.Id && e.Status is ResumeStatus.Pending or ResumeStatus.MoveFailed or ResumeStatus.CompanionMoveFailed or ResumeStatus.CleanupPending)
                .Select(e => e.FullName)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            // One single natural-order scan drives the fallback ordering for every file that has no
            // EXPLICIT user-set Order (a real drag-reorder on the Monitor page) - both genuinely
            // untracked ("New") files and Pending entries that only exist because a skip/preset-
            // override/remove action touched them (FindOrCreatePendingEntry, ResumeEntry.
            // CreatedByQueueEdit above) but were never actually dragged.
            var scannedFiles = scanner.FindVideoFiles(inputPath, config.Processing.VidTypes, config.Processing.MinSizeBytes, config.Processing.Limit)
                .Where(f => (!trackedPaths.Contains(f.FullName) || pendingByPath.ContainsKey(f.FullName)) && !reassignedElsewhere.Contains(f.FullName))
                .ToList();
            var naturalIndex = scannedFiles
                .Select((f, idx) => (f.FullName, idx))
                .ToDictionary(x => x.FullName, x => x.idx, StringComparer.OrdinalIgnoreCase);

            var files = scannedFiles.Where(f => !(pendingByPath.TryGetValue(f.FullName, out var e) && e.Removed)).ToList();

            // A Pending entry reassigned INTO this lane (the Monitor page's lane dropdown) has its
            // own real file physically sitting in a DIFFERENT lane's Input folder - the scan above,
            // scoped to THIS lane's own Input, can never find it. Added directly from its own
            // tracked FullName here, the same way ConversionOrchestrator.PrepareLaneAsync's own
            // pending-entries branch already does - without this, a reassigned file vanished from
            // the Monitor page entirely (correctly excluded from its old lane by reassignedElsewhere
            // above, but never actually appearing anywhere else either), even though it was still
            // genuinely queued and would still be picked up for real by the next pass.
            var scannedPaths = scannedFiles.Select(f => f.FullName).ToHashSet(StringComparer.OrdinalIgnoreCase);
            files.AddRange(pending
                .Where(p => !p.Removed && !scannedPaths.Contains(p.FullName) && File.Exists(p.FullName))
                .Select(p => new FileInfo(p.FullName)));

            // Whether this lane genuinely had incomplete work outstanding when its OWN most recent
            // pass began - recorded once by RunOrchestrator (via CurrentRunStateService.LaneStarted)
            // right before PrepareLane's own bookkeeping could add fresh Pending entries and make it
            // look "resumed" a moment later even though nothing was ever interrupted. Kept per-lane
            // (LaneIsResumedById), not just for whichever lane happens to be running RIGHT NOW -
            // confirmed live: pressing Stop Monitoring mid-pass (or the pass simply finishing) used
            // to make every remaining untouched file in the queue flip to "Resumed", because the old
            // isCurrentLane-only signal fell back to "pending.Any(...)" the instant the lane stopped
            // being current - which is true for ANY tracked Pending entry, including ones that were
            // only ever freshly scanned this same pass and never actually attempted.
            //
            // Falls back to the old pending.Any(...) heuristic only when this lane has no recorded
            // entry yet at all - the gap between the app starting up and its very first pass ever
            // running, where genuinely-carried-over-from-a-prior-session Pending entries exist but
            // LaneStarted hasn't fired even once to record whether they're real resumed work.
            var laneIsResumed = currentRun.LaneIsResumedById.TryGetValue(lane.Id, out var recordedResumed)
                ? recordedResumed
                : pending.Any(p => !p.CreatedByQueueEdit);

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
                // before this very poll. Same for a CreatedByQueueEdit entry - it exists purely to
                // hold queue metadata the user just set, not because this file's own processing was
                // ever actually interrupted.
                var isResumed = entry is not null && !entry.CreatedByQueueEdit && laneIsResumed;
                var isSkipped = entry?.Skipped ?? false;
                var hasOverride = !string.IsNullOrWhiteSpace(entry?.PresetOverride);
                var preset = hasOverride ? entry!.PresetOverride : (ContentClassifier.IsTvFile(file.Name) ? lane.TvPreset : lane.MoviePreset);
                var sizeGb = Math.Round(file.Length / (double)BytesPerGb, 3);
                var isFileBotUnmatched = entry?.FileBotUnmatched ?? false;
                var item = new UpNextItem(lane.Id, lane.DisplayName, file.Name, file.FullName, sizeGb, preset, isResumed, IsError: false, isSkipped, hasOverride, isFileBotUnmatched);
                // A reassigned-in file (added above, never part of this lane's own scan) has no
                // natural scan position here at all - falls to the back of this lane's own tie-break
                // order, same as PrepareLaneAsync's identical NaturalOrderIndex fallback.
                var naturalOrder = naturalIndex.TryGetValue(file.FullName, out var idx) ? idx : int.MaxValue;
                candidates.Add((item, laneOrderIndex, entry?.Order, naturalOrder));
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
                errorItems.Add(new UpNextItem(lane.Id, lane.DisplayName, fileInfo.Name, fileInfo.FullName, sizeGb, preset, IsResumed: false, IsError: true, IsSkipped: false, IsCustomPreset: false, IsFileBotUnmatched: false));
            }

            laneOrderIndex++;
        }

        var items = candidates
            .OrderBy(c => c.Order ?? int.MaxValue)
            .ThenBy(c => c.LaneOrderIndex)
            .ThenBy(c => c.NaturalIndex)
            .Select(c => c.Item)
            .ToList();
        items.AddRange(errorItems);
        return items;
    }

    /// <summary>Finds the Pending resume entry for fullName in this lane, or creates one (Status
    /// Pending) if the file is real but wasn't tracked yet - e.g. a freshly-scanned file the user
    /// reorders/skips/overrides the preset on before the engine's own next pass would have gotten
    /// around to tracking it. Returns null if fullName doesn't resolve to a real file on disk (a
    /// stale request for a file that's since been moved/deleted), or if it already has a tracked
    /// entry under a status other than Pending (e.g. Error) - real bug found live: matching only on
    /// Status == Pending let a drag involving an unrelated Pending file in the same lane as an
    /// Error-status file silently create a SECOND, phantom Pending entry for the already-failed
    /// file, which the engine would then silently re-encode. Matching on LaneId+path regardless of
    /// status first closes that off entirely, rather than only reducing how often it can happen.
    ///
    /// Matches on the full path, not the leaf filename - code-review finding: lanes intentionally
    /// support recursive Input folders, so two different files can legitimately share the same leaf
    /// name in different subfolders (e.g. two different shows' own "episode.mkv"). Matching on the
    /// bare name alone let a skip/remove/preset-override/reorder request silently target the wrong
    /// one of two same-named files, or fail ambiguously. Also simpler than the old approach: since
    /// the caller already has the file's real full path (from the UpNextItem it's acting on), no
    /// directory search is needed at all - just a direct File.Exists check.</summary>
    private static ResumeEntry? FindOrCreatePendingEntry(List<ResumeEntry> resumeState, string laneId, string fullName)
    {
        var existingAnyStatus = resumeState.FirstOrDefault(e =>
            e.LaneId == laneId &&
            string.Equals(e.FullName, fullName, StringComparison.OrdinalIgnoreCase));
        if (existingAnyStatus is not null)
        {
            return existingAnyStatus.Status == ResumeStatus.Pending ? existingAnyStatus : null;
        }

        if (!File.Exists(fullName)) return null;

        // Stamped here too, same as ConversionOrchestrator's own entry-creation - a file touched by
        // a queue-control action before the engine ever tracked it still gets a permanent queue
        // position from the moment it's first created, not a null Order that would otherwise sort
        // it ahead of every already-known file.
        var created = new ResumeEntry { LaneId = laneId, FullName = fullName, Status = ResumeStatus.Pending, CreatedByQueueEdit = true, Order = ResumeQueueOrder.NextOrder(resumeState) };
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
            var removed = resumeStore.Update(AppPaths.GetResumeFilePath(), resumeState => resumeState.RemoveAll(e =>
                e.LaneId == request.LaneId &&
                e.Status == ResumeStatus.Error &&
                string.Equals(e.FullName, request.FullName, StringComparison.OrdinalIgnoreCase)));

            return Results.Json(new { removed });
        });

        // Drag-to-reorder on the Monitor page's In Queue list - sets Order sequentially across the
        // WHOLE submitted list, spanning every lane, not just one - a not-yet-started file from any
        // lane can be prioritized ahead of any other lane's, matching the engine's own global
        // cross-lane picking order (RunOrchestrator). An item whose LaneId doesn't resolve to a
        // configured lane is skipped rather than failing the whole request. A file with no resume
        // entry yet (freshly scanned, never touched before) gets one created here (Status Pending)
        // so the order actually sticks - reusing the exact same FindOrCreatePendingEntry the skip
        // and preset-override endpoints below use.
        app.MapPost("/api/run/queue/reorder", (ReorderQueueRequest request, IConfigStore configStore, IResumeStateStore resumeStore) =>
        {
            var config = configStore.Load(AppPaths.GetConfigFilePath());

            resumeStore.Update(AppPaths.GetResumeFilePath(), resumeState =>
            {
                for (var i = 0; i < request.Items.Count; i++)
                {
                    var item = request.Items[i];
                    var lane = config.Lanes.FirstOrDefault(l => l.Id == item.LaneId);
                    if (lane is null) continue;

                    var entry = FindOrCreatePendingEntry(resumeState, lane.Id, item.FullName);
                    if (entry is not null) entry.Order = i;
                }
                return true;
            });

            return Results.Ok();
        });

        // "Skip" from the queue's 3-dot menu - the entry stays visible (dimmed) but
        // ConversionOrchestrator excludes it from what actually gets encoded. Persists until
        // toggled back off from the same menu, not a true one-shot skip (see ResumeEntry.Skipped).
        app.MapPost("/api/run/queue/skip", (SkipQueueEntryRequest request, IConfigStore configStore, IResumeStateStore resumeStore) =>
        {
            var config = configStore.Load(AppPaths.GetConfigFilePath());
            var lane = config.Lanes.FirstOrDefault(l => l.Id == request.LaneId);
            if (lane is null) return Results.NotFound();

            var found = resumeStore.Update(AppPaths.GetResumeFilePath(), resumeState =>
            {
                var entry = FindOrCreatePendingEntry(resumeState, lane.Id, request.FullName);
                if (entry is null) return false;
                entry.Skipped = request.Skipped;
                return true;
            });

            return found ? Results.Ok() : Results.NotFound();
        });

        // "Remove from queue" for a regular (non-Error) queue item - marks the entry Removed (and
        // implicitly Skipped, so it's never encoded) rather than deleting its resume entry. The
        // file itself is left untouched on disk. Deleting the entry outright was the original
        // implementation, but ComputeUpNext live-rescans each lane's Input folder on every poll -
        // with no tracked entry left behind, an untouched file was rediscovered as "new" and
        // reappeared within ~1.5s, making Remove look like it silently did nothing. Keeping a
        // Removed entry around (same FindOrCreatePendingEntry path skip/preset-override use) keeps
        // the file in trackedPaths so the rescan leaves it alone, while ComputeUpNext's own
        // pendingFiles filter hides it from the list.
        app.MapPost("/api/run/queue/remove", (RemoveQueueEntryRequest request, IConfigStore configStore, IResumeStateStore resumeStore) =>
        {
            var config = configStore.Load(AppPaths.GetConfigFilePath());
            var lane = config.Lanes.FirstOrDefault(l => l.Id == request.LaneId);
            if (lane is null) return Results.NotFound();

            var found = resumeStore.Update(AppPaths.GetResumeFilePath(), resumeState =>
            {
                var entry = FindOrCreatePendingEntry(resumeState, lane.Id, request.FullName);
                if (entry is null) return false;
                entry.Removed = true;
                entry.Skipped = true;
                return true;
            });

            return found ? Results.Ok() : Results.NotFound();
        });

        // Per-file preset override, set by clicking the preset name on a queue row - overrides the
        // lane's TvPreset/MoviePreset for this one file only. Passing null/empty Preset clears the
        // override back to the lane default.
        app.MapPost("/api/run/queue/preset-override", (PresetOverrideRequest request, IConfigStore configStore, IResumeStateStore resumeStore) =>
        {
            var config = configStore.Load(AppPaths.GetConfigFilePath());
            var lane = config.Lanes.FirstOrDefault(l => l.Id == request.LaneId);
            if (lane is null) return Results.NotFound();

            var found = resumeStore.Update(AppPaths.GetResumeFilePath(), resumeState =>
            {
                var entry = FindOrCreatePendingEntry(resumeState, lane.Id, request.FullName);
                if (entry is null) return false;
                entry.PresetOverride = string.IsNullOrWhiteSpace(request.Preset) ? null : request.Preset;
                return true;
            });

            return found ? Results.Ok() : Results.NotFound();
        });

        // "Move to lane" from the queue's 3-dot menu - changes which lane's Output/TvPreset/
        // MoviePreset/TvShowBasePath/MovieBasePath apply to this one file, without touching it on
        // disk or waiting for a re-scan (it stays sitting in its original lane's Input folder;
        // only the ResumeEntry's own LaneId changes). ConversionOrchestrator.PrepareLaneAsync and
        // ComputeUpNext above both know to stop treating this path as "new" for its old lane once
        // it's tracked under a different one, so it's never picked up (or shown) by both at once.
        app.MapPost("/api/run/queue/reassign-lane", (ReassignLaneRequest request, IConfigStore configStore, IResumeStateStore resumeStore) =>
        {
            var config = configStore.Load(AppPaths.GetConfigFilePath());
            var lane = config.Lanes.FirstOrDefault(l => l.Id == request.LaneId);
            if (lane is null) return Results.NotFound();
            var newLane = config.Lanes.FirstOrDefault(l => l.Id == request.NewLaneId);
            if (newLane is null) return Results.NotFound();

            var found = resumeStore.Update(AppPaths.GetResumeFilePath(), resumeState =>
            {
                var entry = FindOrCreatePendingEntry(resumeState, lane.Id, request.FullName);
                if (entry is null) return false;
                entry.LaneId = newLane.Id;
                return true;
            });

            return found ? Results.Ok() : Results.NotFound();
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
            var queueEtaText = ComputeQueueEtaText(upNext, runState);

            return Results.Json(new
            {
                isMonitoring = loopController.IsRunning,
                isStopping = loopController.IsStopping,
                isRunning = snapshot.IsRunning,
                isRenaming = snapshot.IsRenaming,
                runStartedUtc = snapshot.RunStartedUtc,
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
                upNext,
                queueEtaText
            });
        });
    }

    /// <summary>Projected wall-clock completion time for everything still left to process -
    /// whatever's actively encoding right now (ComputeUpNext deliberately excludes it, since it's
    /// not "up next", it's already running - GetCurrentFileRemaining fills that gap) plus
    /// everything still in upNext (skipped/error items excluded, same as the engine itself never
    /// processes them). Summed per-item using that item's own preset's throughput rate - a queue
    /// can mix presets with very different encode speed (e.g. HD/UHD lanes interleaved via
    /// cross-lane priority), so one blended rate for the whole queue would be less accurate than
    /// resolving each item separately. Returns the literal string "Estimating" if any item's rate
    /// can't be resolved yet (a genuinely fresh install, or a preset that's never appeared in a
    /// report or a live sample); otherwise an ISO 8601 timestamp for the client to format into a
    /// local date/time. Returns null if there's genuinely nothing left to estimate (idle, empty
    /// queue).</summary>
    private static string? ComputeQueueEtaText(IReadOnlyList<UpNextItem> upNext, CurrentRunStateService runState)
    {
        var remaining = upNext.Where(i => !i.IsSkipped && !i.IsError).ToList();
        var current = runState.GetCurrentFileRemaining();
        if (remaining.Count == 0 && current is null) return null;

        var totalMinutes = 0.0;

        if (current is { } c)
        {
            var currentRate = runState.GetRateGbPerMinute(c.PresetName);
            if (currentRate is null || currentRate <= 0) return "Estimating";
            totalMinutes += c.RemainingGb / currentRate.Value;
        }

        foreach (var item in remaining)
        {
            var rate = runState.GetRateGbPerMinute(item.Preset);
            if (rate is null || rate <= 0) return "Estimating";
            totalMinutes += item.SizeGb / rate.Value;
        }

        return DateTime.Now.AddMinutes(totalMinutes).ToString("o");
    }
}
