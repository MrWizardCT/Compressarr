using Compressarr.Core.Logging;

namespace Compressarr.Core.Tests.Logging;

public class CsvRunHistoryStoreTests : IDisposable
{
    private readonly string _tempDir = Directory.CreateTempSubdirectory("compressarr-history-store-tests-").FullName;

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    [Fact]
    public void AppendRun_ThenGetHistory_RoundTripsErrorAndWarningCount()
    {
        var store = new CsvRunHistoryStore();
        var record = new RunHistoryRecord(
            Year: 2026, Month: 9, Day: 3, BeginSizeGb: 10, EndSizeGb: 4, FileCount: 3,
            ProcessHours: 0, ProcessMinutes: 5, ProcessSeconds: 0,
            RunNumber: 42, ReportFileName: "report.html",
            ErrorCount: 2, WarningCount: 3);

        store.AppendRun(_tempDir, record);
        var history = store.GetHistory(_tempDir);

        var result = Assert.Single(history);
        Assert.Equal(2, result.ErrorCount);
        Assert.Equal(3, result.WarningCount);
    }

    [Fact]
    public void GetHistory_OldRowWithoutErrorWarningColumns_DefaultsToZero()
    {
        // Simulates a real pre-upgrade CSV row - 11 columns, written before ErrorCount/WarningCount
        // existed. Must not throw, and must not be mistaken for a row with actual errors/warnings.
        var historyFile = Path.Combine(_tempDir, "Compressarr_History.csv");
        File.WriteAllLines(historyFile, new[]
        {
            "yyyy,mm,dd,BegSize,EndSize,FileCount,ProcessHours,ProcessMinutes,ProcessSeconds,RunNumber,ReportFileName",
            "2026,08,15,10,4,3,0,5,0,7,old-report.html"
        });

        var store = new CsvRunHistoryStore();
        var history = store.GetHistory(_tempDir);

        var result = Assert.Single(history);
        Assert.Equal(7, result.RunNumber);
        Assert.Equal(0, result.ErrorCount);
        Assert.Equal(0, result.WarningCount);
    }
}
