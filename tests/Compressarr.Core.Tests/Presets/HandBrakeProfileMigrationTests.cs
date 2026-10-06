using System.Text.Json.Nodes;
using Compressarr.Core.Config;
using Compressarr.Core.Conversion;
using Compressarr.Core.Presets;
using Compressarr.Core.Tests.Conversion;

namespace Compressarr.Core.Tests.Presets;

file sealed class PassThroughExpander : IPathExpander
{
    public string Expand(string value) => value;
    public bool PathExists(string value) => Directory.Exists(value) || File.Exists(value);
}

public class HandBrakeProfileMigrationTests : AppDataTestBase
{
    private readonly JsonConfigStore _configStore = new();
    private readonly JsonResumeStateStore _resumeStore = new();
    private readonly HandBrakeProfileStore _profiles = new();
    private readonly CoordinatorTestLogger _logger = new();

    private string OldPresetsPath => Path.Combine(AppData, "old-presets.json");
    private static string ConfigPath => AppPaths.GetConfigFilePath();
    private static string ResumePath => AppPaths.GetResumeFilePath();
    private static string MarkerPath => Path.Combine(AppPaths.GetProfilesDirectory(), "migration-2.2.json");

    private HandBrakeProfileMigration NewMigration() => new(_configStore, _resumeStore, new PassThroughExpander(), _profiles, _logger);

    private static JsonObject Leaf(string name, int rf = 26, string format = "av_mp4") => new()
    {
        ["PresetName"] = name,
        ["FileFormat"] = format,
        ["Folder"] = false,
        ["Type"] = 1,
        ["VideoQualitySlider"] = rf
    };

    /// <summary>Writes an old-style HandBrake presets.json (folders and all) holding the given leaves.</summary>
    private void WriteOldPresets(params JsonObject[] leaves)
    {
        var folder = new JsonObject
        {
            ["PresetName"] = "Custom Presets",
            ["Folder"] = true,
            ["Type"] = 1,
            ["ChildrenArray"] = new JsonArray(leaves.Select(l => (JsonNode)l).ToArray())
        };
        File.WriteAllText(OldPresetsPath, new JsonObject { ["PresetList"] = new JsonArray(folder) }.ToJsonString());
    }

    private void SaveConfig(string tvPreset, string moviePreset, bool pointAtOldFile = true)
    {
        _configStore.Update(ConfigPath, c =>
        {
            if (pointAtOldFile) c.HandBrake.PresetsPath = OldPresetsPath;
            c.Lanes.Clear();
            c.Lanes.Add(new LaneConfig { Id = "l1", DisplayName = "Lane", TvPreset = tvPreset, MoviePreset = moviePreset });
            return true;
        });
    }

    private JsonObject BuiltInCopy(string name) => (JsonObject)_profiles.GetBuiltIns().Single(p => p.Name == name).Definition.DeepClone();

    [Fact]
    public void LanePresetThatIsAnIdenticalBuiltIn_NeedsNothing()
    {
        WriteOldPresets(BuiltInCopy("Compressarr SD-HD"));
        SaveConfig("Compressarr SD-HD", "Compressarr SD-HD");

        var result = NewMigration().RunIfNeeded()!;

        Assert.Empty(result.Imported);
        Assert.Empty(result.Renamed);
        Assert.False(File.Exists(AppPaths.GetHandBrakeProfilesFilePath())); // nothing to carry over
        Assert.Equal("Compressarr SD-HD", _configStore.Load(ConfigPath).Lanes[0].TvPreset);
    }

    [Fact]
    public void LanePresetNotInTheBuiltIns_IsCopiedIntoYourProfilesUnderTheSameName()
    {
        WriteOldPresets(Leaf("Cartoons x265", rf: 22), Leaf("Never used", rf: 30));
        SaveConfig("Cartoons x265", "");

        var result = NewMigration().RunIfNeeded()!;

        Assert.Equal(new[] { "Cartoons x265" }, result.Imported);
        var copied = _profiles.Find("Cartoons x265")!;
        Assert.False(copied.IsBuiltIn);
        Assert.Equal(22, copied.Definition["VideoQualitySlider"]!.GetValue<int>());
        Assert.Null(_profiles.Find("Never used")); // unreferenced presets are Import's job, not migration's
        Assert.Equal("Cartoons x265", _configStore.Load(ConfigPath).Lanes[0].TvPreset);
    }

    [Fact]
    public void BuiltInNameWithADifferentDefinition_IsKeptAsYoursAndTheLaneRepointed()
    {
        // the user had edited "Compressarr UHD AV1" in their presets.json
        var edited = BuiltInCopy("Compressarr UHD AV1");
        edited["VideoQualitySlider"] = 19;
        WriteOldPresets(edited);
        SaveConfig(tvPreset: "", moviePreset: "Compressarr UHD AV1");

        var result = NewMigration().RunIfNeeded()!;

        var renamed = Assert.Single(result.Renamed);
        Assert.Equal("Compressarr UHD AV1", renamed.Key);
        Assert.Equal("Compressarr UHD AV1 (yours)", renamed.Value);
        Assert.Equal(19, _profiles.Find("Compressarr UHD AV1 (yours)")!.Definition["VideoQualitySlider"]!.GetValue<int>());
        Assert.NotEqual(19, _profiles.Find("Compressarr UHD AV1")!.Definition["VideoQualitySlider"]!.GetValue<int>()); // the built-in stays the original
        Assert.Equal("Compressarr UHD AV1 (yours)", _configStore.Load(ConfigPath).Lanes[0].MoviePreset);
    }

