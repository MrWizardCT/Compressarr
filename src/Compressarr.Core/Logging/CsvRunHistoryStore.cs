using System.Globalization;

namespace Compressarr.Core.Logging;

/// <summary>Ported from Add-CompressarrHistoryRecord/Get-CompressarrHistory and
/// Get-CompressarrRunCount/Set-CompressarrRunCount. Hand-rolled CSV (9 fixed numeric/date-part
/// columns, no embedded commas or quoting concerns) rather than a full CSV library — this is the
/// seam Phase 4 replaces with a structured store for the history-rollup/CPU/web-monitor features.</summary>
public sealed class CsvRunHistoryStore : IRunHistoryStore
{
    private const string HistoryFileName = "Compressarr_History.csv";
    private static readonly string[] Header =
    {
        "yyyy", "mm", "dd", "BegSize", "EndSize", "FileCount", "ProcessHours", "ProcessMinutes", "ProcessSeconds",
        "RunNumber", "ReportFileName", "ErrorCount", "WarningCount"
    };

    public void AppendRun(string logFilePath, RunHistoryRecord record)
    {
        Directory.CreateDirectory(logFilePath);
        var historyFile = Path.Combine(logFilePath, HistoryFileName);

        var isNew = !File.Exists(historyFile);
        using var writer = new StreamWriter(historyFile, append: true);
        if (isNew)
        {
            writer.WriteLine(string.Join(",", Header));
        }

        var fields = new[]
        {
            record.Year.ToString(CultureInfo.InvariantCulture),
            record.Month.ToString("00", CultureInfo.InvariantCulture),
            record.Day.ToString("00", CultureInfo.InvariantCulture),
            record.BeginSizeGb.ToString(CultureInfo.InvariantCulture),
            record.EndSizeGb.ToString(CultureInfo.InvariantCulture),
            record.FileCount.ToString(CultureInfo.InvariantCulture),
            record.ProcessHours.ToString(CultureInfo.InvariantCulture),
            record.ProcessMinutes.ToString(CultureInfo.InvariantCulture),
            record.ProcessSeconds.ToString(CultureInfo.InvariantCulture),
            record.RunNumber.ToString(CultureInfo.InvariantCulture),
            record.ReportFileName,
            record.ErrorCount.ToString(CultureInfo.InvariantCulture),
            record.WarningCount.ToString(CultureInfo.InvariantCulture)
        };
        writer.WriteLine(string.Join(",", fields));
    }

    public IReadOnlyList<RunHistoryRecord> GetHistory(string logFilePath)
    {
        var historyFile = Path.Combine(logFilePath, HistoryFileName);
        if (!File.Exists(historyFile)) return Array.Empty<RunHistoryRecord>();

        var lines = File.ReadAllLines(historyFile);
        var results = new List<RunHistoryRecord>();

        foreach (var line in lines.Skip(1))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var parts = line.Split(',');
            if (parts.Length < 9) continue;

            // RunNumber/ReportFileName/ErrorCount/WarningCount were all added later - rows written
            // before then simply don't have columns 9-12, so default them rather than
            // rejecting/mis-parsing older rows.
            var runNumber = parts.Length > 9 && int.TryParse(parts[9], NumberStyles.Integer, CultureInfo.InvariantCulture, out var rn) ? rn : 0;
            var reportFileName = parts.Length > 10 ? parts[10] : "";
            var errorCount = parts.Length > 11 && int.TryParse(parts[11], NumberStyles.Integer, CultureInfo.InvariantCulture, out var ec) ? ec : 0;
            var warningCount = parts.Length > 12 && int.TryParse(parts[12], NumberStyles.Integer, CultureInfo.InvariantCulture, out var wc) ? wc : 0;

            results.Add(new RunHistoryRecord(
                Year: int.Parse(parts[0], CultureInfo.InvariantCulture),
                Month: int.Parse(parts[1], CultureInfo.InvariantCulture),
                Day: int.Parse(parts[2], CultureInfo.InvariantCulture),
                BeginSizeGb: double.Parse(parts[3], CultureInfo.InvariantCulture),
                EndSizeGb: double.Parse(parts[4], CultureInfo.InvariantCulture),
                FileCount: int.Parse(parts[5], CultureInfo.InvariantCulture),
                ProcessHours: int.Parse(parts[6], CultureInfo.InvariantCulture),
                ProcessMinutes: int.Parse(parts[7], CultureInfo.InvariantCulture),
                ProcessSeconds: int.Parse(parts[8], CultureInfo.InvariantCulture),
                RunNumber: runNumber,
                ReportFileName: reportFileName,
                ErrorCount: errorCount,
                WarningCount: warningCount));
        }

        return results;
    }

    public int GetRunCount(string runCountPath)
    {
        if (!File.Exists(runCountPath)) return 0;
        var raw = File.ReadAllText(runCountPath).Trim();
        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var count) ? count : 0;
    }

    public void IncrementRunCount(string runCountPath)
    {
        var count = GetRunCount(runCountPath) + 1;
        var folder = Path.GetDirectoryName(runCountPath);
        if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
        File.WriteAllText(runCountPath, count.ToString(CultureInfo.InvariantCulture));
    }
}
