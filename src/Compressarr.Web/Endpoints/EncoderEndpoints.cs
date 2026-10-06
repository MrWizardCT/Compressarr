using Compressarr.Core.Config;
using Compressarr.Core.Presets;
using Compressarr.Web.Dtos;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Compressarr.Web.Endpoints;

/// <summary>The Encoder page: where HandBrakeCLI is and what extra options every encode gets
/// (these moved here from Settings in 2.2), plus the Profiles page's read-only list. The
/// install/version helpers the Check/Install button uses are still HandBrakeEndpoints.</summary>
public static class EncoderEndpoints
{
    private static EncoderSettingsDto ToDto(CompressarrConfig config, IHandBrakeProfileStore profiles, IPathExpander pathExpander)
    {
        var all = profiles.GetAll();
        var issues = EncoderValidator.Validate(config, pathExpander).Select(i => new ValidationIssueDto(i.Field, i.Message)).ToList();

        return new EncoderSettingsDto(
            HandBrakeCliPath: config.HandBrake.CliPath,
            HandBrakeOptions: config.HandBrake.Options,
            BuiltInProfileCount: all.Count(p => p.IsBuiltIn),
            UserProfileCount: all.Count(p => !p.IsBuiltIn),
            // Until the ffmpeg encoder exists, every enabled lane is a HandBrake lane.
            HandBrakeLanes: config.Lanes.Where(l => l.Enabled).Select(l => l.DisplayName).ToList(),
            ValidationIssues: issues);
    }

    public static void MapEncoderEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/encoder", (IConfigStore configStore, IHandBrakeProfileStore profiles, IPathExpander pathExpander) =>
        {
            var config = configStore.Load(AppPaths.GetConfigFilePath());
            return Results.Json(ToDto(config, profiles, pathExpander));
        });

        app.MapPut("/api/encoder", (EncoderSettingsDto dto, IConfigStore configStore, IHandBrakeProfileStore profiles, IPathExpander pathExpander) =>
        {
            var result = configStore.Update(AppPaths.GetConfigFilePath(), config =>
            {
                config.HandBrake.CliPath = dto.HandBrakeCliPath;
                config.HandBrake.Options = dto.HandBrakeOptions;
                return ToDto(config, profiles, pathExpander);
            });
            return Results.Json(result);
        });

        app.MapGet("/api/profiles", (IConfigStore configStore, IHandBrakeProfileStore profiles) =>
        {
            var config = configStore.Load(AppPaths.GetConfigFilePath());

            List<string> UsedBy(string name) => config.Lanes
                .Where(l => string.Equals(l.TvPreset, name, StringComparison.OrdinalIgnoreCase)
                         || string.Equals(l.MoviePreset, name, StringComparison.OrdinalIgnoreCase))
                .Select(l => l.DisplayName)
                .ToList();

            var rows = profiles.GetAll().Select(p => new ProfileDto(
                Engine: "handbrake",
                Name: p.Name,
                BuiltIn: p.IsBuiltIn,
                Container: HandBrakeProfileSummary.Container(p.FileFormat),
                Video: HandBrakeProfileSummary.Video(p.Definition),
                Audio: HandBrakeProfileSummary.Audio(p.Definition),
                Description: p.Definition["PresetDescription"]?.GetValue<string>() ?? "",
                UsedBy: UsedBy(p.Name))).ToList();

            return Results.Json(new ProfileListDto(rows, AppPaths.GetHandBrakeProfilesFilePath(), AppPaths.GetHandBrakeActivePresetsFilePath(), profiles.UserFileError));
        });
    }
}
