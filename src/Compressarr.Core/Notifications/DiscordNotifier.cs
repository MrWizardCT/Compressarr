using System.Text.Json;

namespace Compressarr.Core.Notifications;

/// <summary>Discord incoming webhook - just a URL, no auth token needed (the URL itself is the
/// secret, same as Home Assistant's own webhook design). Posts a single embed rather than plain
/// text so the outcome reads at a glance via its color strip, matching how Discord notifications
/// from other tools (CI systems, monitoring bots) typically look.</summary>
public sealed class DiscordNotifier : INotifier
{
    // Discord embed colors are a plain decimal int, not a hex string - standard Discord palette.
    private const int ColorSuccess = 0x2ECC71;
    private const int ColorWarning = 0xF1C40F;
    private const int ColorError = 0xE74C3C;

    private readonly IWebhookSender _sender;

    public DiscordNotifier(IWebhookSender sender)
    {
        _sender = sender;
    }

    public string Type => "discord";
    public string DisplayName => "Discord";

    public IReadOnlyList<NotifierField> Fields { get; } = new[]
    {
        new NotifierField("url", "Webhook URL", "text", Required: true,
            HelpText: "In Discord, go to a channel's Edit Channel > Integrations > Webhooks, create one, and paste its URL here.",
            Placeholder: "https://discord.com/api/webhooks/...")
    };

    public Task<NotifyResult> SendAsync(IReadOnlyDictionary<string, string> settings, NotificationEvent evt, CancellationToken ct)
    {
        var color = evt.Outcome switch
        {
            NotificationOutcome.Error => ColorError,
            NotificationOutcome.Warning => ColorWarning,
            _ => ColorSuccess
        };
        var body = BuildBody(evt.Title, evt.Body, color, evt.TotalFiles, evt.SavedGb, evt.Duration);
        return Post(settings, body, ct);
    }

    public Task<NotifyResult> TestAsync(IReadOnlyDictionary<string, string> settings, CancellationToken ct)
    {
        var body = BuildBody("Compressarr test notification", "This is a test notification from Compressarr.", ColorSuccess, null, null, null);
        return Post(settings, body, ct);
    }

    private Task<NotifyResult> Post(IReadOnlyDictionary<string, string> settings, string body, CancellationToken ct)
    {
        settings.TryGetValue("url", out var url);
        return _sender.PostAsync(url ?? "", HttpMethod.Post, new Dictionary<string, string>(), body, "application/json", ct);
    }

    private static string BuildBody(string title, string message, int color, int? totalFiles, double? savedGb, TimeSpan? duration)
    {
        var fields = new List<object>();
        if (totalFiles is not null) fields.Add(new { name = "Files", value = totalFiles.ToString(), inline = true });
        if (savedGb is not null) fields.Add(new { name = "Saved", value = $"{savedGb:0.##} GB", inline = true });
        if (duration is not null) fields.Add(new { name = "Duration", value = NotificationFormatting.FormatDuration(duration.Value), inline = true });

        var payload = new
        {
            embeds = new[]
            {
                new
                {
                    title,
                    description = message,
                    color,
                    fields,
                    timestamp = DateTimeOffset.UtcNow.ToString("o")
                }
            }
        };
        return JsonSerializer.Serialize(payload);
    }
}
