using System.Net;
using Compressarr.Core.Conversion;

namespace Compressarr.Web.Tests;

/// <summary>
/// Characterization tests for the Monitor page's In Queue list, written against 2.1.8's behavior
/// (Phase 1 of the 2.2 plan). They drive the real endpoints exactly the way the page does and pin
/// down what the queue guarantees today, so the 2.2 restructuring can prove it changed nothing.
/// If one of these fails after a refactor, behavior changed - decide whether that's a bug in the
/// refactor or a deliberate change, don't just edit the assertion.
/// </summary>
public class QueueBehaviorTests
{
    private static readonly LaneSpec Lane1 = new("lane1", "Lane One");
    private static readonly LaneSpec Lane2 = new("lane2", "Lane Two");

    // ---- arrival order is locked ----------------------------------------------------------------

    [Fact]
    public async Task FilesArrivingOverTime_KeepArrivalOrder_EvenWhenALaterOneSortsFirst()
    {
        // The 2.1.8 fix: a file's place is locked the first time anything sees it. A later arrival
        // whose name sorts earlier (AAA < ZZZ) must still join at the END, never displace an
        // earlier one - regardless of how the folder scan happens to enumerate them.
        await using var host = await QueueHost.StartAsync(Lane1);

        host.Drop("lane1", "ZZZ arrived first.mkv");
        Assert.Equal(new[] { "ZZZ arrived first.mkv" }, await host.PollNamesAsync());

        host.Drop("lane1", "AAA arrived second.mkv");
        Assert.Equal(new[] { "ZZZ arrived first.mkv", "AAA arrived second.mkv" }, await host.PollNamesAsync());

        host.Drop("lane1", "MMM arrived third.mkv");
        Assert.Equal(
            new[] { "ZZZ arrived first.mkv", "AAA arrived second.mkv", "MMM arrived third.mkv" },
            await host.PollNamesAsync());
    }

    [Fact]
    public async Task FirstSightingOfAFile_PersistsItAsTrackedWithAnIncreasingOrder()
    {
        // Locked "on first sight" means an entry is written the moment the page polls, not later.
        await using var host = await QueueHost.StartAsync(Lane1);
        host.Drop("lane1", "b.mkv");
        await host.PollAsync();
        host.Drop("lane1", "a.mkv");
        await host.PollAsync();

        var entries = host.ResumeEntries().OrderBy(e => e.Order).ToList();
        Assert.Equal(new[] { "b.mkv", "a.mkv" }, entries.Select(e => Path.GetFileName(e.FullName)));
        Assert.All(entries, e => Assert.Equal(ResumeStatus.Pending, e.Status));
        Assert.True(entries[0].Order < entries[1].Order);
    }

    [Fact]
    public async Task FilesArrivingTogether_AreOrderedOnceAndThenNeverShuffleBetweenPolls()
    {
        await using var host = await QueueHost.StartAsync(Lane1);
        foreach (var name in new[] { "c.mkv", "a.mkv", "b.mkv", "e.mkv", "d.mkv" }) host.Drop("lane1", name);

        var first = await host.PollNamesAsync();
        Assert.Equal(5, first.Count);
        for (var i = 0; i < 5; i++) Assert.Equal(first, await host.PollNamesAsync());
    }

    [Fact]
    public async Task NestedFolders_AreScannedRecursively_AndStillJoinInArrivalOrder()
    {
        await using var host = await QueueHost.StartAsync(Lane1);
        host.Drop("lane1", @"Show B\S01E01.mkv");
        await host.PollAsync();
        host.Drop("lane1", @"Show A\S01E01.mkv");
        host.Drop("lane1", "loose.mkv");

        var names = await host.PollAsync();
        Assert.Equal("Show B", Path.GetFileName(Path.GetDirectoryName(names[0].FullName)));
        Assert.Equal(3, names.Count);
    }

    // ---- single-file actions never move a file --------------------------------------------------

