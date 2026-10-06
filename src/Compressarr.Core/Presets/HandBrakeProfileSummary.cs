using System.Text.Json.Nodes;

namespace Compressarr.Core.Presets;

/// <summary>One-line, human-readable descriptions of a HandBrake profile for the Profiles list
/// ("x265 10-bit, RF 24, veryfast" / "E-AC3 512k"). Read-only and best-effort: a profile that
/// stores something unexpected just gets a shorter description, never an error.</summary>
public static class HandBrakeProfileSummary
{
    private static readonly Dictionary<string, string> VideoEncoders = new(StringComparer.OrdinalIgnoreCase)
    {
        ["x264"] = "x264",
        ["x264_10bit"] = "x264 10-bit",
        ["x265"] = "x265",
        ["x265_10bit"] = "x265 10-bit",
        ["x265_12bit"] = "x265 12-bit",
        ["svt_av1"] = "SVT-AV1",
        ["svt_av1_10bit"] = "SVT-AV1 10-bit",
        ["nvenc_h264"] = "NVENC H.264",
        ["nvenc_h265"] = "NVENC H.265",
        ["nvenc_h265_10bit"] = "NVENC H.265 10-bit",
        ["nvenc_av1"] = "NVENC AV1",
        ["qsv_h264"] = "QSV H.264",
        ["qsv_h265"] = "QSV H.265",
        ["qsv_h265_10bit"] = "QSV H.265 10-bit",
        ["qsv_av1"] = "QSV AV1",
        ["vce_h264"] = "AMF H.264",
        ["vce_h265"] = "AMF H.265",
        ["mpeg4"] = "MPEG-4",
        ["mpeg2"] = "MPEG-2",
        ["VP8"] = "VP8",
        ["VP9"] = "VP9"
    };

    private static readonly Dictionary<string, string> AudioEncoders = new(StringComparer.OrdinalIgnoreCase)
    {
        ["av_aac"] = "AAC",
        ["aac"] = "AAC",
        ["ac3"] = "AC3",
        ["eac3"] = "E-AC3",
        ["opus"] = "Opus",
        ["mp3"] = "MP3",
        ["flac16"] = "FLAC 16-bit",
        ["flac24"] = "FLAC 24-bit",
        ["vorbis"] = "Vorbis",
        ["copy"] = "pass-through"
    };

    public static string Video(JsonObject preset)
    {
        var encoder = Str(preset, "VideoEncoder");
        if (encoder is null) return "";

        var parts = new List<string> { VideoEncoders.TryGetValue(encoder, out var friendly) ? friendly : encoder };

        // VideoQualityType 2 is constant quality (RF); 1 is an average bitrate target.
        if (Int(preset, "VideoQualityType") == 2 && Number(preset, "VideoQualitySlider") is { } rf)
        {
            parts.Add($"RF {rf:0.##}");
        }
        else if (Int(preset, "VideoAvgBitrate") is > 0 and var kbps)
        {
            parts.Add($"{kbps} kbps");
        }

        var speed = Str(preset, "VideoPreset");
        if (!string.IsNullOrEmpty(speed)) parts.Add(speed.All(char.IsDigit) ? $"preset {speed}" : speed); // SVT-AV1 speeds are bare numbers

        return string.Join(", ", parts);
    }

    public static string Audio(JsonObject preset)
    {
        if (preset["AudioList"] is not JsonArray list || list.Count == 0 || list[0] is not JsonObject first) return "";

        var encoder = Str(first, "AudioEncoder");
        if (encoder is null) return "";

        var text = AudioEncoders.TryGetValue(encoder, out var friendly) ? friendly : encoder;
        if (Int(first, "AudioBitrate") is > 0 and var kbps && !encoder.StartsWith("copy", StringComparison.OrdinalIgnoreCase))
        {
            text += $" {kbps}k";
        }

        var behavior = Str(preset, "AudioTrackSelectionBehavior");
        if (behavior == "first") text += " (first matching track)";
        else if (behavior == "all") text += " (all matching tracks)";

        return text;
    }

    /// <summary>"MKV" / "MP4" from the profile's FileFormat, or "" when it is neither.</summary>
    public static string Container(string? fileFormat)
    {
        if (fileFormat is null) return "";
        if (fileFormat.Contains("mkv", StringComparison.OrdinalIgnoreCase)) return "MKV";
        if (fileFormat.Contains("mp4", StringComparison.OrdinalIgnoreCase)) return "MP4";
        return "";
    }

    private static string? Str(JsonObject o, string key) =>
        o[key] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    private static double? Number(JsonObject o, string key)
    {
        if (o[key] is not JsonValue v) return null;
        if (v.TryGetValue<double>(out var d)) return d;
        if (v.TryGetValue<int>(out var i)) return i;
        if (v.TryGetValue<long>(out var l)) return l;
        return null;
    }

    private static int? Int(JsonObject o, string key) =>
        Number(o, key) is { } d ? (int)d : null;
}
