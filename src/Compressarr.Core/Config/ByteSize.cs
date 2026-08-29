using System.Globalization;
using System.Text.RegularExpressions;

namespace Compressarr.Core.Config;

/// <summary>
/// Parses/formats unit-suffix byte-size strings ("0gb", "500mb", "2GB"), matching v1's
/// ConvertTo-CompressarrByteSize, which explicitly parses these rather than relying on any
/// implicit unit-suffix coercion. Used for UI display and for parsing a hand-edited config value;
/// CompressarrConfig itself stores the typed long (ProcessingSettings.MinSizeBytes).
/// </summary>
public static partial class ByteSize
{
    [GeneratedRegex(@"^\s*([0-9]+(?:\.[0-9]+)?)\s*(b|kb|mb|gb|tb)?\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex Pattern();

    private static readonly Dictionary<string, long> UnitMultipliers = new(StringComparer.OrdinalIgnoreCase)
    {
        ["b"] = 1L,
        ["kb"] = 1024L,
        ["mb"] = 1024L * 1024L,
        ["gb"] = 1024L * 1024L * 1024L,
        ["tb"] = 1024L * 1024L * 1024L * 1024L
    };

    public static long Parse(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return 0;

        var match = Pattern().Match(value);
        if (!match.Success)
        {
            throw new FormatException($"'{value}' is not a valid byte size (expected e.g. '500mb', '2gb').");
        }

        var number = double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        var unit = match.Groups[2].Success ? match.Groups[2].Value : "b";
        var multiplier = UnitMultipliers[unit];

        return (long)(number * multiplier);
    }

    public static string Format(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double size = bytes;
        var unitIndex = 0;

        while (size >= 1024 && unitIndex < units.Length - 1)
        {
            size /= 1024;
            unitIndex++;
        }

        return $"{size.ToString("0.##", CultureInfo.InvariantCulture)}{units[unitIndex]}";
    }
}
