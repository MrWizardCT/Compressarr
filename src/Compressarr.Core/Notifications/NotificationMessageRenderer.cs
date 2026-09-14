using Compressarr.Core.Reporting;

namespace Compressarr.Core.Notifications;

/// <summary>Fills {token} placeholders in a NotificationSettings message template (Title or Body)
/// with this run's actual numbers - the one substitution path behind every NotificationMessageStyle,
/// built-in or Custom, so neither can drift from the other's token vocabulary. An unrecognized
/// {token} is left as literal text rather than throwing or being blanked out, so a typo in a
/// user's Custom template degrades to slightly odd-looking text instead of losing the notification
/// entirely - matching every notifier's own "must never fail an otherwise-successful run"
/// contract.</summary>
public static class NotificationMessageRenderer
{
    public static string Render(string template, NotificationOutcome outcome, ReportModel report, string reportFilePath)
    {
        var savedGb = report.TotalBeforeGb - report.TotalAfterGb;
        var savedPct = report.TotalBeforeGb > 0 ? 100 - (report.TotalAfterGb / report.TotalBeforeGb * 100) : 0;

        var result = template;
        foreach (var (token, value) in Tokens(outcome, report, reportFilePath, savedGb, savedPct))
        {
            result = result.Replace(token, value);
        }
        return result;
    }

    private static IEnumerable<(string Token, string Value)> Tokens(
        NotificationOutcome outcome, ReportModel report, string reportFilePath, double savedGb, double savedPct)
    {
        yield return ("{run_number}", report.RunNumber.ToString());
        yield return ("{files}", report.TotalFiles.ToString());
        yield return ("{saved_gb}", savedGb.ToString("0.##"));
        yield return ("{saved_pct}", savedPct.ToString("0.##"));
        yield return ("{before_gb}", report.TotalBeforeGb.ToString("0.##"));
        yield return ("{after_gb}", report.TotalAfterGb.ToString("0.##"));
        yield return ("{duration}", NotificationFormatting.FormatDuration(report.RunTime));
        yield return ("{outcome}", NotificationFormatting.FormatOutcome(outcome));
        yield return ("{error_count}", report.ErrorCount.ToString());
        yield return ("{warning_count}", report.WarningCount.ToString());
        yield return ("{retries_succeeded}", report.RetriesSucceeded.ToString());
        yield return ("{report_path}", reportFilePath);
        yield return ("{today_files}", (report.Today?.FileCount ?? 0).ToString());
        yield return ("{today_saved_gb}", RollupSavedGb(report.Today).ToString("0.##"));
        yield return ("{month_files}", (report.ThisMonth?.FileCount ?? 0).ToString());
        yield return ("{month_saved_gb}", RollupSavedGb(report.ThisMonth).ToString("0.##"));
        yield return ("{year_files}", (report.ThisYear?.FileCount ?? 0).ToString());
        yield return ("{year_saved_gb}", RollupSavedGb(report.ThisYear).ToString("0.##"));
    }

    private static double RollupSavedGb(HistoryRollup? rollup) => rollup is null ? 0 : rollup.BeforeGb - rollup.AfterGb;
}
