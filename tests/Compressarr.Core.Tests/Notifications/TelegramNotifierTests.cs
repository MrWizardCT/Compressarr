using System.Net;
using System.Text.Json;
using Compressarr.Core.Notifications;

namespace Compressarr.Core.Tests.Notifications;

file sealed class FakeNotificationHttpClient : INotificationHttpClient
{
    public HttpStatusCode StatusCode { get; set; } = HttpStatusCode.OK;
    public Uri? LastDestination { get; private set; }
    public string? LastBody { get; private set; }
    public int CallCount { get; private set; }

    public Task<HttpResponseMessage> PostJsonAsync(Uri destination, object payload, IReadOnlyDictionary<string, string>? headers, CancellationToken ct)
    {
        CallCount++;
        LastDestination = destination;
        LastBody = JsonSerializer.Serialize(payload);
        return Task.FromResult(new HttpResponseMessage(StatusCode));
    }

    public Task<HttpResponseMessage> PostFormAsync(Uri destination, IReadOnlyDictionary<string, string> fields, CancellationToken ct) =>
        throw new NotSupportedException("Telegram only ever posts JSON.");

    public Task<HttpResponseMessage> PostTextAsync(Uri destination, string text, IReadOnlyDictionary<string, string>? headers, CancellationToken ct) =>
        throw new NotSupportedException("Telegram only ever posts JSON.");
}

public class TelegramNotifierTests
{
    [Fact]
    public async Task SendAsync_PostsToTelegramApiWithBotTokenInUrl()
    {
        var http = new FakeNotificationHttpClient();
        var notifier = new TelegramNotifier(http);
        var evt = new NotificationEvent(NotificationOutcome.Success, "Title", "Body", 3, 1.5, TimeSpan.FromMinutes(2), null);

        await notifier.SendAsync(new Dictionary<string, string> { ["botToken"] = "123:ABC", ["chatId"] = "999" }, evt, CancellationToken.None);

        Assert.Equal("https://api.telegram.org/bot123:ABC/sendMessage", http.LastDestination!.ToString());
    }

    [Fact]
    public async Task SendAsync_BodyIncludesChatIdAndText()
    {
        var http = new FakeNotificationHttpClient();
        var notifier = new TelegramNotifier(http);
        var evt = new NotificationEvent(NotificationOutcome.Success, "My Title", "My Body", 3, 1.5, TimeSpan.FromMinutes(2), null);

        await notifier.SendAsync(new Dictionary<string, string> { ["botToken"] = "1:A", ["chatId"] = "42" }, evt, CancellationToken.None);

        using var doc = JsonDocument.Parse(http.LastBody!);
        Assert.Equal("42", doc.RootElement.GetProperty("chat_id").GetString());
        var text = doc.RootElement.GetProperty("text").GetString();
        Assert.Contains("My Title", text);
        Assert.Contains("My Body", text);
    }

    [Fact]
    public async Task TestAsync_SendsWithoutRequiringARealEvent()
    {
        var http = new FakeNotificationHttpClient();
        var notifier = new TelegramNotifier(http);

        var result = await notifier.TestAsync(new Dictionary<string, string> { ["botToken"] = "1:A", ["chatId"] = "42" }, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(1, http.CallCount);
        Assert.Contains("Compressarr test notification", http.LastBody);
    }
}
