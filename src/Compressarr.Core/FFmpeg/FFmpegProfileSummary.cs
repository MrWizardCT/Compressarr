namespace Compressarr.Core.FFmpeg;

/// <summary>One-line descriptions of an ffmpeg profile for the Profiles list ("x265 10-bit, CRF 24, veryfast").</summary>
public static class FFmpegProfileSummary
{
    private static readonly Dictionary<string, string> Codecs = new()
    {
        ["libx264"] = "x264",
        ["libx265"] = "x265",
        ["libsvtav1"] = "SVT-AV1",
        ["h264_nvenc"] = "NVENC H.264",
        ["hevc_nvenc"] = "NVENC H.265",
        ["av1_nvenc"] = "NVENC AV1"
    };

    private static readonly Dictionary<string, string> AudioCodecs = new()
    {
        ["aac"] = "AAC", ["ac3"] = "AC3", ["eac3"] = "E-AC3", ["opus"] = "Opus", ["mp3"] = "MP3", ["flac"] = "FLAC"
    };

    public static string Video(FFmpegProfile p)
    {
        var parts = new List<string>();
        var name = Codecs.GetValueOrDefault(p.VideoCodec, p.VideoCodec);
        if (p.PixFmt.Contains("10", StringComparison.Ordinal)) name += " 10-bit";
        else if (p.PixFmt.Contains("12", StringComparison.Ordinal)) name += " 12-bit";
        parts.Add(name);

        if (p.QualityMode == "bitrate") parts.Add($"{p.VideoBitrateKbps} kbps");
        else parts.Add($"{(p.VideoCodec.EndsWith("_nvenc", StringComparison.Ordinal) ? "CQ" : "CRF")} {p.Quality:0.##}");

        if (!string.IsNullOrEmpty(p.Preset)) parts.Add(p.Preset.All(char.IsDigit) ? $"preset {p.Preset}" : p.Preset);
        if (!string.IsNullOrEmpty(p.Tune)) parts.Add($"tune {p.Tune}");
        return string.Join(", ", parts);
    }

    public static string Audio(FFmpegProfile p)
    {
        var encoded = $"{AudioCodecs.GetValueOrDefault(p.AudioCodec, p.AudioCodec)}{(p.AudioCodec == "flac" ? "" : $" {p.AudioBitrateKbps}k")}";
        var text = p.AudioMode == "passthru" ? $"pass-through, else {encoded}" : encoded;
        return text + (p.AudioTracks == "all" ? " (all matching tracks)" : " (first matching track)");
    }
}