    [Fact]
    public async Task PresetOverride_ChangesOnlyThePreset_NeverThePosition_AndCanBeCleared()
    {
        await using var host = await QueueHost.StartAsync(Lane1);
        var a = host.Drop("lane1", "a.mkv");
        var b = host.Drop("lane1", "b.mkv");
        var c = host.Drop("lane1", "c.mkv");
        var before = await host.PollNamesAsync();

        Assert.Equal(HttpStatusCode.OK, await host.OverridePresetAsync("lane1", b, "My Override"));
        var after = await host.PollAsync();
        Assert.Equal(before, after.Select(i => i.FileName));
        var overridden = after.Single(i => i.FullName == b);
        Assert.True(overridden.IsCustomPreset);
        Assert.Equal("My Override", overridden.Preset);
        Assert.False(after.Single(i => i.FullName == a).IsCustomPreset);

        Assert.Equal(HttpStatusCode.OK, await host.OverridePresetAsync("lane1", b, null));
        var cleared = await host.PollAsync();
        Assert.Equal(before, cleared.Select(i => i.FileName));
        Assert.False(cleared.Single(i => i.FullName == b).IsCustomPreset);
        Assert.Equal("Lane Movie Preset", cleared.Single(i => i.FullName == b).Preset);
        _ = c;
    }

    [Fact]
    public async Task PresetOverride_OnALaterFile_DoesNotJumpItAheadOfEarlierOnes()
    {
        // The pre-2.1.7 symptom: touching one untouched file gave it a real position while its
        // untouched siblings were still unpositioned, so it leapt to the top.
        await using var host = await QueueHost.StartAsync(Lane1);
        host.Drop("lane1", "first.mkv");
        host.Drop("lane1", "second.mkv");
        var third = host.Drop("lane1", "third.mkv");
        var before = await host.PollNamesAsync();

        await host.OverridePresetAsync("lane1", third, "Other");
        Assert.Equal(before, await host.PollNamesAsync());
    }

    [Fact]
    public async Task Skip_KeepsTheFileVisibleInPlace_AndCanBeToggledBack()
    {
        await using var host = await QueueHost.StartAsync(Lane1);
        host.Drop("lane1", "a.mkv");
        var b = host.Drop("lane1", "b.mkv");
        host.Drop("lane1", "c.mkv");
        var before = await host.PollNamesAsync();

        await host.SkipAsync("lane1", b);
        var skipped = await host.PollAsync();
        Assert.Equal(before, skipped.Select(i => i.FileName));
        Assert.True(skipped.Single(i => i.FullName == b).IsSkipped);

        await host.SkipAsync("lane1", b, skipped: false);
        var restored = await host.PollAsync();
        Assert.Equal(before, restored.Select(i => i.FileName));
        Assert.False(restored.Single(i => i.FullName == b).IsSkipped);
    }

    [Fact]
    public async Task Remove_HidesTheFile_StaysHiddenAcrossPolls_AndOthersKeepTheirOrder()
    {
        await using var host = await QueueHost.StartAsync(Lane1);
        host.Drop("lane1", "a.mkv");
        var b = host.Drop("lane1", "b.mkv");
        host.Drop("lane1", "c.mkv");
        await host.PollAsync();

        Assert.Equal(HttpStatusCode.OK, await host.RemoveAsync("lane1", b));
        for (var i = 0; i < 3; i++)
            Assert.Equal(new[] { "a.mkv", "c.mkv" }, await host.PollNamesAsync());

        // A removed file is still tracked (so the live rescan doesn't rediscover it), flagged
        // Removed + Skipped so the engine never encodes it.
        var entry = host.ResumeEntries().Single(e => e.FullName == b);
        Assert.True(entry.Removed);
        Assert.True(entry.Skipped);
    }

    // ---- explicit reordering --------------------------------------------------------------------

