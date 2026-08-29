using Compressarr.Core.Logging;
using Compressarr.Core.Reporting;

namespace Compressarr.Core.Tests.Reporting;

file sealed class FakeRunHistoryStore : IRunHistoryStore
{
    public List<RunHistoryRecord> Records = new();
    public void AppendRun(string logFilePath, RunHistoryRecord record) => Records.Add(record);
    public IReadOnlyList<RunHistoryRecord> GetHistory(string logFilePath) => Records;
    public int GetRunCount(string runCountPath) => Records.Count;
    public void IncrementRunCount(string runCountPath) { }
}

public class WebHistoryRollupCalculatorTests
{
    private static RunHistoryRecord RecordOn(DateTime date, int fileCount = 1, double beforeGb = 10, double afterGb = 4) =>
        new(date.Year, date.Month, date.Day, beforeGb, afterGb, fileCount, 0, 0, 0);

    [Fact]
    public void Calculate_EmptyHistory_AllBucketsZero()
    {
        var store = new FakeRunHistoryStore();
        var calc = new WebHistoryRollupCalculator(store) { Now = new DateTime(2026, 1, 2) };

        var result = calc.Calculate("any-path");

        foreach (var bucket in new[] { result.Today, result.Last7Days, result.Last30Days, result.LastYear, result.AllTime })
        {
            Assert.Equal(0, bucket.FileCount);
            Assert.Equal(0, bucket.BeforeGb);
            Assert.Equal(0, bucket.AfterGb);
        }
    }

    [Fact]
    public void Calculate_AllTime_IncludesEverythingRegardlessOfDate()
    {
        var now = new DateTime(2026, 1, 2);
        var store = new FakeRunHistoryStore
        {
            Records = { RecordOn(now), RecordOn(now.AddYears(-5)), RecordOn(now.AddDays(-1000)) }
        };
        var calc = new WebHistoryRollupCalculator(store) { Now = now };

        var result = calc.Calculate("any-path");

        Assert.Equal(3, result.AllTime.FileCount);
    }

    [Fact]
    public void Calculate_RecordEightDaysOld_ExcludedFromLast7Days_ButInLast30AndYearAndAllTime()
    {
        var now = new DateTime(2026, 3, 15);
        var store = new FakeRunHistoryStore { Records = { RecordOn(now.AddDays(-8)) } };
        var calc = new WebHistoryRollupCalculator(store) { Now = now };

        var result = calc.Calculate("any-path");

        Assert.Equal(0, result.Last7Days.FileCount);
        Assert.Equal(1, result.Last30Days.FileCount);
        Assert.Equal(1, result.LastYear.FileCount);
        Assert.Equal(1, result.AllTime.FileCount);
    }

    [Fact]
    public void Calculate_RecordThirtyOneDaysOld_ExcludedFromLast7AndLast30_ButInLastYearAndAllTime()
    {
        var now = new DateTime(2026, 3, 15);
        var store = new FakeRunHistoryStore { Records = { RecordOn(now.AddDays(-31)) } };
        var calc = new WebHistoryRollupCalculator(store) { Now = now };

        var result = calc.Calculate("any-path");

        Assert.Equal(0, result.Last7Days.FileCount);
        Assert.Equal(0, result.Last30Days.FileCount);
        Assert.Equal(1, result.LastYear.FileCount);
        Assert.Equal(1, result.AllTime.FileCount);
    }

    [Fact]
    public void Calculate_RecordOverAYearOld_OnlyInAllTime()
    {
        var now = new DateTime(2026, 3, 15);
        var store = new FakeRunHistoryStore { Records = { RecordOn(now.AddDays(-366)) } };
        var calc = new WebHistoryRollupCalculator(store) { Now = now };

        var result = calc.Calculate("any-path");

        Assert.Equal(0, result.Last7Days.FileCount);
        Assert.Equal(0, result.Last30Days.FileCount);
        Assert.Equal(0, result.LastYear.FileCount);
        Assert.Equal(1, result.AllTime.FileCount);
    }

    [Fact]
    public void Calculate_RecordTodayIncludedInEveryBucket()
    {
        var now = new DateTime(2026, 3, 15);
        var store = new FakeRunHistoryStore { Records = { RecordOn(now) } };
        var calc = new WebHistoryRollupCalculator(store) { Now = now };

        var result = calc.Calculate("any-path");

        Assert.Equal(1, result.Today.FileCount);
        Assert.Equal(1, result.Last7Days.FileCount);
        Assert.Equal(1, result.Last30Days.FileCount);
        Assert.Equal(1, result.LastYear.FileCount);
        Assert.Equal(1, result.AllTime.FileCount);
    }

    [Fact]
    public void Calculate_CalendarBoundaryCrossing_DecemberRunStillCountsInLast7Days_WhenNowIsEarlyJanuary()
    {
        // The case that actually distinguishes "rolling" from the old calendar-aligned
        // HistoryRollupCalculator: a naive Year==now.Year && Month==now.Month filter would wrongly
        // exclude this, since Dec 28 is a different month AND year than Jan 2.
        var now = new DateTime(2026, 1, 2);
        var store = new FakeRunHistoryStore { Records = { RecordOn(new DateTime(2025, 12, 28)) } };
        var calc = new WebHistoryRollupCalculator(store) { Now = now };

        var result = calc.Calculate("any-path");

        Assert.Equal(1, result.Last7Days.FileCount);
    }

    [Fact]
    public void Calculate_SumsFileCountAndSizes_AcrossMultipleRecordsInBucket()
    {
        var now = new DateTime(2026, 3, 15);
        var store = new FakeRunHistoryStore
        {
            Records =
            {
                RecordOn(now, fileCount: 2, beforeGb: 10, afterGb: 4),
                RecordOn(now.AddDays(-1), fileCount: 3, beforeGb: 20, afterGb: 8)
            }
        };
        var calc = new WebHistoryRollupCalculator(store) { Now = now };

        var result = calc.Calculate("any-path");

        Assert.Equal(5, result.Last7Days.FileCount);
        Assert.Equal(30, result.Last7Days.BeforeGb);
        Assert.Equal(12, result.Last7Days.AfterGb);
    }
}
