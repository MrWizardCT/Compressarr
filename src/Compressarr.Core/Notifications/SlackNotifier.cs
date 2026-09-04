using System.Text.Json;

namespace Compressarr.Core.Notifications;

/// <summary>Slack incoming webhook - just a URL, same as Discord. Uses the plain "text" field
/// with Slack's default mrkdwn formatting (*bold*) rather than Block Kit, since every incoming
/// webhook accepts that with no extra setup, whereas Block Kit's exact shape can vary by how the
/// webhook/app was configured.</summary>
public sealed class SlackNotifier : INotifier
{
    private readonly IWebhookSender _sender;

    public SlackNotifier(IWebhookSender sender)
    {
        _sender = sender;
    }

    public string Type => "slack";
    public string DisplayName => "Slack";

    public IReadOnlyList<NotifierField> Fields { get; } = new[]
    {
        new NotifierField("url", "Webhook URL", "text", Required: true)
    };

    public Task<NotifyResult> SendAsync(IReadOnlyDictionary<string, string> settings, NotificationEvent evt, CancellationToken ct)
    {
        var icon = evt.Outcome switch
        {
            NotificationOutcome.Error => ":red_circle:",
            NotificationOutcome.Warning => ":warning:",
            _ => ":white_check_mark:"
        };
        var summary = $"Files: {evt.TotalFiles} | Saved: {evt.SavedGb:0.##} GB | Duration: {NotificationFormatting.FormatDuration(evt.Duration)}";
        var text = $"{icon} *{Escape(evt.Title)}*\n{Escape(evt.Body)}\n{summary}";
        return Post(settings, text, ct);
    }

    public Task<NotifyResult> TestAsync(IReadOnlyDictionary<string, string> settings, CancellationToken ct)
    {
        var text = ":white_check_mark: *Compressarr test notification*\nThis is a test notification from Compressarr.";
        return Post(settings, text, ct);
    }

    private Task<NotifyResult> Post(IReadOnlyDictionary<string, string> settings, string text, CancellationToken ct)
    {
        settings.TryGetValue("url", out var url);
        var body = JsonSerializer.Serialize(new { text });
        return _sender.PostAsync(url ?? "", HttpMethod.Post, new Dictionary<string, string>(), body, "application/json", ct);
    }

    // Slack's mrkdwn treats &, <, > specially - escape them so a filename/report path containing
    // one doesn't get silently mangled or mis-rendered.
    private static string Escape(string text) => text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}
