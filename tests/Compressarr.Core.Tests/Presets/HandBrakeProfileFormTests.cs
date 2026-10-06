using System.Text.Json.Nodes;
using Compressarr.Core.Presets;

namespace Compressarr.Core.Tests.Presets;

public class HandBrakeProfileFormTests : AppDataTestBase
{
    private static HandBrakeProfile BuiltIn(string name) =>
        new HandBrakeProfileStore().GetBuiltIns().Single(p => p.Name == name);

    [Theory]
    [InlineData("Compressarr SD-HD")]
    [InlineData("Compressarr UHD AV1")]
    public void SavingABuiltInUnchanged_ChangesNothingAtAll(string name)
    {
        var definition = (JsonObject)BuiltIn(name).Definition.DeepClone();

        HandBrakeProfileForm.From(definition).ApplyTo(definition);

        // Same keys, same values: the editor must never rewrite what the user didn't touch.
        Assert.True(JsonNode.DeepEquals(BuiltIn(name).Definition, definition));
    }

    [Fact]
    public void From_ReadsTheBuiltInSdHd()
    {
        var f = HandBrakeProfileForm.From(BuiltIn("Compressarr SD-HD").Definition);

        Assert.Equal("av_mkv", f.FileFormat);
        Assert.Equal("x265_10bit", f.VideoEncoder);
        Assert.Equal("rf", f.QualityMode);
        Assert.Equal(24, f.Rf);
        Assert.Equal("veryfast", f.VideoPreset);
        Assert.Equal("main10", f.VideoProfile);
        Assert.Equal("decomb", f.Deinterlace);
        Assert.Equal("eng, und, any", f.AudioLanguages);
        Assert.Equal("first", f.AudioTracks);
        Assert.Equal("encode", f.AudioMode);       // AudioList[0] is eac3, not "copy"
        Assert.Equal("eac3", f.AudioEncoder);
        Assert.Equal(512, f.AudioBitrate);
        Assert.Equal("7point1", f.AudioMixdown);
        Assert.Equal("eng", f.SubtitleLanguages);
        Assert.Equal("all", f.SubtitleTracks);
        Assert.True(f.ChapterMarkers);
    }

    [Fact]
    public void From_ReadsAPassThroughProfile()
    {
        var f = HandBrakeProfileForm.From(BuiltIn("Compressarr UHD AV1").Definition);

        Assert.Equal("passthru", f.AudioMode);
        Assert.Equal("eac3", f.AudioEncoder); // the fallback used when a track can't be copied
        Assert.Contains("truehd", f.PassthroughCodecs);
        Assert.DoesNotContain(f.PassthroughCodecs, c => c.StartsWith("copy:"));
        Assert.Equal("all", f.AudioTracks);
    }

    [Fact]
    public void ApplyTo_ChangesOnlyWhatTheFormOwns_AndKeepsTheRest()
    {
        var definition = (JsonObject)BuiltIn("Compressarr SD-HD").Definition.DeepClone();
        definition["SomethingNewerHandBrakeAdds"] = "keep me";
        definition["PictureSharpenFilter"] = "unsharp";
        ((JsonObject)((JsonArray)definition["AudioList"]!)[0]!)["AudioTrackGainSlider"] = 3;
        var before = (JsonObject)definition.DeepClone();

        var form = HandBrakeProfileForm.From(definition);
        form.Name = "Mine";
        form.Rf = 20;
        form.VideoPreset = "slow";
        form.ApplyTo(definition);

        Assert.Equal("Mine", definition["PresetName"]!.GetValue<string>());
        Assert.Equal(20, definition["VideoQualitySlider"]!.GetValue<double>());
        Assert.Equal("slow", definition["VideoPreset"]!.GetValue<string>());
        Assert.Equal("keep me", definition["SomethingNewerHandBrakeAdds"]!.GetValue<string>());
        Assert.Equal("unsharp", definition["PictureSharpenFilter"]!.GetValue<string>());
        Assert.Equal(3, ((JsonObject)((JsonArray)definition["AudioList"]!)[0]!)["AudioTrackGainSlider"]!.GetValue<int>());

        // every key that is not one the form owns is still byte-for-byte what it was
        foreach (var (key, value) in before)
        {
            if (key is "PresetName" or "VideoQualitySlider" or "VideoPreset") continue;
            Assert.True(JsonNode.DeepEquals(value, definition[key]), $"{key} changed");
        }
    }

    [Fact]
    public void ApplyTo_BitrateMode_SetsAnAverageBitrateTarget()
    {
        var definition = (JsonObject)BuiltIn("Compressarr SD-HD").Definition.DeepClone();
        var form = HandBrakeProfileForm.From(definition);
        form.QualityMode = "bitrate";
        form.VideoBitrate = 2500;

        form.ApplyTo(definition);

        Assert.Equal(1, definition["VideoQualityType"]!.GetValue<int>());
        Assert.Equal(2500, definition["VideoAvgBitrate"]!.GetValue<int>());
        var again = HandBrakeProfileForm.From(definition);
        Assert.Equal("bitrate", again.QualityMode);
        Assert.Equal(2500, again.VideoBitrate);
    }

