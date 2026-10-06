using Compressarr.Core.Config;

namespace Compressarr.Core.Tests.Validation;

file sealed class PassThroughPathExpander : IPathExpander
{
    public string Expand(string value) => value;
    public bool PathExists(string value) => Directory.Exists(value) || File.Exists(value);
}

public class EncoderValidatorTests : IDisposable
{
    private readonly string _tempDir = Directory.CreateTempSubdirectory("compressarr-encoder-validator-tests-").FullName;
    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    private CompressarrConfig MakeHealthyConfig()
    {
        var hbCli = Path.Combine(_tempDir, "HandBrakeCLI.exe");
        File.WriteAllText(hbCli, "");
        return new CompressarrConfig { HandBrake = new HandBrakeSettings { CliPath = hbCli } };
    }

    [Fact]
    public void Validate_HealthyConfig_ReturnsNoIssues()
    {
        Assert.Empty(EncoderValidator.Validate(MakeHealthyConfig(), new PassThroughPathExpander()));
    }

    [Fact]
    public void Validate_MissingHandBrakeCliPath_FlagsField()
    {
        var config = MakeHealthyConfig();
        config.HandBrake.CliPath = Path.Combine(_tempDir, "DoesNotExist.exe");

        var issues = EncoderValidator.Validate(config, new PassThroughPathExpander());

        Assert.Contains(issues, i => i.Field == "handBrakeCliPath");
    }

    [Fact]
    public void Validate_EmptyHandBrakeCliPath_FlagsField()
    {
        var config = MakeHealthyConfig();
        config.HandBrake.CliPath = "";

        var issues = EncoderValidator.Validate(config, new PassThroughPathExpander());

        Assert.Contains(issues, i => i.Field == "handBrakeCliPath");
    }

    // A malformed Extra CLI Options value (an unterminated quote) used to just silently
    // mis-tokenize at encode time with no warning anywhere (v2.1.4 review finding); surfaced here
    // the way every other configuration problem already is.
    [Fact]
    public void Validate_UnbalancedQuotesInHandBrakeOptions_FlagsField()
    {
        var config = MakeHealthyConfig();
        config.HandBrake.Options = "--custom-anamorphic \"16:9 --two-pass";

        var issues = EncoderValidator.Validate(config, new PassThroughPathExpander());

        Assert.Contains(issues, i => i.Field == "handBrakeOptions");
    }

    [Fact]
    public void Validate_BalancedQuotesInHandBrakeOptions_DoesNotFlag()
    {
        var config = MakeHealthyConfig();
        config.HandBrake.Options = "--custom-anamorphic \"16:9\" --two-pass";

        var issues = EncoderValidator.Validate(config, new PassThroughPathExpander());

        Assert.DoesNotContain(issues, i => i.Field == "handBrakeOptions");
    }

    [Fact]
    public void Validate_EmptyHandBrakeOptions_DoesNotFlag()
    {
        var config = MakeHealthyConfig();
        config.HandBrake.Options = "";

        var issues = EncoderValidator.Validate(config, new PassThroughPathExpander());

        Assert.DoesNotContain(issues, i => i.Field == "handBrakeOptions");
    }
}
