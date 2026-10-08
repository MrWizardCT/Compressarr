using System.Runtime.Versioning;

namespace Compressarr.Core.Startup;

/// <summary>
/// Registers (or unregisters) the running executable to launch automatically at user login -
/// backs the "Start with Windows" setting. Same per-platform-factory shape as
/// ITrashService/ICpuUsageSampler: a real Windows implementation, an honest no-op elsewhere.
/// </summary>
public interface IStartupRegistrationService
{
    /// <summary>Makes the login entry match the setting, overwriting it - used when the user changes the setting.</summary>
    void Apply(bool runAtLogin);

    /// <summary>Brings the login entry back in line with the saved setting at startup (a restored backup or imported
    /// settings never touched the registry). Gentler than <see cref="Apply"/>: it never repoints an entry that
    /// still works, so a copy run from somewhere else (a build under development) can't hijack the installed one.</summary>
    void Reconcile(bool runAtLogin);
}

/// <summary>What Reconcile should do to the login entry.</summary>
public enum RunKeyAction { None, Set, Delete }

public static class RunKeyDecider
{
    /// <param name="existing">The current value of the Run entry, or null if there is none.</param>
    /// <param name="currentExe">The running exe.</param>
    /// <param name="exists">Whether a file exists (the entry's target).</param>
    public static RunKeyAction Decide(bool runAtLogin, string? existing, string? currentExe, Func<string, bool> exists)
    {
        if (!runAtLogin) return existing is null ? RunKeyAction.None : RunKeyAction.Delete;
        if (string.IsNullOrWhiteSpace(currentExe)) return RunKeyAction.None;
        if (existing is null) return RunKeyAction.Set;

        // An entry that still points at a real program is left alone; one pointing at nothing (the install moved
        // or was removed) is repointed at this exe.
        var target = existing.Trim().Trim('"');
        return exists(target) ? RunKeyAction.None : RunKeyAction.Set;
    }
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

    public void Reconcile(bool runAtLogin)
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            if (key is null) return;

            var action = RunKeyDecider.Decide(runAtLogin, key.GetValue(ValueName) as string, Environment.ProcessPath, File.Exists);
            if (action == RunKeyAction.Set) key.SetValue(ValueName, $"\"{Environment.ProcessPath}\"");
            else if (action == RunKeyAction.Delete) key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
        catch
        {
            // Best-effort, like Apply.
        }
    }

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
    public void Reconcile(bool runAtLogin) { }
}

public static class StartupRegistrationServiceFactory
{
    public static IStartupRegistrationService CreateForCurrentPlatform()
    {
        if (OperatingSystem.IsWindows()) return new WindowsStartupRegistrationService();
        return new NoOpStartupRegistrationService();
    }
}
