using Compressarr.Core.Config;
using Compressarr.Core.Conversion;

namespace Compressarr.Core.Queue;

public enum QueueEditResult
{
    Ok,
    /// <summary>The request named a lane that isn't configured.</summary>
    UnknownLane,
    /// <summary>The lane is fine but the file no longer exists, or already has an entry that
    /// isn't Pending (e.g. a failed file), so there is nothing editable to change.</summary>
    NotFound,
    /// <summary>The lane the file was to be assigned to isn't configured.</summary>
    UnknownDestinationLane
}

/// <summary>
/// Everything the Monitor page does to the queue, behind one door: build the list it displays, and
/// apply the user's edits (reorder, skip, remove, preset override, clear an error). Wraps
/// <see cref="QueueRules"/> with the atomic load-mutate-save the web layer needs - every edit runs
/// under IResumeStateStore.Update's lock, so a click can never be lost to a concurrent engine save.
/// </summary>
public interface IQueueService
{
    /// <summary>The In Queue list in the order it will be processed. Any file nobody has tracked yet
    /// is locked into the queue (at the end, in arrival order) the first time this sees it, then the
    /// list is rebuilt so what's shown matches what is now permanent.</summary>
    List<UpNextItem> GetUpNext(CompressarrConfig config, QueueRunContext currentRun);

    /// <summary>Sets an explicit order across the WHOLE submitted list (every lane) - what drag-to-
    /// reorder and Move to top/bottom send. Items naming an unconfigured lane, or a file that no
    /// longer exists, are skipped rather than failing the request.</summary>
    void Reorder(CompressarrConfig config, IReadOnlyList<(string LaneId, string FullName)> items);

    QueueEditResult Skip(CompressarrConfig config, string laneId, string fullName, bool skipped);

    /// <summary>Marks the entry Removed (and Skipped, so it's never encoded) rather than deleting
    /// it, so the live rescan keeps treating the file as already tracked. The file itself is left
    /// on disk.</summary>
    QueueEditResult Remove(CompressarrConfig config, string laneId, string fullName);

    /// <summary>Overrides the preset for this one file; null/blank clears it back to the lane's.</summary>
    QueueEditResult OverridePreset(CompressarrConfig config, string laneId, string fullName, string? preset);

    /// <summary>Assigns the file to land in another lane's library (see ResumeEntry.DestinationLaneId): only
    /// the landing library changes, never the file's position, preset, Output folder or lane. Null/blank, or
    /// the file's own lane, clears the assignment. A disabled destination lane is fine - its paths still
    /// exist.</summary>
    QueueEditResult SetDestination(CompressarrConfig config, string laneId, string fullName, string? destinationLaneId);

    /// <summary>Clears a failed file's Error entry (only that status) so the next scan can pick it up
    /// fresh. Returns how many entries were removed.</summary>
    int RemoveError(string laneId, string fullName);
}

public sealed class QueueService : IQueueService
{
    private readonly IResumeStateStore _resumeStore;
    private readonly IPathExpander _pathExpander;
    private readonly IVideoFileScanner _scanner;

    public QueueService(IResumeStateStore resumeStore, IPathExpander pathExpander, IVideoFileScanner scanner)
    {
        _resumeStore = resumeStore;
        _pathExpander = pathExpander;
        _scanner = scanner;
    }

    public List<UpNextItem> GetUpNext(CompressarrConfig config, QueueRunContext currentRun)
    {
        var upNext = QueueRules.BuildUpNext(config, _resumeStore.Load(AppPaths.GetResumeFilePath()), _pathExpander, _scanner, currentRun, out var sawUntracked);
        if (!sawUntracked) return upNext;

        // A file nobody has tracked yet is ordered by live folder-scan position, which isn't
        // arrival order - a later arrival that enumerates earlier would display above one already
        // waiting. Lock every such file in right now, at the end of the queue, the first time
        // anything sees it (this poll, or the engine's own pass - whichever comes first), then
        // redisplay so what's shown matches what's now permanent.
        _resumeStore.Update(AppPaths.GetResumeFilePath(), resumeState =>
        {
            QueueRules.LockInVisibleOrder(config, resumeState, _pathExpander, _scanner);
            return true;
        });
        return QueueRules.BuildUpNext(config, _resumeStore.Load(AppPaths.GetResumeFilePath()), _pathExpander, _scanner, currentRun, out _);
    }

    public void Reorder(CompressarrConfig config, IReadOnlyList<(string LaneId, string FullName)> items)
    {
        _resumeStore.Update(AppPaths.GetResumeFilePath(), resumeState =>
        {
            QueueRules.ApplyExplicitOrder(config, resumeState, items);
            return true;
        });
    }

    public QueueEditResult Skip(CompressarrConfig config, string laneId, string fullName, bool skipped) =>
        Edit(config, laneId, fullName, entry => entry.Skipped = skipped);

    public QueueEditResult Remove(CompressarrConfig config, string laneId, string fullName) =>
        Edit(config, laneId, fullName, entry =>
        {
            entry.Removed = true;
            entry.Skipped = true;
        });

    public QueueEditResult OverridePreset(CompressarrConfig config, string laneId, string fullName, string? preset) =>
        Edit(config, laneId, fullName, entry => entry.PresetOverride = string.IsNullOrWhiteSpace(preset) ? null : preset);

    public QueueEditResult SetDestination(CompressarrConfig config, string laneId, string fullName, string? destinationLaneId)
    {
        var wanted = string.IsNullOrWhiteSpace(destinationLaneId) || destinationLaneId == laneId ? null : destinationLaneId;
        if (wanted is not null && config.Lanes.All(l => l.Id != wanted)) return QueueEditResult.UnknownDestinationLane;

        return Edit(config, laneId, fullName, entry => entry.DestinationLaneId = wanted);
    }

    public int RemoveError(string laneId, string fullName) =>
        _resumeStore.Update(AppPaths.GetResumeFilePath(), resumeState => resumeState.RemoveAll(e =>
            e.LaneId == laneId &&
            e.Status == ResumeStatus.Error &&
            string.Equals(e.FullName, fullName, StringComparison.OrdinalIgnoreCase)));

    /// <summary>The shared shape of every single-file edit: lock in everything currently visible
    /// first (so touching one untouched file can never let it jump ahead of the untouched files
    /// around it), then find-or-create this file's entry and apply the change.</summary>
    private QueueEditResult Edit(CompressarrConfig config, string laneId, string fullName, Action<ResumeEntry> apply)
    {
        var lane = config.Lanes.FirstOrDefault(l => l.Id == laneId);
        if (lane is null) return QueueEditResult.UnknownLane;

        var found = _resumeStore.Update(AppPaths.GetResumeFilePath(), resumeState =>
        {
            QueueRules.LockInVisibleOrder(config, resumeState, _pathExpander, _scanner);
            var entry = QueueRules.FindOrCreatePendingEntry(resumeState, lane.Id, fullName);
            if (entry is null) return false;
            apply(entry);
            return true;
        });

        return found ? QueueEditResult.Ok : QueueEditResult.NotFound;
    }
}