    [Fact]
    public async Task Reorder_SetsTheDisplayedAndStoredOrder_AndALaterArrivalStillJoinsTheEnd()
    {
        await using var host = await QueueHost.StartAsync(Lane1);
        var a = host.Drop("lane1", "a.mkv");
        var b = host.Drop("lane1", "b.mkv");
        var c = host.Drop("lane1", "c.mkv");
        await host.PollAsync();

        // Move to top, as the page does: resubmit the whole list with c first.
        Assert.Equal(HttpStatusCode.OK, await host.ReorderAsync(("lane1", c), ("lane1", a), ("lane1", b)));
        Assert.Equal(new[] { "c.mkv", "a.mkv", "b.mkv" }, await host.PollNamesAsync());

        // Move to bottom: a last.
        await host.ReorderAsync(("lane1", c), ("lane1", b), ("lane1", a));
        Assert.Equal(new[] { "c.mkv", "b.mkv", "a.mkv" }, await host.PollNamesAsync());

        // A file arriving afterwards joins the end of the user's order, not the original scan order.
        host.Drop("lane1", "AAA new.mkv");
        Assert.Equal(new[] { "c.mkv", "b.mkv", "a.mkv", "AAA new.mkv" }, await host.PollNamesAsync());
    }

    [Fact]
    public async Task Reorder_CanInterleaveFilesFromDifferentLanes()
    {
        await using var host = await QueueHost.StartAsync(Lane1, Lane2);
        var a = host.Drop("lane1", "a.mkv");
        await host.PollAsync();
        var x = host.Drop("lane2", "x.mkv");
        Assert.Equal(new[] { "a.mkv", "x.mkv" }, await host.PollNamesAsync());

        await host.ReorderAsync(("lane2", x), ("lane1", a));
        var items = await host.PollAsync();
        Assert.Equal(new[] { "x.mkv", "a.mkv" }, items.Select(i => i.FileName));
        Assert.Equal(new[] { "lane2", "lane1" }, items.Select(i => i.LaneId));
    }

    [Fact]
    public async Task FilesArrivingInDifferentLanes_JoinInArrivalOrderAcrossLanes()
    {
        await using var host = await QueueHost.StartAsync(Lane1, Lane2);
        host.Drop("lane2", "first-in-lane2.mkv");
        await host.PollAsync();
        host.Drop("lane1", "later-in-lane1.mkv");

        // Lane1 comes before lane2 in the config, but the earlier arrival keeps its place.
        Assert.Equal(new[] { "first-in-lane2.mkv", "later-in-lane1.mkv" }, await host.PollNamesAsync());
    }

    // ---- in-progress file -----------------------------------------------------------------------

    [Fact]
    public async Task TheFileCurrentlyEncoding_IsNotListedAsWaiting()
    {
        await using var host = await QueueHost.StartAsync(Lane1);
        var a = host.Drop("lane1", "a.mkv");
        host.Drop("lane1", "b.mkv");
        await host.PollAsync();

        host.RunState.FileStarted("lane1", 1, 2, "a.mkv", a, "Lane Movie Preset", 0.001);
        Assert.Equal(new[] { "b.mkv" }, await host.PollNamesAsync());
    }

    [Fact]
    public async Task TheFileCurrentlyEncoding_IsMatchedByFullPath_NotByLeafName()
    {
        // Two different files can legitimately share a leaf name in different subfolders; only
        // the one actually encoding may be hidden from the queue.
        await using var host = await QueueHost.StartAsync(Lane1);
        var encoding = host.Drop("lane1", @"Show A\Episode 1.mkv");
        var waiting = host.Drop("lane1", @"Show B\Episode 1.mkv");
        await host.PollAsync();

        host.RunState.FileStarted("lane1", 1, 2, "Episode 1.mkv", encoding, "Lane Movie Preset", 0.001);
        var items = await host.PollAsync();
        Assert.Single(items);
        Assert.Equal(waiting, items[0].FullName);
    }

    // ---- New vs Resumed labels ------------------------------------------------------------------

