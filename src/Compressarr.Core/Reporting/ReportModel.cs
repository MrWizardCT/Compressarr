using Compressarr.Core.Conversion;

namespace Compressarr.Core.Reporting;

public sealed class LaneReportSection
{
    public required string LaneDisplayName { get; init; }
    public required IReadOnlyList<ConversionResult> Results { get; init; }
}

public sealed record HistoryRollup(int FileCount, double BeforeGb, double AfterGb);

public sealed class ReportModel
{
    public required DateTime GeneratedAt { get; init; }
    public required TimeSpan RunTime { get; init; }
    public required IReadOnlyList<LaneReportSection> Lanes { get; init; }

    /// <summary>0 means "this pass found nothing to do" - only a run that actually processed
    /// files gets a permanent number (see RunOrchestrator/IRunHistoryStore.IncrementRunCount).
    /// The report shows a plain "Run:" label instead of "Run #N:" when this is 0.</summary>
    public int RunNumber { get; init; }

    public HistoryRollup? Today { get; init; }
    public HistoryRollup? ThisMonth { get; init; }
    public HistoryRollup? ThisYear { get; init; }

    public int TotalFiles => Lanes.Sum(l => l.Results.Count);
    public int ErrorCount => Lanes.Sum(l => l.Results.Count(r => !r.Success));
    public double TotalBeforeGb => Lanes.Sum(l => l.Results.Sum(r => r.BeginSizeGb));
    public double TotalAfterGb => Lanes.Sum(l => l.Results.Sum(r => r.EndSizeGb));
}
