using Compressarr.Core.Config;
using Compressarr.Core.Presets;
using Compressarr.Core.Validation;

namespace Compressarr.Core.Conversion;

/// <summary>The single source of truth for "is this lane's own configuration actually usable" -
/// shared by both the real run-time orchestrators (RunOrchestrator's lane-prep loop,
/// ConversionOrchestrator.PrepareLane's Output-folder check) and the Lanes page's own
/// GET/PUT-time validation, so the UI's red-field warnings and the engine's real skip/warn
/// behavior can never drift apart. A disabled lane is never validated - it's deliberately off,
/// warning about its otherwise-incomplete config would just be noise.</summary>
public static class LaneValidator
{
    public static List<ValidationIssue> Validate(LaneConfig lane, CompressarrConfig config, string presetsPath, IPathExpander pathExpander, IHandBrakePresetService presets)
    {
        var issues = new List<ValidationIssue>();
        if (!lane.Enabled) return issues;

        var inputPath = pathExpander.Expand(lane.Input);
        if (string.IsNullOrWhiteSpace(inputPath) || !Directory.Exists(inputPath))
        {
            issues.Add(new ValidationIssue("input", "Input folder is required and must exist."));
        }

        var outputBase = pathExpander.Expand(lane.Output);
        if (string.IsNullOrWhiteSpace(outputBase) && !config.Processing.OutSameAsIn)
        {
            issues.Add(new ValidationIssue("output", "Output folder is required, unless 'write output to same folder as input' is enabled in Settings."));
        }

        if (string.IsNullOrWhiteSpace(lane.TvPreset) && string.IsNullOrWhiteSpace(lane.MoviePreset))
        {
            // Either field resolves this - both are flagged so the fix is obvious regardless of
            // which one the user ends up filling in.
            issues.Add(new ValidationIssue("tvPreset", "At least a TV or Movie preset is required on this lane."));
            issues.Add(new ValidationIssue("moviePreset", "At least a TV or Movie preset is required on this lane."));
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(lane.TvPreset) && !presets.PresetExists(lane.TvPreset, presetsPath))
            {
                issues.Add(new ValidationIssue("tvPreset", $"TV preset '{lane.TvPreset}' was not found in presets.json - TV episodes in this lane will be skipped."));
            }
            if (!string.IsNullOrWhiteSpace(lane.MoviePreset) && !presets.PresetExists(lane.MoviePreset, presetsPath))
            {
                issues.Add(new ValidationIssue("moviePreset", $"Movie preset '{lane.MoviePreset}' was not found in presets.json - movies in this lane will be skipped."));
            }
        }

        return issues;
    }
}
