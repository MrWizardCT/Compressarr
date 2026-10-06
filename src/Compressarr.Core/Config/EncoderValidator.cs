using Compressarr.Core.Conversion;
using Compressarr.Core.Validation;

namespace Compressarr.Core.Config;

/// <summary>The Encoder page's own "can this tool actually run" checks: the HandBrakeCLI path and the
/// extra-options strings - and, when a lane actually uses ffmpeg, the ffmpeg and ffprobe paths. (Until
/// 2.2 the HandBrake checks lived in SettingsValidator, beside the Settings page's HandBrake card.)
/// RunOrchestrator's early-return guard on a missing CLI enforces the same condition at run time.</summary>
public static class EncoderValidator
{
    public static List<ValidationIssue> Validate(CompressarrConfig config, IPathExpander pathExpander)
    {
        var issues = new List<ValidationIssue>();

        var hbCliPath = pathExpander.Expand(config.HandBrake.CliPath);
        if (string.IsNullOrWhiteSpace(config.HandBrake.CliPath) || !pathExpander.PathExists(config.HandBrake.CliPath))
        {
            issues.Add(new ValidationIssue("handBrakeCliPath", $"HandBrakeCLI.exe is required and must exist - not found at '{hbCliPath}'. Use Check/Install to find or download it automatically."));
        }

        if (!string.IsNullOrWhiteSpace(config.HandBrake.Options) && HandBrakeProcessRunner.HasUnbalancedQuotes(config.HandBrake.Options))
        {
            issues.Add(new ValidationIssue("handBrakeOptions", "Extra CLI options has an unmatched quote (\") - everything after it will be folded into one argument instead of being split as intended. Check for a missing closing quote."));
        }

        // ffmpeg is optional: its paths only matter once a lane is set to use it.
        if (config.Lanes.Any(l => l.Enabled && l.Engine == EncoderEngine.FFmpeg))
        {
            if (string.IsNullOrWhiteSpace(config.FFmpeg.Path) || !pathExpander.PathExists(config.FFmpeg.Path))
            {
                issues.Add(new ValidationIssue("ffmpegPath", $"A lane uses ffmpeg but it was not found at '{pathExpander.Expand(config.FFmpeg.Path)}'. Use Check/Install to find or download it automatically."));
            }
            if (string.IsNullOrWhiteSpace(config.FFmpeg.ProbePath) || !pathExpander.PathExists(config.FFmpeg.ProbePath))
            {
                issues.Add(new ValidationIssue("ffmpegProbePath", $"ffprobe was not found at '{pathExpander.Expand(config.FFmpeg.ProbePath)}'. It comes with ffmpeg and is needed to read each file."));
            }
        }

        if (!string.IsNullOrWhiteSpace(config.FFmpeg.Options) && HandBrakeProcessRunner.HasUnbalancedQuotes(config.FFmpeg.Options))
        {
            issues.Add(new ValidationIssue("ffmpegOptions", "Extra ffmpeg options has an unmatched quote (\") - everything after it will be folded into one argument instead of being split as intended. Check for a missing closing quote."));
        }

        return issues;
    }
}
