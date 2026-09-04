using Compressarr.Core.Config;

namespace Compressarr.Core.Routing;

public interface ICompanionFileService
{
    /// <summary>After a file has been routed into its destination folder, handles whatever else
    /// was sitting alongside it in its original source folder (subtitles, .nfo, artwork, etc).
    /// Safety guard: only acts if originalFileFullName was the ONLY video file (per vidTypes) in
    /// its source folder — a shared/flat folder holding other not-yet-processed videos is left
    /// completely alone. In Delete/Recycle mode, siblings are moved into the destination and the
    /// source folder (plus now-empty ancestors, up to but not including inputRoot) is removed;
    /// in Maintain mode, siblings are copied and the source is left untouched.
    ///
    /// heldBackFullPaths - full paths of sibling videos in this same lane that are Skipped/Removed
    /// from the Monitor page's queue and so will never be picked up for real processing. These are
    /// excluded from the "other videos still present" guard (a permanently-held-back sibling should
    /// never block this file's own companion move indefinitely) but are also never themselves
    /// moved, deleted, or swept as part of any folder cleanup - they, and their OWN companion files
    /// (matched by shared base name, e.g. a held-back video's own .srt), stay exactly where they
    /// are, same as the "file stays on disk, untouched" guarantee Skip/Remove make on their own.</summary>
    void MoveCompanionFiles(
        string originalFileFullName,
        string originalFileDirectory,
        string destinationFolder,
        IReadOnlyList<string> vidTypes,
        DeleteAfterConvertMode deleteAfterConvert,
        string inputRoot,
        IReadOnlySet<string>? heldBackFullPaths = null);
}

/// <summary>Ported from Move-CompressarrCompanionFiles.</summary>
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
        string inputRoot,
        IReadOnlySet<string>? heldBackFullPaths = null)
    {
        if (!Directory.Exists(originalFileDirectory)) return;
        heldBackFullPaths ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // A held-back video's own companions (subtitles etc.) share its base name, differing only
        // by a language code/extension suffix - e.g. "Show.eng.srt" alongside "Show.mp4" - so they
        // must be protected right along with the video itself, not just the video file's own exact
        // path. Matched as "the video's own extension-stripped name, followed by a '.'" rather than
        // a plain prefix, so "S03E1"'s own held-back stem can't accidentally also swallow
        // "S03E10 - Magic.eng.srt" (both start with "S03E1", but only one is followed by ".").
        var heldBackStemPrefixes = heldBackFullPaths
            .Select(p => Path.GetFileNameWithoutExtension(p) + ".")
            .ToList();
        bool IsHeldBack(string fullPath) =>
            heldBackFullPaths.Contains(fullPath) ||
            heldBackStemPrefixes.Any(prefix => Path.GetFileName(fullPath).StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

        var extensions = vidTypes
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => "." + t.Trim().TrimStart('.'))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var otherVideos = Directory.EnumerateFiles(originalFileDirectory)
            .Where(f => extensions.Contains(Path.GetExtension(f)))
            .Where(f => !string.Equals(f, originalFileFullName, StringComparison.OrdinalIgnoreCase))
            .Where(f => !heldBackFullPaths.Contains(f))
            .ToList();

        if (otherVideos.Count > 0)
        {
            // Shared/flat folder - leave everything alone except the file that was actually
            // converted (already handled elsewhere).
            return;
        }

        var siblings = Directory.EnumerateFiles(originalFileDirectory)
            .Where(f => !string.Equals(f, originalFileFullName, StringComparison.OrdinalIgnoreCase))
            .Where(f => !IsHeldBack(f))
            .ToList();

        foreach (var sibling in siblings)
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
        // inspected (only originalFileFullName's own siblings were considered above).
        var inputRootFull = SafeFullPath(inputRoot);
        var originalDirFull = SafeFullPath(originalFileDirectory);
        if (inputRootFull is not null && string.Equals(originalDirFull, inputRootFull, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        // A held-back sibling (Skipped/Removed from the queue) still legitimately sits here -
        // the folder isn't really empty, and it must never be swept/deleted along with it.
        var remaining = Directory.EnumerateFileSystemEntries(originalFileDirectory).ToList();
        if (remaining.Any(IsHeldBack)) return;

        // Clear out anything still left, then remove the now-empty source folder itself.
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
