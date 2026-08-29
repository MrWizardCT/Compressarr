namespace Compressarr.Core.Conversion;

public interface IVideoFileScanner
{
    /// <summary>Recursively finds video files under inputPath matching vidTypes (extensions
    /// without a leading dot), above minSizeBytes, capped at limit results.</summary>
    IReadOnlyList<FileInfo> FindVideoFiles(string inputPath, IReadOnlyList<string> vidTypes, long minSizeBytes, int limit);
}

/// <summary>Ported from Find-CompressarrVideoFiles.</summary>
public sealed class VideoFileScanner : IVideoFileScanner
{
    public IReadOnlyList<FileInfo> FindVideoFiles(string inputPath, IReadOnlyList<string> vidTypes, long minSizeBytes, int limit)
    {
        if (!Directory.Exists(inputPath)) return Array.Empty<FileInfo>();

        var extensions = vidTypes
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => "." + t.Trim().TrimStart('.'))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var effectiveLimit = limit > 0 ? limit : int.MaxValue;

        var results = new List<FileInfo>();
        foreach (var path in EnumerateFilesSafe(inputPath))
        {
            var file = new FileInfo(path);
            if (!extensions.Contains(file.Extension)) continue;
            if (file.Length <= minSizeBytes) continue;

            results.Add(file);
            if (results.Count >= effectiveLimit) break;
        }

        return results;
    }

    private static IEnumerable<string> EnumerateFilesSafe(string root)
    {
        // Get-ChildItem -Recurse -ErrorAction SilentlyContinue tolerates unreadable
        // subdirectories mid-walk rather than aborting the whole scan; Directory.EnumerateFiles
        // throws on the first one, so this walks the tree manually to match that behavior.
        var stack = new Stack<string>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            var dir = stack.Pop();

            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(dir);
            }
            catch (UnauthorizedAccessException) { continue; }
            catch (IOException) { continue; }

            foreach (var file in files) yield return file;

            IEnumerable<string> subDirs;
            try
            {
                subDirs = Directory.EnumerateDirectories(dir);
            }
            catch (UnauthorizedAccessException) { continue; }
            catch (IOException) { continue; }

            foreach (var subDir in subDirs) stack.Push(subDir);
        }
    }
}
