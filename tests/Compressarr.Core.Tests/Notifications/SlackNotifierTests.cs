using System.Text.Json;
using Compressarr.Core.Notifications;

namespace Compressarr.Core.Tests.Notifications;

file sealed class FakeWebhookSender : IWebhookSender
{
    public NotifyResult Result { get; set; } = new(true, "OK");
    public string? LastUrl { get; private set; }
    public string? LastBody { get; private set; }
    public int CallCount { get; private set; }

    public Task<NotifyResult> PostAsync(string url, HttpMethod method, IReadOnlyDictionary<string, string> headers, string body, string contentType, CancellationToken ct)
    {
        CallCount++;
        LastUrl = url;
        LastBody = body;
        return Task.FromResult(Result);
    }
}

public class SlackNotifierTests
{
    [Fact]
    public async Task SendAsync_PostsToConfiguredUrl()
    {
        var sender = new FakeWebhookSender();
        var notifier = new SlackNotifier(sender);
        var evt = new NotificationEvent(NotificationOutcome.Success, "Title", "Body", 3, 1.5, TimeSpan.FromMinutes(2), null);

        await notifier.SendAsync(new Dictionary<string, string> { ["url"] = "https://hooks.slack.com/services/x/y/z" }, evt, CancellationToken.None);

        Assert.Equal("https://hooks.slack.com/services/x/y/z", sender.LastUrl);
    }

    [Fact]
    public async Task SendAsync_TextIncludesTitleBodyAndSummary()
    {
        var sender = new FakeWebhookSender();
        var notifier = new SlackNotifier(sender);
        var evt = new NotificationEvent(NotificationOutcome.Success, "My Title", "My Body", 3, 1.5, TimeSpan.FromMinutes(2), null);

        await notifier.SendAsync(new Dictionary<string, string> { ["url"] = "https://hooks.slack.com/x" }, evt, CancellationToken.None);

        Assert.Contains("My Title", sender.LastBody);
        Assert.Contains("My Body", sender.LastBody);
        Assert.Contains("Files: 3", sender.LastBody);
        Assert.Contains("Saved: 1.5 GB", sender.LastBody);
    }

    [Fact]
    public async Task SendAsync_EscapesSlackMarkdownSpecialCharacters()
    {
        // Checks the DECODED "text" value, not the raw JSON bytes - JsonSerializer's default
        // encoder unicode-escapes &/</> within a string value (& etc), which is still valid
        // JSON and decodes back to the literal character fine; substring-matching the raw wire
        // bytes for "&amp;" would fail even though Slack itself receives the right text.
        var sender = new FakeWebhookSender();
        var notifier = new SlackNotifier(sender);
        var evt = new NotificationEvent(NotificationOutcome.Success, "A & B <report>", "Body", 1, 1, TimeSpan.FromSeconds(1), null);

        await notifier.SendAsync(new Dictionary<string, string> { ["url"] = "https://hooks.slack.com/x" }, evt, CancellationToken.None);

        using var doc = JsonDocument.Parse(sender.LastBody!);
        var text = doc.RootElement.GetProperty("text").GetString();
        Assert.Contains("A &amp; B &lt;report&gt;", text);
    }

    [Fact]
    public async Task TestAsync_SendsWithoutRequiringARealEvent()
    {
        var sender = new FakeWebhookSender();
        var notifier = new SlackNotifier(sender);

        var result = await notifier.TestAsync(new Dictionary<string, string> { ["url"] = "https://hooks.slack.com/x" }, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(1, sender.CallCount);
        Assert.Contains("Compressarr test notification", sender.LastBody);
    }
}
