using System.Text.RegularExpressions;
using Compressarr.Core.Conversion;

namespace Compressarr.Core.Routing;

public interface IFileRouter
{
    /// <summary>Dispatches on the file's auto-detected content type to the matching base path -
    /// a lane's TvShowBasePath for TV episodes, MovieBasePath for everything else. Returns null
    /// (no-op) if moveFiles is false.</summary>
    string? RouteFile(string fileName, bool isTv, string tvShowBasePath, string movieBasePath, bool moveFiles);
}

/// <summary>Ported from Move-CompressarrMovieFile/Move-CompressarrTVFile/Move-CompressarrRoutedFile.</summary>
public sealed partial class FileRouter : IFileRouter
{
    [GeneratedRegex(@"\(([^)]+)\)")]
    private static partial Regex ParenContentPattern();

    [GeneratedRegex(@"(\d{4})( ?- ?)?(\d{4})?")]
    private static partial Regex YearRangePattern();

    public string? RouteFile(string fileName, bool isTv, string tvShowBasePath, string movieBasePath, bool moveFiles)
    {
        if (!moveFiles) return null;

        return isTv ? MoveTvFile(fileName, tvShowBasePath) : MoveMovieFile(fileName, movieBasePath);
    }

    public string? MoveTvFile(string fileName, string outputBase)
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
        var destPath = Path.Combine(destFolder, info.EpisodeFileName);

        Directory.CreateDirectory(destFolder);
        File.Move(fileName, destPath, overwrite: true);
        return destPath;
    }

    public string? MoveMovieFile(string fileName, string outputBase)
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
            var destPath = Path.Combine(movieDestFolder, leaf);
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
