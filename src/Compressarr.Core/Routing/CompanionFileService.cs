using Compressarr.Core.Config;

namespace Compressarr.Core.Routing;

public interface ICompanionFileService
{
    /// <summary>After a file has been routed into its destination folder, moves whatever else in
    /// its source folder belongs to it - subtitles, .nfo, artwork, etc - matched by shared base
    /// name (e.g. "Show.eng.srt" alongside "Show.mp4"). Runs immediately, every time, regardless
    /// of whether other videos are still sitting in the same shared/flat folder - a sibling
    /// episode's own companions are never touched, only this file's own.
    ///
    /// Of the stem-matched candidates, only ones whose extension is in companionExtensions are
    /// treated as wanted and actually moved/copied alongside the video. A stem-matched file whose
    /// extension ISN'T in that list, and anything left over once a folder's last video is gone
    /// (an orphaned companion, or genuinely unrelated content a user placed there), is disposed of
    /// per unmatchedCompanionAction - Maintain leaves it exactly where it is, Delete/Recycle
    /// removes it. deleteAfterConvert==Maintain overrides all of this and guarantees the source is
    /// never touched destructively at all, regardless of unmatchedCompanionAction - that's a
    /// stronger, absolute "hands off source" contract, same as it's always been for the video
    /// itself. The source folder (and now-empty ancestors, up to but not including inputRoot) is
    /// only removed once it's genuinely empty - if unmatchedCompanionAction leaves something
    /// behind on purpose, the folder is left alone rather than deleted out from under it.
    ///
    /// Confirmed live: an earlier "wait until this is the only video left in the folder, then
    /// sweep everything at once" design meant a file's own companions didn't move until the WHOLE
    /// shared batch finished - or never, if a sibling was permanently skipped/removed from the
    /// queue - and even Stop Monitoring landing right after one file finished (before the next
    /// started) left that file's own companions stranded. Moving per-file, immediately, by name
    /// match rather than by "am I the last one" sidesteps all of that. A later version of the
    /// final-sweep step also unconditionally deleted every remaining file once a folder had no
    /// videos left, regardless of what it actually was - risking deletion of non-companion content
    /// a user had placed there themselves. companionExtensions + unmatchedCompanionAction close
    /// that gap: only recognized companion types move automatically, and what happens to anything
    /// else is an explicit user choice, not an assumption.
    ///
    /// routedVideoDestPath is the video's own ACTUAL final destination path, not just a folder -
    /// each companion's own destination filename is derived from THIS path's stem, not the
    /// companion's original one, so a Rename-collision that gave the video "Movie (2).mkv" moves
    /// "Movie.en.srt" to "Movie (2).en.srt" alongside it - video and companions always stay a
    /// matched set. collisionMode applies the SAME Overwrite/Skip/Rename policy the video itself
    /// already gets (v2.1.3 code review finding #5 - a companion collision used to always
    /// overwrite, independent of what was actually configured); a Skip on one companion only
    /// leaves that one file where it is, it does not abort handling the rest.</summary>
    void MoveCompanionFiles(
        string originalFileFullName,
        string originalFileDirectory,
        string routedVideoDestPath,
        IReadOnlyList<string> vidTypes,
        DeleteAfterConvertMode deleteAfterConvert,
        string inputRoot,
        IReadOnlyList<string> companionExtensions,
        DeleteAfterConvertMode unmatchedCompanionAction,
        DestinationCollisionMode collisionMode = DestinationCollisionMode.Overwrite);
}

/// <summary>Ported from Move-CompressarrCompanionFiles; redesigned from a batch-at-the-end sweep
/// to an immediate per-file move (see ICompanionFileService's doc comment for why).</summary>
public sealed class CompanionFileService : ICompanionFileService
{
    private readonly ITrashService _trash;

    public CompanionFileService(ITrashService trash)
    {
        _trash = trash;
    }

