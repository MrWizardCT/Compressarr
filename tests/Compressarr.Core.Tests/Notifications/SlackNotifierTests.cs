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
        throw new NotSupportedException("Slack only ever posts JSON.");

    public Task<HttpResponseMessage> PostTextAsync(Uri destination, string text, IReadOnlyDictionary<string, string>? headers, CancellationToken ct) =>
        throw new NotSupportedException("Slack only ever posts JSON.");
}

public class SlackNotifierTests
{
    [Fact]
    public async Task SendAsync_PostsToConfiguredWebhookUrl()
    {
        var http = new FakeNotificationHttpClient();
        var notifier = new SlackNotifier(http);
        var evt = new NotificationEvent(NotificationOutcome.Success, "Title", "Body", 3, 1.5, TimeSpan.FromMinutes(2), null);

        await notifier.SendAsync(new Dictionary<string, string> { ["url"] = "https://hooks.slack.com/services/T/B/x" }, evt, CancellationToken.None);

        Assert.Equal("https://hooks.slack.com/services/T/B/x", http.LastDestination!.ToString());
    }

    [Fact]
    public async Task SendAsync_BodyIsTextFieldWithTitleAndBody()
    {
        var http = new FakeNotificationHttpClient();
        var notifier = new SlackNotifier(http);
        var evt = new NotificationEvent(NotificationOutcome.Success, "My Title", "My Body", 3, 1.5, TimeSpan.FromMinutes(2), null);

        await notifier.SendAsync(new Dictionary<string, string> { ["url"] = "https://hooks.slack.com/services/T/B/x" }, evt, CancellationToken.None);

        using var doc = JsonDocument.Parse(http.LastBody!);
        var text = doc.RootElement.GetProperty("text").GetString();
        Assert.Contains("My Title", text);
        Assert.Contains("My Body", text);
    }

    [Fact]
    public async Task SendAsync_EscapesSlackMrkdwnSpecialCharacters()
    {
        var http = new FakeNotificationHttpClient();
        var notifier = new SlackNotifier(http);
        var evt = new NotificationEvent(NotificationOutcome.Success, "A & B <C>", "Body", 1, 1, TimeSpan.FromSeconds(1), null);

        await notifier.SendAsync(new Dictionary<string, string> { ["url"] = "https://hooks.slack.com/services/T/B/x" }, evt, CancellationToken.None);

        using var doc = JsonDocument.Parse(http.LastBody!);
        Assert.Contains("A &amp; B &lt;C&gt;", doc.RootElement.GetProperty("text").GetString());
    }

    [Fact]
    public async Task SendAsync_InvalidUrlFailsWithoutCallingTheClient()
    {
        var http = new FakeNotificationHttpClient();
        var notifier = new SlackNotifier(http);
        var evt = new NotificationEvent(NotificationOutcome.Success, "T", "B", 1, 1, TimeSpan.FromSeconds(1), null);

        var result = await notifier.SendAsync(new Dictionary<string, string> { ["url"] = "" }, evt, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(0, http.CallCount);
    }

    [Fact]
    public async Task TestAsync_SendsWithoutRequiringARealEvent()
    {
        var http = new FakeNotificationHttpClient();
        var notifier = new SlackNotifier(http);

        var result = await notifier.TestAsync(new Dictionary<string, string> { ["url"] = "https://hooks.slack.com/services/T/B/x" }, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(1, http.CallCount);
        Assert.Contains("Compressarr test notification", http.LastBody);
    }
}
