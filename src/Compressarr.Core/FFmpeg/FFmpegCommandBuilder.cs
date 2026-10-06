using System.Globalization;
using Compressarr.Core.Conversion;

namespace Compressarr.Core.FFmpeg;

/// <summary>
/// Compiles a profile, a probed source and the plan made from them into the exact ffmpeg argument
/// list. A pure function - the one thing that must never change by accident is the command line,
/// so it is pinned by golden tests (and shown to the user as the editor's "Command that will run").
///
/// Every kept stream is mapped explicitly by its index; there is no "-map 0", so cover art, data
/// streams, extra video streams and unwanted languages can never sneak into the output.
/// </summary>
public static class FFmpegCommandBuilder
{
    /// <param name="extraOptions">The global "Extra ffmpeg options" setting, or null.</param>
    public static IReadOnlyList<string> Build(FFmpegProfile profile, MediaProbeResult probe, FFmpegPlan plan, string sourcePath, string outputPath, string? extraOptions)
    {
        var args = new List<string>
        {
            "-hide_banner", "-nostdin", "-y",
            "-progress", "pipe:1", "-nostats",
            "-i", sourcePath
        };

        // ---- which streams ----
        args.AddRange(new[] { "-map", $"0:{plan.Video.Index}" });
        foreach (var a in plan.Audio) args.AddRange(new[] { "-map", $"0:{a.Stream.Index}" });
        foreach (var s in plan.Subtitles) args.AddRange(new[] { "-map", $"0:{s.Stream.Index}" });
        foreach (var t in plan.Attachments) args.AddRange(new[] { "-map", $"0:{t.Index}" });
        args.AddRange(new[] { "-map_chapters", plan.KeepChapters ? "0" : "-1" });
        args.AddRange(new[] { "-map_metadata", "0" });

        // ---- video ----
        AddVideo(args, profile, plan);

        var filters = new List<string>();
        if (plan.Deinterlace)
        {
            filters.Add($"bwdif=mode=send_frame:parity=auto:deint={(plan.DeinterlaceAll ? "all" : "interlaced")}");
        }
        if (plan.Crop is { } crop) filters.Add($"crop={crop}");
        if (filters.Count > 0) args.AddRange(new[] { "-vf", string.Join(",", filters) });

        if (profile.FramerateMode == "cfr" && profile.Framerate > 0)
        {
            args.AddRange(new[] { "-fps_mode", "cfr", "-r", profile.Framerate.ToString("0.###", CultureInfo.InvariantCulture) });
        }
        else
        {
            args.AddRange(new[] { "-fps_mode", "vfr" });
        }

        // ---- audio ----
        for (var i = 0; i < plan.Audio.Count; i++)
        {
            var d = plan.Audio[i];
            if (d.Action == "copy")
            {
                args.AddRange(new[] { $"-c:a:{i}", "copy" });
                continue;
            }

            args.AddRange(new[] { $"-c:a:{i}", AudioEncoderName(d.Codec ?? "aac") });
            if (d.BitrateKbps is > 0 && !string.Equals(d.Codec, "flac", StringComparison.OrdinalIgnoreCase))
            {
                args.AddRange(new[] { $"-b:a:{i}", $"{d.BitrateKbps}k" });
            }
            if (d.Channels is { } ch) args.AddRange(new[] { $"-ac:a:{i}", ch.ToString(CultureInfo.InvariantCulture) });
        }

        // ---- subtitles ----
        for (var i = 0; i < plan.Subtitles.Count; i++)
        {
            args.AddRange(new[] { $"-c:s:{i}", plan.Subtitles[i].Action == "copy" ? "copy" : plan.Subtitles[i].Codec ?? "copy" });
        }
        if (plan.Attachments.Count > 0) args.AddRange(new[] { "-c:t", "copy" });

        // ---- container ----
        if (profile.Extension == ".mp4")
        {
            if (profile.Optimize) args.AddRange(new[] { "-movflags", "+faststart" });
            if (profile.VideoCodec is "libx265" or "hevc_nvenc") args.AddRange(new[] { "-tag:v", "hvc1" });
        }

        // ---- the user's own extra arguments ----
        if (!string.IsNullOrWhiteSpace(profile.ExtraArgs)) args.AddRange(HandBrakeProcessRunner.SplitExtraOptions(profile.ExtraArgs));
        if (!string.IsNullOrWhiteSpace(extraOptions)) args.AddRange(HandBrakeProcessRunner.SplitExtraOptions(extraOptions));

        args.Add(outputPath);
        return args;
    }

