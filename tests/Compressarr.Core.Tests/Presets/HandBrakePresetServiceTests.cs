using Compressarr.Core.Presets;

namespace Compressarr.Core.Tests.Presets;

public class HandBrakePresetServiceTests : IDisposable
{
    private readonly string _tempDir = Directory.CreateTempSubdirectory("compressarr-preset-tests-").FullName;

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    private string WritePresetsFile(string json)
    {
        var path = Path.Combine(_tempDir, "presets.json");
        File.WriteAllText(path, json);
        return path;
    }

    // A folder-grouped tree: a top-level folder ("General") containing two leaf presets, plus one
    // leaf preset directly under PresetList with no folder grouping - mirrors real HandBrake
    // presets.json shape, confirmed against an actual generated presets.json: a folder node
    // carries "Folder": true alongside a PresetName (used as its label in HandBrake's own UI) and
    // a ChildrenArray, and must NOT be treated as a real, selectable preset itself.
    private const string FixtureTree = """
        {
          "PresetList": [
            {
              "PresetName": "General",
              "Folder": true,
              "ChildrenArray": [
                { "PresetName": "Fast 1080p30", "FileFormat": "av_mp4" },
                { "PresetName": "H.265 MKV 2160p", "FileFormat": "av_mkv" }
              ]
            },
            { "PresetName": "Very Fast 720p30", "FileFormat": "mp4" }
          ]
        }
        """;

    [Fact]
    public void GetPresets_FlattensNestedTree_ReturnsOnlyLeaves()
    {
        var service = new HandBrakePresetService();
        var path = WritePresetsFile(FixtureTree);

        var presets = service.GetPresets(path);

        Assert.Equal(3, presets.Count); // General's 2 children + the top-level leaf - NOT "General" itself
        Assert.DoesNotContain(presets, p => p.PresetName == "General");
        Assert.Contains(presets, p => p.PresetName == "Fast 1080p30" && p.FileFormat == "av_mp4");
        Assert.Contains(presets, p => p.PresetName == "H.265 MKV 2160p" && p.FileFormat == "av_mkv");
        Assert.Contains(presets, p => p.PresetName == "Very Fast 720p30" && p.FileFormat == "mp4");
    }

    [Fact]
    public void GetPresets_FolderNode_NeverIncludedEvenWithoutChildren()
    {
        var service = new HandBrakePresetService();
        var path = WritePresetsFile("""{"PresetList":[{"PresetName":"Empty Folder","Folder":true,"ChildrenArray":[]}]}""");

        Assert.Empty(service.GetPresets(path));
    }

    [Fact]
    public void PresetExists_KnownAndUnknownNames()
    {
        var service = new HandBrakePresetService();
        var path = WritePresetsFile(FixtureTree);

        Assert.True(service.PresetExists("Fast 1080p30", path));
        Assert.False(service.PresetExists("Does Not Exist", path));
    }

    [Theory]
    [InlineData("Fast 1080p30", ".mp4")]
    [InlineData("H.265 MKV 2160p", ".mkv")]
    [InlineData("Very Fast 720p30", ".mp4")]
    public void GetOutputExtension_MapsFileFormatSubstring(string presetName, string expectedExtension)
    {
        var service = new HandBrakePresetService();
        var path = WritePresetsFile(FixtureTree);

        var extension = service.GetOutputExtension(presetName, path, out var warning);

        Assert.Equal(expectedExtension, extension);
        Assert.Null(warning);
    }

    [Fact]
    public void GetOutputExtension_UnknownPreset_DefaultsToMp4WithWarning()
    {
        var service = new HandBrakePresetService();
        var path = WritePresetsFile(FixtureTree);

        var extension = service.GetOutputExtension("Nonexistent", path, out var warning);

        Assert.Equal(".mp4", extension);
        Assert.NotNull(warning);
    }

    [Fact]
    public void GetOutputExtension_UnrecognizedFileFormat_DefaultsToMp4WithWarning()
    {
        var service = new HandBrakePresetService();
        var path = WritePresetsFile("""{"PresetList":[{"PresetName":"Weird","FileFormat":"av_avi"}]}""");

        var extension = service.GetOutputExtension("Weird", path, out var warning);

        Assert.Equal(".mp4", extension);
        Assert.NotNull(warning);
    }

    [Fact]
    public void InvalidateCache_ForcesReReadFromDisk()
    {
        var service = new HandBrakePresetService();
        var path = WritePresetsFile("""{"PresetList":[{"PresetName":"One","FileFormat":"mp4"}]}""");

        Assert.Single(service.GetPresets(path));

        File.WriteAllText(path, """{"PresetList":[{"PresetName":"One","FileFormat":"mp4"},{"PresetName":"Two","FileFormat":"mkv"}]}""");
        service.InvalidateCache(path);

        Assert.Equal(2, service.GetPresets(path).Count);
    }
}