    public void MoveCompanionFiles(
        string originalFileFullName,
        string originalFileDirectory,
        string routedVideoDestPath,
        IReadOnlyList<string> vidTypes,
        DeleteAfterConvertMode deleteAfterConvert,
        string inputRoot,
        IReadOnlyList<string> companionExtensions,
        DeleteAfterConvertMode unmatchedCompanionAction,
        DestinationCollisionMode collisionMode = DestinationCollisionMode.Overwrite)
    {
        if (!Directory.Exists(originalFileDirectory)) return;

        var destinationFolder = Path.GetDirectoryName(routedVideoDestPath)!;
        var videoDestStem = Path.GetFileNameWithoutExtension(routedVideoDestPath);

        var extensions = vidTypes
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => "." + t.Trim().TrimStart('.'))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var wantedExtensions = companionExtensions
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => "." + t.Trim().TrimStart('.'))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Matched as "the video's own extension-stripped name, followed by a '.'" rather than a
        // plain prefix, so "S03E1"'s own stem can't accidentally also claim "S03E10 - Magic.eng.srt"
        // (both start with "S03E1", but only one is followed by ".").
        var stemPrefix = Path.GetFileNameWithoutExtension(originalFileFullName) + ".";
        var ownCompanions = Directory.EnumerateFiles(originalFileDirectory)
            .Where(f => !string.Equals(f, originalFileFullName, StringComparison.OrdinalIgnoreCase))
            .Where(f => Path.GetFileName(f).StartsWith(stemPrefix, StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var sibling in ownCompanions)
        {
            var isWanted = wantedExtensions.Contains(Path.GetExtension(sibling));

            // The part of the companion's own name after its shared stem (e.g. "en.srt" out of
            // "Movie.en.srt") - re-attached to the video's own ACTUAL destination stem below, not
            // the companion's original one, so a Rename-collision on the video carries the
            // companion along under the exact same renamed stem.
            var companionSuffix = Path.GetFileName(sibling)[stemPrefix.Length..];
            var desiredCompanionName = videoDestStem + "." + companionSuffix;

            if (deleteAfterConvert == DeleteAfterConvertMode.Maintain)
            {
                // Maintain never touches the source, period - an unwanted stem-matched file is
                // left exactly where it is, regardless of unmatchedCompanionAction. Collision
                // policy still applies to the copy itself (Skip/Rename), since Maintain is only
                // about the SOURCE never being touched, not about blindly clobbering destination.
                if (isWanted)
                {
                    var desiredDestPath = Path.Combine(destinationFolder, desiredCompanionName);
                    try
                    {
                        var resolvedDestPath = FileRouter.ResolveCollision(desiredDestPath, collisionMode);
                        File.Copy(sibling, resolvedDestPath, overwrite: true);
                    }
                    catch (DestinationCollisionSkippedException)
                    {
                        // This one companion stays uncopied - every other companion still gets
                        // handled normally.
                    }
                }
                continue;
            }

            if (isWanted)
            {
                var desiredDestPath = Path.Combine(destinationFolder, desiredCompanionName);
                try
                {
                    var resolvedDestPath = FileRouter.ResolveCollision(desiredDestPath, collisionMode);
                    File.Move(sibling, resolvedDestPath, overwrite: true);
                }
                catch (DestinationCollisionSkippedException)
                {
                    // Configured to skip - this one companion just stays where it is; every other
                    // companion (and the folder-cleanup logic below) still gets handled normally.
                }
            }
            else if (unmatchedCompanionAction != DeleteAfterConvertMode.Maintain)
            {
                _trash.DeleteFile(sibling, unmatchedCompanionAction);
            }
            // else: unmatchedCompanionAction == Maintain means "leave in place" - do nothing.
        }

        if (deleteAfterConvert == DeleteAfterConvertMode.Maintain) return;

        // Never sweep or remove the lane's Input root itself - it's the lane's persistent watch
        // folder for the next run, and may still hold other unrelated content this function never
        // inspected (only originalFileFullName's own companions were considered above).
        var inputRootFull = SafeFullPath(inputRoot);
        var originalDirFull = SafeFullPath(originalFileDirectory);
        if (inputRootFull is not null && string.Equals(originalDirFull, inputRootFull, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        // A shared/flat folder still holding another video (still queued, skipped, whatever) isn't
        // really empty yet - leave it, and whatever's still sitting alongside that other video,
        // completely alone. Only once every video is gone does cleanup make sense.
        var stillHasVideo = Directory.EnumerateFiles(originalFileDirectory)
            .Any(f => extensions.Contains(Path.GetExtension(f)));
        if (stillHasVideo) return;

        // Whatever's left (an orphaned companion from an earlier call, or genuinely unrelated
        // content) is disposed of per unmatchedCompanionAction, same policy as an unwanted
        // stem-matched sibling above - Maintain leaves it in place, Delete/Recycle removes it.
        if (unmatchedCompanionAction != DeleteAfterConvertMode.Maintain)
        {
            var remaining = Directory.EnumerateFileSystemEntries(originalFileDirectory).ToList();
            foreach (var item in remaining)
            {
                if (Directory.Exists(item))
                {
                    _trash.DeleteFolder(item, unmatchedCompanionAction);
                }
                else
                {
                    _trash.DeleteFile(item, unmatchedCompanionAction);
                }
            }
        }

        // Only remove the folder itself (and cascade upward) once it's genuinely empty - if
        // unmatchedCompanionAction left something behind on purpose, this folder isn't done yet.
        if (Directory.EnumerateFileSystemEntries(originalFileDirectory).Any()) return;

        _trash.DeleteFolder(originalFileDirectory, deleteAfterConvert);

        // Cascade upward: remove each parent folder in turn as long as it's now completely
        // empty, stopping the moment we reach a non-empty folder or the lane's Input root.
        if (inputRootFull is not null)
        {
            var current = Path.GetDirectoryName(originalFileDirectory);
            while (!string.IsNullOrEmpty(current) && Directory.Exists(current) &&
                   !string.Equals(current, inputRootFull, StringComparison.OrdinalIgnoreCase))
            {
                if (Directory.EnumerateFileSystemEntries(current).Any()) break;

                _trash.DeleteFolder(current, deleteAfterConvert);
                current = Path.GetDirectoryName(current);
            }
        }
    }

    private static string? SafeFullPath(string path)
    {
        try { return Directory.Exists(path) ? Path.GetFullPath(path) : null; }
        catch { return null; }
    }
}
