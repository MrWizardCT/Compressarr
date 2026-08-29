using Compressarr.Core.Logging;
using Compressarr.Core.Reporting;

namespace Compressarr.Core.Tests.Reporting;

public class ReportListBuilderTests
{
    private static RunHistoryRecord RecordOn(DateTime date, int runNumber, string reportFileName, double beforeGb = 10, double afterGb = 4, int fileCount = 2) =>
        new(date.Year, date.Month, date.Day, beforeGb, afterGb, fileCount, 0, 0, 0, RunNumber: runNumber, ReportFileName: reportFileName);

    [Fact]
    public void Build_RecordWithinRetention_Included()
    {
        var now = new DateTime(2026, 3, 15);
        var history = new List<RunHistoryRecord> { RecordOn(now.AddDays(-5), 1, "report1.html") };

        var result = ReportListBuilder.Build(history, retentionDays: 30, now, _ => true);

        Assert.Single(result);
        Assert.Equal(1, result[0].RunNumber);
    }

    [Fact]
    public void Build_RecordOutsideRetention_Excluded()
    {
        var now = new DateTime(2026, 3, 15);
        var history = new List<RunHistoryRecord> { RecordOn(now.AddDays(-31), 1, "report1.html") };

        var result = ReportListBuilder.Build(history, retentionDays: 30, now, _ => true);

        Assert.Empty(result);
    }

    [Fact]
    public void Build_RecordWithNoReportFileName_Excluded()
    {
        var now = new DateTime(2026, 3, 15);
        var history = new List<RunHistoryRecord> { RecordOn(now, 1, "") };

        var result = ReportListBuilder.Build(history, retentionDays: 30, now, _ => true);

        Assert.Empty(result);
    }

    [Fact]
    public void Build_ReportFileNoLongerOnDisk_Excluded_EvenIfWithinRetentionWindow()
    {
        var now = new DateTime(2026, 3, 15);
        var history = new List<RunHistoryRecord> { RecordOn(now, 1, "gone.html") };

        var result = ReportListBuilder.Build(history, retentionDays: 30, now, fileName => fileName != "gone.html");

        Assert.Empty(result);
    }

    [Fact]
    public void Build_SortsMostRecentRunNumberFirst()
    {
        var now = new DateTime(2026, 3, 15);
        var history = new List<RunHistoryRecord>
        {
            RecordOn(now.AddDays(-2), 1, "r1.html"),
            RecordOn(now, 3, "r3.html"),
            RecordOn(now.AddDays(-1), 2, "r2.html")
        };

        var result = ReportListBuilder.Build(history, retentionDays: 30, now, _ => true);

        Assert.Equal(new[] { 3, 2, 1 }, result.Select(r => r.RunNumber));
    }

    [Fact]
    public void Build_ComputesSavedPercent()
    {
        var now = new DateTime(2026, 3, 15);
        var history = new List<RunHistoryRecord> { RecordOn(now, 1, "r1.html", beforeGb: 10, afterGb: 4) };

        var result = ReportListBuilder.Build(history, retentionDays: 30, now, _ => true);

        Assert.Equal(60.0, result[0].SavedPercent);
    }

    [Fact]
    public void Build_ZeroBeforeSize_SavedPercentIsZero_NotDivideByZero()
    {
        var now = new DateTime(2026, 3, 15);
        var history = new List<RunHistoryRecord> { RecordOn(now, 1, "r1.html", beforeGb: 0, afterGb: 0) };

        var result = ReportListBuilder.Build(history, retentionDays: 30, now, _ => true);

        Assert.Equal(0, result[0].SavedPercent);
    }
}
