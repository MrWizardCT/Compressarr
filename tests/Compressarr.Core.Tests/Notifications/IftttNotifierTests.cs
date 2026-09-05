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
        throw new NotSupportedException("IFTTT only ever posts JSON.");

    public Task<HttpResponseMessage> PostTextAsync(Uri destination, string text, IReadOnlyDictionary<string, string>? headers, CancellationToken ct) =>
        throw new NotSupportedException("IFTTT only ever posts JSON.");
}

public class IftttNotifierTests
{
    [Fact]
    public async Task SendAsync_BuildsMakerWebhooksUrlFromEventNameAndKey()
    {
        var http = new FakeNotificationHttpClient();
        var notifier = new IftttNotifier(http);
        var evt = new NotificationEvent(NotificationOutcome.Success, "Title", "Body", 3, 1.5, TimeSpan.FromMinutes(2), null);

        await notifier.SendAsync(new Dictionary<string, string> { ["eventName"] = "compressarr_run", ["webhooksKey"] = "abc123" }, evt, CancellationToken.None);

        Assert.Equal("https://maker.ifttt.com/trigger/compressarr_run/with/key/abc123", http.LastDestination!.ToString());
    }

    [Fact]
    public async Task SendAsync_BodyMapsTitleBodyAndReportPathToValue1Value2Value3()
    {
        var http = new FakeNotificationHttpClient();
        var notifier = new IftttNotifier(http);
        var evt = new NotificationEvent(NotificationOutcome.Success, "My Title", "My Body", 3, 1.5, TimeSpan.FromMinutes(2), "C:\\report.html");

        await notifier.SendAsync(new Dictionary<string, string> { ["eventName"] = "e", ["webhooksKey"] = "k" }, evt, CancellationToken.None);

        using var doc = JsonDocument.Parse(http.LastBody!);
        Assert.Equal("My Title", doc.RootElement.GetProperty("value1").GetString());
        Assert.Equal("My Body", doc.RootElement.GetProperty("value2").GetString());
        Assert.Equal("C:\\report.html", doc.RootElement.GetProperty("value3").GetString());
    }

    [Fact]
    public async Task TestAsync_SendsWithoutRequiringARealEvent()
    {
        var http = new FakeNotificationHttpClient();
        var notifier = new IftttNotifier(http);

        var result = await notifier.TestAsync(new Dictionary<string, string> { ["eventName"] = "e", ["webhooksKey"] = "k" }, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(1, http.CallCount);
        Assert.Contains("Compressarr test notification", http.LastBody);
    }
}
