using Compressarr.Core.Logging;

namespace Compressarr.Core.Reporting;

public sealed record ReportListEntry(
    int RunNumber,
    string ReportFileName,
    int FileCount,
    double BeforeGb,
    double AfterGb,
    double SavedPercent,
    DateTime Date,
    int ErrorCount,
    int WarningCount);

/// <summary>
/// Builds the "Reports" table for the web UI's History page: history rows within the configured
/// retention window, most recent first. Deliberately excludes rows with no ReportFileName (older
/// CSV rows written before that field existed have nothing to link to) and rows whose report file
/// no longer exists on disk (retention cleanup may have already deleted it even if the CSV row
/// itself is still within the day-window - never show a dead link).
/// </summary>
public static class ReportListBuilder
{
    public static IReadOnlyList<ReportListEntry> Build(
        IReadOnlyList<RunHistoryRecord> history,
        int retentionDays,
        DateTime now,
        Func<string, bool> reportFileExists)
    {
        var cutoff = now.Date.AddDays(-retentionDays);

        return history
            .Where(r => !string.IsNullOrEmpty(r.ReportFileName))
            .Select(r => new { Record = r, Date = new DateTime(r.Year, r.Month, r.Day) })
            .Where(x => x.Date >= cutoff)
            .Where(x => reportFileExists(x.Record.ReportFileName))
            .OrderByDescending(x => x.Record.RunNumber)
            .Select(x => new ReportListEntry(
                RunNumber: x.Record.RunNumber,
                ReportFileName: x.Record.ReportFileName,
                FileCount: x.Record.FileCount,
                BeforeGb: x.Record.BeginSizeGb,
                AfterGb: x.Record.EndSizeGb,
                SavedPercent: x.Record.BeginSizeGb > 0
                    ? Math.Round(100 - (x.Record.EndSizeGb / x.Record.BeginSizeGb) * 100, 1)
                    : 0,
                Date: x.Date,
                ErrorCount: x.Record.ErrorCount,
                WarningCount: x.Record.WarningCount))
            .ToList();
    }
}
