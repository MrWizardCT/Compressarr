using System.Text.Json;

namespace Compressarr.Core.Notifications;

/// <summary>Notifiarr - a Discord-relay service built specifically for the *arr ecosystem
/// (Sonarr/Radarr/etc.), reached via its "Passthrough" integration
/// (https://notifiarr.wiki/pages/integrations/passthrough/). Notifiarr renders the payload as a
/// Discord embed in whichever channel the user's Notifiarr Discord bot is configured to post to -
/// the channel id is Notifiarr's own routing target, not a raw Discord webhook.</summary>
public sealed class NotifiarrNotifier : INotifier
{
    // Same palette as DiscordNotifier, but as a bare 6-digit hex string per Notifiarr's schema
    // rather than a decimal int.
    private const string ColorSuccess = "2ECC71";
    private const string ColorWarning = "F1C40F";
    private const string ColorError = "E74C3C";

    private readonly IWebhookSender _sender;

    public NotifiarrNotifier(IWebhookSender sender)
    {
        _sender = sender;
    }

    public string Type => "notifiarr";
    public string DisplayName => "Notifiarr";

    public IReadOnlyList<NotifierField> Fields { get; } = new[]
    {
        new NotifierField("apiKey", "Notifiarr API Key", "text", Required: true, Secret: true,
            HelpText: "Found on your Notifiarr account page under My Account > API Key.",
            Placeholder: "01234567-89ab-cdef-0123-456789abcdef"),
        new NotifierField("channelId", "Discord Channel ID", "text", Required: true,
            HelpText: "The Discord channel to post to, allowed for your Notifiarr Discord integration. Enable Developer Mode in Discord, then right-click the channel and Copy Channel ID.",
            Placeholder: "123456789012345678")
    };

    public Task<NotifyResult> SendAsync(IReadOnlyDictionary<string, string> settings, NotificationEvent evt, CancellationToken ct)
    {
        var color = evt.Outcome switch
        {
            NotificationOutcome.Error => ColorError,
            NotificationOutcome.Warning => ColorWarning,
            _ => ColorSuccess
        };
        var fields = new List<object>
        {
            new { title = "Files", text = evt.TotalFiles.ToString(), inline = true },
            new { title = "Saved", text = $"{evt.SavedGb:0.##} GB", inline = true },
            new { title = "Duration", text = NotificationFormatting.FormatDuration(evt.Duration), inline = true }
        };
        return Post(settings, evt.Title, evt.Body, color, fields, ct);
    }

    public Task<NotifyResult> TestAsync(IReadOnlyDictionary<string, string> settings, CancellationToken ct)
    {
        return Post(settings, "Compressarr test notification", "This is a test notification from Compressarr.", ColorSuccess, new List<object>(), ct);
    }

    private Task<NotifyResult> Post(IReadOnlyDictionary<string, string> settings, string title, string description, string color, List<object> fields, CancellationToken ct)
    {
        settings.TryGetValue("apiKey", out var apiKey);
        settings.TryGetValue("channelId", out var channelIdText);
        long.TryParse(channelIdText, out var channelId);

        var url = $"https://notifiarr.com/api/v1/notification/passthrough/{apiKey}";
        var payload = new
        {
            notification = new { update = false, name = "Compressarr", @event = "run-complete" },
            discord = new
            {
                color,
                ping = new { pingUser = 0, pingRole = 0 },
                images = new { thumbnail = "", image = "" },
                text = new { title, icon = "", content = "", description, fields, footer = "" },
                ids = new { channel = channelId }
            }
        };
        var body = JsonSerializer.Serialize(payload);
        var headers = new Dictionary<string, string> { ["Accept"] = "text/plain" };
        return _sender.PostAsync(url, HttpMethod.Post, headers, body, "application/json", ct);
    }
}
