namespace Compressarr.Core.Config;

public enum DeleteAfterConvertMode
{
    Maintain,
    Delete,
    Recycle
}

public enum OpenReportMode
{
    Always,
    OnError,
    Never
}

public sealed class CompressarrConfig
{
    public HandBrakeSettings HandBrake { get; set; } = new();
    public List<LaneConfig> Lanes { get; set; } = new();
    public ProcessingSettings Processing { get; set; } = new();
    public LoggingSettings Logging { get; set; } = new();
    public PostExecSettings PostExec { get; set; } = new();
    public ReportSettings Report { get; set; } = new();
    public RepeatSettings Repeat { get; set; } = new();
    public StartupSettings Startup { get; set; } = new();
    public ArrSettings Arrs { get; set; } = new();
    public WebSettings Web { get; set; } = new();
}

public sealed class HandBrakeSettings
{
    public string CliPath { get; set; } = "%ProgramFiles%\\HandBrake\\HandBrakeCLI.exe";
    public string PresetsPath { get; set; } = "%appdata%\\HandBrake\\presets.json";
    public string Options { get; set; } = "";
}

public sealed class LaneConfig
{
    /// <summary>Stable, immutable once created — the join key used everywhere lanes are referenced.</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string DisplayName { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public string Input { get; set; } = "";
    public string Output { get; set; } = "";
    public string TvPreset { get; set; } = "";
    public string MoviePreset { get; set; } = "";
    public string TvShowBasePath { get; set; } = "";
    public string MovieBasePath { get; set; } = "";
}

public sealed class ProcessingSettings
{
    /// <summary>Extensions without a leading dot (matches v1's stored format — a dot is
    /// prepended at point-of-use when building glob patterns).</summary>
    public List<string> VidTypes { get; set; } = new() { "mkv", "avi", "mp4", "mpg", "ts", "m4v" };
    public bool OutSameAsIn { get; set; } = false;
    public DeleteAfterConvertMode DeleteAfterConvert { get; set; } = DeleteAfterConvertMode.Recycle;
    public bool MoveFiles { get; set; } = true;
    public int Limit { get; set; } = 0;

    /// <summary>Typed byte count. v1 stored this as a unit-suffix string ("0gb"/"500mb"); v2's
    /// own config file doesn't need to preserve that shape, so this is a plain long, with
    /// ByteSizeParser/ByteSizeFormatter available for UI display and for parsing a hand-edited
    /// unit-suffix string.</summary>
    public long MinSizeBytes { get; set; } = 0;
}

public sealed class LoggingSettings
{
    public string LogFilePath { get; set; } = "%CompressarrAppData%\\Logs";
    public int RetentionDays { get; set; } = 30;
}

public sealed class PostExecSettings
{
    public string Cmd { get; set; } = "";
    public string Args { get; set; } = "";
}

public sealed class ReportSettings
{
    public string ReportPath { get; set; } = "%CompressarrAppData%\\Reports";
    public OpenReportMode OpenAfterRun { get; set; } = OpenReportMode.OnError;
}

public sealed class RepeatSettings
{
    public int Count { get; set; } = 0;
    public bool Monitor { get; set; } = false;

    /// <summary>Cadence for IRunLoopController's monitor-mode loop - matches v1's original 60s
    /// countdown between polls.</summary>
    public int PollIntervalSeconds { get; set; } = 60;
}

public sealed class StartupSettings
{
    public int CountdownSeconds { get; set; } = 10;

    /// <summary>When true, the tray host registers itself to launch at Windows login (a per-user
    /// registry Run-key entry - no admin rights needed). Applied by IStartupRegistrationService
    /// whenever settings are saved; no-op on non-Windows platforms.</summary>
    public bool RunAtLogin { get; set; } = false;
}

public sealed class ArrSettings
{
    public ArrServiceSettings Sonarr { get; set; } = new();
    public ArrServiceSettings Radarr { get; set; } = new();
}

public sealed class ArrServiceSettings
{
    public bool Enabled { get; set; } = false;
    public string Url { get; set; } = "";
    public string ApiKey { get; set; } = "";
}

public sealed class WebSettings
{
    /// <summary>Port the embedded web UI/API listens on, bound to all interfaces (0.0.0.0) so
    /// it's reachable from other devices on the LAN - the whole point of the web-first design.
    /// User-configurable from the settings page so it can be changed if it collides with
    /// something else already running on the machine. No authentication in v2.0.0 (matches
    /// Radarr/Sonarr's own default posture).</summary>
    public int Port { get; set; } = 1212;
}
