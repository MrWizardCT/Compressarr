using System.Text.Json;

namespace Compressarr.Core.Notifications;

/// <summary>Telegram Bot API - a bot token plus the target chat id. Sends plain text with no
/// parse_mode rather than Markdown/HTML, since Telegram's Markdown escaping rules are strict and
/// brittle (MarkdownV2 requires escaping a long list of punctuation characters) and a report path
/// or filename containing any of them would otherwise silently break formatting or fail the send.</summary>
public sealed class TelegramNotifier : INotifier
{
    private readonly IWebhookSender _sender;

    public TelegramNotifier(IWebhookSender sender)
    {
        _sender = sender;
    }

    public string Type => "telegram";
    public string DisplayName => "Telegram";

    public IReadOnlyList<NotifierField> Fields { get; } = new[]
    {
        new NotifierField("botToken", "Bot Token", "text", Required: true, Secret: true),
        new NotifierField("chatId", "Chat ID", "text", Required: true)
    };

    public Task<NotifyResult> SendAsync(IReadOnlyDictionary<string, string> settings, NotificationEvent evt, CancellationToken ct)
    {
        var summary = $"Files: {evt.TotalFiles} | Saved: {evt.SavedGb:0.##} GB | Duration: {NotificationFormatting.FormatDuration(evt.Duration)}";
        var text = $"{NotificationFormatting.FormatOutcome(evt.Outcome)}: {evt.Title}\n{evt.Body}\n{summary}";
        return Post(settings, text, ct);
    }

    public Task<NotifyResult> TestAsync(IReadOnlyDictionary<string, string> settings, CancellationToken ct)
    {
        var text = "Compressarr test notification\nThis is a test notification from Compressarr.";
        return Post(settings, text, ct);
    }

    private Task<NotifyResult> Post(IReadOnlyDictionary<string, string> settings, string text, CancellationToken ct)
    {
        settings.TryGetValue("botToken", out var botToken);
        settings.TryGetValue("chatId", out var chatId);
        var url = $"https://api.telegram.org/bot{botToken}/sendMessage";
        var body = JsonSerializer.Serialize(new { chat_id = chatId, text });
        return _sender.PostAsync(url, HttpMethod.Post, new Dictionary<string, string>(), body, "application/json", ct);
    }
}
