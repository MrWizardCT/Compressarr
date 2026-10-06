using System.Text.Json.Nodes;
using Compressarr.Core.Presets;

namespace Compressarr.Core.Tests.Presets;

public class HandBrakeProfileSummaryTests
{
    private static JsonObject Preset(string json) => JsonNode.Parse(json)!.AsObject();

    [Fact]
    public void Video_ConstantQuality_ShowsEncoderRfAndSpeed()
    {
        var p = Preset("""{"VideoEncoder":"x265_10bit","VideoQualityType":2,"VideoQualitySlider":24,"VideoPreset":"veryfast"}""");

        Assert.Equal("x265 10-bit, RF 24, veryfast", HandBrakeProfileSummary.Video(p));
    }

    [Fact]
    public void Video_NumericEncoderPreset_IsLabelledAsAPreset()
    {
        var p = Preset("""{"VideoEncoder":"svt_av1_10bit","VideoQualityType":2,"VideoQualitySlider":30,"VideoPreset":"4"}""");

        Assert.Equal("SVT-AV1 10-bit, RF 30, preset 4", HandBrakeProfileSummary.Video(p));
    }

    [Fact]
    public void Video_AverageBitrate_ShowsKbps()
    {
        var p = Preset("""{"VideoEncoder":"x264","VideoQualityType":1,"VideoAvgBitrate":2500}""");

        Assert.Equal("x264, 2500 kbps", HandBrakeProfileSummary.Video(p));
    }

    [Fact]
    public void Video_UnknownEncoder_ShowsItsRawName_AndNothingStoredShowsNothing()
    {
        Assert.Equal("some_new_encoder", HandBrakeProfileSummary.Video(Preset("""{"VideoEncoder":"some_new_encoder"}""")));
        Assert.Equal("", HandBrakeProfileSummary.Video(Preset("{}")));
    }

    [Fact]
    public void Audio_EncodedTrack_ShowsEncoderBitrateAndSelection()
    {
        var p = Preset("""{"AudioList":[{"AudioEncoder":"eac3","AudioBitrate":512}],"AudioTrackSelectionBehavior":"first"}""");

        Assert.Equal("E-AC3 512k (first matching track)", HandBrakeProfileSummary.Audio(p));
    }

    [Fact]
    public void Audio_Copy_DoesNotShowABitrate()
    {
        var p = Preset("""{"AudioList":[{"AudioEncoder":"copy","AudioBitrate":160}],"AudioTrackSelectionBehavior":"all"}""");

        Assert.Equal("pass-through (all matching tracks)", HandBrakeProfileSummary.Audio(p));
    }

    [Fact]
    public void Audio_NoAudioList_ShowsNothing()
    {
        Assert.Equal("", HandBrakeProfileSummary.Audio(Preset("{}")));
    }

    [Theory]
    [InlineData("av_mkv", "MKV")]
    [InlineData("av_mp4", "MP4")]
    [InlineData("av_webm", "")]
    [InlineData(null, "")]
    public void Container_FromFileFormat(string? format, string expected)
    {
        Assert.Equal(expected, HandBrakeProfileSummary.Container(format));
    }
}
