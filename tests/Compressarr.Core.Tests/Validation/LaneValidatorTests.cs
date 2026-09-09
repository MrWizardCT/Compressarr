using Compressarr.Core.Config;
using Compressarr.Core.Conversion;
using Compressarr.Core.Presets;

namespace Compressarr.Core.Tests.Validation;

file sealed class PassThroughPathExpander : IPathExpander
{
    public string Expand(string value) => value;
    public bool PathExists(string value) => Directory.Exists(value) || File.Exists(value);
}

file sealed class FixedExtensionPresetService : IHandBrakePresetService
{
    private readonly HashSet<string> _existingPresets;
    public FixedExtensionPresetService(params string[] existingPresets) => _existingPresets = existingPresets.ToHashSet(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyList<HandBrakePreset> GetPresets(string presetsPath) => Array.Empty<HandBrakePreset>();
    public IReadOnlyList<string> GetPresetNames(string presetsPath) => _existingPresets.ToList();
    public bool PresetExists(string presetName, string presetsPath) => _existingPresets.Contains(presetName);
    public HandBrakePreset? GetPreset(string presetName, string presetsPath) => null;
    public string GetOutputExtension(string presetName, string presetsPath, out string? warning) { warning = null; return ".mkv"; }
    public void InvalidateCache(string? presetsPath = null) { }
}

public class LaneValidatorTests : IDisposable
{
    private readonly string _tempDir = Directory.CreateTempSubdirectory("compressarr-lane-validator-tests-").FullName;
    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    private static LaneConfig MakeLane(string input, string output, string tvPreset = "", string moviePreset = "", bool enabled = true) => new()
    {
        Id = "lane1",
        DisplayName = "Test Lane",
        Enabled = enabled,
        Input = input,
        Output = output,
        TvPreset = tvPreset,
        MoviePreset = moviePreset
    };

    [Fact]
    public void Validate_HealthyLane_ReturnsNoIssues()
    {
        Directory.CreateDirectory(Path.Combine(_tempDir, "Input"));
        var lane = MakeLane(Path.Combine(_tempDir, "Input"), Path.Combine(_tempDir, "Output"), moviePreset: "Compressarr SD-HD");
        var config = new CompressarrConfig();

        var issues = LaneValidator.Validate(lane, config, "presets.json", new PassThroughPathExpander(), new FixedExtensionPresetService("Compressarr SD-HD"));

        Assert.Empty(issues);
    }

    [Fact]
    public void Validate_DisabledLane_NeverValidated_ReturnsNoIssuesEvenIfBroken()
    {
        var lane = MakeLane(input: "", output: "", enabled: false);
        var config = new CompressarrConfig();

        var issues = LaneValidator.Validate(lane, config, "presets.json", new PassThroughPathExpander(), new FixedExtensionPresetService());

        Assert.Empty(issues);
    }

    [Fact]
    public void Validate_MissingInputFolder_FlagsInputField()
    {
        var lane = MakeLane(input: Path.Combine(_tempDir, "DoesNotExist"), output: Path.Combine(_tempDir, "Output"), moviePreset: "Compressarr SD-HD");
        var config = new CompressarrConfig();

        var issues = LaneValidator.Validate(lane, config, "presets.json", new PassThroughPathExpander(), new FixedExtensionPresetService("Compressarr SD-HD"));

        Assert.Contains(issues, i => i.Field == "input");
    }

    [Fact]
    public void Validate_EmptyInput_FlagsInputField()
    {
        var lane = MakeLane(input: "", output: Path.Combine(_tempDir, "Output"), moviePreset: "Compressarr SD-HD");
        var config = new CompressarrConfig();

        var issues = LaneValidator.Validate(lane, config, "presets.json", new PassThroughPathExpander(), new FixedExtensionPresetService("Compressarr SD-HD"));

        Assert.Contains(issues, i => i.Field == "input");
    }

    [Fact]
    public void Validate_NoOutputAndOutSameAsInOff_FlagsOutputField()
    {
        Directory.CreateDirectory(Path.Combine(_tempDir, "Input"));
        var lane = MakeLane(Path.Combine(_tempDir, "Input"), output: "", moviePreset: "Compressarr SD-HD");
        var config = new CompressarrConfig { Processing = new ProcessingSettings { OutSameAsIn = false } };

        var issues = LaneValidator.Validate(lane, config, "presets.json", new PassThroughPathExpander(), new FixedExtensionPresetService("Compressarr SD-HD"));

        Assert.Contains(issues, i => i.Field == "output");
    }

    [Fact]
    public void Validate_NoOutputButOutSameAsInOn_DoesNotFlagOutputField()
    {
        Directory.CreateDirectory(Path.Combine(_tempDir, "Input"));
        var lane = MakeLane(Path.Combine(_tempDir, "Input"), output: "", moviePreset: "Compressarr SD-HD");
        var config = new CompressarrConfig { Processing = new ProcessingSettings { OutSameAsIn = true } };

        var issues = LaneValidator.Validate(lane, config, "presets.json", new PassThroughPathExpander(), new FixedExtensionPresetService("Compressarr SD-HD"));

        Assert.DoesNotContain(issues, i => i.Field == "output");
    }

    [Fact]
    public void Validate_NeitherPresetConfigured_FlagsBothPresetFields()
    {
        Directory.CreateDirectory(Path.Combine(_tempDir, "Input"));
        var lane = MakeLane(Path.Combine(_tempDir, "Input"), Path.Combine(_tempDir, "Output"));
        var config = new CompressarrConfig();

        var issues = LaneValidator.Validate(lane, config, "presets.json", new PassThroughPathExpander(), new FixedExtensionPresetService());

        Assert.Contains(issues, i => i.Field == "tvPreset");
        Assert.Contains(issues, i => i.Field == "moviePreset");
    }

    [Fact]
    public void Validate_TvPresetNotInPresetsJson_FlagsTvPresetFieldOnly()
    {
        Directory.CreateDirectory(Path.Combine(_tempDir, "Input"));
        var lane = MakeLane(Path.Combine(_tempDir, "Input"), Path.Combine(_tempDir, "Output"), tvPreset: "Ghost Preset");
        var config = new CompressarrConfig();

        var issues = LaneValidator.Validate(lane, config, "presets.json", new PassThroughPathExpander(), new FixedExtensionPresetService("Some Other Preset"));

        Assert.Contains(issues, i => i.Field == "tvPreset");
        Assert.DoesNotContain(issues, i => i.Field == "moviePreset");
    }

    [Fact]
    public void Validate_MoviePresetNotInPresetsJson_FlagsMoviePresetFieldOnly()
    {
        Directory.CreateDirectory(Path.Combine(_tempDir, "Input"));
        var lane = MakeLane(Path.Combine(_tempDir, "Input"), Path.Combine(_tempDir, "Output"), moviePreset: "Ghost Preset");
        var config = new CompressarrConfig();

        var issues = LaneValidator.Validate(lane, config, "presets.json", new PassThroughPathExpander(), new FixedExtensionPresetService("Some Other Preset"));

        Assert.Contains(issues, i => i.Field == "moviePreset");
        Assert.DoesNotContain(issues, i => i.Field == "tvPreset");
    }

    [Fact]
    public void Validate_MultipleSimultaneousProblems_AllAppear()
    {
        var lane = MakeLane(input: "", output: "");
        var config = new CompressarrConfig { Processing = new ProcessingSettings { OutSameAsIn = false } };

        var issues = LaneValidator.Validate(lane, config, "presets.json", new PassThroughPathExpander(), new FixedExtensionPresetService());

        Assert.Contains(issues, i => i.Field == "input");
        Assert.Contains(issues, i => i.Field == "output");
        Assert.Contains(issues, i => i.Field == "tvPreset");
        Assert.Contains(issues, i => i.Field == "moviePreset");
    }
}
