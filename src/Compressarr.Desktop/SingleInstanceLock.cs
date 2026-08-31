using Compressarr.Core.Config;

namespace Compressarr.Desktop;

/// <summary>Cross-platform single-instance enforcement via a plain exclusive file lock, not a
/// Windows-only named Mutex (named Mutex isn't reliably cross-process on Linux/macOS in .NET).
/// The held FileStream is kept open for the process's entire lifetime in a static field; the OS
/// releases the lock automatically on any process exit - normal, crashed, or force-killed (e.g.
/// by the installer's taskkill on update) - so it can never go stale.</summary>
internal static class SingleInstanceLock
{
    private static FileStream? _lockStream;

    /// <summary>Returns true if this process now holds the lock (no other instance is running),
    /// false if another instance already holds it.</summary>
    public static bool TryAcquire()
    {
        var lockPath = Path.Combine(AppPaths.GetAppDataDirectory(), "compressarr.lock");

        try
        {
            Directory.CreateDirectory(AppPaths.GetAppDataDirectory());
            _lockStream = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
    }
}
