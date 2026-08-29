using Microsoft.AspNetCore.Http;
using Compressarr.Core.Config;
using Compressarr.Core.Dependencies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace Compressarr.Web.Endpoints;

public static class HandBrakeEndpoints
{
    public static void MapHandBrakeEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/handbrake/status", (IConfigStore configStore, IPathExpander pathExpander) =>
        {
            var config = configStore.Load(AppPaths.GetConfigFilePath());
            var exists = pathExpander.PathExists(config.HandBrake.CliPath);
            return Results.Json(new { exists });
        });

        app.MapGet("/api/handbrake/latest-release", async (IHandBrakeInstaller installer) =>
        {
            var release = await installer.GetLatestReleaseAsync();
            if (release is null) return Results.Json(new { available = false });

            return Results.Json(new
            {
                available = true,
                release.Version,
                release.AssetName,
                sizeMb = release.SizeBytes / 1024 / 1024
            });
        });

        app.MapPost("/api/handbrake/install", async (IHandBrakeInstaller installer, IConfigStore configStore) =>
        {
            var release = await installer.GetLatestReleaseAsync();
            if (release is null)
            {
                return Results.BadRequest(new { message = "No downloadable HandBrakeCLI build was found for this platform. On Linux, install it via your distro's package manager or Flatpak." });
            }

            var installDir = Path.Combine(AppPaths.GetAppDataDirectory(), "HandBrakeCLI");
            var installedPath = await installer.InstallAsync(release, installDir);

            configStore.Update(AppPaths.GetConfigFilePath(), config =>
            {
                config.HandBrake.CliPath = installedPath;
                return true;
            });

            return Results.Json(new { installedPath, version = release.Version });
        });
    }
}
