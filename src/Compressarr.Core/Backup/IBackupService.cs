using System.IO.Compression;
using Compressarr.Core.Config;
using Compressarr.Core.Orchestration;
using Compressarr.Core.Routing;

namespace Compressarr.Core.Backup;

public sealed record BackupResult(bool Success, string? FileName, string? Error);

public interface IBackupService
{
    /// <summary>Bundles settings/lanes, the run counter, resume state, and the history CSV into a
    /// timestamped zip in the configured backup folder, then prunes bundles older than retention.
    /// Reloads config itself, so it always acts on whatever was most recently saved rather than a
    /// caller's possibly-stale copy.</summary>
    Task<BackupResult> RunBackupAsync();
}

public sealed class BackupService : IBackupService
{
    private readonly IConfigStore _configStore;
    private readonly IPathExpander _pathExpander;
    private readonly ITrashService _trash;

    public BackupService(IConfigStore configStore, IPathExpander pathExpander, ITrashService trash)
    {
        _configStore = configStore;
        _pathExpander = pathExpander;
        _trash = trash;
    }

    public Task<BackupResult> RunBackupAsync()
    {
        try
        {
            var config = _configStore.Load(AppPaths.GetConfigFilePath());
            var folder = _pathExpander.Expand(config.Backup.FolderPath);
            Directory.CreateDirectory(folder);

            var fileName = $"Compressarr_Backup_{DateTime.Now:yyyy-MM-dd_HHmmss}.zip";
            var zipPath = Path.Combine(folder, fileName);

            // Settings/lanes (one file - Lanes is a property inside compressarr.settings.json, not
            // a separate file), the run counter, resume/run state, and the history CSV. Deliberately
            // excludes presets.json (HandBrake's own file, lives outside Compressarr's AppData tree)
            // and the Reports folder (regenerable HTML dumps, already retention-pruned).
            var historyFile = Path.Combine(_pathExpander.Expand(config.Logging.LogFilePath), "Compressarr_History.csv");
            var sources = new[]
            {
                AppPaths.GetConfigFilePath(),
                AppPaths.GetRunCountFilePath(),
                AppPaths.GetResumeFilePath(),
                historyFile
            };

            CreateBundleZip(zipPath, sources);

            RetentionCleaner.CleanUp(_trash, folder, new[] { ".zip" }, config.Backup.RetentionDays, "backup");

            _configStore.Update(AppPaths.GetConfigFilePath(), c =>
            {
                c.Backup.LastRunUtc = DateTimeOffset.UtcNow;
                return true;
            });

            return Task.FromResult(new BackupResult(true, fileName, null));
        }
        catch (Exception ex)
        {
            return Task.FromResult(new BackupResult(false, null, ex.Message));
        }
    }

    /// <summary>Writes whichever of sourceFiles actually exist into a new zip at zipPath, one entry
    /// per file (missing ones are silently skipped - e.g. a fresh install with no resume.json yet).
    /// Extracted as its own internal static method, same pattern as
    /// HandBrakeProcessRunner.DetermineSuccess, so it's testable against real temp files without
    /// needing to fake the AppData-rooted paths RunBackupAsync resolves its sources from.</summary>
    internal static void CreateBundleZip(string zipPath, IEnumerable<string> sourceFiles)
    {
        using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        foreach (var source in sourceFiles)
        {
            if (File.Exists(source))
            {
                zip.CreateEntryFromFile(source, Path.GetFileName(source));
            }
        }
    }
}
