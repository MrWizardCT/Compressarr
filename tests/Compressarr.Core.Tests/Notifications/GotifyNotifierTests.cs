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

public class GotifyNotifierTests
{
    [Fact]
    public async Task SendAsync_PostsToServerSlashMessage()
    {
        var sender = new FakeWebhookSender();
        var notifier = new GotifyNotifier(sender);
        var evt = new NotificationEvent(NotificationOutcome.Success, "Title", "Body", 3, 1.5, TimeSpan.FromMinutes(2), null);

        await notifier.SendAsync(new Dictionary<string, string> { ["server"] = "https://gotify.example.com/", ["appToken"] = "AT1" }, evt, CancellationToken.None);

        Assert.Equal("https://gotify.example.com/message", sender.LastUrl);
    }

    [Fact]
    public async Task SendAsync_SetsAppTokenHeaderAndJsonBody()
    {
        var sender = new FakeWebhookSender();
        var notifier = new GotifyNotifier(sender);
        var evt = new NotificationEvent(NotificationOutcome.Success, "My Title", "My Body", 3, 1.5, TimeSpan.FromMinutes(2), null);

        await notifier.SendAsync(new Dictionary<string, string> { ["server"] = "https://gotify.example.com", ["appToken"] = "AT1" }, evt, CancellationToken.None);

        Assert.Equal("AT1", sender.LastHeaders!["X-Gotify-Key"]);
        using var doc = JsonDocument.Parse(sender.LastBody!);
        Assert.Equal("My Title", doc.RootElement.GetProperty("title").GetString());
        Assert.Contains("My Body", doc.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task SendAsync_ErrorOutcomeUsesHigherPriorityThanSuccess()
    {
        var sender = new FakeWebhookSender();
        var notifier = new GotifyNotifier(sender);
        var evt = new NotificationEvent(NotificationOutcome.Error, "T", "B", 1, 1, TimeSpan.FromSeconds(1), null);

        await notifier.SendAsync(new Dictionary<string, string> { ["server"] = "https://gotify.example.com", ["appToken"] = "AT1" }, evt, CancellationToken.None);

        using var doc = JsonDocument.Parse(sender.LastBody!);
        Assert.True(doc.RootElement.GetProperty("priority").GetInt32() > 5);
    }

    [Fact]
    public async Task TestAsync_SendsWithoutRequiringARealEvent()
    {
        var sender = new FakeWebhookSender();
        var notifier = new GotifyNotifier(sender);

        var result = await notifier.TestAsync(new Dictionary<string, string> { ["server"] = "https://gotify.example.com", ["appToken"] = "AT1" }, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(1, sender.CallCount);
        Assert.Contains("Compressarr test notification", sender.LastBody);
    }
}
