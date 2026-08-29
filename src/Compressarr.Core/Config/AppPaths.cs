namespace Compressarr.Core.Config;

/// <summary>
/// Resolves the per-OS application-data directory Compressarr's config/logs/reports live in by
/// default. Roadmap requirement: preferences must live here, not in the app's install folder, so
/// an upgrade never overwrites user settings.
/// </summary>
public static class AppPaths
{
    private const string AppFolderName = "Compressarr";

    public static string GetAppDataDirectory()
    {
        if (OperatingSystem.IsWindows())
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, AppFolderName);
        }

        if (OperatingSystem.IsMacOS())
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, "Library", "Application Support", AppFolderName);
        }

        // Linux and any other Unix-like platform: XDG Base Directory spec.
        var xdgConfigHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        if (!string.IsNullOrWhiteSpace(xdgConfigHome))
        {
            return Path.Combine(xdgConfigHome, AppFolderName);
        }

        var fallbackHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(fallbackHome, ".config", AppFolderName);
    }

    public static string GetConfigFilePath() => Path.Combine(GetAppDataDirectory(), "compressarr.settings.json");

    public static string GetRunCountFilePath() => Path.Combine(GetAppDataDirectory(), "compressarr.runcount.json");
}
