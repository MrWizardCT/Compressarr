using System.Net;

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
}
