namespace Compressarr.Core.FFmpeg;

/// <summary>Looks for an ffmpeg (and its ffprobe) already on the machine, so Check/Install can offer
/// "use this one" before suggesting a 190 MB download.</summary>
public static class FFmpegLocator
{
    /// <summary>The first folder (PATH, then a few usual homes) holding both ffmpeg and ffprobe, or null.</summary>
    public static (string FFmpeg, string FFprobe)? Find(IEnumerable<string>? extraFolders = null)
    {
        var exe = OperatingSystem.IsWindows() ? ".exe" : "";
        var folders = new List<string>();

        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        folders.AddRange(path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        if (OperatingSystem.IsWindows())
        {
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            folders.Add(Path.Combine(programFiles, "ffmpeg", "bin"));
            folders.Add(@"C:\ffmpeg\bin");
            folders.Add(Path.Combine(localAppData, "Microsoft", "WinGet", "Links"));
            folders.Add(@"C:\ProgramData\chocolatey\bin");
        }
        else
        {
            folders.AddRange(new[] { "/usr/bin", "/usr/local/bin", "/opt/homebrew/bin" });
        }

        if (extraFolders is not null) folders.AddRange(extraFolders);

        foreach (var folder in folders.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var ffmpeg = Path.Combine(folder, "ffmpeg" + exe);
                var ffprobe = Path.Combine(folder, "ffprobe" + exe);
                if (File.Exists(ffmpeg) && File.Exists(ffprobe)) return (ffmpeg, ffprobe);
            }
            catch (ArgumentException)
            {
                // a malformed PATH entry - skip it
            }
        }

        return null;
    }
}
