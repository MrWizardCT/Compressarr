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

public class TelegramNotifierTests
{
    [Fact]
    public async Task SendAsync_PostsToTelegramApiWithBotTokenInUrl()
    {
        var sender = new FakeWebhookSender();
        var notifier = new TelegramNotifier(sender);
        var evt = new NotificationEvent(NotificationOutcome.Success, "Title", "Body", 3, 1.5, TimeSpan.FromMinutes(2), null);

        await notifier.SendAsync(new Dictionary<string, string> { ["botToken"] = "123:ABC", ["chatId"] = "999" }, evt, CancellationToken.None);

        Assert.Equal("https://api.telegram.org/bot123:ABC/sendMessage", sender.LastUrl);
    }

    [Fact]
    public async Task SendAsync_BodyIncludesChatIdAndText()
    {
        var sender = new FakeWebhookSender();
        var notifier = new TelegramNotifier(sender);
        var evt = new NotificationEvent(NotificationOutcome.Success, "My Title", "My Body", 3, 1.5, TimeSpan.FromMinutes(2), null);

        await notifier.SendAsync(new Dictionary<string, string> { ["botToken"] = "1:A", ["chatId"] = "42" }, evt, CancellationToken.None);

        using var doc = JsonDocument.Parse(sender.LastBody!);
        Assert.Equal("42", doc.RootElement.GetProperty("chat_id").GetString());
        var text = doc.RootElement.GetProperty("text").GetString();
        Assert.Contains("My Title", text);
        Assert.Contains("My Body", text);
    }

    [Fact]
    public async Task TestAsync_SendsWithoutRequiringARealEvent()
    {
        var sender = new FakeWebhookSender();
        var notifier = new TelegramNotifier(sender);

        var result = await notifier.TestAsync(new Dictionary<string, string> { ["botToken"] = "1:A", ["chatId"] = "42" }, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(1, sender.CallCount);
        Assert.Contains("Compressarr test notification", sender.LastBody);
    }
}
