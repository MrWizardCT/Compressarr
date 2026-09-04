using System.Text.Json;

namespace Compressarr.Core.Notifications;

/// <summary>Gotify - a self-hosted push notification server, popular in the same self-hosted
/// crowd as ntfy. Server URL is user-configurable (Gotify has no public hosted instance, it's
/// always self-hosted). The app token travels as the X-Gotify-Key header rather than the ?token=
/// query parameter Gotify also accepts, so it never ends up in a server access log's URL.</summary>
public sealed class GotifyNotifier : INotifier
{
    private readonly IWebhookSender _sender;

    public GotifyNotifier(IWebhookSender sender)
    {
        _sender = sender;
    }

    public string Type => "gotify";
    public string DisplayName => "Gotify";

    public IReadOnlyList<NotifierField> Fields { get; } = new[]
    {
        new NotifierField("server", "Server URL", "text", Required: true),
        new NotifierField("appToken", "Application Token", "text", Required: true, Secret: true)
    };

    public Task<NotifyResult> SendAsync(IReadOnlyDictionary<string, string> settings, NotificationEvent evt, CancellationToken ct)
    {
        var priority = evt.Outcome switch
        {
            NotificationOutcome.Error => 8,
            NotificationOutcome.Warning => 7,
            _ => 5
        };
        var summary = $"Files: {evt.TotalFiles} | Saved: {evt.SavedGb:0.##} GB | Duration: {NotificationFormatting.FormatDuration(evt.Duration)}";
        return Post(settings, evt.Title, $"{evt.Body}\n{summary}", priority, ct);
    }

    public Task<NotifyResult> TestAsync(IReadOnlyDictionary<string, string> settings, CancellationToken ct)
    {
        return Post(settings, "Compressarr test notification", "This is a test notification from Compressarr.", 5, ct);
    }

    private Task<NotifyResult> Post(IReadOnlyDictionary<string, string> settings, string title, string message, int priority, CancellationToken ct)
    {
        settings.TryGetValue("server", out var server);
        settings.TryGetValue("appToken", out var appToken);
        var url = $"{(server ?? "").TrimEnd('/')}/message";
        var body = JsonSerializer.Serialize(new { title, message, priority });
        var headers = new Dictionary<string, string> { ["X-Gotify-Key"] = appToken ?? "" };
        return _sender.PostAsync(url, HttpMethod.Post, headers, body, "application/json", ct);
    }
}
