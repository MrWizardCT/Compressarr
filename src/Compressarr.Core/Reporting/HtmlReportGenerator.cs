using System.Net;
using System.Reflection;
using System.Text;

namespace Compressarr.Core.Reporting;

public interface IHtmlReportGenerator
{
    /// <summary>Builds a fully self-contained HTML report — inline CSS, base64-embedded logo/
    /// favicon, no external references. All user-controlled text is HTML-encoded before
    /// interpolation. Layout ported from v1.1's New-CompressarrReport/
    /// Get-CompressarrLaneReportSection (Compressarr.Reporting.psm1) - the user preferred that
    /// format over v2's original dark-theme report.</summary>
    string Generate(ReportModel model);
}

public sealed class HtmlReportGenerator : IHtmlReportGenerator
{
    private const string LogoResourceName = "Compressarr.Core.Assets.CompressarrLogo.png";
    private const string FaviconResourceName = "Compressarr.Core.Assets.CompressarrFavicon.ico";

    public string Generate(ReportModel model)
    {
        var totalFiles = model.Lanes.Sum(l => l.Results.Count);
        var totalBeg = Math.Round(model.Lanes.Sum(l => l.Results.Sum(r => r.BeginSizeGb)), 3);
        var totalEnd = Math.Round(model.Lanes.Sum(l => l.Results.Sum(r => r.EndSizeGb)), 3);
        var totalSavings = Math.Round(totalBeg - totalEnd, 3);
        var savingsPct = totalBeg > 0 ? Math.Round(100 - (totalEnd / totalBeg) * 100, 2) : 0;
        var errorCount = model.ErrorCount;

        var logoTag = LoadEmbeddedBase64(LogoResourceName) is { } logoB64
            ? $"<img src=\"data:image/png;base64,{logoB64}\" alt=\"Compressarr\" class=\"logo\" />"
            : "";
        var faviconTag = LoadEmbeddedBase64(FaviconResourceName) is { } faviconB64
            ? $"<link rel=\"icon\" type=\"image/x-icon\" href=\"data:image/x-icon;base64,{faviconB64}\">"
            : "";
        var runLabel = model.RunNumber > 0 ? $"Run #{model.RunNumber}:" : "Run:";
        var timestamp = model.GeneratedAt.ToString("yyyy-MM-dd HH:mm:ss");

        var statusBanner = errorCount > 0
            ? $"<div class=\"banner err\">{errorCount} error(s) occurred - see the per-lane tables below and the detail logs.</div>"
            : "<div class=\"banner ok\">Run completed with no errors.</div>";

        var sb = new StringBuilder();
        sb.Append($@"<!DOCTYPE html>
<html>
<head>
<meta charset=""utf-8"" />
<title>Compressarr Report - {WebUtility.HtmlEncode(timestamp)}</title>
{faviconTag}
<style>
  body {{ font-family: Segoe UI, Verdana, sans-serif; margin: 2rem; color: #1c1c1c; background: #fafafa; }}
  h1 {{ margin-bottom: 0; }}
  .header {{ display: flex; align-items: center; gap: 0.75rem; }}
  .header h1 {{ line-height: 1; position: relative; top: -12px; }}
  .logo {{ width: 48px; height: 48px; }}
  .muted {{ color: #666; font-weight: normal; font-size: 0.85em; }}
  .banner {{ padding: 0.75rem 1rem; border-radius: 6px; margin: 1rem 0; font-weight: 600; }}
  .banner.ok {{ background: #e3f7e8; color: #16693a; }}
  .banner.err {{ background: #fdeaea; color: #a1231e; }}
  .table-wrap {{ overflow-x: auto; margin: 0.5rem 0 1.5rem 0; }}
  table {{ border-collapse: collapse; width: 100%; min-width: 640px; margin: 0; background: #fff; }}
  th, td {{ border: 1px solid #ddd; padding: 6px 10px; text-align: left; font-size: 0.9em; }}
  th {{ background: #2c3e50; color: #fff; }}
  tr.err {{ background: #fdeaea; }}
  tr.warn {{ background: #fdf6e3; }}
  .warn-text {{ color: #8a6414; }}
  .summary-grid {{ display: flex; gap: 2rem; flex-wrap: wrap; margin: 1rem 0; }}
  .stat {{ background: #fff; border: 1px solid #ddd; border-radius: 6px; padding: 0.75rem 1.25rem; min-width: 140px; }}
  .stat .label {{ font-size: 0.8em; color: #666; }}
  .stat .value {{ font-size: 1.4em; font-weight: 700; }}
</style>
</head>
<body>
<div class=""header"">
  {logoTag}
  <h1>Compressarr Report</h1>
</div>
<p class=""muted"">{WebUtility.HtmlEncode(runLabel)} {WebUtility.HtmlEncode(timestamp)} &nbsp;|&nbsp; Duration: {model.RunTime.Hours}h {model.RunTime.Minutes}m {model.RunTime.Seconds}s</p>
{statusBanner}
<div class=""summary-grid"">
  <div class=""stat""><div class=""label"">Files processed</div><div class=""value"">{totalFiles}</div></div>
  <div class=""stat""><div class=""label"">Before</div><div class=""value"">{totalBeg} GB</div></div>
  <div class=""stat""><div class=""label"">After</div><div class=""value"">{totalEnd} GB</div></div>
  <div class=""stat""><div class=""label"">Saved</div><div class=""value"">{totalSavings} GB ({savingsPct}%)</div></div>
  <div class=""stat""><div class=""label"">Errors</div><div class=""value"">{errorCount}</div></div>
</div>
<h2>By lane</h2>
");

        foreach (var lane in model.Lanes)
        {
            AppendLaneSection(sb, lane);
        }

        if (model.Today is not null || model.ThisMonth is not null || model.ThisYear is not null)
        {
            AppendHistorySection(sb, model);
        }

        sb.Append("</body>\n</html>\n");
        return sb.ToString();
    }

    private static void AppendLaneSection(StringBuilder sb, LaneReportSection lane)
    {
        if (lane.Results.Count == 0)
        {
            sb.Append($"<h3>{WebUtility.HtmlEncode(lane.LaneDisplayName)}</h3>\n<p class=\"muted\">No files processed.</p>\n");
            return;
        }

        var beg = Math.Round(lane.Results.Sum(r => r.BeginSizeGb), 3);
        var end = Math.Round(lane.Results.Sum(r => r.EndSizeGb), 3);

        sb.Append($"<h3>{WebUtility.HtmlEncode(lane.LaneDisplayName)} <span class=\"muted\">({lane.Results.Count} file(s), {beg} GB &rarr; {end} GB)</span></h3>\n");
        sb.Append("<div class=\"table-wrap\">\n<table>\n  <thead><tr><th>File</th><th>Type</th><th>Preset</th><th>Before</th><th>After</th><th>Savings</th><th>Status</th><th>Sonarr/Radarr</th></tr></thead>\n  <tbody>\n");

        foreach (var r in lane.Results)
        {
            var hasWarning = r.Success && !string.IsNullOrEmpty(r.PostProcessWarning);
            var rowClass = !r.Success ? " class=\"err\"" : hasWarning ? " class=\"warn\"" : "";
            var savings = Math.Round(r.BeginSizeGb - r.EndSizeGb, 3);
            var status = r.Success ? "OK" : (r.FailureReason ?? "ERROR");
            var statusHtml = WebUtility.HtmlEncode(status);
            if (!r.Success && !string.IsNullOrEmpty(r.DetailLogFile) && File.Exists(r.DetailLogFile))
            {
                // The detail log is a plain local file, same machine the report itself lives on -
                // a file:// link resolves whether the report was opened directly (the common case)
                // or viewed through the app's own /api/reports/ route.
                var detailUri = new Uri(r.DetailLogFile).AbsoluteUri;
                statusHtml += $"<br><a href=\"{detailUri}\" target=\"_blank\" rel=\"noopener\">Full Details</a>";
            }
            if (hasWarning)
            {
                // A secondary post-process step (companion files, Sonarr/Radarr unmonitor) had a
                // problem even though the conversion itself succeeded - flagged distinctly from a
                // real failure (no red row, doesn't count toward the error total) so it doesn't
                // get missed, without overstating what actually went wrong.
                statusHtml += $"<br><span class=\"warn-text\">&#9888; {WebUtility.HtmlEncode(r.PostProcessWarning)}</span>";
            }
            // Em dash, matching v1.1: blank whenever Sonarr/Radarr integration wasn't attempted
            // for this file - either the conversion failed, or neither service is enabled for
            // this content type - not just when it succeeded/failed.
            var arrStatus = string.IsNullOrEmpty(r.ArrStatus) ? "—" : r.ArrStatus;

            sb.Append($"    <tr{rowClass}>");
            sb.Append($"<td>{WebUtility.HtmlEncode(r.FileName)}</td>");
            sb.Append($"<td>{WebUtility.HtmlEncode(r.ContentType)}</td>");
            sb.Append($"<td>{WebUtility.HtmlEncode(r.PresetName ?? "")}</td>");
            sb.Append($"<td>{r.BeginSizeGb} GB</td>");
            sb.Append($"<td>{r.EndSizeGb} GB</td>");
            sb.Append($"<td>{savings} GB</td>");
            sb.Append($"<td>{statusHtml}</td>");
            sb.Append($"<td>{WebUtility.HtmlEncode(arrStatus)}</td>");
            sb.Append("</tr>\n");
        }

        sb.Append("  </tbody>\n</table>\n</div>\n");
    }

    private static void AppendHistorySection(StringBuilder sb, ReportModel model)
    {
        sb.Append("<h2>History</h2>\n<div class=\"table-wrap\">\n<table>\n  <thead><tr><th>Period</th><th>Before</th><th>After</th><th>Savings</th><th>Files</th></tr></thead>\n  <tbody>\n");

        void Row(string label, HistoryRollup? rollup)
        {
            if (rollup is null) return;
            var pct = rollup.BeforeGb > 0 ? Math.Round(100 - (rollup.AfterGb / rollup.BeforeGb) * 100, 2) : 0;
            sb.Append($"    <tr><td>{WebUtility.HtmlEncode(label)}</td><td>{rollup.BeforeGb} GB</td><td>{rollup.AfterGb} GB</td><td>{pct}%</td><td>{rollup.FileCount}</td></tr>\n");
        }

        Row("Today", model.Today);
        Row("This month", model.ThisMonth);
        Row("This year", model.ThisYear);

        sb.Append("  </tbody>\n</table>\n</div>\n");
    }

    private static string? LoadEmbeddedBase64(string resourceName)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName);
        if (stream is null) return null;

        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return Convert.ToBase64String(ms.ToArray());
    }
}
