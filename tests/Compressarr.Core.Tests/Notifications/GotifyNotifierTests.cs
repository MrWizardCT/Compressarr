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
        throw new NotSupportedException("Gotify only ever posts JSON.");

    public Task<HttpResponseMessage> PostTextAsync(Uri destination, string text, IReadOnlyDictionary<string, string>? headers, CancellationToken ct) =>
        throw new NotSupportedException("Gotify only ever posts JSON.");
}

public class GotifyNotifierTests
{
    [Fact]
    public async Task SendAsync_PostsToServerSlashMessage()
    {
        var http = new FakeNotificationHttpClient();
        var notifier = new GotifyNotifier(http);
        var evt = new NotificationEvent(NotificationOutcome.Success, "Title", "Body", 3, 1.5, TimeSpan.FromMinutes(2), null);

        await notifier.SendAsync(new Dictionary<string, string> { ["server"] = "https://gotify.example.com/", ["appToken"] = "AT1" }, evt, CancellationToken.None);

        Assert.Equal("https://gotify.example.com/message", http.LastDestination!.ToString());
    }

    [Fact]
    public async Task SendAsync_SetsAppTokenHeaderAndJsonBody()
    {
        var http = new FakeNotificationHttpClient();
        var notifier = new GotifyNotifier(http);
        var evt = new NotificationEvent(NotificationOutcome.Success, "My Title", "My Body", 3, 1.5, TimeSpan.FromMinutes(2), null);

        await notifier.SendAsync(new Dictionary<string, string> { ["server"] = "https://gotify.example.com", ["appToken"] = "AT1" }, evt, CancellationToken.None);

        Assert.Equal("AT1", http.LastHeaders!["X-Gotify-Key"]);
        using var doc = JsonDocument.Parse(http.LastBody!);
        Assert.Equal("My Title", doc.RootElement.GetProperty("title").GetString());
        Assert.Contains("My Body", doc.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task SendAsync_ErrorOutcomeUsesHigherPriorityThanSuccess()
    {
        var http = new FakeNotificationHttpClient();
        var notifier = new GotifyNotifier(http);
        var evt = new NotificationEvent(NotificationOutcome.Error, "T", "B", 1, 1, TimeSpan.FromSeconds(1), null);

        await notifier.SendAsync(new Dictionary<string, string> { ["server"] = "https://gotify.example.com", ["appToken"] = "AT1" }, evt, CancellationToken.None);

        using var doc = JsonDocument.Parse(http.LastBody!);
        Assert.True(doc.RootElement.GetProperty("priority").GetInt32() > 5);
    }

    [Fact]
    public async Task TestAsync_SendsWithoutRequiringARealEvent()
    {
        var http = new FakeNotificationHttpClient();
        var notifier = new GotifyNotifier(http);

        var result = await notifier.TestAsync(new Dictionary<string, string> { ["server"] = "https://gotify.example.com", ["appToken"] = "AT1" }, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(1, http.CallCount);
        Assert.Contains("Compressarr test notification", http.LastBody);
    }
}
