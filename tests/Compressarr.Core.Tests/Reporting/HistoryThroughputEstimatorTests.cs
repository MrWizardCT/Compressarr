using Compressarr.Core.Reporting;

namespace Compressarr.Core.Tests.Reporting;

public class HistoryThroughputEstimatorTests : IDisposable
{
    private readonly string _tempDir = Directory.CreateTempSubdirectory("compressarr-throughput-tests-").FullName;
    private readonly HistoryThroughputEstimator _estimator = new();

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    // Mirrors HtmlReportGenerator.AppendLaneSection's exact per-file <tr> shape, plus a History
    // summary table with a different column set - real reports always have both, and the parser
    // must only ever match the per-file rows.
    private static string BuildReport(params (string Preset, double BeginGb, double EndGb, string Duration, bool Failed)[] rows)
    {
        var rowsHtml = string.Join("\n", rows.Select(r =>
        {
            var rowClass = r.Failed ? " class=\"err\"" : "";
            var savings = Math.Round(r.BeginGb - r.EndGb, 3);
            var status = r.Failed ? "ERROR" : "OK";
            return $"    <tr{rowClass}><td>file.mkv</td><td>Movie</td><td>{r.Preset}</td><td>{r.BeginGb} GB</td><td>{r.EndGb} GB</td><td>{savings} GB</td><td>{r.Duration}</td><td>{status}</td><td>—</td></tr>";
        }));

        return $$"""
            <html><body>
            <h3>Lane <span class="muted">(1 file(s), 1 GB &rarr; 1 GB)</span></h3>
            <div class="table-wrap"><table>
              <thead><tr><th>File</th><th>Type</th><th>Preset</th><th>Before</th><th>After</th><th>Savings</th><th>Duration</th><th>Status</th><th>Sonarr/Radarr</th></tr></thead>
              <tbody>
            {{rowsHtml}}
              </tbody>
            </table></div>
            <h2>History</h2>
            <div class="table-wrap"><table>
              <thead><tr><th>Period</th><th>Before</th><th>After</th><th>Savings</th><th>Files</th><th>Time</th></tr></thead>
              <tbody>
                <tr><td>Today</td><td>12.5 GB</td><td>4.2 GB</td><td>66.4%</td><td>3</td><td>0h 45m 0s</td></tr>
              </tbody>
            </table></div>
            </body></html>
            """;
    }

    private string WriteReport(string fileName, params (string Preset, double BeginGb, double EndGb, string Duration, bool Failed)[] rows)
    {
        var path = Path.Combine(_tempDir, fileName);
        File.WriteAllText(path, BuildReport(rows));
        return path;
    }

    [Fact]
    public void Estimate_RealSample_ComputesPerPresetAndGlobalRate()
    {
        // 1 GB in exactly 2 minutes (0h 2m 0s) -> 0.5 GB/min for this preset.
        WriteReport("report1.html", ("Compressarr SD-HD", 1.0, 0.5, "0h 2m 0s", false));

        var result = _estimator.Estimate(_tempDir);

        Assert.True(result.RateGbPerMinuteByPreset.ContainsKey("Compressarr SD-HD"));
        Assert.Equal(0.5, result.RateGbPerMinuteByPreset["Compressarr SD-HD"], precision: 3);
        Assert.NotNull(result.GlobalRateGbPerMinute);
        Assert.Equal(0.5, result.GlobalRateGbPerMinute!.Value, precision: 3);
    }

    [Fact]
    public void Estimate_NeverCrossMatchesTheHistorySummaryTable()
    {
        // The History table's own rows (Period/Before/After/Savings%/Files/Time) must never be
        // mistaken for per-file rows - confirmed by asserting the single real per-file sample
        // above (1 GB / 2 min) drives the result, not the History table's own 12.5 GB/0h45m row.
        WriteReport("report1.html", ("Compressarr SD-HD", 1.0, 0.5, "0h 2m 0s", false));

        var result = _estimator.Estimate(_tempDir);

        Assert.Equal(0.5, result.GlobalRateGbPerMinute!.Value, precision: 3);
    }

