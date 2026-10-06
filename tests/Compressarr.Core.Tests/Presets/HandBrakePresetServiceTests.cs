using System.Text.Json.Nodes;
using Compressarr.Core.Config;
using Compressarr.Core.Presets;

namespace Compressarr.Core.Tests.Presets;

public class HandBrakePresetServiceTests : AppDataTestBase
{
    private static JsonObject Profile(string name, string format) => new()
    {
        ["PresetName"] = name,
        ["FileFormat"] = format,
        ["Folder"] = false
    };

    private static (HandBrakePresetService Service, HandBrakeProfileStore Store) Make()
    {
        var store = new HandBrakeProfileStore();
        return (new HandBrakePresetService(store), store);
    }

    [Fact]
    public void GetPresetNames_IsBuiltInsPlusYours_SortedIgnoringCase()
    {
        var (service, store) = Make();
        store.AddUserProfiles(new[] { Profile("Alpha", "av_mkv"), Profile("zulu", "av_mp4") });

        Assert.Equal(
            new[] { "Alpha", "Compressarr SD-HD", "Compressarr UHD AV1", "zulu" },
            service.GetPresetNames().ToArray());
    }

    [Fact]
    public void PresetExists_KnownAndUnknownNames_IgnoringCase()
    {
        var (service, store) = Make();
        store.AddUserProfiles(new[] { Profile("Mine", "av_mkv") });

        Assert.True(service.PresetExists("Compressarr SD-HD"));
        Assert.True(service.PresetExists("compressarr sd-hd"));
        Assert.True(service.PresetExists("Mine"));
        Assert.False(service.PresetExists("Fast 1080p30")); // a HandBrake stock preset - not ours unless imported
        Assert.False(service.PresetExists("Does Not Exist"));
    }

    [Theory]
    [InlineData("Compressarr SD-HD", ".mkv")]
    [InlineData("Mp4One", ".mp4")]
    [InlineData("MkvOne", ".mkv")]
    public void GetOutputExtension_MapsFileFormatSubstring(string presetName, string expectedExtension)
    {
        var (service, store) = Make();
        store.AddUserProfiles(new[] { Profile("Mp4One", "av_mp4"), Profile("MkvOne", "av_mkv") });

        var extension = service.GetOutputExtension(presetName, out var warning);

        Assert.Equal(expectedExtension, extension);
        Assert.Null(warning);
    }

    [Fact]
    public void GetOutputExtension_UnknownPreset_DefaultsToMp4WithWarning()
    {
        var (service, _) = Make();

        var extension = service.GetOutputExtension("Nonexistent", out var warning);

        Assert.Equal(".mp4", extension);
        Assert.NotNull(warning);
    }

    [Fact]
    public void GetOutputExtension_UnrecognizedFileFormat_DefaultsToMp4WithWarning()
    {
        var (service, store) = Make();
        store.AddUserProfiles(new[] { Profile("Weird", "av_avi") });

        var extension = service.GetOutputExtension("Weird", out var warning);

        Assert.Equal(".mp4", extension);
        Assert.NotNull(warning);
    }

    [Fact]
    public void PreparePresetSource_ReturnsTheGeneratedFile_AndItExists()
    {
        var (service, _) = Make();

        var source = service.PreparePresetSource();

        Assert.Equal(AppPaths.GetHandBrakeActivePresetsFilePath(), source);
        Assert.True(File.Exists(source));
    }

    [Fact]
    public void PreparePresetSource_PicksUpAProfileChange()
    {
        var (service, store) = Make();
        var first = File.ReadAllText(service.PreparePresetSource());

        store.AddUserProfiles(new[] { Profile("Mine", "av_mkv") });

        Assert.DoesNotContain("Mine", first);
        Assert.Contains("Mine", File.ReadAllText(service.PreparePresetSource()));
    }
}
