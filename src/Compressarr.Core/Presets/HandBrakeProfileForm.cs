using System.Text.Json.Nodes;
using Compressarr.Core.Validation;

namespace Compressarr.Core.Presets;

/// <summary>
/// The part of a HandBrake preset the Profiles editor shows and changes, as a flat form. It is a
/// view over the stored preset, not a replacement for it: <see cref="ApplyTo"/> only sets the keys
/// listed here and leaves every other key (extra picture filters, per-track audio settings,
/// anything a newer HandBrake adds) exactly as it was, so editing in Compressarr never loses
/// something the HandBrake app stored. Value sets were checked against HandBrakeCLI itself
/// (--encoder-*-list, and --preset-export after applying each option).
/// </summary>
public sealed class HandBrakeProfileForm
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string FileFormat { get; set; } = "av_mkv";
    public bool Optimize { get; set; }

    public string VideoEncoder { get; set; } = "x265_10bit";
    public string VideoPreset { get; set; } = "";
    public string VideoProfile { get; set; } = "auto";
    public string VideoLevel { get; set; } = "auto";
    public string VideoTune { get; set; } = "";
    public string ExtraVideoOptions { get; set; } = "";

    /// <summary>"rf" (constant quality) or "bitrate" (average bitrate).</summary>
    public string QualityMode { get; set; } = "rf";
    public double Rf { get; set; } = 24;
    public int VideoBitrate { get; set; }

    /// <summary>"vfr" (same as source), "cfr" (constant) or "pfr" (peak).</summary>
    public string FramerateMode { get; set; } = "vfr";
    public string Framerate { get; set; } = "";

    /// <summary>"off", "decomb", "deinterlace" (yadif) or "bwdif".</summary>
    public string Deinterlace { get; set; } = "off";

    /// <summary>HandBrake's PictureCropMode: 0 automatic, 2 none (other values are kept as they are).</summary>
    public int CropMode { get; set; }

    public string AudioLanguages { get; set; } = "eng";

    /// <summary>"first" or "all" matching tracks.</summary>
    public string AudioTracks { get; set; } = "first";

    /// <summary>"passthru" (copy a track untouched when its format is ticked, otherwise encode it) or
    /// "encode" (always encode).</summary>
    public string AudioMode { get; set; } = "encode";
    public List<string> PassthroughCodecs { get; set; } = new();
    public string AudioEncoder { get; set; } = "av_aac";
    public int AudioBitrate { get; set; } = 160;
    public string AudioMixdown { get; set; } = "stereo";

    public string SubtitleLanguages { get; set; } = "";

    /// <summary>"none", "first" or "all" matching tracks.</summary>
    public string SubtitleTracks { get; set; } = "none";
    public bool ChapterMarkers { get; set; } = true;

    public static HandBrakeProfileForm From(JsonObject p)
    {
        var form = new HandBrakeProfileForm
        {
            Name = Str(p, "PresetName") ?? "",
            Description = Str(p, "PresetDescription") ?? "",
            FileFormat = Str(p, "FileFormat") ?? "av_mkv",
            Optimize = Bool(p, "Optimize"),
            VideoEncoder = Str(p, "VideoEncoder") ?? "x265_10bit",
            VideoPreset = Str(p, "VideoPreset") ?? "",
            VideoProfile = Str(p, "VideoProfile") ?? "auto",
            VideoLevel = Str(p, "VideoLevel") ?? "auto",
            VideoTune = Str(p, "VideoTune") ?? "",
            ExtraVideoOptions = Str(p, "VideoOptionExtra") ?? "",
            QualityMode = Num(p, "VideoQualityType") == 1 ? "bitrate" : "rf",
            Rf = Num(p, "VideoQualitySlider") ?? 24,
            VideoBitrate = (int)(Num(p, "VideoAvgBitrate") ?? 0),
            FramerateMode = Str(p, "VideoFramerateMode") ?? "vfr",
            Framerate = Str(p, "VideoFramerate") is { } fr && fr != "auto" ? fr : "",
            Deinterlace = Str(p, "PictureDeinterlaceFilter") ?? "off",
            CropMode = (int)(Num(p, "PictureCropMode") ?? 0),
            AudioLanguages = string.Join(", ", Strings(p, "AudioLanguageList")),
            AudioTracks = Str(p, "AudioTrackSelectionBehavior") ?? "first",
            SubtitleLanguages = string.Join(", ", Strings(p, "SubtitleLanguageList")),
            SubtitleTracks = Str(p, "SubtitleTrackSelectionBehavior") ?? "none",
            ChapterMarkers = Bool(p, "ChapterMarkers")
        };

        var first = p["AudioList"] is JsonArray list && list.Count > 0 ? list[0] as JsonObject : null;
        var encoder = first is null ? null : Str(first, "AudioEncoder");
        if (encoder == "copy")
        {
            form.AudioMode = "passthru";
            form.AudioEncoder = Str(p, "AudioEncoderFallback") ?? "av_aac";
            form.PassthroughCodecs = Strings(p, "AudioCopyMask").Select(m => m.StartsWith("copy:") ? m[5..] : m).ToList();
        }
        else
        {
            form.AudioMode = "encode";
            form.AudioEncoder = encoder ?? Str(p, "AudioEncoderFallback") ?? "av_aac";
            // The mask only matters in pass-through mode, but keep it so switching modes in the editor
            // starts from what the profile already had.
            form.PassthroughCodecs = Strings(p, "AudioCopyMask").Select(m => m.StartsWith("copy:") ? m[5..] : m).ToList();
        }
        if (first is not null)
        {
            form.AudioBitrate = (int)(Num(first, "AudioBitrate") ?? 160);
            form.AudioMixdown = Str(first, "AudioMixdown") ?? "stereo";
        }

        return form;
    }

    /// <summary>Writes the form's values onto <paramref name="p"/> (a preset leaf), touching only the
    /// keys this form owns - and only the ones whose value actually changed, so saving without
    /// changing anything leaves the stored preset byte-for-byte as it was (a key the preset never had
    /// is not invented just because the form shows a default for it).</summary>
    public void ApplyTo(JsonObject p)
    {
        var was = From(p);

        void Set(string key, JsonNode? value, bool changed)
        {
            if (changed) p[key] = value;
        }

        Set("PresetName", Name.Trim(), Name.Trim() != was.Name);
        Set("PresetDescription", Description, Description != was.Description);
        Set("FileFormat", FileFormat, FileFormat != was.FileFormat);
        Set("Optimize", Optimize, Optimize != was.Optimize);

        Set("VideoEncoder", VideoEncoder, VideoEncoder != was.VideoEncoder);
        Set("VideoPreset", VideoPreset, VideoPreset != was.VideoPreset);
        Set("VideoProfile", VideoProfile, VideoProfile != was.VideoProfile);
        Set("VideoLevel", VideoLevel, VideoLevel != was.VideoLevel);
        Set("VideoTune", VideoTune, VideoTune != was.VideoTune);
        Set("VideoOptionExtra", ExtraVideoOptions, ExtraVideoOptions != was.ExtraVideoOptions);

        if (QualityMode != was.QualityMode)
        {
            p["VideoQualityType"] = QualityMode == "bitrate" ? 1 : 2;
        }
        if (QualityMode == "bitrate")
        {
            Set("VideoAvgBitrate", VideoBitrate, VideoBitrate != was.VideoBitrate);
        }
        else
        {
            Set("VideoQualitySlider", Rf, Rf != was.Rf);
        }

        Set("VideoFramerateMode", FramerateMode, FramerateMode != was.FramerateMode);
        var rate = FramerateMode == "vfr" || string.IsNullOrWhiteSpace(Framerate) ? "" : Framerate;
        Set("VideoFramerate", rate == "" ? "auto" : rate, rate != was.Framerate);

        Set("PictureDeinterlaceFilter", Deinterlace, Deinterlace != was.Deinterlace);
        Set("PictureCropMode", CropMode, CropMode != was.CropMode);

        var audioLanguages = SplitList(AudioLanguages);
        Set("AudioLanguageList", ToArray(audioLanguages), !audioLanguages.SequenceEqual(SplitList(was.AudioLanguages)));
        Set("AudioTrackSelectionBehavior", AudioTracks, AudioTracks != was.AudioTracks);

        var audioChanged = AudioMode != was.AudioMode || AudioEncoder != was.AudioEncoder
            || AudioBitrate != was.AudioBitrate || AudioMixdown != was.AudioMixdown
            || (AudioMode == "passthru" && !PassthroughCodecs.SequenceEqual(was.PassthroughCodecs));
        if (audioChanged)
        {
            if (p["AudioList"] is not JsonArray list)
            {
                list = new JsonArray();
                p["AudioList"] = list;
            }
            if (list.Count == 0 || list[0] is not JsonObject first)
            {
                first = new JsonObject
                {
                    ["AudioCompressionLevel"] = 0,
                    ["AudioNormalizeMixLevel"] = false,
                    ["AudioSamplerate"] = "auto",
                    ["AudioTrackQualityEnable"] = false,
                    ["AudioTrackQuality"] = -1,
                    ["AudioTrackGainSlider"] = 0,
                    ["AudioTrackDRCSlider"] = 0
                };
                if (list.Count == 0) list.Add(first); else list[0] = first;
            }

            if (AudioMode == "passthru")
            {
                first["AudioEncoder"] = "copy";
                p["AudioEncoderFallback"] = AudioEncoder;
                p["AudioCopyMask"] = ToArray(PassthroughCodecs.Select(c => "copy:" + c));
            }
            else
            {
                first["AudioEncoder"] = AudioEncoder;
            }
            first["AudioBitrate"] = AudioBitrate;
            first["AudioMixdown"] = AudioMixdown;
        }

        var subtitleLanguages = SplitList(SubtitleLanguages);
        Set("SubtitleLanguageList", ToArray(subtitleLanguages), !subtitleLanguages.SequenceEqual(SplitList(was.SubtitleLanguages)));
        Set("SubtitleTrackSelectionBehavior", SubtitleTracks, SubtitleTracks != was.SubtitleTracks);
        Set("ChapterMarkers", ChapterMarkers, ChapterMarkers != was.ChapterMarkers);
    }

    /// <summary>Everything wrong with the form itself (name uniqueness is checked against the store by
    /// the caller). Field names match the editor's input ids.</summary>
    public List<ValidationIssue> Validate()
    {
        var issues = new List<ValidationIssue>();

        var name = Name?.Trim() ?? "";
        if (name.Length == 0)
        {
            issues.Add(new ValidationIssue("name", "A profile needs a name."));
        }
        else if (name.IndexOfAny(new[] { '/', '\\', '"' }) >= 0 || name.Any(char.IsControl))
        {
            // HandBrake reads "/" in --preset as a folder separator, and the name is passed on the
            // command line in quotes.
            issues.Add(new ValidationIssue("name", "A name can't contain / \\ or quotation marks."));
        }

        if (string.IsNullOrWhiteSpace(VideoEncoder)) issues.Add(new ValidationIssue("videoEncoder", "Choose a video encoder."));

        if (QualityMode == "bitrate")
        {
            if (VideoBitrate <= 0) issues.Add(new ValidationIssue("videoBitrate", "The video bitrate must be above 0 kbps."));
        }
        else if (Rf < 0 || Rf > 70)
        {
            issues.Add(new ValidationIssue("rf", "Constant quality (RF) must be between 0 and 70."));
        }

        if (FramerateMode is "cfr" or "pfr" && !double.TryParse(Framerate, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var fps))
        {
            issues.Add(new ValidationIssue("framerate", "Choose a frame rate for constant/peak frame rate."));
        }

        if (SplitList(AudioLanguages).Count == 0)
        {
            issues.Add(new ValidationIssue("audioLanguages", "List at least one audio language (e.g. eng), or 'any'."));
        }

        if (AudioMode == "passthru" && PassthroughCodecs.Count == 0)
        {
            issues.Add(new ValidationIssue("passthrough", "Tick at least one format to pass through, or choose 'Always encode'."));
        }

        if (string.IsNullOrWhiteSpace(AudioEncoder)) issues.Add(new ValidationIssue("audioEncoder", "Choose an audio encoder."));

        if (AudioBitrate < 0 || AudioBitrate > 3000) issues.Add(new ValidationIssue("audioBitrate", "The audio bitrate must be between 0 and 3000 kbps."));

        if (SubtitleTracks != "none" && SplitList(SubtitleLanguages).Count == 0)
        {
            issues.Add(new ValidationIssue("subtitleLanguages", "List at least one subtitle language, or set Keep to 'None'."));
        }

        return issues;
    }

    private static List<string> SplitList(string value) =>
        value.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    private static JsonArray ToArray(IEnumerable<string> values) => new(values.Select(v => (JsonNode)v).ToArray());

    private static string? Str(JsonObject o, string key) =>
        o[key] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    private static double? Num(JsonObject o, string key)
    {
        if (o[key] is not JsonValue v) return null;
        if (v.TryGetValue<double>(out var d)) return d;
        if (v.TryGetValue<int>(out var i)) return i;
        if (v.TryGetValue<long>(out var l)) return l;
        return null;
    }

    private static bool Bool(JsonObject o, string key) =>
        o[key] is JsonValue v && v.TryGetValue<bool>(out var b) && b;

    private static List<string> Strings(JsonObject o, string key) =>
        o[key] is JsonArray a ? a.Select(n => n is JsonValue v && v.TryGetValue<string>(out var s) ? s : null).OfType<string>().ToList() : new List<string>();
}
