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

public class PushoverNotifierTests
{
    [Fact]
    public async Task SendAsync_PostsToPushoverApi()
    {
        var sender = new FakeWebhookSender();
        var notifier = new PushoverNotifier(sender);
        var evt = new NotificationEvent(NotificationOutcome.Success, "Title", "Body", 3, 1.5, TimeSpan.FromMinutes(2), null);

        await notifier.SendAsync(new Dictionary<string, string> { ["appToken"] = "app123", ["userKey"] = "user456" }, evt, CancellationToken.None);

        Assert.Equal("https://api.pushover.net/1/messages.json", sender.LastUrl);
    }

    [Fact]
    public async Task SendAsync_BodyIsFormEncodedWithTokenUserTitleAndMessage()
    {
        var sender = new FakeWebhookSender();
        var notifier = new PushoverNotifier(sender);
        var evt = new NotificationEvent(NotificationOutcome.Success, "My Title", "My Body", 3, 1.5, TimeSpan.FromMinutes(2), null);

        await notifier.SendAsync(new Dictionary<string, string> { ["appToken"] = "app123", ["userKey"] = "user456" }, evt, CancellationToken.None);

        Assert.Contains("token=app123", sender.LastBody);
        Assert.Contains("user=user456", sender.LastBody);
        Assert.Contains("title=My+Title", sender.LastBody);
        Assert.Contains("priority=0", sender.LastBody);
    }

    [Fact]
    public async Task SendAsync_ErrorOutcomeUsesHighPriority()
    {
        var sender = new FakeWebhookSender();
        var notifier = new PushoverNotifier(sender);
        var evt = new NotificationEvent(NotificationOutcome.Error, "Title", "Body", 3, 1.5, TimeSpan.FromMinutes(2), null);

        await notifier.SendAsync(new Dictionary<string, string> { ["appToken"] = "a", ["userKey"] = "u" }, evt, CancellationToken.None);

        Assert.Contains("priority=1", sender.LastBody);
    }

    [Fact]
    public async Task TestAsync_SendsWithoutRequiringARealEvent()
    {
        var sender = new FakeWebhookSender();
        var notifier = new PushoverNotifier(sender);

        var result = await notifier.TestAsync(new Dictionary<string, string> { ["appToken"] = "a", ["userKey"] = "u" }, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(1, sender.CallCount);
        Assert.Contains("Compressarr+test+notification", sender.LastBody);
    }
}
