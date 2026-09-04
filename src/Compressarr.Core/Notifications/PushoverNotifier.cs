using System.Net;
using System.Text;

namespace Compressarr.Core.Notifications;

/// <summary>Pushover - form-encoded POST per their long-standing documented API
/// (token/user/title/message/priority), not their newer JSON variant, since form-encoding is the
/// guaranteed-stable option every client integration example uses.</summary>
public sealed class PushoverNotifier : INotifier
{
    private readonly IWebhookSender _sender;

    public PushoverNotifier(IWebhookSender sender)
    {
        _sender = sender;
    }

    public string Type => "pushover";
    public string DisplayName => "Pushover";

    public IReadOnlyList<NotifierField> Fields { get; } = new[]
    {
        new NotifierField("appToken", "Application API Token", "text", Required: true, Secret: true),
        new NotifierField("userKey", "User Key", "text", Required: true, Secret: true)
    };

    public Task<NotifyResult> SendAsync(IReadOnlyDictionary<string, string> settings, NotificationEvent evt, CancellationToken ct)
    {
        var summary = $"Files: {evt.TotalFiles} | Saved: {evt.SavedGb:0.##} GB | Duration: {NotificationFormatting.FormatDuration(evt.Duration)}";
        var priority = evt.Outcome == NotificationOutcome.Error ? "1" : "0";
        return Post(settings, evt.Title, $"{evt.Body}\n{summary}", priority, ct);
    }

    public Task<NotifyResult> TestAsync(IReadOnlyDictionary<string, string> settings, CancellationToken ct)
    {
        return Post(settings, "Compressarr test notification", "This is a test notification from Compressarr.", "0", ct);
    }

    private Task<NotifyResult> Post(IReadOnlyDictionary<string, string> settings, string title, string message, string priority, CancellationToken ct)
    {
        settings.TryGetValue("appToken", out var appToken);
        settings.TryGetValue("userKey", out var userKey);
        // WebUtility.UrlEncode (space -> '+'), not Uri.EscapeDataString (space -> %20) - the
        // former matches actual application/x-www-form-urlencoded semantics.
        var body = new StringBuilder()
            .Append("token=").Append(WebUtility.UrlEncode(appToken ?? ""))
            .Append("&user=").Append(WebUtility.UrlEncode(userKey ?? ""))
            .Append("&title=").Append(WebUtility.UrlEncode(title))
            .Append("&message=").Append(WebUtility.UrlEncode(message))
            .Append("&priority=").Append(priority)
            .ToString();
        return _sender.PostAsync("https://api.pushover.net/1/messages.json", HttpMethod.Post, new Dictionary<string, string>(), body, "application/x-www-form-urlencoded", ct);
    }
}
