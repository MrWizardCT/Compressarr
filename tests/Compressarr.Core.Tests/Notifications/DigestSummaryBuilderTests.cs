using Compressarr.Core.Logging;
using Compressarr.Core.Notifications;

namespace Compressarr.Core.Tests.Notifications;

public class DigestSummaryBuilderTests
{
    private static readonly DateOnly Today = new(2026, 9, 10); // a Thursday

    private static RunHistoryRecord RecordOn(DateOnly date, int fileCount, double beginGb, double endGb) =>
        new(date.Year, date.Month, date.Day, beginGb, endGb, fileCount, 0, 30, 0);

    [Fact]
    public void BuildDaily_OnlyIncludesYesterday()
    {
        var history = new[]
        {
            RecordOn(Today, 5, 10, 5),               // today - excluded
            RecordOn(Today.AddDays(-1), 3, 9, 4.5),   // yesterday - included
            RecordOn(Today.AddDays(-2), 7, 20, 10),   // 2 days ago - excluded
        };

        var summary = DigestSummaryBuilder.BuildDaily(history, Today);

        Assert.Equal(3, summary.TotalFiles);
        Assert.Equal(9, summary.BeginSizeGb);
        Assert.Equal(4.5, summary.EndSizeGb);
    }

    [Fact]
    public void BuildDaily_MultipleRecordsSameDay_Sums()
    {
        var yesterday = Today.AddDays(-1);
        var history = new[]
        {
            RecordOn(yesterday, 2, 4, 2),
            RecordOn(yesterday, 3, 6, 3),
        };

        var summary = DigestSummaryBuilder.BuildDaily(history, Today);

        Assert.Equal(5, summary.TotalFiles);
        Assert.Equal(10, summary.BeginSizeGb);
        Assert.Equal(5, summary.EndSizeGb);
    }

    [Fact]
    public void BuildWeekly_IncludesTrailingSevenDays_ExcludesOlderAndToday()
    {
        var history = new[]
        {
            RecordOn(Today, 1, 1, 1),                 // today - excluded
            RecordOn(Today.AddDays(-1), 1, 2, 1),      // day -1 - included
            RecordOn(Today.AddDays(-7), 1, 2, 1),      // day -7 - included (boundary)
            RecordOn(Today.AddDays(-8), 100, 200, 100),// day -8 - excluded
        };

        var summary = DigestSummaryBuilder.BuildWeekly(history, Today);

        Assert.Equal(2, summary.TotalFiles);
        Assert.Equal(4, summary.BeginSizeGb);
        Assert.Equal(2, summary.EndSizeGb);
    }

    [Fact]
    public void BuildDaily_NoMatchingRecords_ReturnsZeroedSummary()
    {
        var summary = DigestSummaryBuilder.BuildDaily(Array.Empty<RunHistoryRecord>(), Today);

        Assert.Equal(0, summary.TotalFiles);
        Assert.Equal(0, summary.BeginSizeGb);
        Assert.Equal(0, summary.EndSizeGb);
        Assert.Equal(0, summary.SavedPercent); // must not throw on 0/0
        Assert.Equal(TimeSpan.Zero, summary.TotalDuration);
    }

    // A private helper's RecordOn above always passes 0h/30m/0s - fixed so every existing test's
    // own assertions on TotalFiles/BeginSizeGb/EndSizeGb stay exactly as they were, since none of
    // them care about duration. This one uses its own records with distinct, varied durations
    // specifically to prove the sum - the actual bug found live: a real Daily/Weekly digest
    // notification always showed "Duration: 0s" regardless of real processing time, because
    // ToNotificationEvent hardcoded TimeSpan.Zero instead of ever summing RunHistoryRecord's own
    // ProcessHours/ProcessMinutes/ProcessSeconds fields across the matching period.
    [Fact]
    public void BuildDaily_SumsDurationAcrossMatchingRecords()
    {
        var yesterday = Today.AddDays(-1);
        var history = new[]
        {
            new RunHistoryRecord(yesterday.Year, yesterday.Month, yesterday.Day, 10, 5, 3, ProcessHours: 1, ProcessMinutes: 15, ProcessSeconds: 20),
            new RunHistoryRecord(yesterday.Year, yesterday.Month, yesterday.Day, 8, 4, 2, ProcessHours: 0, ProcessMinutes: 50, ProcessSeconds: 50),
        };

        var summary = DigestSummaryBuilder.BuildDaily(history, Today);

        Assert.Equal(new TimeSpan(2, 6, 10), summary.TotalDuration);
    }

    [Fact]
    public void ToNotificationEvent_UsesSummaryDuration_NotZero()
    {
        var summary = new DigestSummary(4, 20, 10, new TimeSpan(3, 30, 0));

        var evt = summary.ToNotificationEvent("Daily Digest");

        Assert.Equal(new TimeSpan(3, 30, 0), evt.Duration);
    }

    [Fact]
    public void SavedPercent_ComputesFromBeginAndEnd()
    {
        var summary = new DigestSummary(10, 100, 40, TimeSpan.Zero);

        Assert.Equal(60, summary.SavedGb);
        Assert.Equal(60, summary.SavedPercent);
    }

    [Theory]
    [InlineData(1, "Compressed 1 file, reducing original size from 10 GB to 5 GB, saving 50% of original size.")]
    [InlineData(2, "Compressed 2 files, reducing original size from 10 GB to 5 GB, saving 50% of original size.")]
    [InlineData(0, "Compressed 0 files, reducing original size from 10 GB to 5 GB, saving 50% of original size.")]
    public void ToMessage_MatchesExactWording(int totalFiles, string expected)
    {
        var summary = new DigestSummary(totalFiles, 10, 5, TimeSpan.Zero);

        Assert.Equal(expected, summary.ToMessage());
    }
}
