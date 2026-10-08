using System.IO.Compression;
using Compressarr.Core.Backup;
using Compressarr.Core.Config;
using Compressarr.Core.Routing;
using Compressarr.Core.Tests.Presets;

namespace Compressarr.Core.Tests.Backup;

file sealed class PassThroughPathExpander : IPathExpander
{
    public string Expand(string value) => value;
    public bool PathExists(string value) => Directory.Exists(value) || File.Exists(value);
}

file sealed class FixedConfigStore : IConfigStore
{
    public CompressarrConfig Config { get; }
    public FixedConfigStore(CompressarrConfig config) => Config = config;

    public CompressarrConfig Load(string path) => Config;
    public void Save(CompressarrConfig config, string path) { }
    public T Update<T>(string path, Func<CompressarrConfig, T> mutate) => mutate(Config);
}

file sealed class NoTrash : ITrashService
{
    public void DeleteFile(string path, DeleteAfterConvertMode mode) { }
    public void DeleteFolder(string path, DeleteAfterConvertMode mode) { }
}

/// <summary>The run history is part of every backup, wherever it currently lives: the app data folder (2.2), or -
/// until 2.2 has started once - the Logs folder where 2.1.x kept it.</summary>
public class BackupHistoryFileTests : AppDataTestBase
{
    private const string Rows = "yyyy,mm,dd,BegSize,EndSize\n2026,09,01,10,4\n";

    private (BackupService Service, string BackupFolder, string LogFolder) Build()
    {
        var backupFolder = Path.Combine(AppData, "Backups");
        var logFolder = Path.Combine(AppData, "Logs");
        var config = new CompressarrConfig();
        config.Backup.FolderPath = backupFolder;
        config.Logging.LogFilePath = logFolder;
        return (new BackupService(new FixedConfigStore(config), new PassThroughPathExpander(), new NoTrash()), backupFolder, logFolder);
    }

    private static string[] EntriesOf(string folder)
    {
        using var zip = ZipFile.OpenRead(Assert.Single(Directory.GetFiles(folder, "Compressarr_Backup_*.zip")));
        return zip.Entries.Select(e => e.Name).ToArray();
    }

    [Fact]
    public async Task TheHistoryInTheAppDataFolder_IsBackedUp_AndRestoredThere()
    {
        var (service, backupFolder, _) = Build();
        File.WriteAllText(AppPaths.GetHistoryFilePath(), Rows);

        var backup = await service.RunBackupAsync();
        File.Delete(AppPaths.GetHistoryFilePath());
        var restore = await service.RestoreBackupAsync(backup.FileName!, backupFolder);

        Assert.Contains("Compressarr_History.csv", EntriesOf(backupFolder));
        Assert.True(restore.Success);
        Assert.Equal(Rows, File.ReadAllText(AppPaths.GetHistoryFilePath()));
    }

    [Fact]
    public async Task AnUnmigrated21HistoryInTheLogsFolder_IsStillBackedUp()
    {
        var (service, backupFolder, logFolder) = Build();
        Directory.CreateDirectory(logFolder);
        File.WriteAllText(Path.Combine(logFolder, "Compressarr_History.csv"), Rows);

        await service.RunBackupAsync();

        Assert.Contains("Compressarr_History.csv", EntriesOf(backupFolder));
    }
}
