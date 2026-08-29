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
    /// in Maintain mode, siblings are copied and the source is left untouched.</summary>
    void MoveCompanionFiles(
        string originalFileFullName,
        string originalFileDirectory,
        string destinationFolder,
        IReadOnlyList<string> vidTypes,
        DeleteAfterConvertMode deleteAfterConvert,
        string inputRoot);
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
        string inputRoot)
    {
        if (!Directory.Exists(originalFileDirectory)) return;

        var extensions = vidTypes
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => "." + t.Trim().TrimStart('.'))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var otherVideos = Directory.EnumerateFiles(originalFileDirectory)
            .Where(f => extensions.Contains(Path.GetExtension(f)))
            .Where(f => !string.Equals(f, originalFileFullName, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (otherVideos.Count > 0)
        {
            // Shared/flat folder - leave everything alone except the file that was actually
            // converted (already handled elsewhere).
            return;
        }

        var siblings = Directory.EnumerateFiles(originalFileDirectory)
            .Where(f => !string.Equals(f, originalFileFullName, StringComparison.OrdinalIgnoreCase))
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

        // Clear out anything still left, then remove the now-empty source folder itself.
        foreach (var item in Directory.EnumerateFileSystemEntries(originalFileDirectory))
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
