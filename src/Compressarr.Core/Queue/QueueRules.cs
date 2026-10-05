using Compressarr.Core.Config;
using Compressarr.Core.Conversion;

namespace Compressarr.Core.Queue;

/// <summary>One row of the Monitor page's In Queue list. Lives in Core (not the web project) so
/// the queue rules that produce it can be tested and reused without referencing the web layer.
/// FullName (the file's real absolute path) is what every queue-mutation request identifies a row
/// by - FileName (the bare leaf name, kept for display only) is NOT unique: lanes intentionally
/// support recursive Input folders, so two different files can legitimately share the same leaf
/// name in different subfolders (e.g. two different shows' own "episode.mkv"). Code-review finding:
/// matching on FileName alone let a skip/remove/preset-override/reorder request silently target the
/// wrong one of two same-named files, or fail ambiguously.</summary>
public sealed record UpNextItem(string LaneId, string LaneDisplayName, string FileName, string FullName, double SizeGb, string? Preset, bool IsResumed, bool IsError, bool IsSkipped, bool IsCustomPreset, bool IsFileBotUnmatched);

/// <summary>What the queue display needs to know about the run currently in progress: the full path
/// of the file being encoded (excluded from the waiting list) and, per lane, whether that lane
/// genuinely had interrupted work when its own most recent pass began.</summary>
public sealed record QueueRunContext(string? CurrentFileFullName, IReadOnlyDictionary<string, bool> LaneIsResumedById);

/// <summary>
/// The rules for the queue - which files are tracked, in what order, and what the Monitor page
/// shows - in ONE place. These used to be implemented separately in the web endpoints, the engine's
/// lane preparation and its next-file picker, each with a comment saying it "must never disagree"
/// with the others; this is what makes that true by construction.
///
/// Pure rules over the resume state: no HTTP, no persistence (QueueService adds atomic load/save
/// around these for the web layer; the engine calls them directly on its in-memory state).
/// </summary>
public static class QueueRules
{
    private const long BytesPerGb = 1024L * 1024L * 1024L;

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
    public static ResumeEntry? FindOrCreatePendingEntry(List<ResumeEntry> resumeState, string laneId, string fullName)
    {
        var existingAnyStatus = resumeState.FirstOrDefault(e =>
            e.LaneId == laneId &&
            string.Equals(e.FullName, fullName, StringComparison.OrdinalIgnoreCase));
        if (existingAnyStatus is not null)
        {
            return existingAnyStatus.Status == ResumeStatus.Pending ? existingAnyStatus : null;
        }

        if (!File.Exists(fullName)) return null;

        // Stamped here too, same as the engine's own entry-creation - a file touched by a
        // queue-control action before the engine ever tracked it still gets a permanent queue
        // position from the moment it's first created, not a null Order that would otherwise sort
        // it ahead of every already-known file.
        var created = new ResumeEntry { LaneId = laneId, FullName = fullName, Status = ResumeStatus.Pending, CreatedByQueueEdit = true, Order = ResumeQueueOrder.NextOrder(resumeState) };
        resumeState.Add(created);
        return created;
    }

    /// <summary>Ensures every file currently visible as part of the "New"/unlocked queue - across
    /// EVERY enabled lane, not just the one a single-file request is about to touch - has a real,
    /// permanently Order-stamped Pending entry, before that request's own FindOrCreatePendingEntry
    /// call runs.
    ///
    /// Real gap: a single-file queue-control action (skip/preset-override/remove) only ever
    /// created/stamped an Order for the ONE file it targeted - every other genuinely-new file
    /// sitting around it kept sorting by live natural scan order (Order == null, i.e. sorts last).
    /// The moment the touched file got a real, low Order while its siblings stayed at "last", it
    /// visibly jumped ahead of files that were ahead of it moments earlier - breaking "once files
    /// appear in the queue, their order is locked" any time Monitor wasn't actively mid-pass to
    /// backfill everyone at once (BackfillMissingOrder only ever runs once per RunOrchestrator
    /// pass, not on every queue-control request).
    ///
    /// Mirrors BuildUpNext's own per-lane scan/exclusion logic (trackedPaths, natural scan order)
    /// so the set of files this locks in - and the order it locks them into - always matches what's
    /// already on screen.</summary>
    public static void LockInVisibleOrder(CompressarrConfig config, List<ResumeEntry> resumeState, IPathExpander pathExpander, IVideoFileScanner scanner)
    {
        var nextOrder = ResumeQueueOrder.NextOrder(resumeState);

        foreach (var lane in config.Lanes)
        {
            if (!lane.Enabled) continue;

            var inputPath = pathExpander.Expand(lane.Input);
            if (string.IsNullOrWhiteSpace(inputPath) || !Directory.Exists(inputPath)) continue;

            var trackedPaths = resumeState.Where(e => e.LaneId == lane.Id).Select(e => e.FullName).ToHashSet(StringComparer.OrdinalIgnoreCase);

            List<FileInfo> scannedFiles;
            try
            {
                scannedFiles = scanner.FindVideoFiles(inputPath, config.Processing.VidTypes, config.Processing.MinSizeBytes, config.Processing.Limit).ToList();
            }
            catch
            {
                // An unreachable Input path here just means this lane's own files stay unlocked
                // until the next request that can actually scan it - never worth failing the
                // caller's own single-file action over.
                continue;
            }

            var naturalIndex = scannedFiles
                .Select((f, idx) => (f.FullName, idx))
                .ToDictionary(x => x.FullName, x => x.idx, StringComparer.OrdinalIgnoreCase);

            var unlocked = resumeState
                .Where(e => e.LaneId == lane.Id && e.Status == ResumeStatus.Pending && !e.Order.HasValue)
                .Select(e => (Natural: naturalIndex.TryGetValue(e.FullName, out var idx) ? idx : int.MaxValue, Entry: (ResumeEntry?)e, NewFullName: (string?)null))
                .Concat(scannedFiles
                    .Where(f => !trackedPaths.Contains(f.FullName))
                    .Select(f => (Natural: naturalIndex.TryGetValue(f.FullName, out var idx) ? idx : int.MaxValue, Entry: (ResumeEntry?)null, NewFullName: (string?)f.FullName)))
                .OrderBy(x => x.Natural);

            foreach (var item in unlocked)
            {
                if (item.Entry is not null)
                {
                    item.Entry.Order = nextOrder++;
                }
                else
                {
                    // CreatedByQueueEdit: this is bookkeeping for a brand-new file, not interrupted
                    // work - without it the Monitor page would label it "Resumed" whenever its lane
                    // already has other tracked entries.
                    resumeState.Add(new ResumeEntry { LaneId = lane.Id, FullName = item.NewFullName!, Status = ResumeStatus.Pending, CreatedByQueueEdit = true, Order = nextOrder++ });
                }
            }
        }
    }

