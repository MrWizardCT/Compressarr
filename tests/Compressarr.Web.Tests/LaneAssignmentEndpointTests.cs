using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Compressarr.Core.Config;
using Compressarr.Core.Conversion;
using Compressarr.Core.Logging;
using Microsoft.Extensions.DependencyInjection;

namespace Compressarr.Web.Tests;

/// <summary>"Lands in" lane assignment through the real endpoints: the Monitor page shows where a
/// file will land, the choice is saved without touching anything else about the file, and a deleted
/// destination lane is flagged rather than silently dropped. (The routing itself is covered by the
/// engine tests in Core.Tests.)</summary>
public class LaneAssignmentEndpointTests
{
    private static readonly LaneSpec Home = new("hdsd", "SD-HD");
    private static readonly LaneSpec Kids = new("kids", "Kids");

    [Fact]
    public async Task AssigningAFile_ShowsWhereItWillLandOnItsQueueRow()
    {
        await using var host = await QueueHost.StartAsync(Home, Kids);
        var file = host.Drop("hdsd", "Sesame Street (1969).mkv");

        Assert.Equal(HttpStatusCode.OK, await host.SetDestinationAsync("hdsd", file, "kids"));

        var row = Assert.Single(await host.PollAsync());
        Assert.Equal("hdsd", row.LaneId);                 // it still lives in its own lane
        Assert.Equal("SD-HD", row.LaneDisplayName);
        Assert.Equal("kids", row.DestinationLaneId);
        Assert.Equal("Kids", row.DestinationLaneName);
        Assert.False(row.DestinationMissing);
    }

    [Fact]
    public async Task TheCurrentStatus_SaysWhereTheFileBeingEncodedWillLand()
    {
        await using var host = await QueueHost.StartAsync(Home, Kids);
        var ordinary = host.Drop("hdsd", "a.mkv");
        var assigned = host.Drop("hdsd", "Sesame Street (1969).mkv");
        await host.SetDestinationAsync("hdsd", assigned, "kids");
        var state = host.Services.GetRequiredService<Compressarr.Web.CurrentRunStateService>();
        state.RunStarted("20260101_000000");
        state.LaneStarted("hdsd", "SD-HD", false);

        state.FileStarted("hdsd", 1, 2, "a.mkv", ordinary, "Some Preset", 1);
        var own = await StatusLandsInAsync(host);
        state.FileStarted("hdsd", 2, 2, "Sesame Street (1969).mkv", assigned, "Some Preset", 1);
        var redirected = await StatusLandsInAsync(host);

        Assert.Equal("SD-HD", own);        // no assignment: it lands in its own lane
        Assert.Equal("Kids", redirected);  // assigned to Kids
    }

    private static async Task<string?> StatusLandsInAsync(QueueHost host)
    {
        var status = await host.Client.GetFromJsonAsync<System.Text.Json.Nodes.JsonObject>("/api/run/status");
        return status!["landsInLaneName"]?.GetValue<string>();
    }

    [Fact]
    public async Task AnOrdinaryRow_HasNoDestination()
    {
        await using var host = await QueueHost.StartAsync(Home, Kids);
        host.Drop("hdsd", "a.mkv");

        var row = Assert.Single(await host.PollAsync());

        Assert.Null(row.DestinationLaneId);
        Assert.Null(row.DestinationLaneName);
    }

    [Fact]
    public async Task ClearingTheAssignment_OrChoosingTheFilesOwnLane_RemovesTheMarker()
    {
        await using var host = await QueueHost.StartAsync(Home, Kids);
        var file = host.Drop("hdsd", "a.mkv");

        await host.SetDestinationAsync("hdsd", file, "kids");
        Assert.Equal(HttpStatusCode.OK, await host.SetDestinationAsync("hdsd", file, null));
        Assert.Null((await host.PollAsync()).Single().DestinationLaneId);

        await host.SetDestinationAsync("hdsd", file, "kids");
        Assert.Equal(HttpStatusCode.OK, await host.SetDestinationAsync("hdsd", file, "hdsd"));
        Assert.Null((await host.PollAsync()).Single().DestinationLaneId);
    }

