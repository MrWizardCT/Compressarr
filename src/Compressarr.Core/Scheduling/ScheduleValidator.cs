using Compressarr.Core.Config;
using Compressarr.Core.Validation;

namespace Compressarr.Core.Scheduling;

/// <summary>Checks the Scheduler page's own fields. Only the windows in use for the chosen layout are
/// checked, and only while the schedule is on - an unused or switched-off window can hold anything
/// without it mattering. An unreadable time is never fatal (SchedulePolicy just ignores that window),
/// which is why this reports it rather than refusing the save.</summary>
public static class ScheduleValidator
{
    private static readonly string[] DayNames = { "Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday" };

    public static List<ValidationIssue> Validate(ScheduleSettings schedule)
    {
        var issues = new List<ValidationIssue>();
        if (!schedule.Enabled) return issues;

        switch (schedule.Mode)
        {
            case ScheduleMode.Everyday:
                Check(issues, "dayStart", "Daytime start", schedule.DayStart);
                Check(issues, "dayEnd", "Daytime end", schedule.DayEnd);
                break;
            case ScheduleMode.WeekdaysAndWeekends:
                Check(issues, "dayStart", "Weekday daytime start", schedule.DayStart);
                Check(issues, "dayEnd", "Weekday daytime end", schedule.DayEnd);
                Check(issues, "weekendDayStart", "Weekend daytime start", schedule.WeekendDayStart);
                Check(issues, "weekendDayEnd", "Weekend daytime end", schedule.WeekendDayEnd);
                break;
            case ScheduleMode.EachDay:
                for (var i = 0; i < schedule.Days.Count && i < 7; i++)
                {
                    Check(issues, $"day{i}Start", $"{DayNames[i]} daytime start", schedule.Days[i].Start);
                    Check(issues, $"day{i}End", $"{DayNames[i]} daytime end", schedule.Days[i].End);
                }
                if (schedule.Days.Count < 7)
                {
                    issues.Add(new ValidationIssue("days", "The schedule has fewer than seven days; the missing days have no daytime window."));
                }
                break;
        }
        return issues;
    }

    private static void Check(List<ValidationIssue> issues, string field, string label, string? value)
    {
        if (!SchedulePolicy.TryParseTime(value, out _))
        {
            issues.Add(new ValidationIssue(field, $"{label} must be a time like 08:00 (24-hour) - until it is fixed that window is ignored."));
        }
    }
}
