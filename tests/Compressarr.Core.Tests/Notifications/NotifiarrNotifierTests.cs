using System.Text.Json;
using Compressarr.Core.Notifications;

namespace Compressarr.Core.Tests.Notifications;

file sealed class FakeWebhookSender : IWebhookSender
{
    public NotifyResult Result { get; set; } = new(true, "OK");
    public string? LastUrl { get; private set; }
    public IReadOnlyDictionary<string, string>? LastHeaders { get; private set; }
    public string? LastBody { get; private set; }
    public int CallCount { get; private set; }

    public Task<NotifyResult> PostAsync(string url, HttpMethod method, IReadOnlyDictionary<string, string> headers, string body, string contentType, CancellationToken ct)
    {
        CallCount++;
        LastUrl = url;
        LastHeaders = headers;
        LastBody = body;
        return Task.FromResult(Result);
    }
}

public class NotifiarrNotifierTests
{
    [Fact]
    public async Task SendAsync_PostsToPassthroughUrlWithApiKeyInPath()
    {
        var sender = new FakeWebhookSender();
        var notifier = new NotifiarrNotifier(sender);
        var evt = new NotificationEvent(NotificationOutcome.Success, "Title", "Body", 3, 1.5, TimeSpan.FromMinutes(2), null);

        await notifier.SendAsync(new Dictionary<string, string> { ["apiKey"] = "key123", ["channelId"] = "987654321" }, evt, CancellationToken.None);

        Assert.Equal("https://notifiarr.com/api/v1/notification/passthrough/key123", sender.LastUrl);
    }

    [Fact]
    public async Task SendAsync_BodyIncludesChannelIdAsNumberAndDiscordText()
    {
        var sender = new FakeWebhookSender();
        var notifier = new NotifiarrNotifier(sender);
        var evt = new NotificationEvent(NotificationOutcome.Success, "My Title", "My Body", 3, 1.5, TimeSpan.FromMinutes(2), null);

        await notifier.SendAsync(new Dictionary<string, string> { ["apiKey"] = "key123", ["channelId"] = "987654321" }, evt, CancellationToken.None);

        using var doc = JsonDocument.Parse(sender.LastBody!);
        Assert.Equal(987654321, doc.RootElement.GetProperty("discord").GetProperty("ids").GetProperty("channel").GetInt64());
        Assert.Equal("My Title", doc.RootElement.GetProperty("discord").GetProperty("text").GetProperty("title").GetString());
        Assert.Equal("My Body", doc.RootElement.GetProperty("discord").GetProperty("text").GetProperty("description").GetString());
        Assert.Equal("Compressarr", doc.RootElement.GetProperty("notification").GetProperty("name").GetString());
    }

    [Fact]
    public async Task SendAsync_ColorIsHexStringNotDecimal()
    {
        var sender = new FakeWebhookSender();
        var notifier = new NotifiarrNotifier(sender);
        var evt = new NotificationEvent(NotificationOutcome.Error, "T", "B", 1, 1, TimeSpan.FromSeconds(1), null);

        await notifier.SendAsync(new Dictionary<string, string> { ["apiKey"] = "k", ["channelId"] = "1" }, evt, CancellationToken.None);

        using var doc = JsonDocument.Parse(sender.LastBody!);
        Assert.Equal("E74C3C", doc.RootElement.GetProperty("discord").GetProperty("color").GetString());
    }

    [Fact]
    public async Task TestAsync_SendsWithoutRequiringARealEvent()
    {
        var sender = new FakeWebhookSender();
        var notifier = new NotifiarrNotifier(sender);

        var result = await notifier.TestAsync(new Dictionary<string, string> { ["apiKey"] = "k", ["channelId"] = "1" }, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(1, sender.CallCount);
        Assert.Contains("Compressarr test notification", sender.LastBody);
    }
}
