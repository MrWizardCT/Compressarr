namespace Compressarr.Core.Logging;

public sealed record RunHistoryRecord(
    int Year,
    int Month,
    int Day,
    double BeginSizeGb,
    double EndSizeGb,
    int FileCount,
    int ProcessHours,
    int ProcessMinutes,
    int ProcessSeconds,
    /// <summary>The persistent, permanent "this was the Nth run ever" number (matches
    /// IRunHistoryStore.GetRunCount at the moment this run was recorded) - 0 for rows written
    /// before this field existed, since old CSV rows don't carry it.</summary>
    int RunNumber = 0,
    /// <summary>Just the report's file name (e.g. "Compressarr_2026-08-29_12-00-00_Report.html"),
    /// not a full path - the web UI resolves it against the *current* Report.ReportPath at
    /// request time, so a later change to that setting doesn't strand old links. Empty for rows
    /// written before this field existed.</summary>
    string ReportFileName = "",
    /// <summary>How many files in this run failed outright (ReportModel.ErrorCount at record
    /// time). 0 for rows written before this field existed - indistinguishable from a genuinely
    /// clean run, same tradeoff RunNumber/ReportFileName already accept for old rows.</summary>
    int ErrorCount = 0,
    /// <summary>How many files succeeded but had a secondary post-process problem
    /// (ReportModel.WarningCount at record time - see ConversionResult.PostProcessWarning).</summary>
    int WarningCount = 0);

/// <summary>
/// Narrow history/run-count interface — deliberately not the final schema. Phase 4 (web-based
/// real-time monitoring, running totals, run count) swaps CsvRunHistoryStore for a proper
/// structured store (most likely SQLite) behind this same interface, without touching callers.
/// </summary>
public interface IRunHistoryStore
{
    /// <summary>Appends one row for a run that processed at least one file — an empty pass is
    /// never recorded, mirroring v1's Invoke-CompressarrRun gating.</summary>
    void AppendRun(string logFilePath, RunHistoryRecord record);

    IReadOnlyList<RunHistoryRecord> GetHistory(string logFilePath);

    /// <summary>Persistent, cumulative count of runs that processed at least one file. Returns 0
    /// if never run before — also how the app recognizes "first launch ever" to decide which UI
    /// to show.</summary>
    int GetRunCount(string runCountPath);

    void IncrementRunCount(string runCountPath);
}
