using Compressarr.Core.Config;
using Compressarr.Core.Conversion;
using Compressarr.Core.Notifications;
using Compressarr.Core.Reporting;

namespace Compressarr.Core.Tests.Notifications;

public class NotificationMessageRendererTests
{
    private static ReportModel SampleReport() => new()
    {
        GeneratedAt = new DateTime(2026, 9, 11),
        RunTime = TimeSpan.FromMinutes(72), // 1h 12m
        RunNumber = 47,
        RetriesSucceeded = 2,
        Today = new HistoryRollup(15, 20.0, 9.9),
        ThisMonth = new HistoryRollup(212, 300.0, 156.2),
        ThisYear = new HistoryRollup(1904, 2500.0, 1292.5),
        Lanes = new[]
        {
            new LaneReportSection
            {
                LaneDisplayName = "Movies",
                Results = new[]
                {
                    Result(26.7, 18.3, success: true, warning: "companion move failed"),
                }
            }
        }
    };

    private static ConversionResult Result(double beforeGb, double afterGb, bool success, string? warning = null) => new()
    {
        LaneId = "lane1",
        FileName = "file.mkv",
        FullName = @"C:\file.mkv",
        ContentType = "movie",
        BeginSizeGb = beforeGb,
        EndSizeGb = afterGb,
        Success = success,
        PostProcessWarning = warning
    };

    [Fact]
    public void Render_Standard_SubstitutesAllTokensUsed()
    {
        var report = SampleReport();
        var rendered = NotificationMessageRenderer.Render(
            NotificationMessagePresets.Templates[NotificationMessageStyle.Standard].Body,
            NotificationOutcome.Success, report, @"C:\reports\r.html");

        Assert.Equal("1 file(s) processed, 8.4 GB saved (31.46%) in 1h 12m.", rendered);
    }

    [Fact]
    public void Render_Detailed_IncludesErrorAndWarningCounts()
    {
        var report = SampleReport();
        var rendered = NotificationMessageRenderer.Render(
            NotificationMessagePresets.Templates[NotificationMessageStyle.Detailed].Body,
            NotificationOutcome.Warning, report, @"C:\reports\r.html");

        Assert.Contains("Errors: 0 - Warnings: 1", rendered);
        Assert.Contains("26.7 GB -> 18.3 GB", rendered);
    }

    [Fact]
    public void Render_Outcome_UsesFormattedOutcomeText()
    {
        var report = SampleReport();
        var rendered = NotificationMessageRenderer.Render("{outcome}", NotificationOutcome.Error, report, "");
        Assert.Equal("Error", rendered);
    }

    [Fact]
    public void Render_HistoricalRollupTokens_UseTheirOwnRollupNotThisRun()
    {
        var report = SampleReport();
        var rendered = NotificationMessageRenderer.Render(
            "{today_files}/{today_saved_gb} {month_files}/{month_saved_gb} {year_files}/{year_saved_gb}",
            NotificationOutcome.Success, report, "");

        Assert.Equal("15/10.1 212/143.8 1904/1207.5", rendered);
    }

    [Fact]
    public void Render_MissingRollup_TreatsAsZeroRatherThanThrowing()
    {
        var report = SampleReport();
        report = new ReportModel
        {
            GeneratedAt = report.GeneratedAt,
            RunTime = report.RunTime,
            RunNumber = report.RunNumber,
            Lanes = report.Lanes,
            Today = null
        };

        var rendered = NotificationMessageRenderer.Render("{today_files}/{today_saved_gb}", NotificationOutcome.Success, report, "");
        Assert.Equal("0/0", rendered);
    }

    [Fact]
    public void Render_UnknownToken_LeftAsLiteralText()
    {
        var report = SampleReport();
        var rendered = NotificationMessageRenderer.Render("Run {run_number}: {not_a_real_token}", NotificationOutcome.Success, report, "");
        Assert.Equal("Run 47: {not_a_real_token}", rendered);
    }

    [Fact]
    public void Render_ReportPathToken_UsesPassedInPath()
    {
        var report = SampleReport();
        var rendered = NotificationMessageRenderer.Render("{report_path}", NotificationOutcome.Success, report, @"C:\Compressarr\Reports\r.html");
        Assert.Equal(@"C:\Compressarr\Reports\r.html", rendered);
    }
}
