using System.Text.Json;

namespace Compressarr.Core.Conversion;

public enum ResumeStatus
{
    Pending,
    Completed,
    Error,

    /// <summary>The encode itself succeeded, but routing the result into its TV/Movie base path
    /// failed (unreachable network drive, disk full, permission error, etc.) - the encoded file is
    /// sitting wherever HandBrake wrote it (EncodedFilePath), not lost, and ConversionOrchestrator
    /// retries just the move (no re-encode) at the start of this lane's next pass.</summary>
    MoveFailed,

    /// <summary>The video itself finished and was routed to its final destination successfully,
    /// but moving its companion files (subtitles, .nfo, artwork) alongside it failed - the
    /// companions are still sitting in PendingCompanionSourceDirectory, stranded. Unlike
    /// MoveFailed, the video is fully done; ConversionOrchestrator retries just the companion move
    /// (and, once that succeeds, the folder-emptiness cleanup that was skipped the first time) at
    /// the start of this lane's next pass. A real gap found via code review: without a status to
    /// track this, the entry was left Completed and nothing ever retried the stranded companions.</summary>
    CompanionMoveFailed,

    /// <summary>The video (and, if any, its companions) are both fully done and correctly filed -
    /// only the source folder's own cleanup remains, deferred because the Sonarr/Radarr rescan
    /// this entry's pass triggered was never positively confirmed complete (see
    /// ArrRescanOutcome.SafeToCleanUp - a timeout, a "failed" status, or a cancelled wait). Unlike
    /// MoveFailed/CompanionMoveFailed, nothing about the video or its companions needs redoing;
    /// ConversionOrchestrator retries just the *arr confirmation (and, once THAT succeeds, the
    /// folder cleanup that was skipped the first time) at the start of this lane's next pass. A
    /// real gap found via code review: without a status to track this, the entry was left
    /// Completed and nothing ever came back to finish the deferred cleanup, potentially leaving an
    /// empty source folder behind forever.</summary>
    CleanupPending
}

public sealed class ResumeEntry
{
    public required string LaneId { get; set; }
    public required string FullName { get; set; }
    public ResumeStatus Status { get; set; }

    /// <summary>Only set (and only meaningful) when Status is MoveFailed - where the already-
    /// encoded file actually sits (in the lane's Output folder, not FullName's original Input
    /// location) so a later retry can find and route it without re-encoding. Deliberately a
    /// collision-safe staging name (not a human-readable one) for as long as MoveFailed persists -
    /// see ConversionOrchestrator's own notes on why a deterministic name is never left sitting in
    /// Output unrouted.</summary>
    public string? EncodedFilePath { get; set; }

    /// <summary>Only set (and only meaningful) when Status is MoveFailed - the clean, human-
    /// readable filename (e.g. "Movie.mkv") EncodedFilePath's own file should be classified and
    /// renamed/routed under once the retry succeeds. Kept separate from EncodedFilePath itself
    /// because that path stays under a collision-safe staging name while MoveFailed persists, so
    /// nothing downstream can derive the real title/episode info from it any more.</summary>
    public string? EncodedFileDesiredName { get; set; }

    /// <summary>The exception message from this entry's most recent failed move-retry attempt -
    /// lets ConversionOrchestrator tell "still the same unresolved problem as last poll" apart
    /// from "something new/different just happened." Real gap found live: an offline destination
    /// (a network share down overnight) got retried every single poll, and every one of those
    /// retries logged at Error severity - which, combined with the "don't keep an empty/error-free
    /// pass's log" cleanup, meant a KEPT log file every poll for as long as the outage lasted, the
    /// same file-proliferation problem all over again just gated on "error" instead of "empty."
    /// Null once the entry isn't MoveFailed any more (succeeded, or removed).</summary>
    public string? LastRetryFailureMessage { get; set; }

    /// <summary>Only set (and only meaningful) when Status is CompanionMoveFailed or
    /// CleanupPending - the video's own original source folder. For CompanionMoveFailed, this is
    /// where the stranded companion files are still sitting, so a later retry can find and move
    /// them without needing the video's own path (which may already be gone by then). For
    /// CleanupPending, this is simply the folder a later retry's own CleanUpEmptySourceFolder call
    /// targets, once a fresh *arr confirmation says it's actually safe to.</summary>
    public string? PendingCompanionSourceDirectory { get; set; }

    /// <summary>Only set (and only meaningful) when Status is CompanionMoveFailed or
    /// CleanupPending - the video's own actual final destination path. For CompanionMoveFailed,
    /// needed to derive each companion's destination filename from the video's real (possibly
    /// collision-renamed) stem - same reasoning as ICompanionFileService.MoveCompanionFiles' own
    /// routedVideoDestPath parameter. For CleanupPending, needed only for its filename (to
    /// re-derive the *arr lookup name and TV/movie classification for a fresh rescan attempt).</summary>
    public string? PendingCompanionVideoDestPath { get; set; }

    /// <summary>User-set queue position within this lane's Pending entries, lower first - drives
    /// drag-to-reorder on the Monitor page's In Queue list. Entries without an explicit Order
    /// (existing/untouched files) sort after any that have one, in their original order.</summary>
    public int? Order { get; set; }

