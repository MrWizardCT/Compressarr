using System.Text.Json.Nodes;
using Compressarr.Core.Presets;

namespace Compressarr.Core.Tests.Presets;

public class HandBrakePresetFileTests : IDisposable
{
    private readonly string _tempDir = Directory.CreateTempSubdirectory("compressarr-presetfile-tests-").FullName;

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    private string Write(string json)
    {
        var path = Path.Combine(_tempDir, "presets.json");
        File.WriteAllText(path, json);
        return path;
    }

    // A folder-grouped tree: a top-level folder ("General") containing two leaf presets, plus one
    // leaf preset directly under PresetList with no folder grouping - mirrors real HandBrake
    // presets.json shape: a folder node carries "Folder": true alongside a PresetName (used as its
    // label in HandBrake's own UI) and a ChildrenArray, and must NOT be treated as a real,
    // selectable preset itself.
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
    public void GetLeaves_FlattensNestedTree_ReturnsOnlyLeaves()
    {
        var leaves = HandBrakePresetFile.GetLeaves(JsonNode.Parse(FixtureTree));

        Assert.Equal(3, leaves.Count); // General's 2 children + the top-level leaf - NOT "General" itself
        Assert.DoesNotContain(leaves, p => p.Name == "General");
        Assert.Contains(leaves, p => p.Name == "Fast 1080p30" && p.FileFormat == "av_mp4");
        Assert.Contains(leaves, p => p.Name == "H.265 MKV 2160p" && p.FileFormat == "av_mkv");
        Assert.Contains(leaves, p => p.Name == "Very Fast 720p30" && p.FileFormat == "mp4");
    }

    [Fact]
    public void GetLeaves_RecordsTheFolderEachLeafSatIn()
    {
        var leaves = HandBrakePresetFile.GetLeaves(JsonNode.Parse(FixtureTree));

        Assert.Equal("General", leaves.Single(l => l.Name == "Fast 1080p30").Group);
        Assert.Null(leaves.Single(l => l.Name == "Very Fast 720p30").Group);
    }

    [Fact]
    public void GetLeaves_FolderNode_NeverIncludedEvenWithoutChildren()
    {
        var root = JsonNode.Parse("""{"PresetList":[{"PresetName":"Empty Folder","Folder":true,"ChildrenArray":[]}]}""");

        Assert.Empty(HandBrakePresetFile.GetLeaves(root));
    }

    [Fact]
    public void GetLeaves_LeafThatAlsoHasChildren_WalksBoth()
    {
        // The original parser checked "is a leaf" and "has children" independently, not exclusively.
        var root = JsonNode.Parse("""{"PresetList":[{"PresetName":"Parent","ChildrenArray":[{"PresetName":"Child"}]}]}""");

        var names = HandBrakePresetFile.GetLeaves(root).Select(l => l.Name).ToList();

        Assert.Equal(new[] { "Parent", "Child" }, names);
    }

    [Fact]
    public void TryRead_ValidFile_ReturnsRoot()
    {
        Assert.True(HandBrakePresetFile.TryRead(Write(FixtureTree), out var root, out var error));
        Assert.NotNull(root);
        Assert.Null(error);
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("""{"hello":"world"}""")]
    [InlineData("""[1,2,3]""")]
    public void TryRead_NotAPresetsFile_ReportsWhy(string content)
    {
        Assert.False(HandBrakePresetFile.TryRead(Write(content), out var root, out var error));
        Assert.Null(root);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void TryRead_MissingFile_ReportsWhy()
    {
        Assert.False(HandBrakePresetFile.TryRead(Path.Combine(_tempDir, "nope.json"), out _, out var error));
        Assert.False(string.IsNullOrWhiteSpace(error));
    }
}
