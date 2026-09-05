using System.Net;
using Compressarr.Core.Notifications;

namespace Compressarr.Core.Tests.Notifications;

file sealed class FakeNotificationHttpClient : INotificationHttpClient
{
    public HttpStatusCode StatusCode { get; set; } = HttpStatusCode.OK;
    public Uri? LastDestination { get; private set; }
    public IReadOnlyDictionary<string, string>? LastFields { get; private set; }
    public int CallCount { get; private set; }

    public Task<HttpResponseMessage> PostJsonAsync(Uri destination, object payload, IReadOnlyDictionary<string, string>? headers, CancellationToken ct) =>
        throw new NotSupportedException("Pushover only ever posts form-encoded.");

    public Task<HttpResponseMessage> PostFormAsync(Uri destination, IReadOnlyDictionary<string, string> fields, CancellationToken ct)
    {
        CallCount++;
        LastDestination = destination;
        LastFields = fields;
        return Task.FromResult(new HttpResponseMessage(StatusCode));
    }

    public Task<HttpResponseMessage> PostTextAsync(Uri destination, string text, IReadOnlyDictionary<string, string>? headers, CancellationToken ct) =>
        throw new NotSupportedException("Pushover only ever posts form-encoded.");
}

public class PushoverNotifierTests
{
    [Fact]
    public async Task SendAsync_PostsToPushoverApi()
    {
        var http = new FakeNotificationHttpClient();
        var notifier = new PushoverNotifier(http);
        var evt = new NotificationEvent(NotificationOutcome.Success, "Title", "Body", 3, 1.5, TimeSpan.FromMinutes(2), null);

        await notifier.SendAsync(new Dictionary<string, string> { ["appToken"] = "app123", ["userKey"] = "user456" }, evt, CancellationToken.None);

        Assert.Equal("https://api.pushover.net/1/messages.json", http.LastDestination!.ToString());
    }

    [Fact]
    public async Task SendAsync_FieldsIncludeTokenUserTitleAndMessage()
    {
        var http = new FakeNotificationHttpClient();
        var notifier = new PushoverNotifier(http);
        var evt = new NotificationEvent(NotificationOutcome.Success, "My Title", "My Body", 3, 1.5, TimeSpan.FromMinutes(2), null);

        await notifier.SendAsync(new Dictionary<string, string> { ["appToken"] = "app123", ["userKey"] = "user456" }, evt, CancellationToken.None);

        Assert.Equal("app123", http.LastFields!["token"]);
        Assert.Equal("user456", http.LastFields!["user"]);
        Assert.Equal("My Title", http.LastFields!["title"]);
        Assert.Equal("0", http.LastFields!["priority"]);
    }

    [Fact]
    public async Task SendAsync_ErrorOutcomeUsesHighPriority()
    {
        var http = new FakeNotificationHttpClient();
        var notifier = new PushoverNotifier(http);
        var evt = new NotificationEvent(NotificationOutcome.Error, "Title", "Body", 3, 1.5, TimeSpan.FromMinutes(2), null);

        await notifier.SendAsync(new Dictionary<string, string> { ["appToken"] = "a", ["userKey"] = "u" }, evt, CancellationToken.None);

        Assert.Equal("1", http.LastFields!["priority"]);
    }

    [Fact]
    public async Task TestAsync_SendsWithoutRequiringARealEvent()
    {
        var http = new FakeNotificationHttpClient();
        var notifier = new PushoverNotifier(http);

        var result = await notifier.TestAsync(new Dictionary<string, string> { ["appToken"] = "a", ["userKey"] = "u" }, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(1, http.CallCount);
        Assert.Contains("Compressarr test notification", http.LastFields!["title"]);
    }
}
