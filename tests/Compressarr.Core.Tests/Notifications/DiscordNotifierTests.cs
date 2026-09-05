using System.Net;
using System.Text.Json;
using Compressarr.Core.Notifications;

namespace Compressarr.Core.Tests.Notifications;

file sealed class FakeNotificationHttpClient : INotificationHttpClient
{
    public HttpStatusCode StatusCode { get; set; } = HttpStatusCode.OK;
    public Uri? LastDestination { get; private set; }
    public string? LastBody { get; private set; }
    public IReadOnlyDictionary<string, string>? LastHeaders { get; private set; }
    public int CallCount { get; private set; }

    public Task<HttpResponseMessage> PostJsonAsync(Uri destination, object payload, IReadOnlyDictionary<string, string>? headers, CancellationToken ct)
    {
        CallCount++;
        LastDestination = destination;
        LastBody = JsonSerializer.Serialize(payload);
        LastHeaders = headers;
        return Task.FromResult(new HttpResponseMessage(StatusCode));
    }

    public Task<HttpResponseMessage> PostFormAsync(Uri destination, IReadOnlyDictionary<string, string> fields, CancellationToken ct) =>
        throw new NotSupportedException("Discord only ever posts JSON.");

    public Task<HttpResponseMessage> PostTextAsync(Uri destination, string text, IReadOnlyDictionary<string, string>? headers, CancellationToken ct) =>
        throw new NotSupportedException("Discord only ever posts JSON.");
}

public class DiscordNotifierTests
{
    [Fact]
    public async Task SendAsync_PostsToConfiguredWebhookUrl()
    {
        var http = new FakeNotificationHttpClient();
        var notifier = new DiscordNotifier(http);
        var evt = new NotificationEvent(NotificationOutcome.Success, "Title", "Body", 3, 1.5, TimeSpan.FromMinutes(2), null);

        await notifier.SendAsync(new Dictionary<string, string> { ["url"] = "https://discord.com/api/webhooks/1/abc" }, evt, CancellationToken.None);

        Assert.Equal("https://discord.com/api/webhooks/1/abc", http.LastDestination!.ToString());
    }

    [Fact]
    public async Task SendAsync_BodyIsAnEmbedWithTitleAndDescription()
    {
        var http = new FakeNotificationHttpClient();
        var notifier = new DiscordNotifier(http);
        var evt = new NotificationEvent(NotificationOutcome.Success, "My Title", "My Body", 3, 1.5, TimeSpan.FromMinutes(2), null);

        await notifier.SendAsync(new Dictionary<string, string> { ["url"] = "https://discord.com/api/webhooks/1/abc" }, evt, CancellationToken.None);

        using var doc = JsonDocument.Parse(http.LastBody!);
        var embed = doc.RootElement.GetProperty("embeds")[0];
        Assert.Equal("My Title", embed.GetProperty("title").GetString());
        Assert.Equal("My Body", embed.GetProperty("description").GetString());
    }

    [Fact]
    public async Task SendAsync_ErrorOutcomeUsesRedColor()
    {
        var http = new FakeNotificationHttpClient();
        var notifier = new DiscordNotifier(http);
        var evt = new NotificationEvent(NotificationOutcome.Error, "T", "B", 1, 1, TimeSpan.FromSeconds(1), null);

        await notifier.SendAsync(new Dictionary<string, string> { ["url"] = "https://discord.com/api/webhooks/1/abc" }, evt, CancellationToken.None);

        using var doc = JsonDocument.Parse(http.LastBody!);
        Assert.Equal(0xE74C3C, doc.RootElement.GetProperty("embeds")[0].GetProperty("color").GetInt32());
    }

    [Fact]
    public async Task SendAsync_InvalidUrlFailsWithoutCallingTheClient()
    {
        var http = new FakeNotificationHttpClient();
        var notifier = new DiscordNotifier(http);
        var evt = new NotificationEvent(NotificationOutcome.Success, "T", "B", 1, 1, TimeSpan.FromSeconds(1), null);

        var result = await notifier.SendAsync(new Dictionary<string, string> { ["url"] = "not a url" }, evt, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(0, http.CallCount);
    }

    [Fact]
    public async Task TestAsync_SendsWithoutRequiringARealEvent()
    {
        var http = new FakeNotificationHttpClient();
        var notifier = new DiscordNotifier(http);

        var result = await notifier.TestAsync(new Dictionary<string, string> { ["url"] = "https://discord.com/api/webhooks/1/abc" }, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(1, http.CallCount);
        Assert.Contains("Compressarr test notification", http.LastBody);
    }
}
