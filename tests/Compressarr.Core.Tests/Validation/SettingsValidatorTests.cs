using Compressarr.Core.Config;

namespace Compressarr.Core.Tests.Validation;

file sealed class PassThroughPathExpander : IPathExpander
{
    public string Expand(string value) => value;
    public bool PathExists(string value) => Directory.Exists(value) || File.Exists(value);
}

public class SettingsValidatorTests : IDisposable
{
    private readonly string _tempDir = Directory.CreateTempSubdirectory("compressarr-settings-validator-tests-").FullName;
    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    private CompressarrConfig MakeHealthyConfig()
    {
        var hbCli = Path.Combine(_tempDir, "HandBrakeCLI.exe");
        var presets = Path.Combine(_tempDir, "presets.json");
        File.WriteAllText(hbCli, "");
        File.WriteAllText(presets, "{}");

        return new CompressarrConfig
        {
            HandBrake = new HandBrakeSettings { CliPath = hbCli, PresetsPath = presets },
            FileBot = new FileBotSettings { Enabled = false },
            Processing = new ProcessingSettings { VidTypes = new() { "mkv" } }
        };
    }

    [Fact]
    public void Validate_HealthyConfig_ReturnsNoIssues()
    {
        var config = MakeHealthyConfig();

        var issues = SettingsValidator.Validate(config, new PassThroughPathExpander());

        Assert.Empty(issues);
    }

    [Fact]
    public void Validate_MissingHandBrakeCliPath_FlagsField()
    {
        var config = MakeHealthyConfig();
        config.HandBrake.CliPath = Path.Combine(_tempDir, "DoesNotExist.exe");

        var issues = SettingsValidator.Validate(config, new PassThroughPathExpander());

        Assert.Contains(issues, i => i.Field == "handBrakeCliPath");
    }

    [Fact]
    public void Validate_EmptyHandBrakeCliPath_FlagsField()
    {
        var config = MakeHealthyConfig();
        config.HandBrake.CliPath = "";

        var issues = SettingsValidator.Validate(config, new PassThroughPathExpander());

        Assert.Contains(issues, i => i.Field == "handBrakeCliPath");
    }

    [Fact]
    public void Validate_MissingPresetsPath_FlagsField()
    {
        var config = MakeHealthyConfig();
        config.HandBrake.PresetsPath = Path.Combine(_tempDir, "DoesNotExist.json");

        var issues = SettingsValidator.Validate(config, new PassThroughPathExpander());

        Assert.Contains(issues, i => i.Field == "presetsPath");
    }

    [Fact]
    public void Validate_FileBotDisabled_MissingFileBotPath_DoesNotFlag()
    {
        var config = MakeHealthyConfig();
        config.FileBot.Enabled = false;
        config.FileBot.CliPath = Path.Combine(_tempDir, "DoesNotExist.exe");

        var issues = SettingsValidator.Validate(config, new PassThroughPathExpander());

        Assert.DoesNotContain(issues, i => i.Field == "fileBotCliPath");
    }

    [Fact]
    public void Validate_FileBotEnabled_MissingFileBotPath_FlagsField()
    {
        var config = MakeHealthyConfig();
        config.FileBot.Enabled = true;
        config.FileBot.CliPath = Path.Combine(_tempDir, "DoesNotExist.exe");

        var issues = SettingsValidator.Validate(config, new PassThroughPathExpander());

        Assert.Contains(issues, i => i.Field == "fileBotCliPath");
    }

    [Fact]
    public void Validate_FileBotEnabled_ExistingFileBotPath_DoesNotFlag()
    {
        var config = MakeHealthyConfig();
        var fileBotPath = Path.Combine(_tempDir, "filebot.exe");
        File.WriteAllText(fileBotPath, "");
        config.FileBot.Enabled = true;
        config.FileBot.CliPath = fileBotPath;

        var issues = SettingsValidator.Validate(config, new PassThroughPathExpander());

        Assert.DoesNotContain(issues, i => i.Field == "fileBotCliPath");
    }

    [Fact]
    public void Validate_EmptyVidTypes_FlagsField()
    {
        var config = MakeHealthyConfig();
        config.Processing.VidTypes = new();

        var issues = SettingsValidator.Validate(config, new PassThroughPathExpander());

        Assert.Contains(issues, i => i.Field == "vidTypes");
    }

    [Fact]
    public void Validate_MultipleSimultaneousProblems_AllAppear()
    {
        var config = new CompressarrConfig
        {
            HandBrake = new HandBrakeSettings { CliPath = "", PresetsPath = "" },
            FileBot = new FileBotSettings { Enabled = true, CliPath = "" },
            Processing = new ProcessingSettings { VidTypes = new() }
        };

        var issues = SettingsValidator.Validate(config, new PassThroughPathExpander());

        Assert.Contains(issues, i => i.Field == "handBrakeCliPath");
        Assert.Contains(issues, i => i.Field == "presetsPath");
        Assert.Contains(issues, i => i.Field == "fileBotCliPath");
        Assert.Contains(issues, i => i.Field == "vidTypes");
    }
}
