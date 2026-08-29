using Microsoft.AspNetCore.Http;
using Compressarr.Core.Config;
using Compressarr.Web.Dtos;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace Compressarr.Web.Endpoints;

public static class LaneEndpoints
{
    public static void MapLaneEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/lanes", (IConfigStore configStore) =>
        {
            var config = configStore.Load(AppPaths.GetConfigFilePath());
            return Results.Json(config.Lanes.Select(ConfigMapping.ToLaneDto).ToList());
        });

        app.MapPost("/api/lanes", (IConfigStore configStore) =>
        {
            var dto = configStore.Update(AppPaths.GetConfigFilePath(), config =>
            {
                var lane = new LaneConfig
                {
                    DisplayName = $"New Lane {config.Lanes.Count + 1}",
                    Enabled = true
                };
                config.Lanes.Add(lane);
                return ConfigMapping.ToLaneDto(lane);
            });

            return Results.Json(dto);
        });

        app.MapPut("/api/lanes/{id}", (string id, LaneDto dto, IConfigStore configStore) =>
        {
            var result = configStore.Update(AppPaths.GetConfigFilePath(), config =>
            {
                var lane = config.Lanes.FirstOrDefault(l => l.Id == id);
                if (lane is null) return null;

                ConfigMapping.ApplyLaneDto(lane, dto);
                return ConfigMapping.ToLaneDto(lane);
            });

            return result is null ? Results.NotFound() : Results.Json(result);
        });

        app.MapDelete("/api/lanes/{id}", (string id, IConfigStore configStore) =>
        {
            var removed = configStore.Update(AppPaths.GetConfigFilePath(), config => config.Lanes.RemoveAll(l => l.Id == id));
            return removed == 0 ? Results.NotFound() : Results.NoContent();
        });
    }
}
