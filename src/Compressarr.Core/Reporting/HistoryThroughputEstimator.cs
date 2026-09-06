using System.Globalization;
using System.Text.RegularExpressions;

namespace Compressarr.Core.Reporting;

/// <summary>A rough starting throughput estimate (GB of source video per minute), by preset and
/// blended globally, parsed from recently-generated HTML reports. This is a placeholder only -
/// it exists to give the queue-ETA feature something to show before any file has actually
/// finished in the current session; live per-run samples (see IRunProgressReporter.
/// FileThroughputSample / CurrentRunStateService) take over immediately once real data exists.</summary>
public sealed record HistoricalThroughput(
    IReadOnlyDictionary<string, double> RateGbPerMinuteByPreset,
    double? GlobalRateGbPerMinute);

public interface IHistoryThroughputEstimator
{
    HistoricalThroughput Estimate(string reportPath);
}

/// <summary>Parses HtmlReportGenerator's own per-file table rows directly out of recent report
/// files on disk - there's no separate structured (CSV/JSON) per-file history, but the reports
/// themselves already carry exactly what's needed (File, Type, Preset, Before/After/Savings GB,
/// Duration) in a consistent, machine-generated shape.</summary>
public sealed partial class HistoryThroughputEstimator : IHistoryThroughputEstimator
{
    // Deliberately small and recency-biased - this is only a rough placeholder seed until the
    // current session has its own live data, not a rigorous historical average. A bigger window
    // would also risk pulling in stale results from whenever presets/hardware were different.
    private const int MaxRecentReports = 5;

    // Below this, a "conversion" is almost certainly a throwaway dev/test file (a handful of
    // bytes written via File.WriteAllText, common in this project's own test runs) rather than a
    // real video - including one would badly skew the rate toward unrealistic speed.
    private const double MinSampleGb = 0.05;

    // Matches HtmlReportGenerator.AppendLaneSection's per-file <tr> shape exactly: an optional
    // class="err"/"warn" attribute, then File/Type/Preset cells (contents not needed, matched
    // loosely), then three consecutive "<td>N GB</td>" cells (Before/After/Savings) and a
    // "<td>Hh Mm Ss</td>" duration cell. This specific run of GB-cell-then-duration-cell shape
    // cannot collide with the separate History summary table further down the same report file,
    // which has a different column set (Period/Before/After/Savings%/Files/Time - only two GB
    // cells, followed by a percent, not a third GB cell). Savings specifically can be negative
    // (Math.Round(begin - end, 3) - a tiny already-efficient source file can end up slightly
    // LARGER after conversion, e.g. "-0.006 GB") - confirmed against real report data, where an
    // unsigned-only version of this pattern silently matched nothing at all.
    [GeneratedRegex(
        @"<tr(?: class=""(?<rowclass>[^""]*)"")?><td>.*?</td><td>.*?</td><td>(?<preset>[^<]*)</td><td>(?<begin>[\d.]+) GB</td><td>-?[\d.]+ GB</td><td>-?[\d.]+ GB</td><td>(?<h>\d+)h (?<m>\d+)m (?<s>\d+)s</td>",
        RegexOptions.IgnoreCase)]
    private static partial Regex RowPattern();

    public HistoricalThroughput Estimate(string reportPath)
    {
        var samples = new List<(string? Preset, double Gb, double Minutes)>();

        if (Directory.Exists(reportPath))
        {
            var recentFiles = Directory.EnumerateFiles(reportPath, "*.html")
                .Select(f => new FileInfo(f))
                .OrderByDescending(f => f.LastWriteTime)
                .Take(MaxRecentReports);

            foreach (var file in recentFiles)
            {
                string html;
                try { html = File.ReadAllText(file.FullName); }
                catch { continue; } // unreadable/locked/deleted mid-enumeration - just skip it

                foreach (Match match in RowPattern().Matches(html))
                {
                    // A failed/incomplete conversion's elapsed time doesn't reflect real encode
                    // throughput - only count rows that actually succeeded.
                    if (string.Equals(match.Groups["rowclass"].Value, "err", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (!double.TryParse(match.Groups["begin"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var gb)
                        || gb < MinSampleGb)
                    {
                        continue;
                    }

                    var totalMinutes = int.Parse(match.Groups["h"].Value) * 60
                        + int.Parse(match.Groups["m"].Value)
                        + int.Parse(match.Groups["s"].Value) / 60.0;
                    if (totalMinutes <= 0)
                    {
                        continue;
                    }

                    var preset = match.Groups["preset"].Value;
                    samples.Add((string.IsNullOrWhiteSpace(preset) ? null : preset, gb, totalMinutes));
                }
            }
        }

        var byPreset = samples
            .Where(s => s.Preset is not null)
            .GroupBy(s => s.Preset!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Sum(s => s.Gb) / g.Sum(s => s.Minutes), StringComparer.OrdinalIgnoreCase);

        double? global = samples.Count > 0 ? samples.Sum(s => s.Gb) / samples.Sum(s => s.Minutes) : null;

        return new HistoricalThroughput(byPreset, global);
    }
}
