namespace Compressarr.Web.Dtos;

public sealed record ValidationIssueDto(string Field, string Message);

public sealed record SettingsDto(
    string HandBrakeCliPath,
    string PresetsPath,
    string HandBrakeOptions,
    bool FileBotEnabled,
    string FileBotCliPath,
    bool FileBotTvEnabled,
    string FileBotTvArgs,
    bool FileBotMovieEnabled,
    string FileBotMovieArgs,
    List<string> VidTypes,
    bool OutSameAsIn,
    string DeleteAfterConvert,
    bool MoveFiles,
    bool ClearTitleMetadata,
    int Limit,
    long MinSizeBytes,
    List<string> CompanionExtensions,
    string UnmatchedCompanionAction,
    string OnDestinationCollision,
    string LogFilePath,
    int RetentionDays,
    bool KeepSuccessfulHandBrakeLogs,
    string PostExecCmd,
    string PostExecArgs,
    string ReportPath,
    string OpenAfterRun,
    int RepeatCount,
    bool RepeatMonitor,
    bool LaunchMonitorAtStartup,
    int PollIntervalSeconds,
    string QueueEtaFormat,
    ArrServiceDto Sonarr,
    ArrServiceDto Radarr,
    int WebPort,
    bool RunAtLogin,
    string BackupFolderPath,
    int BackupIntervalDays,
    int BackupRetentionDays,
    DateTimeOffset? BackupLastRunUtc,
    List<ValidationIssueDto> ValidationIssues,
    ScheduleDto? Schedule = null);

/// <summary>The optional day/night encode schedule (ScheduleSettings). Enums travel as their names. A
/// settings save from a client that predates the schedule sends no Schedule at all, which leaves the
/// saved schedule untouched.</summary>
public sealed record ScheduleDto(
    bool Enabled,
    string DayStart,
    string DayEnd,
    bool WeekendDifferent,
    string WeekendDayStart,
    string WeekendDayEnd,
    string DayPriority,
    string NightPriority,
    bool OnlyEncodeOffHours,
    string WhenDayStarts);

public sealed record ArrServiceDto(bool Enabled, string Url, string ApiKey);
