using Compressarr.Core.Config;

namespace Compressarr.Core.Routing;

public interface ICompanionFileService
{
    /// <summary>After a file has been routed into its destination folder, moves whatever else in
    /// its source folder belongs to it - subtitles, .nfo, artwork, etc - matched by shared base
    /// name (e.g. "Show.eng.srt" alongside "Show.mp4"). Runs immediately, every time, regardless
    /// of whether other videos are still sitting in the same shared/flat folder - a sibling
    /// episode's own companions are never touched, only this file's own. In Delete/Recycle mode
    /// they're moved into the destination; in Maintain mode they're copied and the source is left
    /// untouched. Once no video files remain in the source folder at all, it (and now-empty
    /// ancestors, up to but not including inputRoot) is removed too.
    ///
    /// Confirmed live: an earlier "wait until this is the only video left in the folder, then
    /// sweep everything at once" design meant a file's own companions didn't move until the WHOLE
    /// shared batch finished - or never, if a sibling was permanently skipped/removed from the
    /// queue - and even Stop Monitoring landing right after one file finished (before the next
    /// started) left that file's own companions stranded. Moving per-file, immediately, by name
    /// match rather than by "am I the last one" sidesteps all of that.</summary>
    void MoveCompanionFiles(
        string originalFileFullName,
        string originalFileDirectory,
        string destinationFolder,
        IReadOnlyList<string> vidTypes,
        DeleteAfterConvertMode deleteAfterConvert,
        string inputRoot);
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
        string destinationFolder,
        IReadOnlyList<string> vidTypes,
        DeleteAfterConvertMode deleteAfterConvert,
        string inputRoot)
    {
        if (!Directory.Exists(originalFileDirectory)) return;

        var extensions = vidTypes
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
            var destPath = Path.Combine(destinationFolder, Path.GetFileName(sibling));
            if (deleteAfterConvert == DeleteAfterConvertMode.Maintain)
            {
                File.Copy(sibling, destPath, overwrite: true);
            }
            else
            {
                if (File.Exists(destPath)) File.Delete(destPath);
                File.Move(sibling, destPath);
            }
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

        // Clear out anything still left (companions of the last file just moved above; anything
        // else here is orphaned, non-per-file content), then remove the now-empty source folder.
        var remaining = Directory.EnumerateFileSystemEntries(originalFileDirectory).ToList();
        foreach (var item in remaining)
        {
            if (Directory.Exists(item))
            {
                try { Directory.Delete(item, recursive: true); } catch { }
            }
            else
            {
                _trash.DeleteFile(item, deleteAfterConvert);
            }
        }
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
