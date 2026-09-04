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

public class NtfyNotifierTests
{
    [Fact]
    public async Task SendAsync_PostsToServerSlashTopic()
    {
        var sender = new FakeWebhookSender();
        var notifier = new NtfyNotifier(sender);
        var evt = new NotificationEvent(NotificationOutcome.Success, "Title", "Body", 3, 1.5, TimeSpan.FromMinutes(2), null);

        await notifier.SendAsync(new Dictionary<string, string> { ["server"] = "https://ntfy.sh/", ["topic"] = "my-topic" }, evt, CancellationToken.None);

        Assert.Equal("https://ntfy.sh/my-topic", sender.LastUrl);
    }

    [Fact]
    public async Task SendAsync_SetsTitleHeaderAndBodyIsPlainText()
    {
        var sender = new FakeWebhookSender();
        var notifier = new NtfyNotifier(sender);
        var evt = new NotificationEvent(NotificationOutcome.Success, "My Title", "My Body", 3, 1.5, TimeSpan.FromMinutes(2), null);

        await notifier.SendAsync(new Dictionary<string, string> { ["server"] = "https://ntfy.sh", ["topic"] = "t" }, evt, CancellationToken.None);

        Assert.Equal("My Title", sender.LastHeaders!["Title"]);
        Assert.Contains("My Body", sender.LastBody);
    }

    [Fact]
    public async Task SendAsync_ErrorOutcomeUsesHighPriorityAndAlertTag()
    {
        var sender = new FakeWebhookSender();
        var notifier = new NtfyNotifier(sender);
        var evt = new NotificationEvent(NotificationOutcome.Error, "T", "B", 1, 1, TimeSpan.FromSeconds(1), null);

        await notifier.SendAsync(new Dictionary<string, string> { ["server"] = "https://ntfy.sh", ["topic"] = "t" }, evt, CancellationToken.None);

        Assert.Equal("4", sender.LastHeaders!["Priority"]);
        Assert.Equal("rotating_light", sender.LastHeaders!["Tags"]);
    }

    [Fact]
    public async Task SendAsync_OmitsAuthorizationHeaderWhenNoAccessToken()
    {
        var sender = new FakeWebhookSender();
        var notifier = new NtfyNotifier(sender);
        var evt = new NotificationEvent(NotificationOutcome.Success, "T", "B", 1, 1, TimeSpan.FromSeconds(1), null);

        await notifier.SendAsync(new Dictionary<string, string> { ["server"] = "https://ntfy.sh", ["topic"] = "t" }, evt, CancellationToken.None);

        Assert.False(sender.LastHeaders!.ContainsKey("Authorization"));
    }

    [Fact]
    public async Task SendAsync_IncludesAuthorizationHeaderWhenAccessTokenProvided()
    {
        var sender = new FakeWebhookSender();
        var notifier = new NtfyNotifier(sender);
        var evt = new NotificationEvent(NotificationOutcome.Success, "T", "B", 1, 1, TimeSpan.FromSeconds(1), null);

        await notifier.SendAsync(new Dictionary<string, string> { ["server"] = "https://ntfy.sh", ["topic"] = "t", ["accessToken"] = "tk_abc" }, evt, CancellationToken.None);

        Assert.Equal("Bearer tk_abc", sender.LastHeaders!["Authorization"]);
    }

    [Fact]
    public async Task TestAsync_SendsWithoutRequiringARealEvent()
    {
        var sender = new FakeWebhookSender();
        var notifier = new NtfyNotifier(sender);

        var result = await notifier.TestAsync(new Dictionary<string, string> { ["server"] = "https://ntfy.sh", ["topic"] = "t" }, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(1, sender.CallCount);
        Assert.Equal("Compressarr test notification", sender.LastHeaders!["Title"]);
        Assert.Contains("This is a test notification from Compressarr.", sender.LastBody);
    }
}
