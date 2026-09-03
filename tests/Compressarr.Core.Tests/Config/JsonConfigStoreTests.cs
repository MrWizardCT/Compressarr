using Compressarr.Core.Config;

namespace Compressarr.Core.Tests.Config;

public class JsonConfigStoreTests : IDisposable
{
    private readonly string _tempDir = Directory.CreateTempSubdirectory("compressarr-tests-").FullName;

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    private string ConfigPath => Path.Combine(_tempDir, "config.json");

    [Fact]
    public void Load_MissingFile_ReturnsDefaults()
    {
        var store = new JsonConfigStore();

        var config = store.Load(ConfigPath);

        Assert.Equal(2, config.Lanes.Count);
        Assert.Contains(config.Lanes, l => l.Id == "hdsd");
        Assert.Contains(config.Lanes, l => l.Id == "uhd");
    }

    [Fact]
    public void Load_MalformedJson_ThrowsClearError()
    {
        File.WriteAllText(ConfigPath, "{ not valid json");
        var store = new JsonConfigStore();

        var ex = Assert.Throws<InvalidDataException>(() => store.Load(ConfigPath));
        Assert.Contains(ConfigPath, ex.Message);
    }

    [Fact]
    public void LaneRoundTrip_AddRenameRemove_LeavesOtherLanesUntouched()
    {
        var store = new JsonConfigStore();
        var config = DefaultConfigFactory.Create();

        // Add a third lane.
        config.Lanes.Add(new LaneConfig { Id = "custom", DisplayName = "Custom Lane" });
        store.Save(config, ConfigPath);

        var reloaded = store.Load(ConfigPath);
        Assert.Equal(3, reloaded.Lanes.Count);
        var custom = reloaded.Lanes.Single(l => l.Id == "custom");
        Assert.Equal("Custom Lane", custom.DisplayName);

        // Rename the custom lane; hdsd/uhd must be unaffected.
        custom.DisplayName = "Renamed Lane";
        store.Save(reloaded, ConfigPath);

        var afterRename = store.Load(ConfigPath);
        Assert.Equal("Renamed Lane", afterRename.Lanes.Single(l => l.Id == "custom").DisplayName);
        Assert.Equal("HD/SD", afterRename.Lanes.Single(l => l.Id == "hdsd").DisplayName);
        Assert.Equal("UHD", afterRename.Lanes.Single(l => l.Id == "uhd").DisplayName);

        // Remove the custom lane.
        afterRename.Lanes.RemoveAll(l => l.Id == "custom");
        store.Save(afterRename, ConfigPath);

        var afterRemove = store.Load(ConfigPath);
        Assert.Equal(2, afterRemove.Lanes.Count);
        Assert.DoesNotContain(afterRemove.Lanes, l => l.Id == "custom");
    }

    [Fact]
    public void Update_ReturnsMutateResult_AndPersistsTheChange()
    {
        var store = new JsonConfigStore();
        store.Save(DefaultConfigFactory.Create(), ConfigPath);

        var newCount = store.Update(ConfigPath, config =>
        {
            config.Lanes.Add(new LaneConfig { Id = "extra", DisplayName = "Extra" });
            return config.Lanes.Count;
        });

        Assert.Equal(3, newCount);
        Assert.Equal(3, store.Load(ConfigPath).Lanes.Count);
    }

    [Fact]
    public async Task Update_ConcurrentCalls_DoNotLoseEitherChange()
    {
        // Reproduces the "Save All Lanes" race this test guards against: two concurrent
        // load-mutate-save cycles against different lanes must not clobber each other, and must
        // not throw a file-in-use IOException from racing directly on the file write.
        var store = new JsonConfigStore();
        store.Save(DefaultConfigFactory.Create(), ConfigPath);

        var task1 = Task.Run(() => store.Update(ConfigPath, config =>
        {
            var lane = config.Lanes.Single(l => l.Id == "hdsd");
            lane.DisplayName = "Updated HD/SD";
            return true;
        }));
        var task2 = Task.Run(() => store.Update(ConfigPath, config =>
        {
            var lane = config.Lanes.Single(l => l.Id == "uhd");
            lane.DisplayName = "Updated UHD";
            return true;
        }));

        await Task.WhenAll(task1, task2);

        var final = store.Load(ConfigPath);
        Assert.Equal("Updated HD/SD", final.Lanes.Single(l => l.Id == "hdsd").DisplayName);
        Assert.Equal("Updated UHD", final.Lanes.Single(l => l.Id == "uhd").DisplayName);
    }

