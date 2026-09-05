using System.Net;
using Compressarr.Core.Notifications;

namespace Compressarr.Core.Tests.Notifications;

file sealed class FakeNotificationHttpClient : INotificationHttpClient
{
    public HttpStatusCode StatusCode { get; set; } = HttpStatusCode.OK;
    public Uri? LastDestination { get; private set; }
    public string? LastText { get; private set; }
    public IReadOnlyDictionary<string, string>? LastHeaders { get; private set; }
    public int CallCount { get; private set; }

    public Task<HttpResponseMessage> PostJsonAsync(Uri destination, object payload, IReadOnlyDictionary<string, string>? headers, CancellationToken ct) =>
        throw new NotSupportedException("ntfy only ever posts raw text.");

    public Task<HttpResponseMessage> PostFormAsync(Uri destination, IReadOnlyDictionary<string, string> fields, CancellationToken ct) =>
        throw new NotSupportedException("ntfy only ever posts raw text.");

    public Task<HttpResponseMessage> PostTextAsync(Uri destination, string text, IReadOnlyDictionary<string, string>? headers, CancellationToken ct)
    {
        CallCount++;
        LastDestination = destination;
        LastText = text;
        LastHeaders = headers;
        return Task.FromResult(new HttpResponseMessage(StatusCode));
    }
}

public class NtfyNotifierTests
{
    [Fact]
    public async Task SendAsync_PostsToServerSlashTopic()
    {
        var http = new FakeNotificationHttpClient();
        var notifier = new NtfyNotifier(http);
        var evt = new NotificationEvent(NotificationOutcome.Success, "Title", "Body", 3, 1.5, TimeSpan.FromMinutes(2), null);

        await notifier.SendAsync(new Dictionary<string, string> { ["server"] = "https://ntfy.sh/", ["topic"] = "my-topic" }, evt, CancellationToken.None);

        Assert.Equal("https://ntfy.sh/my-topic", http.LastDestination!.ToString());
    }

    [Fact]
    public async Task SendAsync_SetsTitleHeaderAndBodyIsPlainText()
    {
        var http = new FakeNotificationHttpClient();
        var notifier = new NtfyNotifier(http);
        var evt = new NotificationEvent(NotificationOutcome.Success, "My Title", "My Body", 3, 1.5, TimeSpan.FromMinutes(2), null);

        await notifier.SendAsync(new Dictionary<string, string> { ["server"] = "https://ntfy.sh", ["topic"] = "t" }, evt, CancellationToken.None);

        Assert.Equal("My Title", http.LastHeaders!["Title"]);
        Assert.Contains("My Body", http.LastText);
    }

    [Fact]
    public async Task SendAsync_ErrorOutcomeUsesHighPriorityAndAlertTag()
    {
        var http = new FakeNotificationHttpClient();
        var notifier = new NtfyNotifier(http);
        var evt = new NotificationEvent(NotificationOutcome.Error, "T", "B", 1, 1, TimeSpan.FromSeconds(1), null);

        await notifier.SendAsync(new Dictionary<string, string> { ["server"] = "https://ntfy.sh", ["topic"] = "t" }, evt, CancellationToken.None);

        Assert.Equal("4", http.LastHeaders!["Priority"]);
        Assert.Equal("rotating_light", http.LastHeaders!["Tags"]);
    }

    [Fact]
    public async Task SendAsync_OmitsAuthorizationHeaderWhenNoAccessToken()
    {
        var http = new FakeNotificationHttpClient();
        var notifier = new NtfyNotifier(http);
        var evt = new NotificationEvent(NotificationOutcome.Success, "T", "B", 1, 1, TimeSpan.FromSeconds(1), null);

        await notifier.SendAsync(new Dictionary<string, string> { ["server"] = "https://ntfy.sh", ["topic"] = "t" }, evt, CancellationToken.None);

        Assert.False(http.LastHeaders!.ContainsKey("Authorization"));
    }

    [Fact]
    public async Task SendAsync_IncludesAuthorizationHeaderWhenAccessTokenProvided()
    {
        var http = new FakeNotificationHttpClient();
        var notifier = new NtfyNotifier(http);
        var evt = new NotificationEvent(NotificationOutcome.Success, "T", "B", 1, 1, TimeSpan.FromSeconds(1), null);

        await notifier.SendAsync(new Dictionary<string, string> { ["server"] = "https://ntfy.sh", ["topic"] = "t", ["accessToken"] = "tk_abc" }, evt, CancellationToken.None);

        Assert.Equal("Bearer tk_abc", http.LastHeaders!["Authorization"]);
    }

    [Fact]
    public async Task TestAsync_SendsWithoutRequiringARealEvent()
    {
        var http = new FakeNotificationHttpClient();
        var notifier = new NtfyNotifier(http);

        var result = await notifier.TestAsync(new Dictionary<string, string> { ["server"] = "https://ntfy.sh", ["topic"] = "t" }, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(1, http.CallCount);
        Assert.Equal("Compressarr test notification", http.LastHeaders!["Title"]);
        Assert.Contains("This is a test notification from Compressarr.", http.LastText);
    }
}