    [Fact]
    public async Task GenuineLeftoverWork_ShowsResumed_WhileABrandNewFileInTheSameLaneShowsNew()
    {
        await using var host = await QueueHost.StartAsync(Lane1);
        var leftover = host.Drop("lane1", "leftover.mkv");
        host.SeedResume(new ResumeEntry { LaneId = "lane1", FullName = leftover, Status = ResumeStatus.Pending, Order = 0 });
        host.Drop("lane1", "brand-new.mkv");

        var items = await host.PollAsync();
        Assert.True(items.Single(i => i.FileName == "leftover.mkv").IsResumed);
        Assert.False(items.Single(i => i.FileName == "brand-new.mkv").IsResumed);
    }

    [Fact]
    public async Task EditingOneFilesQueueSettings_NeverFlipsTheRestToResumed()
    {
        // Real past bug: a queue edit on one untracked file made the whole lane look "resumed".
        await using var host = await QueueHost.StartAsync(Lane1);
        var a = host.Drop("lane1", "a.mkv");
        host.Drop("lane1", "b.mkv");
        host.Drop("lane1", "c.mkv");

        await host.OverridePresetAsync("lane1", a, "Other");
        await host.SkipAsync("lane1", a);
        await host.ReorderAsync(("lane1", a), ("lane1", Path.Combine(Path.GetDirectoryName(a)!, "b.mkv")));

        Assert.All(await host.PollAsync(), i => Assert.False(i.IsResumed));
    }

    // ---- other entry kinds ----------------------------------------------------------------------

    [Fact]
    public async Task FailedFiles_AreListedAfterTheWaitingOnes_AndFlaggedAsErrors()
    {
        await using var host = await QueueHost.StartAsync(Lane1);
        var failed = host.Drop("lane1", "failed.mkv");
        host.SeedResume(new ResumeEntry { LaneId = "lane1", FullName = failed, Status = ResumeStatus.Error, Order = 0 });
        host.Drop("lane1", "waiting.mkv");

        var items = await host.PollAsync();
        Assert.Equal(new[] { "waiting.mkv", "failed.mkv" }, items.Select(i => i.FileName));
        Assert.False(items[0].IsError);
        Assert.True(items[1].IsError);
    }

    [Fact]
    public async Task CompletedFiles_AreNotListed_EvenIfTheSourceStillExists()
    {
        await using var host = await QueueHost.StartAsync(Lane1);
        var done = host.Drop("lane1", "done.mkv");
        host.SeedResume(new ResumeEntry { LaneId = "lane1", FullName = done, Status = ResumeStatus.Completed, Order = 0 });
        host.Drop("lane1", "waiting.mkv");

        Assert.Equal(new[] { "waiting.mkv" }, await host.PollNamesAsync());
    }

    [Fact]
    public async Task DisabledLanes_AreNeitherListedNorTracked()
    {
        await using var host = await QueueHost.StartAsync(Lane1, new LaneSpec("off", "Disabled Lane", Enabled: false));
        host.Drop("off", "ignored.mkv");
        host.Drop("lane1", "counted.mkv");

        Assert.Equal(new[] { "counted.mkv" }, await host.PollNamesAsync());
        Assert.DoesNotContain(host.ResumeEntries(), e => e.LaneId == "off");
    }

    [Fact]
    public async Task NonVideoFiles_AreIgnored()
    {
        await using var host = await QueueHost.StartAsync(Lane1);
        host.Drop("lane1", "movie.mkv");
        host.Drop("lane1", "subtitle.srt");
        host.Drop("lane1", "notes.txt");

        Assert.Equal(new[] { "movie.mkv" }, await host.PollNamesAsync());
    }

    [Fact]
    public async Task AFileDeletedFromDisk_DisappearsFromTheQueue()
    {
        await using var host = await QueueHost.StartAsync(Lane1);
        var a = host.Drop("lane1", "a.mkv");
        host.Drop("lane1", "b.mkv");
        await host.PollAsync();

        File.Delete(a);
        Assert.Equal(new[] { "b.mkv" }, await host.PollNamesAsync());
    }

    [Fact]
    public async Task AnEmptyQueue_ReturnsAnEmptyList()
    {
        await using var host = await QueueHost.StartAsync(Lane1);
        Assert.Empty(await host.PollAsync());
    }
}
