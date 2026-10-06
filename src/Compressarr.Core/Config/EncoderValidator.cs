using Compressarr.Core.Conversion;
using Compressarr.Core.Validation;

namespace Compressarr.Core.Config;

/// <summary>The Encoder page's own "can this tool actually run" checks: the HandBrakeCLI path and
/// the extra-options string. (Until 2.2 these lived in SettingsValidator, beside the Settings
/// page's HandBrake card.) RunOrchestrator's early-return guard on a missing CLI enforces the same
/// condition at run time.</summary>
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

        return issues;
    }
}