    [Fact]
    public void ClearConfiguration_OverwritingWithFreshDefaults_DiscardsEveryCustomization()
    {
        // Exercises the exact composition Settings' "Clear Configuration" maintenance button uses
        // (Save(DefaultConfigFactory.Create(), path)) against a config that's been customized the
        // way a real installation would be - a renamed default lane, an added custom lane, and a
        // changed setting - to prove the reset actually discards all of it rather than merging
        // over the customizations the way a partial-override Load() normally would.
        var store = new JsonConfigStore();
        var customized = DefaultConfigFactory.Create();
        customized.Lanes.Single(l => l.Id == "hdsd").DisplayName = "My Custom Movies Lane";
        customized.Lanes.Add(new LaneConfig { Id = "extra", DisplayName = "Extra Lane" });
        customized.Logging.RetentionDays = 999;
        customized.Arrs.Sonarr.Enabled = true;
        customized.Arrs.Sonarr.ApiKey = "some-real-key";
        store.Save(customized, ConfigPath);

        store.Save(DefaultConfigFactory.Create(), ConfigPath);

        var reset = store.Load(ConfigPath);
        Assert.Equal(2, reset.Lanes.Count);
        Assert.Equal("HD/SD", reset.Lanes.Single(l => l.Id == "hdsd").DisplayName);
        Assert.DoesNotContain(reset.Lanes, l => l.Id == "extra");
        Assert.NotEqual(999, reset.Logging.RetentionDays);
        Assert.False(reset.Arrs.Sonarr.Enabled);
        Assert.Equal("", reset.Arrs.Sonarr.ApiKey);
    }

    [Fact]
    public void ResetLanes_ClearingAndAddingOne_LeavesOtherSettingsUntouched()
    {
        // Exercises the exact composition Settings' "Reset Lanes" maintenance button uses
        // (Lanes.Clear() + Add one fresh lane, via Update) - narrower than Clear Configuration:
        // every lane is gone and replaced with exactly one new blank one, but a customized
        // non-lane setting must survive untouched.
        var store = new JsonConfigStore();
        var customized = DefaultConfigFactory.Create();
        customized.Lanes.Add(new LaneConfig { Id = "extra", DisplayName = "Extra Lane" });
        customized.Logging.RetentionDays = 999;
        customized.Arrs.Sonarr.Enabled = true;
        store.Save(customized, ConfigPath);

        store.Update(ConfigPath, config =>
        {
            config.Lanes.Clear();
            config.Lanes.Add(new LaneConfig { DisplayName = "New Lane 1", Enabled = true });
            return true;
        });

        var reset = store.Load(ConfigPath);
        var lane = Assert.Single(reset.Lanes);
        Assert.Equal("New Lane 1", lane.DisplayName);
        Assert.True(lane.Enabled);
        Assert.Equal(999, reset.Logging.RetentionDays);
        Assert.True(reset.Arrs.Sonarr.Enabled);
    }

    [Fact]
    public void Load_PartialOverrideFile_FillsRemainingFieldsFromDefaults()
    {
        File.WriteAllText(ConfigPath, """{"processing":{"retentionDays":99}}""");
        var store = new JsonConfigStore();

        var config = store.Load(ConfigPath);

        // Unrelated defaults survive a partial override file untouched.
        Assert.Equal(2, config.Lanes.Count);
        Assert.Equal("%ProgramFiles%\\HandBrake\\HandBrakeCLI.exe", config.HandBrake.CliPath);
    }
}