    [Fact]
    public void QueuedFileOverrides_AreMigratedToo()
    {
        WriteOldPresets(Leaf("Cartoons x265"));
        var edited = BuiltInCopy("Compressarr SD-HD");
        edited["VideoQualitySlider"] = 18;
        File.WriteAllText(OldPresetsPath, new JsonObject
        {
            ["PresetList"] = new JsonArray(new JsonObject
            {
                ["PresetName"] = "Custom Presets", ["Folder"] = true,
                ["ChildrenArray"] = new JsonArray(Leaf("Cartoons x265"), edited)
            })
        }.ToJsonString());
        SaveConfig("", "");
        _resumeStore.Save(new List<ResumeEntry>
        {
            new() { LaneId = "l1", FullName = @"C:\a.mkv", Status = ResumeStatus.Pending, PresetOverride = "Cartoons x265" },
            new() { LaneId = "l1", FullName = @"C:\b.mkv", Status = ResumeStatus.Pending, PresetOverride = "Compressarr SD-HD" },
            new() { LaneId = "l1", FullName = @"C:\c.mkv", Status = ResumeStatus.Pending }
        }, ResumePath);

        var result = NewMigration().RunIfNeeded()!;

        Assert.Equal(new[] { "Cartoons x265" }, result.Imported);
        var entries = _resumeStore.Load(ResumePath);
        Assert.Equal("Cartoons x265", entries.Single(e => e.FullName == @"C:\a.mkv").PresetOverride);
        Assert.Equal("Compressarr SD-HD (yours)", entries.Single(e => e.FullName == @"C:\b.mkv").PresetOverride);
        Assert.Null(entries.Single(e => e.FullName == @"C:\c.mkv").PresetOverride);
    }

    [Fact]
    public void PresetMissingFromTheOldFile_IsReportedAndLeftAlone()
    {
        WriteOldPresets(Leaf("Something else"));
        SaveConfig("Ghost", "");

        var result = NewMigration().RunIfNeeded()!;

        Assert.Equal(new[] { "Ghost" }, result.Missing);
        Assert.Equal("Ghost", _configStore.Load(ConfigPath).Lanes[0].TvPreset);
        Assert.Contains(_logger.Logs, l => l.Message.Contains("Ghost") && l.Severity == Compressarr.Core.Logging.LogSeverity.Error);
    }

    [Fact]
    public void OldPresetsFileGone_DoesNotFailAndStillMarksItDone()
    {
        SaveConfig("Cartoons x265", "");

        var result = NewMigration().RunIfNeeded()!;

        Assert.Equal(new[] { "Cartoons x265" }, result.Missing);
        Assert.True(File.Exists(MarkerPath));
    }

    [Fact]
    public void RunsOnlyOnce()
    {
        WriteOldPresets(Leaf("Cartoons x265"));
        SaveConfig("Cartoons x265", "");
        Assert.NotNull(NewMigration().RunIfNeeded());

        Assert.Null(NewMigration().RunIfNeeded());
    }

    [Fact]
    public void MarkerRecordsWhatWasDone()
    {
        WriteOldPresets(Leaf("Cartoons x265"));
        SaveConfig("Cartoons x265", "Ghost");

        NewMigration().RunIfNeeded();

        var marker = JsonNode.Parse(File.ReadAllText(MarkerPath))!;
        Assert.Equal("Cartoons x265", marker["imported"]![0]!.GetValue<string>());
        Assert.Equal("Ghost", marker["missing"]![0]!.GetValue<string>());
        Assert.NotNull(marker["migratedAtUtc"]);
    }

    [Fact]
    public void FreshInstall_NothingToMigrate_StillRecordsTheRunAndTouchesNothing()
    {
        var result = NewMigration().RunIfNeeded()!;

        Assert.Empty(result.Imported);
        Assert.Empty(result.Renamed);
        Assert.Empty(result.Missing);
        Assert.True(File.Exists(MarkerPath));
        Assert.False(File.Exists(AppPaths.GetHandBrakeProfilesFilePath()));
    }

    [Fact]
    public void ReRunningAfterAnInterruptedAttempt_ReusesTheExistingCopy()
    {
        // simulate "the copy was written but the config repoint never happened": no marker yet
        var edited = BuiltInCopy("Compressarr UHD AV1");
        edited["VideoQualitySlider"] = 19;
        WriteOldPresets(edited);
        SaveConfig("", "Compressarr UHD AV1");
        var copy = (JsonObject)edited.DeepClone();
        copy["PresetName"] = "Compressarr UHD AV1 (yours)";
        _profiles.AddUserProfiles(new[] { copy });

        var result = NewMigration().RunIfNeeded()!;

        Assert.Equal("Compressarr UHD AV1 (yours)", _configStore.Load(ConfigPath).Lanes[0].MoviePreset);
        Assert.Single(_profiles.GetAll(), p => p.Name.StartsWith("Compressarr UHD AV1 (yours)"));
        Assert.Single(result.Renamed);
    }
}
