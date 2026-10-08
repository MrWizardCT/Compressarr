namespace Compressarr.Core.Diagnostics;

public sealed record FileSystemBrowseEntry(string Name, string FullPath);

public sealed record FileSystemBrowseResult(
    string? CurrentPath,
    string? ParentPath,
    IReadOnlyList<FileSystemBrowseEntry> Directories)
{
    /// <summary>The files in CurrentPath - only filled when the caller asked for files (the tool-path pickers).</summary>
    public IReadOnlyList<FileSystemBrowseEntry> Files { get; init; } = Array.Empty<FileSystemBrowseEntry>();
}

/// <summary>
/// Server-side directory browser for the web UI's folder-picker fields. A browser's native file/
/// folder picker only ever sees the viewing device's own filesystem (and modern browsers don't
/// expose an absolute path from it anyway) - since Compressarr's paths must resolve on the
/// machine actually running the app, and the web UI is explicitly reachable from other devices,
/// a client-side picker can't work here at all. This is the same approach Radarr/Sonarr use.
/// </summary>
public interface IFileSystemBrowser
{
    /// <summary>Lists subdirectories at path. A null/empty/whitespace path returns the top-level
    /// roots (drive letters on Windows, "/" on Unix) instead of a single directory's contents.</summary>
    /// <param name="includeFiles">Also list the files in the folder (executables only on Windows) - used to
    /// pick HandBrakeCLI / ffmpeg. A path that is itself a file starts the listing in its folder.</param>
    FileSystemBrowseResult Browse(string? path, bool includeFiles = false);
}

public sealed class FileSystemBrowser : IFileSystemBrowser
{
    public FileSystemBrowseResult Browse(string? path, bool includeFiles = false)
    {
        if (includeFiles && !string.IsNullOrWhiteSpace(path) && File.Exists(path))
        {
            path = Path.GetDirectoryName(path);
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            return BrowseRoots();
        }

        if (!Directory.Exists(path))
        {
            return BrowseRoots();
        }

        var directories = new List<FileSystemBrowseEntry>();
        try
        {
            foreach (var dir in Directory.EnumerateDirectories(path).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
            {
                // Skip anything we can't even stat (permission-denied junctions, etc.) rather than
                // letting one bad entry fail the whole listing.
                try
                {
                    var info = new DirectoryInfo(dir);
                    if (info.Attributes.HasFlag(FileAttributes.Hidden) || info.Attributes.HasFlag(FileAttributes.System)) continue;
                    directories.Add(new FileSystemBrowseEntry(info.Name, info.FullName));
                }
                catch { }
            }
        }
        catch
        {
            // Unreadable directory (permissions, disconnected network share, etc.) - return an
            // empty listing rather than throwing, so the browse modal shows "nothing here"
            // instead of erroring out.
        }

        var files = new List<FileSystemBrowseEntry>();
        if (includeFiles)
        {
            try
            {
                foreach (var file in Directory.EnumerateFiles(path).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
                {
                    try
                    {
                        var info = new FileInfo(file);
                        if (info.Attributes.HasFlag(FileAttributes.Hidden) || info.Attributes.HasFlag(FileAttributes.System)) continue;
                        // Windows tools are .exe; elsewhere an executable has no telltale extension.
                        if (OperatingSystem.IsWindows() && !string.Equals(info.Extension, ".exe", StringComparison.OrdinalIgnoreCase)) continue;
                        files.Add(new FileSystemBrowseEntry(info.Name, info.FullName));
                    }
                    catch { }
                }
            }
            catch { }
        }

        var parent = GetParentPath(path);
        return new FileSystemBrowseResult(path, parent, directories) { Files = files };
    }

    private static FileSystemBrowseResult BrowseRoots()
    {
        var roots = new List<FileSystemBrowseEntry>();

        if (OperatingSystem.IsWindows())
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                if (!drive.IsReady) continue;
                roots.Add(new FileSystemBrowseEntry(drive.Name, drive.RootDirectory.FullName));
            }
        }
        else
        {
            roots.Add(new FileSystemBrowseEntry("/", "/"));
        }

        return new FileSystemBrowseResult(null, null, roots);
    }

    private static string? GetParentPath(string path)
    {
        try
        {
            var parent = Directory.GetParent(path);
            return parent?.FullName;
        }
        catch
        {
            return null;
        }
    }
}