    [Fact]
    public void Estimate_FailedRow_IsExcluded()
    {
        WriteReport("report1.html", ("Compressarr SD-HD", 5.0, 0.0, "0h 1m 0s", Failed: true));

        var result = _estimator.Estimate(_tempDir);

        Assert.Empty(result.RateGbPerMinuteByPreset);
        Assert.Null(result.GlobalRateGbPerMinute);
    }

    [Fact]
    public void Estimate_TinyFile_IsExcluded()
    {
        // Well under the 0.05 GB floor - a throwaway dev/test file, not a real video.
        WriteReport("report1.html", ("Compressarr SD-HD", 0.0001, 0.00005, "0h 0m 1s", false));

        var result = _estimator.Estimate(_tempDir);

        Assert.Empty(result.RateGbPerMinuteByPreset);
        Assert.Null(result.GlobalRateGbPerMinute);
    }

    [Fact]
    public void Estimate_MultiplePresets_ComputesEachSeparately()
    {
        WriteReport("report1.html",
            ("HD Preset", 2.0, 1.0, "0h 4m 0s", false),   // 0.5 GB/min
            ("UHD Preset", 10.0, 5.0, "0h 20m 0s", false)); // 0.5 GB/min too, but tracked separately

        var result = _estimator.Estimate(_tempDir);

        Assert.Equal(0.5, result.RateGbPerMinuteByPreset["HD Preset"], precision: 3);
        Assert.Equal(0.5, result.RateGbPerMinuteByPreset["UHD Preset"], precision: 3);
    }

    [Fact]
    public void Estimate_OnlyUsesFiveMostRecentReports()
    {
        // Six reports, each with a different (and easily distinguishable) rate - only the 5 most
        // recently-written should be counted. The oldest carries an extreme 100 GB/min rate that
        // would obviously skew the result if it were included.
        WriteReport("old.html", ("P", 100.0, 50.0, "0h 1m 0s", false));
        File.SetLastWriteTime(Path.Combine(_tempDir, "old.html"), DateTime.Now.AddDays(-10));

        for (var i = 0; i < 5; i++)
        {
            var path = WriteReport($"recent{i}.html", ("P", 1.0, 0.5, "0h 2m 0s", false)); // 0.5 GB/min
            File.SetLastWriteTime(path, DateTime.Now.AddMinutes(-i));
        }

        var result = _estimator.Estimate(_tempDir);

        Assert.Equal(0.5, result.RateGbPerMinuteByPreset["P"], precision: 3);
    }

    [Fact]
    public void Estimate_NegativeSavings_StillMatches()
    {
        // Confirmed happening in a real report: a tiny already-efficient source file can end up
        // slightly LARGER after conversion, giving a negative Savings cell (e.g. "-0.006 GB") - an
        // unsigned-only version of the row pattern silently matched nothing at all against real
        // report data, even though it passed every hand-built fixture test here.
        WriteReport("report1.html", ("Compressarr SD-HD", 1.0, 1.006, "0h 2m 0s", false));

        var result = _estimator.Estimate(_tempDir);

        Assert.True(result.RateGbPerMinuteByPreset.ContainsKey("Compressarr SD-HD"));
    }

    [Fact]
    public void Estimate_NoReports_ReturnsEmptyResult()
    {
        var result = _estimator.Estimate(_tempDir);

        Assert.Empty(result.RateGbPerMinuteByPreset);
        Assert.Null(result.GlobalRateGbPerMinute);
    }

    [Fact]
    public void Estimate_NonExistentFolder_ReturnsEmptyResult()
    {
        var result = _estimator.Estimate(Path.Combine(_tempDir, "does-not-exist"));

        Assert.Empty(result.RateGbPerMinuteByPreset);
        Assert.Null(result.GlobalRateGbPerMinute);
    }
}
