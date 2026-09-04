using System.IO.Compression;
using Compressarr.Core.Config;
using Compressarr.Core.Orchestration;
using Compressarr.Core.Routing;

namespace Compressarr.Core.Backup;

public sealed record BackupResult(bool Success, string? FileName, string? Error);

public sealed record BackupFileInfo(string FileName, long SizeBytes, DateTimeOffset CreatedUtc);

public interface IBackupService
{
    /// <summary>Bundles settings/lanes, the run counter, resume state, and the history CSV into a
    /// timestamped zip in the configured backup folder, then prunes bundles older than retention.
    /// Reloads config itself, so it always acts on whatever was most recently saved rather than a
    /// caller's possibly-stale copy.</summary>
    Task<BackupResult> RunBackupAsync();

    /// <summary>Lists the zip files in folderOverride (or the saved config's Backup.FolderPath if
    /// null/blank), newest first. Empty (not an error) if the folder doesn't exist yet - e.g.
    /// before the very first backup has run. Accepting an override (rather than only ever reading
    /// the saved config) is what lets Settings show/restore from a folder the user has typed or
    /// browsed to but not saved yet - including on a first launch/new machine, before any
    /// settings have ever been saved at all.</summary>
    IReadOnlyList<BackupFileInfo> ListBackups(string? folderOverride = null);

    /// <summary>Restores settings/lanes (validated + merged-over-defaults, same as
    /// /api/settings/import), the run counter, resume state, and the history CSV from a backup
    /// zip previously created by RunBackupAsync. fileName is resolved against folderOverride (or
    /// the saved config's folder if null/blank) - Path.GetFileName strips any path components
    /// first, so this can't escape that folder. Overwrites whichever of those files the backup
    /// actually contains - anything the backup doesn't include (e.g. no resume.json because none
    /// existed at backup time) is left untouched rather than deleted.</summary>
    Task<BackupResult> RestoreBackupAsync(string fileName, string? folderOverride = null);
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

    private string ResolveFolder(string? folderOverride, CompressarrConfig config) =>
        _pathExpander.Expand(string.IsNullOrWhiteSpace(folderOverride) ? config.Backup.FolderPath : folderOverride);

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

    public IReadOnlyList<BackupFileInfo> ListBackups(string? folderOverride = null)
    {
        var config = _configStore.Load(AppPaths.GetConfigFilePath());
        var folder = ResolveFolder(folderOverride, config);
        if (!Directory.Exists(folder)) return Array.Empty<BackupFileInfo>();

        return Directory.EnumerateFiles(folder, "*.zip")
            .Select(f => new FileInfo(f))
            .OrderByDescending(f => f.CreationTimeUtc)
            .Select(f => new BackupFileInfo(f.Name, f.Length, f.CreationTimeUtc))
            .ToList();
    }

    public async Task<BackupResult> RestoreBackupAsync(string fileName, string? folderOverride = null)
    {
        try
        {
            var config = _configStore.Load(AppPaths.GetConfigFilePath());
            var folder = ResolveFolder(folderOverride, config);

            // Strips any directory component the caller might have sent, so this can only ever
            // read a file that's directly inside the resolved backup folder - never an arbitrary
            // path elsewhere on disk.
            var safeName = Path.GetFileName(fileName);
            var zipPath = Path.Combine(folder, safeName);

            if (!File.Exists(zipPath))
            {
                return new BackupResult(false, null, "Backup file not found.");
            }

            var tempDir = Path.Combine(Path.GetTempPath(), $"compressarr-restore-{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);
            try
            {
                ZipFile.ExtractToDirectory(zipPath, tempDir, overwriteFiles: true);

                // Settings/lanes go through the same validate + merge-over-defaults path
                // /api/settings/import already uses (load the extracted file through IConfigStore,
                // then Save it as the real config), rather than a raw file copy - an
                // older/partial backup still restores cleanly.
                CompressarrConfig? restoredConfig = null;
                var extractedSettings = Path.Combine(tempDir, "compressarr.settings.json");
                if (File.Exists(extractedSettings))
                {
                    restoredConfig = _configStore.Load(extractedSettings);
                    _configStore.Save(restoredConfig, AppPaths.GetConfigFilePath());
                }

                RestoreEntry(tempDir, "compressarr.runcount.json", AppPaths.GetRunCountFilePath());
                RestoreEntry(tempDir, "compressarr.resume.json", AppPaths.GetResumeFilePath());

                // Lands wherever the just-restored config's Log folder points - falls back to the
                // pre-restore config if this particular backup didn't include settings at all.
                var effectiveConfig = restoredConfig ?? config;
                var historyDest = Path.Combine(_pathExpander.Expand(effectiveConfig.Logging.LogFilePath), "Compressarr_History.csv");
                RestoreEntry(tempDir, "Compressarr_History.csv", historyDest);

                return new BackupResult(true, safeName, null);
            }
            finally
            {
                try { Directory.Delete(tempDir, recursive: true); } catch { }
            }
        }
        catch (Exception ex)
        {
            return new BackupResult(false, null, ex.Message);
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

    /// <summary>Copies entryName from inside an already-extracted backup (tempDir) to destPath, if
    /// that entry was actually present in the backup - a no-op otherwise (restoring an older
    /// backup that predates a given file shouldn't delete whatever's there now). Extracted as its
    /// own internal static method, same reasoning as CreateBundleZip - testable against real temp
    /// paths without needing to fake the AppData-rooted destinations RestoreBackupAsync resolves
    /// for the run counter/resume file.</summary>
    internal static void RestoreEntry(string tempDir, string entryName, string destPath)
    {
        var source = Path.Combine(tempDir, entryName);
        if (!File.Exists(source)) return;

        var destDir = Path.GetDirectoryName(destPath);
        if (!string.IsNullOrEmpty(destDir)) Directory.CreateDirectory(destDir);
        File.Copy(source, destPath, overwrite: true);
    }
}
