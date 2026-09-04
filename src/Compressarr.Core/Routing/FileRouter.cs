using System.Text.RegularExpressions;
using Compressarr.Core.Config;
using Compressarr.Core.Conversion;

namespace Compressarr.Core.Routing;

/// <summary>Thrown by FileRouter when DestinationCollisionMode.Skip is configured and the
/// destination already exists - deliberately distinct from a real move failure (unreachable path,
/// disk full, permission error) so ConversionOrchestrator can treat it as a warning, not an error:
/// the file wasn't moved because that's exactly what was configured, not because something went
/// wrong.</summary>
public sealed class DestinationCollisionSkippedException : Exception
{
    public DestinationCollisionSkippedException(string destPath)
        : base($"Destination already exists, move skipped: '{destPath}'")
    {
    }
}

public interface IFileRouter
{
    /// <summary>Dispatches on the file's auto-detected content type to the matching base path -
    /// a lane's TvShowBasePath for TV episodes, MovieBasePath for everything else. Returns null
    /// (no-op) if moveFiles is false. Throws DestinationCollisionSkippedException if the
    /// destination already exists and collisionMode is Skip.</summary>
    string? RouteFile(string fileName, bool isTv, string tvShowBasePath, string movieBasePath, bool moveFiles,
        DestinationCollisionMode collisionMode = DestinationCollisionMode.Overwrite);
}

/// <summary>Ported from Move-CompressarrMovieFile/Move-CompressarrTVFile/Move-CompressarrRoutedFile.</summary>
public sealed partial class FileRouter : IFileRouter
{
    [GeneratedRegex(@"\(([^)]+)\)")]
    private static partial Regex ParenContentPattern();

    [GeneratedRegex(@"(\d{4})( ?- ?)?(\d{4})?")]
    private static partial Regex YearRangePattern();

    public string? RouteFile(string fileName, bool isTv, string tvShowBasePath, string movieBasePath, bool moveFiles,
        DestinationCollisionMode collisionMode = DestinationCollisionMode.Overwrite)
    {
        if (!moveFiles) return null;

        return isTv ? MoveTvFile(fileName, tvShowBasePath, collisionMode) : MoveMovieFile(fileName, movieBasePath, collisionMode);
    }

    /// <summary>Resolves destPath against an existing file at that path per collisionMode: unchanged
    /// for Overwrite (the caller's File.Move(overwrite: true) handles it) or when nothing's there
    /// yet, a uniquified sibling path for Rename, or throws for Skip. A pure static function so
    /// collision behavior is testable independent of an actual file move.</summary>
    internal static string ResolveCollision(string destPath, DestinationCollisionMode collisionMode)
    {
        if (!File.Exists(destPath)) return destPath;

        return collisionMode switch
        {
            DestinationCollisionMode.Skip => throw new DestinationCollisionSkippedException(destPath),
            DestinationCollisionMode.Rename => MakeUniquePath(destPath),
            _ => destPath
        };
    }

    private static string MakeUniquePath(string destPath)
    {
        var dir = Path.GetDirectoryName(destPath) ?? "";
        var nameWithoutExt = Path.GetFileNameWithoutExtension(destPath);
        var ext = Path.GetExtension(destPath);

        var n = 2;
        string candidate;
        do
        {
            candidate = Path.Combine(dir, $"{nameWithoutExt} ({n}){ext}");
            n++;
        } while (File.Exists(candidate));

        return candidate;
    }

    public string? MoveTvFile(string fileName, string outputBase, DestinationCollisionMode collisionMode = DestinationCollisionMode.Overwrite)
    {
        if (string.IsNullOrWhiteSpace(outputBase))
        {
            throw new InvalidOperationException($"Compressarr: cannot move '{fileName}' - TV Show base path is not configured for this lane.");
        }

        var info = ContentClassifier.GetEpisodeInfo(fileName);
        if (!info.HasSeasonAndEpisode)
        {
            return null;
        }

        var destFolder = Path.Combine(outputBase, info.ShowName, "Season " + info.Season);
        Directory.CreateDirectory(destFolder);
        var destPath = ResolveCollision(Path.Combine(destFolder, info.EpisodeFileName), collisionMode);

        File.Move(fileName, destPath, overwrite: true);
        return destPath;
    }

    public string? MoveMovieFile(string fileName, string outputBase, DestinationCollisionMode collisionMode = DestinationCollisionMode.Overwrite)
    {
        if (string.IsNullOrWhiteSpace(outputBase))
        {
            throw new InvalidOperationException($"Compressarr: cannot move '{fileName}' - Movie base path is not configured for this lane.");
        }

        Directory.CreateDirectory(outputBase);

        var leaf = Path.GetFileName(fileName);
        var movieFolderName = ContentClassifier.GetMovieFolderName(leaf);
        var yearMatch = ParenContentPattern().Match(fileName);
        var movieYear = yearMatch.Success ? yearMatch.Groups[1].Value : null;

        var movieFolders = Directory.Exists(outputBase)
            ? Directory.EnumerateDirectories(outputBase, "*", SearchOption.AllDirectories)
                .Where(d => Path.GetFileName(d).Contains("movie", StringComparison.OrdinalIgnoreCase))
                .OrderBy(d => d, StringComparer.OrdinalIgnoreCase)
                .ToList()
            : new List<string>();

        string MoveIntoBucket(string bucketFolder)
        {
            var movieDestFolder = Path.Combine(bucketFolder, movieFolderName);
            Directory.CreateDirectory(movieDestFolder);
            var destPath = ResolveCollision(Path.Combine(movieDestFolder, leaf), collisionMode);
            File.Move(fileName, destPath, overwrite: true);
            return destPath;
        }

        if (movieFolders.Count == 1)
        {
            return MoveIntoBucket(movieFolders[0]);
        }

        if (movieYear is not null)
        {
            foreach (var movieFolder in movieFolders)
            {
                var folderName = Path.GetFileName(movieFolder);
                var yearParts = YearRangePattern().Split(folderName);
                var minYear = yearParts.Length > 1 ? yearParts[1] : null;
                var maxYear = yearParts.Length > 3 ? yearParts[3] : null;

                var isMatch = string.Equals(folderName, movieYear, StringComparison.OrdinalIgnoreCase)
                    || movieYear == minYear
                    || (!string.IsNullOrEmpty(minYear) && !string.IsNullOrEmpty(maxYear)
                        && string.CompareOrdinal(movieYear, minYear) >= 0
                        && string.CompareOrdinal(movieYear, maxYear) <= 0);

                if (isMatch)
                {
                    return MoveIntoBucket(movieFolder);
                }
            }
        }

        return MoveIntoBucket(outputBase);
    }
}
