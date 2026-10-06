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

    private static async Task<JsonObject> GetScheduleAsync(QueueHost host) =>
        (await host.Client.GetFromJsonAsync<JsonObject>("/api/schedule"))!;

    private static async Task PutScheduleAsync(QueueHost host, JsonObject schedule)
    {
        using var response = await host.Client.PutAsJsonAsync("/api/schedule", schedule);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task EnableOffHoursOnlyAsync(QueueHost host)
    {
        var (start, end) = WindowAroundNow();
        var schedule = await GetScheduleAsync(host);
        schedule["enabled"] = true;
        schedule["dayStart"] = start;
        schedule["dayEnd"] = end;
        schedule["onlyEncodeOffHours"] = true;
        await PutScheduleAsync(host, schedule);
    }

    [Fact]
    public async Task TheScheduleIsOffByDefault_WithTheDocumentedDefaults()
    {
        await using var host = await QueueHost.StartAsync(Lane1);

        var schedule = await GetScheduleAsync(host);

        Assert.False(schedule["enabled"]!.GetValue<bool>());
        Assert.False(schedule["onlyEncodeOffHours"]!.GetValue<bool>());
        Assert.Equal("Everyday", schedule["mode"]!.GetValue<string>());
        Assert.Equal("BelowNormal", schedule["dayPriority"]!.GetValue<string>());
        Assert.Equal("Normal", schedule["nightPriority"]!.GetValue<string>());
        Assert.Equal("FinishCurrentFile", schedule["whenDayStarts"]!.GetValue<string>());
        Assert.Equal(7, schedule["days"]!.AsArray().Count);
        Assert.Equal("Sunday", schedule["days"]![0]!["day"]!.GetValue<string>());
    }

    [Fact]
    public async Task ScheduleSettings_SurviveASaveAndReload_IncludingEachDaysOwnWindow()
    {
        await using var host = await QueueHost.StartAsync(Lane1);
        var schedule = await GetScheduleAsync(host);
        schedule["enabled"] = true;
        schedule["mode"] = "EachDay";
        schedule["dayStart"] = "07:30";
        schedule["dayEnd"] = "23:15";
        schedule["weekendDayStart"] = "11:00";
        schedule["weekendDayEnd"] = "11:00";
        schedule["days"]![3]!["start"] = "13:00"; // Wednesday
        schedule["days"]![3]!["end"] = "15:30";
        schedule["dayPriority"] = "Low";
        schedule["nightPriority"] = "Realtime";
        schedule["onlyEncodeOffHours"] = true;
        schedule["whenDayStarts"] = "SuspendEncode";

        await PutScheduleAsync(host, schedule);
        var saved = await GetScheduleAsync(host);

        Assert.True(saved["enabled"]!.GetValue<bool>());
        Assert.Equal("EachDay", saved["mode"]!.GetValue<string>());
        Assert.Equal("07:30", saved["dayStart"]!.GetValue<string>());
        Assert.Equal("23:15", saved["dayEnd"]!.GetValue<string>());
        Assert.Equal("11:00", saved["weekendDayStart"]!.GetValue<string>());
        Assert.Equal("Wednesday", saved["days"]![3]!["day"]!.GetValue<string>());
        Assert.Equal("13:00", saved["days"]![3]!["start"]!.GetValue<string>());
        Assert.Equal("15:30", saved["days"]![3]!["end"]!.GetValue<string>());
        Assert.Equal("Low", saved["dayPriority"]!.GetValue<string>());
        Assert.Equal("Realtime", saved["nightPriority"]!.GetValue<string>());
        Assert.True(saved["onlyEncodeOffHours"]!.GetValue<bool>());
        Assert.Equal("SuspendEncode", saved["whenDayStarts"]!.GetValue<string>());
    }

    [Fact]
    public async Task SavingSettings_NeverTouchesTheSchedule_AndSavingTheScheduleNeverTouchesSettings()
    {
        await using var host = await QueueHost.StartAsync(Lane1);
        var schedule = await GetScheduleAsync(host);
        schedule["enabled"] = true;
        schedule["dayStart"] = "06:00";
        await PutScheduleAsync(host, schedule);

        // A normal Settings-page save (which carries no schedule at all).
        var settings = (await host.Client.GetFromJsonAsync<JsonObject>("/api/settings"))!;
        Assert.False(settings.ContainsKey("schedule"));
        settings["pollIntervalSeconds"] = 123;
        using (var put = await host.Client.PutAsJsonAsync("/api/settings", settings)) Assert.Equal(HttpStatusCode.OK, put.StatusCode);

        var after = await GetScheduleAsync(host);
        Assert.True(after["enabled"]!.GetValue<bool>());
        Assert.Equal("06:00", after["dayStart"]!.GetValue<string>());

        // ...and the other way round.
        after["dayStart"] = "07:00";
        await PutScheduleAsync(host, after);
        var settingsAfter = (await host.Client.GetFromJsonAsync<JsonObject>("/api/settings"))!;
        Assert.Equal(123, settingsAfter["pollIntervalSeconds"]!.GetValue<int>());
    }

    [Fact]
    public async Task AnUnreadableTime_IsFlaggedForTheWindowInUse_AndIgnoredForOthers()
    {
        await using var host = await QueueHost.StartAsync(Lane1);
        var schedule = await GetScheduleAsync(host);
        schedule["enabled"] = true;
        schedule["mode"] = "Everyday";
        schedule["dayStart"] = "later";
        schedule["weekendDayStart"] = "also bad"; // unused in this layout

        using var response = await host.Client.PutAsJsonAsync("/api/schedule", schedule);
        var saved = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

        var fields = saved.GetProperty("validationIssues").EnumerateArray().Select(i => i.GetProperty("field").GetString()).ToList();
        Assert.Equal(new[] { "dayStart" }, fields);
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
