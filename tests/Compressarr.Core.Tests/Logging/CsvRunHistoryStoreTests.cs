using Compressarr.Core.Config;
using Compressarr.Core.Logging;
using Compressarr.Core.Tests.Presets;

namespace Compressarr.Core.Tests.Logging;

public class CsvRunHistoryStoreTests : AppDataTestBase
{
    // Where 2.1.x kept the history (the Logs folder) - what every store call is still handed. The live file is
    // AppPaths.GetHistoryFilePath().
    private string _tempDir => Directory.CreateDirectory(Path.Combine(AppData, "Logs")).FullName;

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
    public void AppendRun_ThenGetHistory_RoundTripsTheRedirectCount()
    {
        var store = new CsvRunHistoryStore();

        store.AppendRun(_tempDir, new RunHistoryRecord(2026, 9, 3, 10, 4, 3, 0, 5, 0, RunNumber: 42, ReportFileName: "report.html", RedirectCount: 2));

        Assert.Equal(2, Assert.Single(store.GetHistory(_tempDir)).RedirectCount);
    }

    [Fact]
    public void RedirectCount_IsAnAdditiveTrailingColumn_SoEveryEarlierColumnKeepsItsPosition()
    {
        // A 2.1.x build reads columns by index and ignores anything past the last one it knows, so the new
        // column must sit at the very end and nothing before it may move.
        var store = new CsvRunHistoryStore();
        store.AppendRun(_tempDir, new RunHistoryRecord(2026, 9, 3, 10, 4, 3, 0, 5, 0, RunNumber: 42, ReportFileName: "report.html", ErrorCount: 2, WarningCount: 3, RedirectCount: 4));

        var lines = File.ReadAllLines(AppPaths.GetHistoryFilePath());
        var header = lines[0].Split(',');
        var row = lines[1].Split(',');

        Assert.Equal("WarningCount", header[12]);
        Assert.Equal("RedirectCount", header[13]);
        Assert.Equal(14, row.Length);
        Assert.Equal("report.html", row[10]);
        Assert.Equal("2", row[11]);
        Assert.Equal("3", row[12]);
        Assert.Equal("4", row[13]);
    }

    [Fact]
    public void GetHistory_RowWithoutTheRedirectColumn_DefaultsToZero()
    {
        File.WriteAllLines(Path.Combine(_tempDir, "Compressarr_History.csv"), new[]
        {
            "yyyy,mm,dd,BegSize,EndSize,FileCount,ProcessHours,ProcessMinutes,ProcessSeconds,RunNumber,ReportFileName,ErrorCount,WarningCount",
            "2026,08,15,10,4,3,0,5,0,7,old-report.html,1,2"
        });

        var result = Assert.Single(new CsvRunHistoryStore().GetHistory(_tempDir));

        Assert.Equal(2, result.WarningCount);
        Assert.Equal(0, result.RedirectCount);
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

    // ---- the history file lives in the app data folder, not the Logs folder -----------------------

    private string LegacyFile => Path.Combine(_tempDir, "Compressarr_History.csv");

    private const string Header = "yyyy,mm,dd,BegSize,EndSize,FileCount,ProcessHours,ProcessMinutes,ProcessSeconds,RunNumber,ReportFileName,ErrorCount,WarningCount";

    [Fact]
    public void AppendRun_WritesToTheAppDataFolder_NotTheLogsFolder()
    {
        new CsvRunHistoryStore().AppendRun(_tempDir, new RunHistoryRecord(2026, 9, 3, 10, 4, 3, 0, 5, 0, RunNumber: 1, ReportFileName: "r.html"));

        Assert.True(File.Exists(AppPaths.GetHistoryFilePath()));
        Assert.False(File.Exists(LegacyFile));
        Assert.StartsWith(AppData, AppPaths.GetHistoryFilePath());
    }

    [Fact]
    public void An21InstallsFileInTheLogsFolder_IsCarriedAcrossOnFirstUse_AndTheOldCopyRemoved()
    {
        File.WriteAllLines(LegacyFile, new[] { Header, "2026,08,15,10,4,3,0,5,0,7,old-report.html,1,2" });
        var store = new CsvRunHistoryStore();

        var first = store.GetHistory(_tempDir);
        store.AppendRun(_tempDir, new RunHistoryRecord(2026, 9, 3, 10, 4, 3, 0, 5, 0, RunNumber: 8, ReportFileName: "new.html"));

        Assert.Equal(7, Assert.Single(first).RunNumber);
        Assert.Equal(new[] { 7, 8 }, store.GetHistory(_tempDir).Select(r => r.RunNumber).ToArray());
        Assert.True(File.Exists(AppPaths.GetHistoryFilePath()));
        Assert.False(File.Exists(LegacyFile)); // a single live file - a stale copy would come back after Clear History
    }

    [Fact]
    public void WhenTheAppDataFileExists_AFileLeftInTheLogsFolderIsNotMergedInOrOverwritten()
    {
        File.WriteAllLines(AppPaths.GetHistoryFilePath(), new[] { Header, "2026,09,01,10,4,3,0,5,0,50,live.html,0,0" });
        File.WriteAllLines(LegacyFile, new[] { Header, "2026,08,15,99,4,3,0,5,0,7,stale.html,0,0" });

        var history = new CsvRunHistoryStore().GetHistory(_tempDir);

        Assert.Equal(50, Assert.Single(history).RunNumber);
        Assert.True(File.Exists(LegacyFile)); // left exactly as it was
    }

    [Fact]
    public void NoFileAnywhere_IsAnEmptyHistory_AndCreatesNothing()
    {
        var history = new CsvRunHistoryStore().GetHistory(_tempDir);

        Assert.Empty(history);
        Assert.False(File.Exists(AppPaths.GetHistoryFilePath()));
    }
}
