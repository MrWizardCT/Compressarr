using System.Text.Json.Nodes;
using Compressarr.Core.Presets;

namespace Compressarr.Core.Tests.Presets;

public class PresetInstallerTests : IDisposable
{
    private readonly string _tempDir = Directory.CreateTempSubdirectory("compressarr-preset-installer-tests-").FullName;
    private readonly PresetInstaller _installer = new();

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    private string PresetsPath => Path.Combine(_tempDir, "presets.json");

    [Fact]
    public void GetBundledPresetsJson_ContainsCompressarrPresets()
    {
        var json = _installer.GetBundledPresetsJson();

        Assert.Contains("Compressarr SD-HD", json);
        Assert.Contains("Compressarr UHD AV1", json);
    }

    [Fact]
    public void GetBundledPresetsJson_NoPresetNameContainsASlash()
    {
        // HandBrakeCLI's --preset argument treats '/' as a folder-path separator when resolving
        // a preset by name, so a PresetName containing one (e.g. the original "Compressarr
        // SD/HD") is reported as "Invalid preset" even though it's genuinely present in the
        // file - confirmed by an actual HandBrakeCLI run against a real merged presets.json.
        // This guards against that mistake recurring if the bundled file is ever edited again.
        var service = new HandBrakePresetService();
        var tempPath = Path.Combine(_tempDir, "bundled-check.json");
        File.WriteAllText(tempPath, _installer.GetBundledPresetsJson());

        var names = service.GetPresetNames(tempPath);

        Assert.All(names, name => Assert.DoesNotContain('/', name));
    }

    [Fact]
    public void NeedsMergePrompt_FileMissing_ReturnsFalse()
    {
        Assert.False(_installer.NeedsMergePrompt(PresetsPath));
    }

    [Fact]
    public void NeedsMergePrompt_FileExists_ReturnsTrue()
    {
        File.WriteAllText(PresetsPath, "{}");

        Assert.True(_installer.NeedsMergePrompt(PresetsPath));
    }

    [Fact]
    public void InstallFresh_WritesBundledContentAndCreatesParentFolder()
    {
        var nestedPath = Path.Combine(_tempDir, "nested", "presets.json");

        _installer.InstallFresh(nestedPath);

        Assert.True(File.Exists(nestedPath));
        Assert.Contains("Compressarr SD-HD", File.ReadAllText(nestedPath));
    }

    [Fact]
    public void Merge_NoExistingCustomPresetsFolder_AppendsCompressarrFolderWithoutTouchingOthers()
    {
        File.WriteAllText(PresetsPath, """
            {
              "PresetList": [
                { "PresetName": "My Own Preset", "FileFormat": "av_mp4", "Folder": false }
              ]
            }
            """);

        _installer.Merge(PresetsPath);

        var tree = JsonNode.Parse(File.ReadAllText(PresetsPath))!.AsObject();
        var presetList = tree["PresetList"]!.AsArray();

        Assert.Contains(presetList, n => n!["PresetName"]!.GetValue<string>() == "My Own Preset");
        var compressarrFolder = presetList.Single(n => n!["PresetName"]!.GetValue<string>() == "Custom Presets");
        var children = compressarrFolder!["ChildrenArray"]!.AsArray();
        Assert.Contains(children, c => c!["PresetName"]!.GetValue<string>() == "Compressarr SD-HD");
        Assert.Contains(children, c => c!["PresetName"]!.GetValue<string>() == "Compressarr UHD AV1");
    }

    [Fact]
    public void Merge_ExistingCustomPresetsFolderWithUnrelatedPresets_UpsertsWithoutRemovingUserPresets()
    {
        File.WriteAllText(PresetsPath, """
            {
              "PresetList": [
                {
                  "PresetName": "Custom Presets",
                  "Folder": true,
                  "ChildrenArray": [
                    { "PresetName": "My Custom Thing", "FileFormat": "av_mp4" }
                  ]
                }
              ]
            }
            """);

        _installer.Merge(PresetsPath);

        var tree = JsonNode.Parse(File.ReadAllText(PresetsPath))!.AsObject();
        var folder = tree["PresetList"]!.AsArray().Single(n => n!["PresetName"]!.GetValue<string>() == "Custom Presets");
        var children = folder!["ChildrenArray"]!.AsArray();

        Assert.Equal(3, children.Count); // the user's own preset + Compressarr's 2
        Assert.Contains(children, c => c!["PresetName"]!.GetValue<string>() == "My Custom Thing");
        Assert.Contains(children, c => c!["PresetName"]!.GetValue<string>() == "Compressarr SD-HD");
        Assert.Contains(children, c => c!["PresetName"]!.GetValue<string>() == "Compressarr UHD AV1");
    }

    [Fact]
    public void Merge_RunTwice_IsIdempotent_DoesNotDuplicateCompressarrPresets()
    {
        File.WriteAllText(PresetsPath, """{ "PresetList": [] }""");

        _installer.Merge(PresetsPath);
        _installer.Merge(PresetsPath);

        var tree = JsonNode.Parse(File.ReadAllText(PresetsPath))!.AsObject();
        var folder = tree["PresetList"]!.AsArray().Single(n => n!["PresetName"]!.GetValue<string>() == "Custom Presets");
        var children = folder!["ChildrenArray"]!.AsArray();

        Assert.Equal(2, children.Count);
    }

    [Fact]
    public void Merge_ThenParsedByHandBrakePresetService_ExposesCompressarrPresets()
    {
        File.WriteAllText(PresetsPath, """
            {
              "PresetList": [
                { "PresetName": "Unrelated", "FileFormat": "av_mp4" }
              ]
            }
            """);

        _installer.Merge(PresetsPath);

        var presetService = new HandBrakePresetService();
        var names = presetService.GetPresetNames(PresetsPath);

        Assert.Contains("Compressarr SD-HD", names);
        Assert.Contains("Compressarr UHD AV1", names);
        Assert.Contains("Unrelated", names);
    }
}