    [Fact]
    public void ApplyTo_Framerate_VariableIsAuto_ConstantKeepsTheRate()
    {
        var definition = (JsonObject)BuiltIn("Compressarr SD-HD").Definition.DeepClone();
        var form = HandBrakeProfileForm.From(definition);

        form.FramerateMode = "cfr";
        form.Framerate = "24";
        form.ApplyTo(definition);
        Assert.Equal("cfr", definition["VideoFramerateMode"]!.GetValue<string>());
        Assert.Equal("24", definition["VideoFramerate"]!.GetValue<string>());

        form.FramerateMode = "vfr";
        form.ApplyTo(definition);
        Assert.Equal("auto", definition["VideoFramerate"]!.GetValue<string>());
    }

    [Fact]
    public void ApplyTo_SwitchingBetweenPassThroughAndEncode_WritesTheRightKeys()
    {
        var definition = (JsonObject)BuiltIn("Compressarr SD-HD").Definition.DeepClone();
        var form = HandBrakeProfileForm.From(definition);

        form.AudioMode = "passthru";
        form.PassthroughCodecs = new() { "aac", "ac3" };
        form.AudioEncoder = "opus";
        form.ApplyTo(definition);

        var first = (JsonObject)((JsonArray)definition["AudioList"]!)[0]!;
        Assert.Equal("copy", first["AudioEncoder"]!.GetValue<string>());
        Assert.Equal("opus", definition["AudioEncoderFallback"]!.GetValue<string>());
        Assert.Equal(new[] { "copy:aac", "copy:ac3" }, definition["AudioCopyMask"]!.AsArray().Select(n => n!.GetValue<string>()).ToArray());

        form.AudioMode = "encode";
        form.AudioEncoder = "av_aac";
        form.ApplyTo(definition);
        Assert.Equal("av_aac", ((JsonObject)((JsonArray)definition["AudioList"]!)[0]!)["AudioEncoder"]!.GetValue<string>());
    }

    [Fact]
    public void ApplyTo_APresetWithNoAudioList_GetsOne()
    {
        var definition = new JsonObject { ["PresetName"] = "Bare" };
        var form = new HandBrakeProfileForm { Name = "Bare", AudioMode = "encode", AudioEncoder = "ac3", AudioBitrate = 384, AudioMixdown = "5point1" };

        form.ApplyTo(definition);

        var first = (JsonObject)((JsonArray)definition["AudioList"]!)[0]!;
        Assert.Equal("ac3", first["AudioEncoder"]!.GetValue<string>());
        Assert.Equal(384, first["AudioBitrate"]!.GetValue<int>());
    }

    [Fact]
    public void ApplyTo_LanguageLists_AreSplitTrimmedAndDeduplicated()
    {
        var definition = new JsonObject();
        var form = new HandBrakeProfileForm { Name = "X", AudioLanguages = "eng, und ,ENG;jpn", SubtitleLanguages = "eng" };

        form.ApplyTo(definition);

        Assert.Equal(new[] { "eng", "und", "jpn" }, definition["AudioLanguageList"]!.AsArray().Select(n => n!.GetValue<string>()).ToArray());
    }

    // ---- validation ----------------------------------------------------------------------------

    private static HandBrakeProfileForm Valid() => HandBrakeProfileForm.From(BuiltIn("Compressarr SD-HD").Definition);

    [Fact]
    public void Validate_AValidForm_HasNoIssues() => Assert.Empty(Valid().Validate());

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData("say \"hi\"")]
    public void Validate_BadNames_AreFlagged(string name)
    {
        var f = Valid();
        f.Name = name;

        Assert.Contains(f.Validate(), i => i.Field == "name");
    }

    [Fact]
    public void Validate_FlagsTheOtherFieldsToo()
    {
        var f = Valid();
        f.QualityMode = "bitrate"; f.VideoBitrate = 0;
        f.FramerateMode = "cfr"; f.Framerate = "";
        f.AudioLanguages = " ";
        f.AudioMode = "passthru"; f.PassthroughCodecs = new();
        f.AudioBitrate = 99999;
        f.SubtitleTracks = "all"; f.SubtitleLanguages = "";

        var fields = f.Validate().Select(i => i.Field).ToList();

        Assert.Contains("videoBitrate", fields);
        Assert.Contains("framerate", fields);
        Assert.Contains("audioLanguages", fields);
        Assert.Contains("passthrough", fields);
        Assert.Contains("audioBitrate", fields);
        Assert.Contains("subtitleLanguages", fields);
    }

    [Fact]
    public void Validate_NoSubtitles_NeedsNoLanguage()
    {
        var f = Valid();
        f.SubtitleTracks = "none";
        f.SubtitleLanguages = "";

        Assert.Empty(f.Validate());
    }

    [Fact]
    public void Validate_RfOutOfRange_IsFlagged()
    {
        var f = Valid();
        f.Rf = 99;

        Assert.Contains(f.Validate(), i => i.Field == "rf");
    }
}
