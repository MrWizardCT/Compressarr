namespace Compressarr.Web.Dtos;

/// <summary>One day's daytime window on the Scheduler page. Day is the name (Sunday first, matching
/// the order of ScheduleSettings.Days); Start equal to End means no daytime window that day.</summary>
public sealed record ScheduleDayDto(string Day, string Start, string End);

/// <summary>The optional day/night encode schedule (ScheduleSettings) as the Scheduler page edits it.
/// Enums travel as their names.</summary>
public sealed record ScheduleDto(
    bool Enabled,
    string Mode,
    string DayStart,
    string DayEnd,
    string WeekendDayStart,
    string WeekendDayEnd,
    List<ScheduleDayDto> Days,
    string DayPriority,
    string NightPriority,
    bool OnlyEncodeOffHours,
    string WhenDayStarts,
    List<ValidationIssueDto> ValidationIssues);