    /// <summary>Computes the files waiting to be compressed across every enabled lane, for the
    /// Monitor page's "Up Next" section - mirrors the engine's own lane-preparation and global
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
    /// order - using the identical three-level tie-break the engine's own global picking loop uses
    /// (explicit Order, then each file's own lane's position in config.Lanes, then that file's
    /// natural scan order within its own lane), so the displayed queue and the actual processing
    /// order can never disagree. Error entries are surfaced too (their own bucket, IsError=true,
    /// appended after every lane's Pending items) purely for visibility - they are never fed into
    /// the engine's own Pending filter, so listing them here doesn't change what gets processed.
    ///
    /// sawUntracked is true when at least one listed file has no resume entry yet - the caller
    /// (QueueService) then locks those in and recomputes, so a file's place is permanent from the
    /// first time anything sees it.</summary>
    public static List<UpNextItem> BuildUpNext(
        CompressarrConfig config,
        List<ResumeEntry> resumeState,
        IPathExpander pathExpander,
        IVideoFileScanner scanner,
        QueueRunContext currentRun,
        out bool sawUntracked)
    {
        sawUntracked = false;

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

            // One single natural-order scan drives the fallback ordering for every file that has no
            // EXPLICIT user-set Order (a real drag-reorder on the Monitor page) - both genuinely
            // untracked ("New") files and Pending entries that only exist because a skip/preset-
            // override/remove action touched them (FindOrCreatePendingEntry, ResumeEntry.
            // CreatedByQueueEdit) but were never actually dragged.
            var scannedFiles = scanner.FindVideoFiles(inputPath, config.Processing.VidTypes, config.Processing.MinSizeBytes, config.Processing.Limit)
                .Where(f => !trackedPaths.Contains(f.FullName) || pendingByPath.ContainsKey(f.FullName))
                .ToList();
            var naturalIndex = scannedFiles
                .Select((f, idx) => (f.FullName, idx))
                .ToDictionary(x => x.FullName, x => x.idx, StringComparer.OrdinalIgnoreCase);

            var files = scannedFiles.Where(f => !(pendingByPath.TryGetValue(f.FullName, out var e) && e.Removed)).ToList();

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
                // Matched on FullName alone, not laneDisplayName+leaf-name - leaf FileName is NOT
                // unique (lanes intentionally support recursive Input folders, so two different
                // files can share the same leaf name in different subfolders). Matching on the
                // weaker pair could wrongly exclude a second, not-currently-encoding file that
                // happens to share the active file's leaf name. FullName is globally unique, so it
                // can't be fooled by a name collision.
                if (string.Equals(file.FullName, currentRun.CurrentFileFullName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                pendingByPath.TryGetValue(file.FullName, out var entry);
                if (entry is null) sawUntracked = true;

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
                candidates.Add((item, laneOrderIndex, entry?.Order, naturalIndex[file.FullName]));
            }

            var errorEntries = resumeState.Where(e => e.LaneId == lane.Id && e.Status == ResumeStatus.Error).ToList();
            foreach (var entry in errorEntries)
            {
                var fileInfo = new FileInfo(entry.FullName);
                // A dead Error entry (source file gone) is cleaned up automatically by the engine
                // the next time this lane's pass actually runs - just skip showing a ghost row for
                // it here in the meantime, rather than duplicating that cleanup in a read-only
                // status endpoint.
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
}
