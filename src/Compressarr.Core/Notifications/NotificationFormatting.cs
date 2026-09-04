namespace Compressarr.Core.Notifications;

/// <summary>Small shared helpers for laying out a NotificationEvent's numbers - used by Discord/
/// Slack (and any future chat-style notifier) so duration/outcome formatting stays consistent
/// across channels instead of each one reinventing it slightly differently.</summary>
internal static class NotificationFormatting
{
    public static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalHours >= 1) return $"{(int)duration.TotalHours}h {duration.Minutes}m";
        if (duration.TotalMinutes >= 1) return $"{duration.Minutes}m {duration.Seconds}s";
        return $"{duration.Seconds}s";
    }

    public static string FormatOutcome(NotificationOutcome outcome) => outcome switch
    {
        NotificationOutcome.Error => "Error",
        NotificationOutcome.Warning => "Warning",
        _ => "Success"
    };
}
