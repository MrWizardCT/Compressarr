using Compressarr.Core.Config;

namespace Compressarr.Core.Notifications;

/// <summary>Built-in Title/Body template pairs for NotificationSettings.MessageStyle - the "few
/// options to pick from" alternative to a fully free-text Custom template. Rendered through the
/// exact same NotificationMessageRenderer as a user's own Custom template, so a built-in style is
/// just a pre-written pair of the same token strings, nothing more privileged. Custom is
/// deliberately absent here - its template pair lives on NotificationSettings itself
/// (CustomTitleTemplate/CustomBodyTemplate), not in this fixed set.</summary>
public static class NotificationMessagePresets
{
    public static readonly IReadOnlyDictionary<NotificationMessageStyle, (string Title, string Body)> Templates =
        new Dictionary<NotificationMessageStyle, (string Title, string Body)>
        {
            [NotificationMessageStyle.Minimal] = (
                "Compressarr run #{run_number} - {outcome}",
                "{files} file(s), {saved_gb} GB saved."),

            [NotificationMessageStyle.Standard] = (
                "Compressarr run #{run_number} - {outcome}",
                "{files} file(s) processed, {saved_gb} GB saved ({saved_pct}%) in {duration}."),

            [NotificationMessageStyle.Detailed] = (
                "Compressarr run #{run_number} - {outcome}",
                "{files} file(s) processed in {duration}.\n" +
                "{before_gb} GB -> {after_gb} GB ({saved_pct}% smaller)\n" +
                "Errors: {error_count} - Warnings: {warning_count}"),
        };
}
