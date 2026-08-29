using System.Net;
using System.Text;

namespace Compressarr.Core.Reporting;

public interface IHtmlReportGenerator
{
    /// <summary>Builds a fully self-contained HTML report — inline CSS, base64-embedded logo if
    /// provided, no external references. All user-controlled text is HTML-encoded before
    /// interpolation. Ported from New-CompressarrReport/Get-CompressarrLaneReportSection.</summary>
    string Generate(ReportModel model, byte[]? logoPngBytes);
}

public sealed class HtmlReportGenerator : IHtmlReportGenerator
{
    public string Generate(ReportModel model, byte[]? logoPngBytes)
    {
        var logoTag = logoPngBytes is null
            ? ""
            : $"<img src=\"data:image/png;base64,{Convert.ToBase64String(logoPngBytes)}\" alt=\"Compressarr\" class=\"logo\" />";

        var statusClass = model.ErrorCount > 0 ? "status-error" : "status-ok";
        var statusText = model.ErrorCount > 0 ? $"{model.ErrorCount} error(s)" : "No errors";

        var sb = new StringBuilder();
        sb.Append($@"<!DOCTYPE html>
<html>
<head>
<meta charset=""utf-8"" />
<title>Compressarr Report</title>
<style>
  body {{ font-family: Segoe UI, Arial, sans-serif; background: #1e1e1e; color: #e0e0e0; margin: 0; padding: 24px; }}
  .header {{ display: flex; align-items: center; gap: 16px; margin-bottom: 16px; }}
  .logo {{ height: 48px; }}
  h1 {{ margin: 0; font-size: 22px; }}
  .status-banner {{ padding: 10px 16px; border-radius: 6px; font-weight: bold; margin-bottom: 16px; }}
  .status-ok {{ background: #1e4620; color: #8fd98f; }}
  .status-error {{ background: #4a1e1e; color: #f79999; }}
  .stat-grid {{ display: flex; gap: 16px; margin-bottom: 24px; flex-wrap: wrap; }}
  .stat {{ background: #2a2a2a; border-radius: 6px; padding: 12px 20px; min-width: 120px; }}
  .stat .label {{ font-size: 12px; color: #999; }}
  .stat .value {{ font-size: 20px; font-weight: bold; }}
  table {{ border-collapse: collapse; width: 100%; margin-bottom: 24px; }}
  th, td {{ border: 1px solid #3a3a3a; padding: 6px 10px; text-align: left; font-size: 13px; }}
  th {{ background: #2a2a2a; }}
  tr.err {{ background: #3a1e1e; }}
  h3 {{ border-bottom: 1px solid #3a3a3a; padding-bottom: 4px; }}
</style>
</head>
<body>
  <div class=""header"">
    {logoTag}
    <h1>Compressarr Report — {WebUtility.HtmlEncode(model.GeneratedAt.ToString("yyyy-MM-dd HH:mm:ss"))}</h1>
  </div>
  <div class=""status-banner {statusClass}"">{WebUtility.HtmlEncode(statusText)}</div>
  <div class=""stat-grid"">
    <div class=""stat""><div class=""label"">Files</div><div class=""value"">{model.TotalFiles}</div></div>
    <div class=""stat""><div class=""label"">Before</div><div class=""value"">{model.TotalBeforeGb:N2} GB</div></div>
    <div class=""stat""><div class=""label"">After</div><div class=""value"">{model.TotalAfterGb:N2} GB</div></div>
    <div class=""stat""><div class=""label"">Saved</div><div class=""value"">{(model.TotalBeforeGb - model.TotalAfterGb):N2} GB</div></div>
    <div class=""stat""><div class=""label"">Errors</div><div class=""value"">{model.ErrorCount}</div></div>
  </div>
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
        sb.Append($"  <h3>{WebUtility.HtmlEncode(lane.LaneDisplayName)}</h3>\n");
        sb.Append("  <table>\n    <tr><th>File</th><th>Type</th><th>Preset</th><th>Before</th><th>After</th><th>Savings</th><th>Status</th><th>Sonarr/Radarr</th></tr>\n");

        foreach (var r in lane.Results)
        {
            var rowClass = r.Success ? "" : " class=\"err\"";
            var savings = r.BeginSizeGb - r.EndSizeGb;
            var status = r.Success ? "OK" : "FAILED";

            sb.Append($"    <tr{rowClass}>");
            sb.Append($"<td>{WebUtility.HtmlEncode(r.FileName)}</td>");
            sb.Append($"<td>{WebUtility.HtmlEncode(r.ContentType)}</td>");
            sb.Append($"<td>{WebUtility.HtmlEncode(r.PresetName ?? "")}</td>");
            sb.Append($"<td>{r.BeginSizeGb:N3} GB</td>");
            sb.Append($"<td>{r.EndSizeGb:N3} GB</td>");
            sb.Append($"<td>{savings:N3} GB</td>");
            sb.Append($"<td>{WebUtility.HtmlEncode(status)}</td>");
            sb.Append($"<td>{WebUtility.HtmlEncode(r.ArrStatus ?? "")}</td>");
            sb.Append("</tr>\n");
        }

        sb.Append("  </table>\n");
    }

    private static void AppendHistorySection(StringBuilder sb, ReportModel model)
    {
        sb.Append("  <h3>History</h3>\n  <table>\n    <tr><th>Period</th><th>Files</th><th>Before</th><th>After</th></tr>\n");

        void Row(string label, HistoryRollup? rollup)
        {
            if (rollup is null) return;
            sb.Append($"    <tr><td>{WebUtility.HtmlEncode(label)}</td><td>{rollup.FileCount}</td><td>{rollup.BeforeGb:N2} GB</td><td>{rollup.AfterGb:N2} GB</td></tr>\n");
        }

        Row("Today", model.Today);
        Row("This Month", model.ThisMonth);
        Row("This Year", model.ThisYear);

        sb.Append("  </table>\n");
    }
}