    [Fact]
    public async Task AssigningAFile_ChangesNothingElse_NotPositionPresetOrSkip()
    {
        await using var host = await QueueHost.StartAsync(Home, Kids);
        var a = host.Drop("hdsd", "a.mkv");
        var b = host.Drop("hdsd", "b.mkv");
        var c = host.Drop("hdsd", "c.mkv");
        await host.OverridePresetAsync("hdsd", b, "My Preset");
        await host.SkipAsync("hdsd", b);
        await host.ReorderAsync(("hdsd", c), ("hdsd", a), ("hdsd", b)); // c, a, b
        var before = (await host.PollAsync()).Select(i => (i.FileName, i.Preset, i.IsSkipped, i.IsCustomPreset)).ToList();

        await host.SetDestinationAsync("hdsd", b, "kids");

        var after = await host.PollAsync();
        Assert.Equal(before, after.Select(i => (i.FileName, i.Preset, i.IsSkipped, i.IsCustomPreset)).ToList());
        Assert.Equal(new[] { "c.mkv", "a.mkv", "b.mkv" }, after.Select(i => i.FileName).ToArray());
        Assert.All(after, i => Assert.Equal("hdsd", i.LaneId));
    }

    [Fact]
    public async Task TheAssignmentIsStable_AcrossPolls_AndOtherEditsToTheSameFile()
    {
        await using var host = await QueueHost.StartAsync(Home, Kids);
        var file = host.Drop("hdsd", "a.mkv");
        await host.SetDestinationAsync("hdsd", file, "kids");

        for (var i = 0; i < 3; i++) Assert.Equal("kids", (await host.PollAsync()).Single().DestinationLaneId);

        await host.OverridePresetAsync("hdsd", file, "X");
        await host.SkipAsync("hdsd", file);
        await host.SkipAsync("hdsd", file, skipped: false);
        await host.ReorderAsync(("hdsd", file));

        Assert.Equal("kids", (await host.PollAsync()).Single().DestinationLaneId);
        Assert.Equal("kids", host.ResumeEntries().Single().DestinationLaneId);
    }

    [Fact]
    public async Task AssigningAFileTheEngineHasNotTrackedYet_StillWorks_AndLocksItsPlaceInLine()
    {
        await using var host = await QueueHost.StartAsync(Home, Kids);
        var first = host.Drop("hdsd", "z-first.mkv");
        await host.PollAsync(); // tracked, in arrival order
        var second = host.Drop("hdsd", "a-second.mkv"); // sorts before "z-first" on a scan

        await host.SetDestinationAsync("hdsd", second, "kids");

        Assert.Equal(new[] { "z-first.mkv", "a-second.mkv" }, await host.PollNamesAsync());
    }

    [Fact]
    public async Task AnAssignmentToAnUnknownLane_IsRejected_AndNothingIsSaved()
    {
        await using var host = await QueueHost.StartAsync(Home, Kids);
        var file = host.Drop("hdsd", "a.mkv");

        Assert.Equal(HttpStatusCode.BadRequest, await host.SetDestinationAsync("hdsd", file, "no-such-lane"));

        Assert.Null((await host.PollAsync()).Single().DestinationLaneId);
    }

    [Fact]
    public async Task AssigningFromAnUnknownLane_OrForAFileThatIsGone_Returns404()
    {
        await using var host = await QueueHost.StartAsync(Home, Kids);
        var file = host.Drop("hdsd", "a.mkv");

        Assert.Equal(HttpStatusCode.NotFound, await host.SetDestinationAsync("nope", file, "kids"));
        Assert.Equal(HttpStatusCode.NotFound, await host.SetDestinationAsync("hdsd", Path.Combine(host.Root, "missing.mkv"), "kids"));
    }

    [Fact]
    public async Task ADisabledDestinationLane_CanBeChosen()
    {
        await using var host = await QueueHost.StartAsync(Home, new LaneSpec("kids", "Kids", Enabled: false));
        var file = host.Drop("hdsd", "a.mkv");

        Assert.Equal(HttpStatusCode.OK, await host.SetDestinationAsync("hdsd", file, "kids"));
        Assert.Equal("Kids", (await host.PollAsync()).Single().DestinationLaneName);
    }

