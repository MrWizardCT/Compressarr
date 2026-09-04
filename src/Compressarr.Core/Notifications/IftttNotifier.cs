using System.Text.Json;

namespace Compressarr.Core.Notifications;

/// <summary>IFTTT Maker Webhooks - technically already reachable via the Generic Webhook notifier
/// (its body already includes value1/value2/value3 aliases for this exact reason), but a dedicated
/// entry means the user only has to paste their event name and Webhooks key instead of hand-building
/// the "https://maker.ifttt.com/trigger/{event}/with/key/{key}" URL themselves - same UX reasoning
/// as building dedicated Discord/Slack notifiers instead of leaving those to Generic Webhook too.</summary>
public sealed class IftttNotifier : INotifier
{
    private readonly IWebhookSender _sender;

    public IftttNotifier(IWebhookSender sender)
    {
        _sender = sender;
    }

    public string Type => "ifttt";
    public string DisplayName => "IFTTT";

    public IReadOnlyList<NotifierField> Fields { get; } = new[]
    {
        new NotifierField("eventName", "Event Name", "text", Required: true),
        new NotifierField("webhooksKey", "Webhooks Key", "text", Required: true, Secret: true)
    };

    public Task<NotifyResult> SendAsync(IReadOnlyDictionary<string, string> settings, NotificationEvent evt, CancellationToken ct)
    {
        return Post(settings, evt.Title, evt.Body, evt.ReportPath, ct);
    }

    public Task<NotifyResult> TestAsync(IReadOnlyDictionary<string, string> settings, CancellationToken ct)
    {
        return Post(settings, "Compressarr test notification", "This is a test notification from Compressarr.", null, ct);
    }

    private Task<NotifyResult> Post(IReadOnlyDictionary<string, string> settings, string value1, string value2, string? value3, CancellationToken ct)
    {
        settings.TryGetValue("eventName", out var eventName);
        settings.TryGetValue("webhooksKey", out var webhooksKey);
        var url = $"https://maker.ifttt.com/trigger/{eventName}/with/key/{webhooksKey}";
        var body = JsonSerializer.Serialize(new { value1, value2, value3 });
        return _sender.PostAsync(url, HttpMethod.Post, new Dictionary<string, string>(), body, "application/json", ct);
    }
}
