namespace Compressarr.Core.Routing;

/// <summary>
/// Per-OS "move to trash/recycle bin" abstraction. v1 used a Windows-only VB.NET API
/// (Microsoft.VisualBasic.FileIO.FileSystem); v2 needs a real per-OS implementation since it's
/// no longer Windows-only. All implementations fall back to a hard delete + a warning on
/// failure, so trash issues never block a run — same soft-fail posture as v1's
/// arr-integration/toast-notification handling.
/// </summary>
public interface ITrashService
{
    /// <summary>Deletes or recycles a file depending on mode. Maintain is a no-op guard so
    /// callers can pass the configured DeleteAfterConvertMode straight through.</summary>
    void DeleteFile(string path, Config.DeleteAfterConvertMode mode);

    /// <summary>Deletes or recycles a directory (and anything still in it) depending on mode.</summary>
    void DeleteFolder(string path, Config.DeleteAfterConvertMode mode);
}
