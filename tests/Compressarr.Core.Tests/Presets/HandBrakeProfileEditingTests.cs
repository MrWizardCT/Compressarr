using System.Text.Json.Nodes;
using Compressarr.Core.Config;
using Compressarr.Core.Conversion;
using Compressarr.Core.Presets;

namespace Compressarr.Core.Tests.Presets;

/// <summary>Replace/Remove on the store, the references helper, and the importer.</summary>
public class HandBrakeProfileEditingTests : AppDataTestBase
{
    private static JsonObject Leaf(string name, int rf = 26, string format = "av_mp4") => new()
    {
        ["PresetName"] = name,
        ["FileFormat"] = format,
        ["Folder"] = false,
        ["Type"] = 1,
        ["VideoEncoder"] = "x264",
        ["VideoQualityType"] = 2,
        ["VideoQualitySlider"] = rf
    };

    private readonly HandBrakeProfileStore _store = new();

    // ---- store: replace / remove ----------------------------------------------------------------

    [Fact]
    public void Replace_ChangesTheDefinition_KeepsItsPlace_AndRegeneratesTheActiveFile()
    {
        _store.AddUserProfiles(new[] { Leaf("A"), Leaf("B"), Leaf("C") });

        _store.ReplaceUserProfile("B", Leaf("B", rf: 18));

        var user = _store.GetAll().Where(p => !p.IsBuiltIn).ToList();
        Assert.Equal(new[] { "A", "B", "C" }, user.Select(p => p.Name).ToArray());
        Assert.Equal(18, user[1].Definition["VideoQualitySlider"]!.GetValue<int>());
        Assert.Contains("\"VideoQualitySlider\": 18", File.ReadAllText(AppPaths.GetHandBrakeActivePresetsFilePath()));
    }

    [Fact]
    public void Replace_CanRename_AndAChangeOfCaseIsNotAClash()
    {
        _store.AddUserProfiles(new[] { Leaf("Mine"), Leaf("Other") });

        _store.ReplaceUserProfile("Mine", Leaf("MINE"));
        _store.ReplaceUserProfile("Other", Leaf("Renamed"));

        Assert.Equal(new[] { "MINE", "Renamed" }, _store.GetAll().Where(p => !p.IsBuiltIn).Select(p => p.Name).ToArray());
        Assert.Null(_store.Find("Other"));
    }

    [Fact]
    public void Replace_ToANameThatIsTaken_Throws_AndChangesNothing()
    {
        _store.AddUserProfiles(new[] { Leaf("A"), Leaf("B") });
        var before = File.ReadAllBytes(AppPaths.GetHandBrakeProfilesFilePath());

        Assert.Throws<InvalidOperationException>(() => _store.ReplaceUserProfile("A", Leaf("b")));
        Assert.Throws<InvalidOperationException>(() => _store.ReplaceUserProfile("A", Leaf("Compressarr SD-HD")));

        Assert.Equal(before, File.ReadAllBytes(AppPaths.GetHandBrakeProfilesFilePath()));
    }

    [Fact]
    public void BuiltIns_AreLocked_AgainstReplaceAndRemove()
    {
        Assert.Throws<InvalidOperationException>(() => _store.ReplaceUserProfile("Compressarr SD-HD", Leaf("Compressarr SD-HD")));
        Assert.Throws<InvalidOperationException>(() => _store.RemoveUserProfile("Compressarr UHD AV1"));
        Assert.Equal(2, _store.GetAll().Count);
    }

    [Fact]
    public void ReplaceAndRemove_OfAnUnknownName_Throw()
    {
        Assert.Throws<InvalidOperationException>(() => _store.ReplaceUserProfile("Nope", Leaf("Nope")));
        Assert.Throws<InvalidOperationException>(() => _store.RemoveUserProfile("Nope"));
    }

    [Fact]
    public void Remove_DropsTheProfile_FromBothFiles()
    {
        _store.AddUserProfiles(new[] { Leaf("A"), Leaf("B") });

        _store.RemoveUserProfile("a");

        Assert.Null(_store.Find("A"));
        Assert.NotNull(_store.Find("B"));
        Assert.DoesNotContain("\"A\"", File.ReadAllText(AppPaths.GetHandBrakeActivePresetsFilePath()));
        Assert.Null(new HandBrakeProfileStore().Find("A"));
    }

    [Fact]
    public void Remove_TheLastOne_LeavesAValidFileAndNoYoursFolder()
    {
        _store.AddUserProfiles(new[] { Leaf("Only") });

        _store.RemoveUserProfile("Only");

        Assert.Equal(2, new HandBrakeProfileStore().GetAll().Count);
        Assert.DoesNotContain("Yours", File.ReadAllText(AppPaths.GetHandBrakeActivePresetsFilePath()));
    }