    /// <summary>User-set "skip this pass" - a Skipped Pending entry stays visible in the queue
    /// (dimmed) but ConversionOrchestrator excludes it from what actually gets encoded. Persists
    /// until explicitly un-skipped - not a true one-shot "just this pass," since there's no
    /// reliable single moment to auto-clear it at (a monitoring pass has no natural end event the
    /// resume file can hook into) - the user toggles it back on from the same queue row.</summary>
    public bool Skipped { get; set; }

    /// <summary>User-set preset for this specific file, overriding the lane's TvPreset/MoviePreset
    /// for this one entry only. Null/empty means "use the lane default" (the normal behavior).</summary>
    public string? PresetOverride { get; set; }

    /// <summary>User-set "Remove from queue" - unlike Skipped, a Removed entry is dropped from the
    /// Monitor page's In Queue list entirely, not just dimmed. Confirmed live: the queue's Input
    /// folder is live-rescanned on every poll, so simply deleting the resume entry (the original
    /// implementation) let an untouched file get rediscovered and reappear within ~1.5s, making
    /// Remove look like it did nothing. Setting this flag instead - and having it also imply
    /// Skipped, so ConversionOrchestrator never encodes it - keeps the file tracked (so the live
    /// rescan skips it, same as any other tracked path) while hiding it from the UI, with the same
    /// "stays until explicitly toggled back" reasoning as Skipped.</summary>
    public bool Removed { get; set; }

    /// <summary>Set when this entry was created purely as bookkeeping for a queue-control action
    /// (reorder/skip/preset-override/remove) on a file the current pass hadn't tracked yet, rather
    /// than by ConversionOrchestrator's own real resume-from-interrupted-run bookkeeping. Exists
    /// solely so the Monitor page's New/Resumed badge isn't fooled by it - without this, touching
    /// even one fresh file's queue metadata makes ComputeUpNext's "does this lane have pending work
    /// left over from before" heuristic (any Pending entry at all) fire, mislabeling every other
    /// file in that lane "Resumed" too, and reordering the whole queue (which touches every file at
    /// once) made literally the entire lane show "Resumed". Confirmed live on the real running app.</summary>
    public bool CreatedByQueueEdit { get; set; }

    /// <summary>Set once, at entry-creation time, when the optional FileBot pre-processing pass
    /// (IFileBotRunner) ran for this lane but left this specific file's name/location untouched -
    /// treated as "FileBot didn't/couldn't confidently match this one." Drives the Monitor page's
    /// "Unmatched" queue badge. Never recomputed for an already-tracked entry.</summary>
    public bool FileBotUnmatched { get; set; }
}

public interface IResumeStateStore
{
    List<ResumeEntry> Load(string path);
    void Save(List<ResumeEntry> state, string path);
    void DeleteIfComplete(List<ResumeEntry> state, string path);

    /// <summary>Atomically loads resume state, applies mutate, and saves the result - all under
    /// one lock. Mirrors IConfigStore.Update for the exact same reason: the web UI's queue-control
    /// endpoints (reorder/skip/preset-override/remove) can fire concurrently with each other and
    /// with ConversionOrchestrator's own in-flight pass, and separate Load()+Save() calls race on
    /// the read - each starts from the same pre-mutation snapshot, so whichever Save happens last
    /// silently clobbers the other's change (a lost update), even though neither call throws.
    /// Confirmed live: a user's reorder/skip/preset-override change was wiped out the moment the
    /// in-flight file being encoded finished and ConversionOrchestrator did its own next Save.</summary>
    T Update<T>(string path, Func<List<ResumeEntry>, T> mutate);
}

/// <summary>Ported from Import-CompressarrResumeState/Export-CompressarrResumeState. Written to
/// disk after every single file processed (not batched), so it's always accurate to the file
/// currently in flight — an interrupted run resumes mid-lane instead of rescanning already
/// attempted files.</summary>
public sealed class JsonResumeStateStore : IResumeStateStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    private readonly SemaphoreSlim _lock = new(1, 1);

    public List<ResumeEntry> Load(string path)
    {
        if (!File.Exists(path)) return new List<ResumeEntry>();

        var raw = File.ReadAllText(path);
        return JsonSerializer.Deserialize<List<ResumeEntry>>(raw, Options) ?? new List<ResumeEntry>();
    }

    public void Save(List<ResumeEntry> state, string path)
    {
        var folder = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(folder) && !Directory.Exists(folder))
        {
            Directory.CreateDirectory(folder);
        }

        File.WriteAllText(path, JsonSerializer.Serialize(state, Options));
    }

    public void DeleteIfComplete(List<ResumeEntry> state, string path)
    {
        var hasOutstanding = state.Any(e => e.Status is ResumeStatus.Pending or ResumeStatus.Error or ResumeStatus.MoveFailed or ResumeStatus.CompanionMoveFailed or ResumeStatus.CleanupPending);
        if (!hasOutstanding && File.Exists(path))
        {
            File.Delete(path);
        }
    }

    public T Update<T>(string path, Func<List<ResumeEntry>, T> mutate)
    {
        _lock.Wait();
        try
        {
            var state = Load(path);
            var result = mutate(state);
            Save(state, path);
            return result;
        }
        finally
        {
            _lock.Release();
        }
    }
}
