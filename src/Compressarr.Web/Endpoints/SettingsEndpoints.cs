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
        app.MapGet("/api/settings", (IConfigStore configStore, IPathExpander pathExpander) =>
        {
            var config = configStore.Load(AppPaths.GetConfigFilePath());
            var issues = SettingsValidator.Validate(config, pathExpander).Select(i => new ValidationIssueDto(i.Field, i.Message)).ToList();
            return Results.Json(ConfigMapping.ToSettingsDto(config, issues));
        });

        app.MapPut("/api/settings", (SettingsDto dto, IConfigStore configStore, IStartupRegistrationService startupRegistration, IPathExpander pathExpander) =>
        {
            var result = configStore.Update(AppPaths.GetConfigFilePath(), config =>
            {
                ConfigMapping.ApplySettingsDto(config, dto);
                var issues = SettingsValidator.Validate(config, pathExpander).Select(i => new ValidationIssueDto(i.Field, i.Message)).ToList();
                return ConfigMapping.ToSettingsDto(config, issues);
            });
            startupRegistration.Apply(dto.RunAtLogin);
            return Results.Json(result);
        });
    }
}