    private static void AddVideo(List<string> args, FFmpegProfile profile, FFmpegPlan plan)
    {
        var codec = profile.VideoCodec;
        var bitrateMode = profile.QualityMode == "bitrate" && profile.VideoBitrateKbps > 0;
        var quality = profile.Quality.ToString("0.##", CultureInfo.InvariantCulture);
        var video = plan.Video;

        args.AddRange(new[] { "-c:v", codec });
        if (!string.IsNullOrWhiteSpace(profile.Preset)) args.AddRange(new[] { "-preset", profile.Preset });

        var nvenc = codec.EndsWith("_nvenc", StringComparison.Ordinal);
        if (nvenc)
        {
            // NVENC has no CRF: constant quality is variable-bitrate rate control with a CQ target.
            if (bitrateMode) args.AddRange(new[] { "-rc", "vbr", "-b:v", $"{profile.VideoBitrateKbps}k" });
            else args.AddRange(new[] { "-rc", "vbr", "-cq", quality, "-b:v", "0" });
        }
        else if (bitrateMode)
        {
            args.AddRange(new[] { "-b:v", $"{profile.VideoBitrateKbps}k" });
        }
        else
        {
            args.AddRange(new[] { "-crf", quality });
        }

        if (!string.IsNullOrWhiteSpace(profile.VideoProfile)) args.AddRange(new[] { "-profile:v", profile.VideoProfile });
        if (!string.IsNullOrWhiteSpace(profile.PixFmt)) args.AddRange(new[] { "-pix_fmt", profile.PixFmt });

        // Tune: x264/x265 and NVENC take a plain -tune; SVT-AV1 takes it inside -svtav1-params.
        if (!string.IsNullOrWhiteSpace(profile.Tune) && codec != "libsvtav1") args.AddRange(new[] { "-tune", profile.Tune });

        // Colour information is carried over unchanged (HDR10 depends on it).
        AddIfKnown(args, "-color_primaries", video.ColorPrimaries);
        AddIfKnown(args, "-color_trc", video.ColorTransfer);
        AddIfKnown(args, "-colorspace", video.ColorSpace);
        AddIfKnown(args, "-color_range", video.ColorRange);

        if (codec == "libx265")
        {
            var parameters = new List<string>();
            if (video.Mastering is { } m) parameters.Add($"master-display={X265MasterDisplay(m)}");
            if (video.ContentLight is { } l && (l.MaxContent > 0 || l.MaxAverage > 0)) parameters.Add($"max-cll={l.MaxContent},{l.MaxAverage}");
            if (parameters.Count > 0) args.AddRange(new[] { "-x265-params", string.Join(":", parameters) });
        }
        else if (codec == "libsvtav1")
        {
            var parameters = new List<string>();
            var tune = SvtTune(profile.Tune);
            if (tune is not null) parameters.Add(tune);
            if (video.Mastering is { } m) parameters.Add($"mastering-display={SvtMasteringDisplay(m)}");
            if (video.ContentLight is { } l && (l.MaxContent > 0 || l.MaxAverage > 0)) parameters.Add($"content-light={l.MaxContent},{l.MaxAverage}");
            if (parameters.Count > 0) args.AddRange(new[] { "-svtav1-params", string.Join(":", parameters) });
        }
    }

    private static void AddIfKnown(List<string> args, string flag, string value)
    {
        if (!string.IsNullOrWhiteSpace(value) && value != "unknown" && value != "unspecified") args.AddRange(new[] { flag, value });
    }

    internal static string AudioEncoderName(string codec) => codec.ToLowerInvariant() switch
    {
        "opus" => "libopus",
        "mp3" => "libmp3lame",
        "aac" => "aac",
        var other => other
    };

    /// <summary>SVT-AV1's tune, as the parameter text ffmpeg passes through.</summary>
    internal static string? SvtTune(string tune) => tune.ToLowerInvariant() switch
    {
        "vq" => "tune=0",
        "psnr" => "tune=1",
        "ssim" => "tune=2",
        "iq" => "tune=3",
        "ms-ssim" => "tune=4",
        "fastdecode" => "fast-decode=1",
        _ => null
    };

    /// <summary>x265's master-display string: G(x,y)B(x,y)R(x,y)WP(x,y)L(max,min), integers in the
    /// units ffprobe's fractions already use.</summary>
    internal static string X265MasterDisplay(MasteringDisplay m) =>
        $"G({m.GreenX},{m.GreenY})B({m.BlueX},{m.BlueY})R({m.RedX},{m.RedY})WP({m.WhiteX},{m.WhiteY})L({m.MaxLuminance},{m.MinLuminance})";

    /// <summary>SVT-AV1 wants the same fields as decimals (chromaticity 0-1, luminance in nits).</summary>
    internal static string SvtMasteringDisplay(MasteringDisplay m)
    {
        static string C(int v) => (v / 50000.0).ToString("0.#####", CultureInfo.InvariantCulture);
        static string L(int v) => (v / 10000.0).ToString("0.####", CultureInfo.InvariantCulture);
        return $"G({C(m.GreenX)},{C(m.GreenY)})B({C(m.BlueX)},{C(m.BlueY)})R({C(m.RedX)},{C(m.RedY)})WP({C(m.WhiteX)},{C(m.WhiteY)})L({L(m.MaxLuminance)},{L(m.MinLuminance)})";
    }
}
