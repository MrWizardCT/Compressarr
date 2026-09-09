using Compressarr.Core.Validation;

namespace Compressarr.Core.Config;

/// <summary>Checks the same "can this app actually run at all" conditions RunOrchestrator's own
/// early-return guards (HandBrakeCLI/presets.json) and FileBotRunner's own path check already
/// enforce at run time, plus two conditions with no existing runtime guard (FileBot path when
/// enabled but not yet needed to start scanning; VidTypes) - surfaced on the Settings page so a
/// broken base configuration is visible before a real run ever hits it, not just log-only.</summary>
public static class SettingsValidator
{
    public static List<ValidationIssue> Validate(CompressarrConfig config, IPathExpander pathExpander)
    {
        var issues = new List<ValidationIssue>();

        var hbCliPath = pathExpander.Expand(config.HandBrake.CliPath);
        if (string.IsNullOrWhiteSpace(config.HandBrake.CliPath) || !pathExpander.PathExists(config.HandBrake.CliPath))
        {
            issues.Add(new ValidationIssue("handBrakeCliPath", $"HandBrakeCLI.exe is required and must exist - not found at '{hbCliPath}'. Use Check/Install to find or download it automatically."));
        }

        var presetsPath = pathExpander.Expand(config.HandBrake.PresetsPath);
        if (string.IsNullOrWhiteSpace(config.HandBrake.PresetsPath) || !pathExpander.PathExists(config.HandBrake.PresetsPath))
        {
            issues.Add(new ValidationIssue("presetsPath", $"The HandBrake presets file is required and must exist - not found at '{presetsPath}'. Use Install/Merge Presets to create it."));
        }

        if (config.FileBot.Enabled)
        {
            var fileBotPath = pathExpander.Expand(config.FileBot.CliPath);
            if (string.IsNullOrWhiteSpace(config.FileBot.CliPath) || !pathExpander.PathExists(config.FileBot.CliPath))
            {
                issues.Add(new ValidationIssue("fileBotCliPath", $"FileBot is enabled but its path is required and must exist - not found at '{fileBotPath}'."));
            }
        }

        if (config.Processing.VidTypes.Count == 0)
        {
            issues.Add(new ValidationIssue("vidTypes", "At least one video extension is required - without one, no file can ever be recognized as a video to convert."));
        }

        return issues;
    }
}