    [Fact]
    public async Task IfTheDestinationLaneIsDeleted_TheRowIsFlagged_ItStaysAssigned_AndNothingFallsBackToTheHomeLane()
    {
        await using var host = await QueueHost.StartAsync(Home, Kids);
        var file = host.Drop("hdsd", "a.mkv");
        await host.SetDestinationAsync("hdsd", file, "kids");

        host.DeleteLane("kids");

        var row = Assert.Single(await host.PollAsync());
        Assert.True(row.DestinationMissing);
        Assert.Null(row.DestinationLaneName);
        Assert.Equal("kids", row.DestinationLaneId); // the assignment isn't silently cleared
        Assert.Equal("kids", host.ResumeEntries().Single().DestinationLaneId);
    }
    [Fact]
    public async Task DeletingALane_CanFirstAskHowManyQueuedFilesAreSetToLandInIt()
    {
        await using var host = await QueueHost.StartAsync(Home, Kids);
        var a = host.Drop("hdsd", "a.mkv");
        var b = host.Drop("hdsd", "b.mkv");
        var c = host.Drop("hdsd", "c.mkv");
        await host.SetDestinationAsync("hdsd", a, "kids");
        await host.SetDestinationAsync("hdsd", b, "kids");
        await host.RemoveAsync("hdsd", b); // removed from the queue: no longer waiting to land anywhere

        using var doc = JsonDocument.Parse(await host.Client.GetStringAsync("/api/lanes/kids/redirected-files"));
        Assert.Equal(1, doc.RootElement.GetProperty("waitingFiles").GetInt32());

        using var none = JsonDocument.Parse(await host.Client.GetStringAsync("/api/lanes/hdsd/redirected-files"));
        Assert.Equal(0, none.RootElement.GetProperty("waitingFiles").GetInt32()); // a lane's own files aren't "redirected to" it
    }

    [Fact]
    public async Task AFinishedFileHeldOnAFailedMove_StillCountsAsWaitingForItsDestinationLane()
    {
        await using var host = await QueueHost.StartAsync(Home, Kids);
        var file = host.Drop("hdsd", "a.mkv");
        host.SeedResume(new ResumeEntry { LaneId = "hdsd", FullName = file, Status = ResumeStatus.MoveFailed, DestinationLaneId = "kids", EncodedFilePath = file });

        using var doc = JsonDocument.Parse(await host.Client.GetStringAsync("/api/lanes/kids/redirected-files"));

        Assert.Equal(1, doc.RootElement.GetProperty("waitingFiles").GetInt32());
    }

    [Fact]
    public async Task TheHistoryReportsList_CarriesEachRunsRedirectCount_ForTheHighlight()
    {
        await using var host = await QueueHost.StartAsync(Home, Kids);
        var config = host.Services.GetRequiredService<IConfigStore>().Load(AppPaths.GetConfigFilePath());
        var expander = host.Services.GetRequiredService<IPathExpander>();
        var logPath = expander.Expand(config.Logging.LogFilePath);
        var reportPath = expander.Expand(config.Report.ReportPath);
        Directory.CreateDirectory(reportPath);
        File.WriteAllText(Path.Combine(reportPath, "run1.html"), "<html></html>");
        File.WriteAllText(Path.Combine(reportPath, "run2.html"), "<html></html>");
        var today = DateTime.Now;
        var history = host.Services.GetRequiredService<IRunHistoryStore>();
        history.AppendRun(logPath, new RunHistoryRecord(today.Year, today.Month, today.Day, 10, 4, 2, 0, 1, 0, RunNumber: 1, ReportFileName: "run1.html"));
        history.AppendRun(logPath, new RunHistoryRecord(today.Year, today.Month, today.Day, 10, 4, 2, 0, 1, 0, RunNumber: 2, ReportFileName: "run2.html", RedirectCount: 3));

        using var doc = JsonDocument.Parse(await host.Client.GetStringAsync("/api/history/reports"));

        var byRun = doc.RootElement.EnumerateArray().ToDictionary(e => e.GetProperty("runNumber").GetInt32(), e => e.GetProperty("redirectCount").GetInt32());
        Assert.Equal(3, byRun[2]);
        Assert.Equal(0, byRun[1]);
    }
}
