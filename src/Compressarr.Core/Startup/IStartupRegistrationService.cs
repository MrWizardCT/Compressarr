using System.Runtime.Versioning;

namespace Compressarr.Core.Startup;

/// <summary>
/// Registers (or unregisters) the running executable to launch automatically at user login -
/// backs the "Start with Windows" setting. Same per-platform-factory shape as
/// ITrashService/ICpuUsageSampler: a real Windows implementation, an honest no-op elsewhere.
/// </summary>
public interface IStartupRegistrationService
{
    void Apply(bool runAtLogin);
}

/// <summary>Per-user registry Run-key entry (HKCU\...\CurrentVersion\Run) - no admin rights
/// needed, and reversible by simply removing the value (which Apply(false) does). Points at
/// Environment.ProcessPath, i.e. the exact exe currently running, so it always matches wherever
/// this install actually lives.</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsStartupRegistrationService : IStartupRegistrationService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Compressarr";

    public void Apply(bool runAtLogin)
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            if (key is null) return;

            if (runAtLogin)
            {
                var exePath = Environment.ProcessPath;
                if (string.IsNullOrWhiteSpace(exePath)) return;

                key.SetValue(ValueName, $"\"{exePath}\"");
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }
        }
        catch
        {
            // Best-effort only - a registry write failure (locked-down policy, etc.) must never
            // fail a settings save.
        }
    }
}

/// <summary>No equivalent mechanism wired up for macOS/Linux yet (a LaunchAgent plist / a
/// .desktop autostart entry respectively) - explicit deferral, same framing as MacCpuUsageSampler
/// and the Linux HandBrakeCLI auto-download gap elsewhere in this codebase.</summary>
public sealed class NoOpStartupRegistrationService : IStartupRegistrationService
{
    public void Apply(bool runAtLogin) { }
}

public static class StartupRegistrationServiceFactory
{
    public static IStartupRegistrationService CreateForCurrentPlatform()
    {
        if (OperatingSystem.IsWindows()) return new WindowsStartupRegistrationService();
        return new NoOpStartupRegistrationService();
    }
}
