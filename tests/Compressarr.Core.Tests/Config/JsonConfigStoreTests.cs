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
