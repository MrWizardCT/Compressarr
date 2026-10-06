using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Compressarr.Web.Tests;

/// <summary>The optional day/night schedule through the real HTTP surface: it is off by default,
/// round-trips through the Settings API, is reported on the status endpoint the top bar polls, and
/// "Run anyway" releases a hold. (The window arithmetic itself is covered in Core.Tests.)</summary>
public class ScheduleEndpointTests
{
    private static readonly LaneSpec Lane1 = new("lane1", "Lane One");

    // A daytime window around "now" (2 hours either side), so the test is daytime whenever it runs -
    // windows may cross midnight, so no clamping is needed.
    private static (string Start, string End) WindowAroundNow()
    {
        var now = DateTime.Now;
        return (now.AddHours(-2).ToString("HH:mm"), now.AddHours(2).ToString("HH:mm"));
    }

    private static async Task<JsonObject> GetSettingsAsync(QueueHost host) =>
        (await host.Client.GetFromJsonAsync<JsonObject>("/api/settings"))!;

    private static async Task PutSettingsAsync(QueueHost host, JsonObject settings)
    {
        using var response = await host.Client.PutAsJsonAsync("/api/settings", settings);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task EnableOffHoursOnlyAsync(QueueHost host)
    {
        var (start, end) = WindowAroundNow();
        var settings = await GetSettingsAsync(host);
        var schedule = settings["schedule"]!.AsObject();
        schedule["enabled"] = true;
        schedule["dayStart"] = start;
        schedule["dayEnd"] = end;
        schedule["onlyEncodeOffHours"] = true;
        await PutSettingsAsync(host, settings);
    }

    [Fact]
    public async Task TheScheduleIsOffByDefault_WithTheDocumentedDefaults()
    {
        await using var host = await QueueHost.StartAsync(Lane1);

        var schedule = (await GetSettingsAsync(host))["schedule"]!.AsObject();

        Assert.False(schedule["enabled"]!.GetValue<bool>());
        Assert.False(schedule["onlyEncodeOffHours"]!.GetValue<bool>());
        Assert.Equal("BelowNormal", schedule["dayPriority"]!.GetValue<string>());
        Assert.Equal("Normal", schedule["nightPriority"]!.GetValue<string>());
        Assert.Equal("FinishCurrentFile", schedule["whenDayStarts"]!.GetValue<string>());
    }

    [Fact]
    public async Task ScheduleSettings_SurviveASaveAndReload()
    {
        await using var host = await QueueHost.StartAsync(Lane1);
        var settings = await GetSettingsAsync(host);
        var schedule = settings["schedule"]!.AsObject();
        schedule["enabled"] = true;
        schedule["dayStart"] = "07:30";
        schedule["dayEnd"] = "23:15";
        schedule["weekendDifferent"] = true;
        schedule["weekendDayStart"] = "11:00";
        schedule["weekendDayEnd"] = "11:00";
        schedule["dayPriority"] = "Low";
        schedule["nightPriority"] = "High";
        schedule["onlyEncodeOffHours"] = true;
        schedule["whenDayStarts"] = "SuspendEncode";

        await PutSettingsAsync(host, settings);
        var saved = (await GetSettingsAsync(host))["schedule"]!.AsObject();

        Assert.True(saved["enabled"]!.GetValue<bool>());
        Assert.Equal("07:30", saved["dayStart"]!.GetValue<string>());
        Assert.Equal("23:15", saved["dayEnd"]!.GetValue<string>());
        Assert.True(saved["weekendDifferent"]!.GetValue<bool>());
        Assert.Equal("11:00", saved["weekendDayStart"]!.GetValue<string>());
        Assert.Equal("Low", saved["dayPriority"]!.GetValue<string>());
        Assert.Equal("High", saved["nightPriority"]!.GetValue<string>());
        Assert.True(saved["onlyEncodeOffHours"]!.GetValue<bool>());
        Assert.Equal("SuspendEncode", saved["whenDayStarts"]!.GetValue<string>());
    }

    [Fact]
    public async Task ASettingsSaveWithNoScheduleAtAll_LeavesTheSavedScheduleAlone()
    {
        await using var host = await QueueHost.StartAsync(Lane1);
        var settings = await GetSettingsAsync(host);
        settings["schedule"]!["enabled"] = true;
        settings["schedule"]!["dayStart"] = "06:00";
        await PutSettingsAsync(host, settings);

        // What a client that predates the schedule would send.
        var legacy = await GetSettingsAsync(host);
        legacy.Remove("schedule");
        await PutSettingsAsync(host, legacy);

        var after = (await GetSettingsAsync(host))["schedule"]!.AsObject();
        Assert.True(after["enabled"]!.GetValue<bool>());
        Assert.Equal("06:00", after["dayStart"]!.GetValue<string>());
    }

    [Fact]
    public async Task AnUnreadableTime_IsFlaggedOnTheSettingsPage()
    {
        await using var host = await QueueHost.StartAsync(Lane1);
        var settings = await GetSettingsAsync(host);
        settings["schedule"]!["enabled"] = true;
        settings["schedule"]!["dayStart"] = "later";

        using var response = await host.Client.PutAsJsonAsync("/api/settings", settings);
        var saved = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

        var fields = saved.GetProperty("validationIssues").EnumerateArray().Select(i => i.GetProperty("field").GetString()).ToList();
        Assert.Contains("scheduleDayStart", fields);
    }

    [Fact]
    public async Task Status_ReportsAnInactiveScheduleWhenItIsOff()
    {
        await using var host = await QueueHost.StartAsync(Lane1);

        using var doc = JsonDocument.Parse(await host.Client.GetStringAsync("/api/run/status"));
        var schedule = doc.RootElement.GetProperty("schedule");

        Assert.False(schedule.GetProperty("enabled").GetBoolean());
        Assert.False(schedule.GetProperty("isHeld").GetBoolean());
    }

    [Fact]
    public async Task Status_ReportsAHeldQueueDuringTheDaytimeWindow_AndRunAnywayReleasesIt()
    {
        await using var host = await QueueHost.StartAsync(Lane1);
        host.Drop("lane1", "a.mkv");
        await EnableOffHoursOnlyAsync(host);

        using (var held = JsonDocument.Parse(await host.Client.GetStringAsync("/api/run/status")))
        {
            var schedule = held.RootElement.GetProperty("schedule");
            Assert.True(schedule.GetProperty("enabled").GetBoolean());
            Assert.True(schedule.GetProperty("isDaytime").GetBoolean());
            Assert.True(schedule.GetProperty("isHeld").GetBoolean());
            Assert.Equal("BelowNormal", schedule.GetProperty("priority").GetString());
            Assert.NotEqual(JsonValueKind.Null, schedule.GetProperty("nextChange").ValueKind);
        }

        using var response = await host.Client.PostAsync("/api/run/run-anyway", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var released = JsonDocument.Parse(await host.Client.GetStringAsync("/api/run/status"));
        var after = released.RootElement.GetProperty("schedule");
        Assert.False(after.GetProperty("isHeld").GetBoolean());
        Assert.True(after.GetProperty("overrideActive").GetBoolean());
    }

    [Fact]
    public async Task TheQueueStillListsFilesInArrivalOrderWhileTheyAreHeld()
    {
        await using var host = await QueueHost.StartAsync(Lane1);
        host.Drop("lane1", "z.mkv");
        await EnableOffHoursOnlyAsync(host);

        Assert.Equal(new[] { "z.mkv" }, await host.PollNamesAsync());
        host.Drop("lane1", "a.mkv");
        Assert.Equal(new[] { "z.mkv", "a.mkv" }, await host.PollNamesAsync());
    }
}
