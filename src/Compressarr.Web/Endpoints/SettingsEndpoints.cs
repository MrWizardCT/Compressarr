using Microsoft.AspNetCore.Http;
using Compressarr.Core.Config;
using Compressarr.Web.Dtos;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace Compressarr.Web.Endpoints;

public static class SettingsEndpoints
{
    public static void MapSettingsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/settings", (IConfigStore configStore) =>
        {
            var config = configStore.Load(AppPaths.GetConfigFilePath());
            return Results.Json(ConfigMapping.ToSettingsDto(config));
        });

        app.MapPut("/api/settings", (SettingsDto dto, IConfigStore configStore) =>
        {
            var configPath = AppPaths.GetConfigFilePath();
            var config = configStore.Load(configPath);
            ConfigMapping.ApplySettingsDto(config, dto);
            configStore.Save(config, configPath);
            return Results.Json(ConfigMapping.ToSettingsDto(config));
        });
    }
}
