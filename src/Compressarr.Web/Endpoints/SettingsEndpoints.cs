using Microsoft.AspNetCore.Http;
using Compressarr.Core.Config;
using Compressarr.Core.Startup;
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

        app.MapPut("/api/settings", (SettingsDto dto, IConfigStore configStore, IStartupRegistrationService startupRegistration) =>
        {
            var result = configStore.Update(AppPaths.GetConfigFilePath(), config =>
            {
                ConfigMapping.ApplySettingsDto(config, dto);
                return ConfigMapping.ToSettingsDto(config);
            });
            startupRegistration.Apply(dto.RunAtLogin);
            return Results.Json(result);
        });
    }
}