    // ---- references -----------------------------------------------------------------------------

    private static CompressarrConfig ConfigWithLane(string tv, string movie) => new()
    {
        Lanes = { new LaneConfig { Id = "l1", DisplayName = "Lane One", TvPreset = tv, MoviePreset = movie } }
    };

    [Fact]
    public void Find_ReportsLanesAndQueuedOverrides_ButNotFinishedOnes()
    {
        var config = ConfigWithLane("Mine", "");
        var resume = new List<ResumeEntry>
        {
            new() { LaneId = "l1", FullName = "a", Status = ResumeStatus.Pending, PresetOverride = "mine" },
            new() { LaneId = "l1", FullName = "b", Status = ResumeStatus.Completed, PresetOverride = "Mine" },
            new() { LaneId = "l1", FullName = "c", Status = ResumeStatus.Pending, PresetOverride = "Other" }
        };

        var usage = ProfileReferences.Find(config, resume, "Mine");

        Assert.Equal(new[] { "Lane One" }, usage.Lanes);
        Assert.Equal(1, usage.QueuedFiles);
        Assert.True(usage.InUse);
        Assert.False(ProfileReferences.Find(config, resume, "Unused").InUse);
    }

    // ---- importer -------------------------------------------------------------------------------

    private string WriteSource(params JsonObject[] leaves)
    {
        var path = Path.Combine(AppData, "source-presets.json");
        var folder = new JsonObject
        {
            ["PresetName"] = "General", ["Folder"] = true, ["Type"] = 0,
            ["ChildrenArray"] = new JsonArray(leaves.Select(l => (JsonNode)l).ToArray())
        };
        File.WriteAllText(path, new JsonObject { ["PresetList"] = new JsonArray(folder) }.ToJsonString());
        return path;
    }

    private HandBrakeProfileImporter Importer => new(_store);

    [Fact]
    public void Read_ListsLeavesWithTheirGroup_AndFlagsWhatIsAlreadyHere()
    {
        _store.AddUserProfiles(new[] { Leaf("Taken") });
        var identicalBuiltIn = (JsonObject)_store.GetBuiltIns()[0].Definition.DeepClone();
        var editedBuiltIn = (JsonObject)_store.GetBuiltIns()[1].Definition.DeepClone();
        editedBuiltIn["VideoQualitySlider"] = 5;
        var path = WriteSource(Leaf("Fresh"), Leaf("Taken"), identicalBuiltIn, editedBuiltIn);

        var listing = Importer.Read(path);

        Assert.Null(listing.Error);
        var byName = listing.Candidates.ToDictionary(c => c.Name);
        Assert.Equal(ImportCandidateStatus.New, byName["Fresh"].Status);
        Assert.Equal("General", byName["Fresh"].Group);
        Assert.Equal("x264, RF 26", byName["Fresh"].Video);
        Assert.Equal(ImportCandidateStatus.NameUsed, byName["Taken"].Status);
        Assert.Equal(ImportCandidateStatus.AlreadyBuiltIn, byName["Compressarr SD-HD"].Status);
        Assert.Equal(ImportCandidateStatus.NameUsed, byName["Compressarr UHD AV1"].Status); // same name, different recipe
    }

    [Fact]
    public void Read_NotAPresetsFile_ReportsWhy()
    {
        var path = Path.Combine(AppData, "junk.json");
        File.WriteAllText(path, "{\"x\":1}");

        var listing = Importer.Read(path);

        Assert.NotNull(listing.Error);
        Assert.Empty(listing.Candidates);
    }

    [Fact]
    public void Import_CopiesTheChosenPresets_AsPlainCustomLeaves_AndLeavesTheSourceAlone()
    {
        var official = Leaf("Fast 1080p30");
        official["Type"] = 0;
        official["Default"] = true;
        official["ChildrenArray"] = new JsonArray(Leaf("junk"));
        var path = WriteSource(official, Leaf("Not chosen"));
        var sourceBefore = File.ReadAllBytes(path);

        var outcome = Importer.Import(path, new[] { "Fast 1080p30" }, ImportConflictMode.KeepBoth);

        Assert.Equal(new[] { "Fast 1080p30" }, outcome.Imported);
        var copy = _store.Find("Fast 1080p30")!;
        Assert.False(copy.IsBuiltIn);
        Assert.Equal(1, copy.Definition["Type"]!.GetValue<int>());
        Assert.False(copy.Definition["Default"]!.GetValue<bool>());
        Assert.False(copy.Definition["Folder"]!.GetValue<bool>());
        Assert.Empty(copy.Definition["ChildrenArray"]!.AsArray());
        Assert.Null(_store.Find("Not chosen"));
        Assert.Equal(sourceBefore, File.ReadAllBytes(path));
    }

