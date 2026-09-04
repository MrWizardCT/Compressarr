using Compressarr.Core.Notifications;

namespace Compressarr.Core.Tests.Notifications;

/// <summary>Fakes at the IWebhookSender seam (rather than HttpMessageHandler) since that's the
/// boundary every INotifier actually depends on - same reasoning ArrUnmonitorServiceTests'
/// FakeArrClient already establishes for the Sonarr/Radarr integration.</summary>
file sealed class FakeWebhookSender : IWebhookSender
{
    public NotifyResult Result { get; set; } = new(true, "OK");
    public string? LastUrl { get; private set; }
    public HttpMethod? LastMethod { get; private set; }
    public IReadOnlyDictionary<string, string>? LastHeaders { get; private set; }
    public string? LastBody { get; private set; }
    public int CallCount { get; private set; }

    public Task<NotifyResult> PostAsync(string url, HttpMethod method, IReadOnlyDictionary<string, string> headers, string body, string contentType, CancellationToken ct)
    {
        CallCount++;
        LastUrl = url;
        LastMethod = method;
        LastHeaders = headers;
        LastBody = body;
        return Task.FromResult(Result);
    }
}

public class WebhookNotifierTests
{
    private static NotificationEvent SampleEvent() => new(
        NotificationOutcome.Success, "Title", "Body", TotalFiles: 3, SavedGb: 1.5, Duration: TimeSpan.FromMinutes(2), ReportPath: "C:\\report.html");

    [Fact]
    public async Task SendAsync_PostsToConfiguredUrl_DefaultingToPost()
    {
        var sender = new FakeWebhookSender();
        var notifier = new WebhookNotifier(sender);

        await notifier.SendAsync(new Dictionary<string, string> { ["url"] = "https://example.com/hook" }, SampleEvent(), CancellationToken.None);

        Assert.Equal("https://example.com/hook", sender.LastUrl);
        Assert.Equal(HttpMethod.Post, sender.LastMethod);
    }

    [Fact]
    public async Task SendAsync_HonorsConfiguredMethod()
    {
        var sender = new FakeWebhookSender();
        var notifier = new WebhookNotifier(sender);

        await notifier.SendAsync(new Dictionary<string, string> { ["url"] = "https://example.com/hook", ["method"] = "PUT" }, SampleEvent(), CancellationToken.None);

        Assert.Equal(HttpMethod.Put, sender.LastMethod);
    }

    [Fact]
    public async Task SendAsync_ParsesCustomHeaders()
    {
        var sender = new FakeWebhookSender();
        var notifier = new WebhookNotifier(sender);

        await notifier.SendAsync(new Dictionary<string, string>
        {
            ["url"] = "https://example.com/hook",
            ["headers"] = "Authorization: Bearer abc123\nX-Custom: value"
        }, SampleEvent(), CancellationToken.None);

        Assert.Equal("Bearer abc123", sender.LastHeaders!["Authorization"]);
        Assert.Equal("value", sender.LastHeaders!["X-Custom"]);
    }

    [Fact]
    public async Task SendAsync_BodyIncludesIftttValueAliases()
    {
        // IFTTT's Maker/Webhooks service only populates a triggered applet's action from JSON
        // keys named exactly value1/value2/value3 - confirmed live-research finding, not a guess.
        var sender = new FakeWebhookSender();
        var notifier = new WebhookNotifier(sender);

        await notifier.SendAsync(new Dictionary<string, string> { ["url"] = "https://maker.ifttt.com/x" }, SampleEvent(), CancellationToken.None);

        Assert.Contains("\"value1\":\"Title\"", sender.LastBody);
        Assert.Contains("\"value2\":\"Body\"", sender.LastBody);
    }

    [Fact]
    public async Task TestAsync_SendsWithoutRequiringARealEvent()
    {
        var sender = new FakeWebhookSender();
        var notifier = new WebhookNotifier(sender);

        var result = await notifier.TestAsync(new Dictionary<string, string> { ["url"] = "https://example.com/hook" }, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(1, sender.CallCount);
        Assert.Contains("Compressarr test notification", sender.LastBody);
    }
}
