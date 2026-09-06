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
public sealed class FileRouter : IFileRouter
{
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

        var leaf = Path.GetFileName(fileName);
        var movieFolderName = ContentClassifier.GetMovieFolderName(leaf);

        // Every movie gets its own folder directly under outputBase - no bucket/range-folder
        // auto-detection. An earlier version tried to auto-detect year-bucket folders (e.g.
        // "01. Movies 1920-1979") by scanning outputBase for any folder whose name merely
        // contained "movie", but that matched ordinary movie folders too (a title like "Scary
        // Movie (2026)" contains "Movie") and silently nested every subsequent movie inside
        // whichever one happened to be the sole match - confirmed misrouting real files in
        // production. Bucket folders aren't used, so removed rather than made safer.
        var movieDestFolder = Path.Combine(outputBase, movieFolderName);
        Directory.CreateDirectory(movieDestFolder);
        var destPath = ResolveCollision(Path.Combine(movieDestFolder, leaf), collisionMode);
        File.Move(fileName, destPath, overwrite: true);
        return destPath;
    }
}
