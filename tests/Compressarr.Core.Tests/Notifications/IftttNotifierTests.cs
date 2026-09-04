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

public class IftttNotifierTests
{
    [Fact]
    public async Task SendAsync_BuildsMakerWebhooksUrlFromEventNameAndKey()
    {
        var sender = new FakeWebhookSender();
        var notifier = new IftttNotifier(sender);
        var evt = new NotificationEvent(NotificationOutcome.Success, "Title", "Body", 3, 1.5, TimeSpan.FromMinutes(2), null);

        await notifier.SendAsync(new Dictionary<string, string> { ["eventName"] = "compressarr_run", ["webhooksKey"] = "abc123" }, evt, CancellationToken.None);

        Assert.Equal("https://maker.ifttt.com/trigger/compressarr_run/with/key/abc123", sender.LastUrl);
    }

    [Fact]
    public async Task SendAsync_BodyMapsTitleBodyAndReportPathToValue1Value2Value3()
    {
        var sender = new FakeWebhookSender();
        var notifier = new IftttNotifier(sender);
        var evt = new NotificationEvent(NotificationOutcome.Success, "My Title", "My Body", 3, 1.5, TimeSpan.FromMinutes(2), "C:\\report.html");

        await notifier.SendAsync(new Dictionary<string, string> { ["eventName"] = "e", ["webhooksKey"] = "k" }, evt, CancellationToken.None);

        using var doc = JsonDocument.Parse(sender.LastBody!);
        Assert.Equal("My Title", doc.RootElement.GetProperty("value1").GetString());
        Assert.Equal("My Body", doc.RootElement.GetProperty("value2").GetString());
        Assert.Equal("C:\\report.html", doc.RootElement.GetProperty("value3").GetString());
    }

    [Fact]
    public async Task TestAsync_SendsWithoutRequiringARealEvent()
    {
        var sender = new FakeWebhookSender();
        var notifier = new IftttNotifier(sender);

        var result = await notifier.TestAsync(new Dictionary<string, string> { ["eventName"] = "e", ["webhooksKey"] = "k" }, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(1, sender.CallCount);
        Assert.Contains("Compressarr test notification", sender.LastBody);
    }
}
