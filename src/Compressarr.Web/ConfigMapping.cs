using Compressarr.Core.Config;
using Compressarr.Web.Dtos;

namespace Compressarr.Web;

/// <summary>Maps CompressarrConfig <-> the web API's DTOs. Direct port of the field-by-field
/// logic MainViewModel.ApplyConfig/BuildConfig used to do for Avalonia data binding, now facing
/// JSON instead.</summary>
internal static class ConfigMapping
{
    public static SettingsDto ToSettingsDto(CompressarrConfig config) => new(
        HandBrakeCliPath: config.HandBrake.CliPath,
        PresetsPath: config.HandBrake.PresetsPath,
        HandBrakeOptions: config.HandBrake.Options,
        VidTypes: config.Processing.VidTypes,
        OutSameAsIn: config.Processing.OutSameAsIn,
        DeleteAfterConvert: config.Processing.DeleteAfterConvert.ToString(),
        MoveFiles: config.Processing.MoveFiles,
        ClearTitleMetadata: config.Processing.ClearTitleMetadata,
        Limit: config.Processing.Limit,
        MinSizeBytes: config.Processing.MinSizeBytes,
        OnDestinationCollision: config.Processing.OnDestinationCollision.ToString(),
        LogFilePath: config.Logging.LogFilePath,
        RetentionDays: config.Logging.RetentionDays,
        PostExecCmd: config.PostExec.Cmd,
        PostExecArgs: config.PostExec.Args,
        ReportPath: config.Report.ReportPath,
        OpenAfterRun: config.Report.OpenAfterRun.ToString(),
        RepeatCount: config.Repeat.Count,
        RepeatMonitor: config.Repeat.Monitor,
        PollIntervalSeconds: config.Repeat.PollIntervalSeconds,
        Sonarr: new ArrServiceDto(config.Arrs.Sonarr.Enabled, config.Arrs.Sonarr.Url, config.Arrs.Sonarr.ApiKey),
        Radarr: new ArrServiceDto(config.Arrs.Radarr.Enabled, config.Arrs.Radarr.Url, config.Arrs.Radarr.ApiKey),
        WebPort: config.Web.Port,
        RunAtLogin: config.Startup.RunAtLogin,
        BackupFolderPath: config.Backup.FolderPath,
        BackupIntervalDays: config.Backup.IntervalDays,
        BackupRetentionDays: config.Backup.RetentionDays,
        BackupLastRunUtc: config.Backup.LastRunUtc);

    public static void ApplySettingsDto(CompressarrConfig config, SettingsDto dto)
    {
        config.HandBrake.CliPath = dto.HandBrakeCliPath;
        config.HandBrake.PresetsPath = dto.PresetsPath;
        config.HandBrake.Options = dto.HandBrakeOptions;
        config.Processing.VidTypes = dto.VidTypes;
        config.Processing.OutSameAsIn = dto.OutSameAsIn;
        config.Processing.DeleteAfterConvert = Enum.Parse<DeleteAfterConvertMode>(dto.DeleteAfterConvert);
        config.Processing.MoveFiles = dto.MoveFiles;
        config.Processing.ClearTitleMetadata = dto.ClearTitleMetadata;
        config.Processing.Limit = dto.Limit;
        config.Processing.MinSizeBytes = dto.MinSizeBytes;
        config.Processing.OnDestinationCollision = Enum.Parse<DestinationCollisionMode>(dto.OnDestinationCollision);
        config.Logging.LogFilePath = dto.LogFilePath;
        config.Logging.RetentionDays = dto.RetentionDays;
        config.PostExec.Cmd = dto.PostExecCmd;
        config.PostExec.Args = dto.PostExecArgs;
        config.Report.ReportPath = dto.ReportPath;
        config.Report.OpenAfterRun = Enum.Parse<OpenReportMode>(dto.OpenAfterRun);
        config.Repeat.Count = dto.RepeatCount;
        config.Repeat.Monitor = dto.RepeatMonitor;
        config.Repeat.PollIntervalSeconds = dto.PollIntervalSeconds;
        config.Arrs.Sonarr = new ArrServiceSettings { Enabled = dto.Sonarr.Enabled, Url = dto.Sonarr.Url, ApiKey = dto.Sonarr.ApiKey };
        config.Arrs.Radarr = new ArrServiceSettings { Enabled = dto.Radarr.Enabled, Url = dto.Radarr.Url, ApiKey = dto.Radarr.ApiKey };
        config.Web.Port = dto.WebPort;
        config.Startup.RunAtLogin = dto.RunAtLogin;
        config.Backup.FolderPath = dto.BackupFolderPath;
        config.Backup.IntervalDays = dto.BackupIntervalDays;
        config.Backup.RetentionDays = dto.BackupRetentionDays;
        // BackupLastRunUtc is read-only from the client's perspective - set only by BackupService
        // itself after a real backup runs, never round-tripped back in from a settings save.
    }

    public static LaneDto ToLaneDto(LaneConfig lane) => new(
        lane.Id, lane.DisplayName, lane.Enabled, lane.Input, lane.Output,
        lane.TvPreset, lane.MoviePreset, lane.TvShowBasePath, lane.MovieBasePath);

    public static void ApplyLaneDto(LaneConfig lane, LaneDto dto)
    {
        lane.DisplayName = dto.DisplayName;
        lane.Enabled = dto.Enabled;
        lane.Input = dto.Input;
        lane.Output = dto.Output;
        lane.TvPreset = dto.TvPreset;
        lane.MoviePreset = dto.MoviePreset;
        lane.TvShowBasePath = dto.TvShowBasePath;
        lane.MovieBasePath = dto.MovieBasePath;
    }
}
