namespace Compressarr.Core.Notifications;

/// <summary>ntfy - simple pub/sub push. The POST body is the raw message text (not JSON); title,
/// priority, and tags travel as headers, per ntfy's documented publish API. Server URL is
/// user-configurable rather than fixed, since ntfy is commonly self-hosted - defaults to the
/// public ntfy.sh instance in the UI placeholder only, not hardcoded here.</summary>
public sealed class NtfyNotifier : INotifier
{
    private readonly IWebhookSender _sender;

    public NtfyNotifier(IWebhookSender sender)
    {
        _sender = sender;
    }

    public string Type => "ntfy";
    public string DisplayName => "ntfy";

    public IReadOnlyList<NotifierField> Fields { get; } = new[]
    {
        new NotifierField("server", "Server URL", "text", Required: true,
            HelpText: "The ntfy server to publish to. Defaults to the public ntfy.sh instance - change this if you're self-hosting ntfy.",
            Placeholder: "https://ntfy.sh", DefaultValue: "https://ntfy.sh"),
        new NotifierField("topic", "Topic", "text", Required: true,
            HelpText: "Any string works. Pick something unique and hard to guess - anyone who knows your topic name can subscribe to it too.",
            Placeholder: "compressarr-a1b2c3"),
        new NotifierField("accessToken", "Access Token (optional, for protected topics)", "text", Required: false, Secret: true,
            HelpText: "Only needed if your topic is access-controlled. Leave blank for a public topic on a public server.",
            Placeholder: "tk_...")
    };

    public Task<NotifyResult> SendAsync(IReadOnlyDictionary<string, string> settings, NotificationEvent evt, CancellationToken ct)
    {
        var (priority, tags) = evt.Outcome switch
        {
            NotificationOutcome.Error => ("4", "rotating_light"),
            NotificationOutcome.Warning => ("4", "warning"),
            _ => ("3", "white_check_mark")
        };
        var summary = $"Files: {evt.TotalFiles} | Saved: {evt.SavedGb:0.##} GB | Duration: {NotificationFormatting.FormatDuration(evt.Duration)}";
        var message = $"{evt.Body}\n{summary}";
        return Post(settings, evt.Title, message, priority, tags, ct);
    }

    public Task<NotifyResult> TestAsync(IReadOnlyDictionary<string, string> settings, CancellationToken ct)
    {
        return Post(settings, "Compressarr test notification", "This is a test notification from Compressarr.", "3", "white_check_mark", ct);
    }

    private Task<NotifyResult> Post(IReadOnlyDictionary<string, string> settings, string title, string message, string priority, string tags, CancellationToken ct)
    {
        settings.TryGetValue("server", out var server);
        settings.TryGetValue("topic", out var topic);
        var url = $"{(server ?? "").TrimEnd('/')}/{topic}";

        var headers = new Dictionary<string, string>
        {
            ["Title"] = title,
            ["Priority"] = priority,
            ["Tags"] = tags
        };
        if (settings.TryGetValue("accessToken", out var token) && !string.IsNullOrWhiteSpace(token))
        {
            headers["Authorization"] = $"Bearer {token}";
        }

        // StringContent's mediaType parameter must be the bare type - it appends the charset
        // itself from the Encoding argument, so "text/plain; charset=utf-8" here throws.
        return _sender.PostAsync(url, HttpMethod.Post, headers, message, "text/plain", ct);
    }
}