    [Fact]
    public void Import_NamesWithASlash_AreMadeSafe()
    {
        var path = WriteSource(Leaf("Web/Gmail Large"));

        Importer.Import(path, new[] { "Web/Gmail Large" }, ImportConflictMode.KeepBoth);

        Assert.NotNull(_store.Find("Web-Gmail Large"));
    }

    [Fact]
    public void Import_NameClash_KeepBoth_AddsAnImportedCopy_EvenTwice()
    {
        _store.AddUserProfiles(new[] { Leaf("Taken", rf: 10) });
        var path = WriteSource(Leaf("Taken", rf: 30));

        var first = Importer.Import(path, new[] { "Taken" }, ImportConflictMode.KeepBoth);
        var second = Importer.Import(path, new[] { "Taken" }, ImportConflictMode.KeepBoth);

        Assert.Equal(new[] { "Taken (imported)" }, first.Imported);
        Assert.Equal(new[] { "Taken (imported) 2" }, second.Imported);
        Assert.Equal(10, _store.Find("Taken")!.Definition["VideoQualitySlider"]!.GetValue<int>());
    }

    [Fact]
    public void Import_NameClash_Replace_OverwritesYoursInPlace()
    {
        _store.AddUserProfiles(new[] { Leaf("Taken", rf: 10), Leaf("After") });
        var path = WriteSource(Leaf("Taken", rf: 30));

        var outcome = Importer.Import(path, new[] { "Taken" }, ImportConflictMode.Replace);

        Assert.Equal(new[] { "Taken" }, outcome.Replaced);
        Assert.Equal(30, _store.Find("Taken")!.Definition["VideoQualitySlider"]!.GetValue<int>());
        Assert.Equal(new[] { "Taken", "After" }, _store.GetAll().Where(p => !p.IsBuiltIn).Select(p => p.Name).ToArray());
    }

    [Fact]
    public void Import_NameClash_Skip_ChangesNothing()
    {
        _store.AddUserProfiles(new[] { Leaf("Taken", rf: 10) });
        var path = WriteSource(Leaf("Taken", rf: 30));

        var outcome = Importer.Import(path, new[] { "Taken" }, ImportConflictMode.Skip);

        Assert.Equal(new[] { "Taken" }, outcome.Skipped);
        Assert.Equal(10, _store.Find("Taken")!.Definition["VideoQualitySlider"]!.GetValue<int>());
    }

    [Fact]
    public void Import_ClashWithABuiltIn_NeverReplacesIt_EvenWhenReplaceIsChosen()
    {
        var edited = (JsonObject)_store.GetBuiltIns()[0].Definition.DeepClone();
        edited["VideoQualitySlider"] = 5;
        var path = WriteSource(edited);
        var original = _store.GetBuiltIns()[0].Definition.DeepClone();

        var outcome = Importer.Import(path, new[] { "Compressarr SD-HD" }, ImportConflictMode.Replace);

        Assert.Equal(new[] { "Compressarr SD-HD (imported)" }, outcome.Imported);
        Assert.True(JsonNode.DeepEquals(original, _store.Find("Compressarr SD-HD")!.Definition));
    }

    [Fact]
    public void Import_AnIdenticalBuiltIn_IsSkipped()
    {
        var path = WriteSource((JsonObject)_store.GetBuiltIns()[0].Definition.DeepClone());

        var outcome = Importer.Import(path, new[] { "Compressarr SD-HD" }, ImportConflictMode.KeepBoth);

        Assert.Empty(outcome.Imported);
        Assert.Equal(new[] { "Compressarr SD-HD" }, outcome.Skipped);
    }

    [Fact]
    public void Import_ManyAtOnce_AreAllAdded_AndTheActiveFileIsRegenerated()
    {
        var path = WriteSource(Leaf("One"), Leaf("Two"), Leaf("Three"));

        var outcome = Importer.Import(path, new[] { "One", "Two", "Three" }, ImportConflictMode.KeepBoth);

        Assert.Equal(3, outcome.Imported.Count);
        Assert.Contains("Three", File.ReadAllText(AppPaths.GetHandBrakeActivePresetsFilePath()));
    }

    [Fact]
    public void Import_AnUnreadableFile_Throws()
    {
        var path = Path.Combine(AppData, "junk.json");
        File.WriteAllText(path, "nope");

        Assert.Throws<InvalidOperationException>(() => Importer.Import(path, new[] { "x" }, ImportConflictMode.KeepBoth));
    }
}
