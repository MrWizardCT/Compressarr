using System.Text.RegularExpressions;

namespace Compressarr.Core.Conversion;

public sealed record EpisodeInfo(
    bool HasSeasonAndEpisode,
    string? Season,
    string? Episode,
    string ShowName,
    string EpisodeFileName);

/// <summary>
/// TV-vs-Movie auto-detection: the single dispatch point used throughout the app (conversion,
/// routing, reporting) to decide whether a file is a TV episode. Ported verbatim from
/// Get-CompressarrEpisodeInfo/Test-CompressarrIsTVFile, including the deliberately uncapped
/// \d+ digit groups (the original tool this was based on capped both at \d{1,2}, which silently
/// mis-parsed any season/episode number running 3+ digits, e.g. absolute-numbering schemes like
/// S01E123).
/// </summary>
public static partial class ContentClassifier
{
    [GeneratedRegex(@"S*(\d+)(x|E)(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex EpisodePattern();

    [GeneratedRegex(@"^(.*?\(\d{4}\))")]
    private static partial Regex MovieYearTagPattern();

    public static EpisodeInfo GetEpisodeInfo(string fileName)
    {
        var pattern = EpisodePattern();

        var splitFull = pattern.Split(fileName);
        var season = splitFull.Length > 1 ? splitFull[1] : null;
        var episode = splitFull.Length > 3 ? splitFull[3] : null;

        var epiName = Path.GetFileName(fileName);
        var splitName = pattern.Split(epiName);
        var showName = splitName.Length > 0 ? splitName[0] : "";
        if (showName.Length > 0)
        {
            // Drop the single separator character (usually '.', ' ', or '-') that sat directly
            // in front of the season marker.
            showName = showName[..^1].Trim();
        }
        showName = showName.TrimEnd('-').Trim();

        var hasSeasonAndEpisode = !string.IsNullOrEmpty(season) && !string.IsNullOrEmpty(episode);

        return new EpisodeInfo(hasSeasonAndEpisode, season, episode, showName, epiName);
    }

    public static bool IsTvFile(string fileName) => GetEpisodeInfo(fileName).HasSeasonAndEpisode;

    /// <summary>The per-movie subfolder name a converted movie is filed under: the base filename
    /// up through and including its "(YYYY)" year tag, with anything after that discarded -
    /// "Caddyshack (1980) {edition-Director's Cut}.mkv" becomes "Caddyshack (1980)". Filenames
    /// with no year tag fall back to the full base filename unchanged.</summary>
    public static string GetMovieFolderName(string fileName)
    {
        var baseName = Path.GetFileNameWithoutExtension(fileName);
        var match = MovieYearTagPattern().Match(baseName);
        return match.Success ? match.Groups[1].Value.Trim() : baseName.Trim();
    }
}
