using Microsoft.AspNetCore.Http;
using Compressarr.Core.Config;
using Compressarr.Core.Presets;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace Compressarr.Web.Endpoints;

public static class PresetEndpoints
{
    public static void MapPresetEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/presets", (string path, IHandBrakePresetService presetService, IPathExpander pathExpander) =>
        {
            var expanded = pathExpander.Expand(path);
            if (!File.Exists(expanded)) return Results.Json(Array.Empty<string>());

            try
            {
                return Results.Json(presetService.GetPresetNames(expanded));
            }
            catch
            {
                return Results.Json(Array.Empty<string>());
            }
        });

        app.MapGet("/api/presets/status", (IConfigStore configStore, IPresetInstaller presetInstaller, IPathExpander pathExpander) =>
        {
            var config = configStore.Load(AppPaths.GetConfigFilePath());
            var presetsPath = pathExpander.Expand(config.HandBrake.PresetsPath);
            return Results.Json(new { needsMergePrompt = presetInstaller.NeedsMergePrompt(presetsPath) });
        });

        app.MapPost("/api/presets/install", (InstallPresetsRequest request, IConfigStore configStore, IPresetInstaller presetInstaller, IPathExpander pathExpander) =>
        {
            var config = configStore.Load(AppPaths.GetConfigFilePath());
            var presetsPath = pathExpander.Expand(config.HandBrake.PresetsPath);

            if (request.Mode == "merge")
            {
                presetInstaller.Merge(presetsPath);
            }
            else
            {
                presetInstaller.InstallFresh(presetsPath);
            }

            return Results.Ok();
        });

        app.MapPost("/api/presets/reload", (IConfigStore configStore, IHandBrakePresetService presetService, IPathExpander pathExpander) =>
        {
            var config = configStore.Load(AppPaths.GetConfigFilePath());
            var presetsPath = pathExpander.Expand(config.HandBrake.PresetsPath);
            presetService.InvalidateCache(presetsPath);
            return Results.Ok();
        });
    }
}

public sealed record InstallPresetsRequest(string Mode);
