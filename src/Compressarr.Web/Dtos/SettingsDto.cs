namespace Compressarr.Web.Dtos;

public sealed record SettingsDto(
    string HandBrakeCliPath,
    string PresetsPath,
    string HandBrakeOptions,
    List<string> VidTypes,
    bool OutSameAsIn,
    string DeleteAfterConvert,
    bool MoveFiles,
    bool ClearTitleMetadata,
    int Limit,
    long MinSizeBytes,
    string OnDestinationCollision,
    string LogFilePath,
    int RetentionDays,
    string PostExecCmd,
    string PostExecArgs,
    string ReportPath,
    string OpenAfterRun,
    int RepeatCount,
    bool RepeatMonitor,
    int PollIntervalSeconds,
    ArrServiceDto Sonarr,
    ArrServiceDto Radarr,
    int WebPort,
    bool RunAtLogin,
    string BackupFolderPath,
    int BackupIntervalDays,
    int BackupRetentionDays,
    DateTimeOffset? BackupLastRunUtc);

public sealed record ArrServiceDto(bool Enabled, string Url, string ApiKey);
