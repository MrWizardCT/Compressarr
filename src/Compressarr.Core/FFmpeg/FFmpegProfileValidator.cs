using Compressarr.Core.Validation;

namespace Compressarr.Core.FFmpeg;

/// <summary>What is wrong with an ffmpeg profile as the editor stands (uniqueness of the name is checked
/// against the store by the caller). Field names match the editor's input ids.</summary>
public static class FFmpegProfileValidator
{
    private static readonly string[] VideoCodecs = { "libx265", "libx264", "libsvtav1", "hevc_nvenc", "h264_nvenc", "av1_nvenc" };
    private static readonly string[] AudioCodecs = { "aac", "ac3", "eac3", "opus", "mp3", "flac" };

    public static List<ValidationIssue> Validate(FFmpegProfile p)
    {
        var issues = new List<ValidationIssue>();

        var name = p.Name?.Trim() ?? "";
        if (name.Length == 0) issues.Add(new ValidationIssue("name", "A profile needs a name."));
        else if (name.IndexOfAny(new[] { '/', '\\', '"' }) >= 0 || name.Any(char.IsControl))
        {
            issues.Add(new ValidationIssue("name", "A name can't contain / \\ or quotation marks."));
        }

        if (!p.Container.Equals("mkv", StringComparison.OrdinalIgnoreCase) && !p.Container.Equals("mp4", StringComparison.OrdinalIgnoreCase))
        {
            issues.Add(new ValidationIssue("container", "Choose MKV or MP4."));
        }

        if (!VideoCodecs.Contains(p.VideoCodec)) issues.Add(new ValidationIssue("videoCodec", "Choose a video encoder."));

        if (p.QualityMode == "bitrate")
        {
            if (p.VideoBitrateKbps <= 0) issues.Add(new ValidationIssue("videoBitrate", "The video bitrate must be above 0 kbps."));
        }
        else if (p.Quality < 0 || p.Quality > 63)
        {
            issues.Add(new ValidationIssue("quality", "Quality (CRF/CQ) must be between 0 and 63."));
        }

        if (p.FramerateMode == "cfr" && p.Framerate <= 0) issues.Add(new ValidationIssue("framerate", "Choose a frame rate for constant frame rate."));

        if (p.AudioLanguages.Count(l => !string.IsNullOrWhiteSpace(l)) == 0)
        {
            issues.Add(new ValidationIssue("audioLanguages", "List at least one audio language (e.g. eng), or 'any'."));
        }

        if (p.AudioMode == "passthru" && p.PassthroughCodecs.Count == 0)
        {
            issues.Add(new ValidationIssue("passthrough", "Tick at least one format to pass through, or choose 'Always encode'."));
        }

        if (!AudioCodecs.Contains(p.AudioCodec)) issues.Add(new ValidationIssue("audioCodec", "Choose an audio encoder."));
        if (p.AudioBitrateKbps < 0 || p.AudioBitrateKbps > 3000) issues.Add(new ValidationIssue("audioBitrate", "The audio bitrate must be between 0 and 3000 kbps."));

        if (p.SubtitleTracks != "none" && p.SubtitleLanguages.Count(l => !string.IsNullOrWhiteSpace(l)) == 0)
        {
            issues.Add(new ValidationIssue("subtitleLanguages", "List at least one subtitle language, or set Keep to 'None'."));
        }

        return issues;
    }
}
