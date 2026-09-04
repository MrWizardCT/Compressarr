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

public class DiscordNotifierTests
{
    [Fact]
    public async Task SendAsync_PostsToConfiguredUrl()
    {
        var sender = new FakeWebhookSender();
        var notifier = new DiscordNotifier(sender);
        var evt = new NotificationEvent(NotificationOutcome.Success, "Title", "Body", 3, 1.5, TimeSpan.FromMinutes(2), null);

        await notifier.SendAsync(new Dictionary<string, string> { ["url"] = "https://discord.com/api/webhooks/123/abc" }, evt, CancellationToken.None);

        Assert.Equal("https://discord.com/api/webhooks/123/abc", sender.LastUrl);
    }

    [Fact]
    public async Task SendAsync_BuildsAnEmbedWithTitleAndDescription()
    {
        var sender = new FakeWebhookSender();
        var notifier = new DiscordNotifier(sender);
        var evt = new NotificationEvent(NotificationOutcome.Success, "My Title", "My Body", 3, 1.5, TimeSpan.FromMinutes(2), null);

        await notifier.SendAsync(new Dictionary<string, string> { ["url"] = "https://discord.com/x" }, evt, CancellationToken.None);

        Assert.Contains("\"title\":\"My Title\"", sender.LastBody);
        Assert.Contains("\"description\":\"My Body\"", sender.LastBody);
    }

    [Theory]
    [InlineData(NotificationOutcome.Success, 3066993)]  // 0x2ECC71
    [InlineData(NotificationOutcome.Warning, 15844367)] // 0xF1C40F
    [InlineData(NotificationOutcome.Error, 15158332)]   // 0xE74C3C
    public async Task SendAsync_ColorMatchesOutcome(NotificationOutcome outcome, int expectedColor)
    {
        var sender = new FakeWebhookSender();
        var notifier = new DiscordNotifier(sender);
        var evt = new NotificationEvent(outcome, "T", "B", 1, 1, TimeSpan.FromSeconds(1), null);

        await notifier.SendAsync(new Dictionary<string, string> { ["url"] = "https://discord.com/x" }, evt, CancellationToken.None);

        Assert.Contains($"\"color\":{expectedColor}", sender.LastBody);
    }

    [Fact]
    public async Task TestAsync_SendsWithoutRequiringARealEvent()
    {
        var sender = new FakeWebhookSender();
        var notifier = new DiscordNotifier(sender);

        var result = await notifier.TestAsync(new Dictionary<string, string> { ["url"] = "https://discord.com/x" }, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(1, sender.CallCount);
        Assert.Contains("Compressarr test notification", sender.LastBody);
    }
}
