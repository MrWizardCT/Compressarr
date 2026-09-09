using Compressarr.Core.Conversion;

namespace Compressarr.Core.Reporting;

public sealed class LaneReportSection
{
    public required string LaneDisplayName { get; init; }
    public required IReadOnlyList<ConversionResult> Results { get; init; }

    /// <summary>Lane/run-level problems (missing preset, missing Output folder, FileBot path not
    /// found, etc) that aren't tied to any one file in Results - shown as their own notice on this
    /// lane's section, same ERROR-code-plus-tooltip treatment a file row gets. Empty for a
    /// perfectly healthy lane. See ReportErrorCode's 106+ values.</summary>
    public IReadOnlyList<ReportErrorCode> LaneProblems { get; init; } = Array.Empty<ReportErrorCode>();
}

/// <summary>TotalTimeSeconds defaults to 0 (rather than being required) so existing call sites
/// that only care about file counts/sizes don't all need updating.</summary>
public sealed record HistoryRollup(int FileCount, double BeforeGb, double AfterGb, double TotalTimeSeconds = 0);

public sealed class ReportModel
{
    public required DateTime GeneratedAt { get; init; }
    public required TimeSpan RunTime { get; init; }
    public required IReadOnlyList<LaneReportSection> Lanes { get; init; }

    /// <summary>Path to this run's own plain-text summary log - linked from the Status column for
    /// any non-encode failure (a move failure, a missing preset), where HandBrake's own detail log
    /// would be irrelevant or nonexistent. Null only in contexts (existing tests, mainly) that
    /// build a ReportModel without going through RunOrchestrator.</summary>
    public string? SummaryLogFilePath { get; init; }

    /// <summary>0 means "this pass found nothing to do" - only a run that actually processed
    /// files gets a permanent number (see RunOrchestrator/IRunHistoryStore.IncrementRunCount).
    /// The report shows a plain "Run:" label instead of "Run #N:" when this is 0.</summary>
    public int RunNumber { get; init; }

    public HistoryRollup? Today { get; init; }
    public HistoryRollup? ThisMonth { get; init; }
    public HistoryRollup? ThisYear { get; init; }

    public bool HasAnyLaneProblems => Lanes.Any(l => l.LaneProblems.Count > 0);

    public int TotalFiles => Lanes.Sum(l => l.Results.Count);
    public int ErrorCount => Lanes.Sum(l => l.Results.Count(r => !r.Success));

    /// <summary>Count of otherwise-successful files that still had a secondary post-process
    /// problem (companion-file move or Sonarr/Radarr unmonitor) - see
    /// <see cref="Conversion.ConversionResult.PostProcessWarning"/>. Distinct from
    /// <see cref="ErrorCount"/>: these files did convert and get filed correctly.</summary>
    public int WarningCount => Lanes.Sum(l => l.Results.Count(r => r.Success && !string.IsNullOrEmpty(r.PostProcessWarning)));

    public double TotalBeforeGb => Lanes.Sum(l => l.Results.Sum(r => r.BeginSizeGb));
    public double TotalAfterGb => Lanes.Sum(l => l.Results.Sum(r => r.EndSizeGb));
}
