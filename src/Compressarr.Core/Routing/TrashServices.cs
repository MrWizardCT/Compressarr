using Compressarr.Core.Config;

namespace Compressarr.Core.Routing;

public interface ITrashWarningSink
{
    void Warn(string message);
}

/// <summary>No-op sink for callers/tests that don't care about trash-fallback warnings.</summary>
public sealed class NullTrashWarningSink : ITrashWarningSink
{
    public void Warn(string message) { }
}

public abstract class TrashServiceBase : ITrashService
{
    private readonly ITrashWarningSink _warnings;

    protected TrashServiceBase(ITrashWarningSink warnings)
    {
        _warnings = warnings;
    }

    public void DeleteFile(string path, DeleteAfterConvertMode mode)
    {
        if (mode == DeleteAfterConvertMode.Maintain) return;

        if (mode == DeleteAfterConvertMode.Recycle)
        {
            try
            {
                MoveFileToTrash(path);
                return;
            }
            catch (Exception ex)
            {
                _warnings.Warn($"Compressarr: recycle failed for '{path}' ({ex.Message}) - deleting instead.");
            }
        }

        try { File.Delete(path); } catch { /* best-effort, matches v1's -ErrorAction SilentlyContinue */ }
    }

    public void DeleteFolder(string path, DeleteAfterConvertMode mode)
    {
        if (mode == DeleteAfterConvertMode.Maintain) return;
        if (!Directory.Exists(path)) return;

        if (mode == DeleteAfterConvertMode.Recycle)
        {
            try
            {
                MoveFolderToTrash(path);
                return;
            }
            catch (Exception ex)
            {
                _warnings.Warn($"Compressarr: recycle failed for '{path}' ({ex.Message}) - deleting instead.");
            }
        }

        try { Directory.Delete(path, recursive: true); } catch { }
    }

    protected abstract void MoveFileToTrash(string path);
    protected abstract void MoveFolderToTrash(string path);
}

/// <summary>Uses Microsoft.VisualBasic.FileIO.FileSystem's Recycle Bin support (still works on
/// modern .NET, functional only on Windows at runtime).</summary>
public sealed class WindowsTrashService : TrashServiceBase
{
    public WindowsTrashService(ITrashWarningSink warnings) : base(warnings) { }

    protected override void MoveFileToTrash(string path) =>
        Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(path, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);

    protected override void MoveFolderToTrash(string path) =>
        Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(path, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
}

/// <summary>Good-enough Phase 1 approximation: moves into ~/.Trash with collision-suffixing.
/// No Finder integration (no .DS_Store metadata, doesn't appear with original-location info in
/// Finder's "Put Back") — a real Finder-integrated trash needs native AppleScript/Foundation
/// interop, deferred past Phase 1.</summary>
public sealed class MacTrashService : TrashServiceBase
{
    public MacTrashService(ITrashWarningSink warnings) : base(warnings) { }

    protected override void MoveFileToTrash(string path) => MoveToTrash(path, isDirectory: false);
    protected override void MoveFolderToTrash(string path) => MoveToTrash(path, isDirectory: true);

    private static void MoveToTrash(string path, bool isDirectory)
    {
        var trashDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".Trash");
        Directory.CreateDirectory(trashDir);

        var dest = UniqueDestination(trashDir, Path.GetFileName(path));
        if (isDirectory) Directory.Move(path, dest);
        else File.Move(path, dest);
    }

    private static string UniqueDestination(string trashDir, string name)
    {
        var dest = Path.Combine(trashDir, name);
        if (!File.Exists(dest) && !Directory.Exists(dest)) return dest;

        var baseName = Path.GetFileNameWithoutExtension(name);
        var ext = Path.GetExtension(name);
        for (var i = 1; ; i++)
        {
            var candidate = Path.Combine(trashDir, $"{baseName} {i}{ext}");
            if (!File.Exists(candidate) && !Directory.Exists(candidate)) return candidate;
        }
    }
}

/// <summary>Implements the XDG Trash spec directly ($XDG_DATA_HOME/Trash/files + a .trashinfo
/// sidecar per item) rather than shelling out to `gio trash`, which isn't guaranteed present.</summary>
public sealed class LinuxTrashService : TrashServiceBase
{
    public LinuxTrashService(ITrashWarningSink warnings) : base(warnings) { }

    protected override void MoveFileToTrash(string path) => MoveToTrash(path);
    protected override void MoveFolderToTrash(string path) => MoveToTrash(path);

    private static void MoveToTrash(string path)
    {
        var dataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        if (string.IsNullOrWhiteSpace(dataHome))
        {
            dataHome = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
        }

        var trashFiles = Path.Combine(dataHome, "Trash", "files");
        var trashInfo = Path.Combine(dataHome, "Trash", "info");
        Directory.CreateDirectory(trashFiles);
        Directory.CreateDirectory(trashInfo);

        var name = Path.GetFileName(path);
        var (destFile, destInfo) = UniqueDestination(trashFiles, trashInfo, name);

        var isDirectory = Directory.Exists(path);
        var deletionDate = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss");
        var infoContent = $"[Trash Info]\nPath={Uri.EscapeDataString(Path.GetFullPath(path))}\nDeletionDate={deletionDate}\n";
        File.WriteAllText(destInfo, infoContent);

        if (isDirectory) Directory.Move(path, destFile);
        else File.Move(path, destFile);
    }

    private static (string file, string info) UniqueDestination(string trashFiles, string trashInfo, string name)
    {
        var file = Path.Combine(trashFiles, name);
        var info = Path.Combine(trashInfo, name + ".trashinfo");
        if (!File.Exists(file) && !Directory.Exists(file) && !File.Exists(info)) return (file, info);

        var baseName = Path.GetFileNameWithoutExtension(name);
        var ext = Path.GetExtension(name);
        for (var i = 1; ; i++)
        {
            var candidateName = $"{baseName}.{i}{ext}";
            var candidateFile = Path.Combine(trashFiles, candidateName);
            var candidateInfo = Path.Combine(trashInfo, candidateName + ".trashinfo");
            if (!File.Exists(candidateFile) && !Directory.Exists(candidateFile) && !File.Exists(candidateInfo))
            {
                return (candidateFile, candidateInfo);
            }
        }
    }
}

public static class TrashServiceFactory
{
    public static ITrashService CreateForCurrentPlatform(ITrashWarningSink? warnings = null)
    {
        warnings ??= new NullTrashWarningSink();

        if (OperatingSystem.IsWindows()) return new WindowsTrashService(warnings);
        if (OperatingSystem.IsMacOS()) return new MacTrashService(warnings);
        return new LinuxTrashService(warnings);
    }
}
