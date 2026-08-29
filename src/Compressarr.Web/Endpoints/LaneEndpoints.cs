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
            var configPath = AppPaths.GetConfigFilePath();
            var config = configStore.Load(configPath);

            var lane = new LaneConfig
            {
                DisplayName = $"New Lane {config.Lanes.Count + 1}",
                Enabled = true
            };
            config.Lanes.Add(lane);
            configStore.Save(config, configPath);

            return Results.Json(ConfigMapping.ToLaneDto(lane));
        });

        app.MapPut("/api/lanes/{id}", (string id, LaneDto dto, IConfigStore configStore) =>
        {
            var configPath = AppPaths.GetConfigFilePath();
            var config = configStore.Load(configPath);

            var lane = config.Lanes.FirstOrDefault(l => l.Id == id);
            if (lane is null) return Results.NotFound();

            ConfigMapping.ApplyLaneDto(lane, dto);
            configStore.Save(config, configPath);

            return Results.Json(ConfigMapping.ToLaneDto(lane));
        });

        app.MapDelete("/api/lanes/{id}", (string id, IConfigStore configStore) =>
        {
            var configPath = AppPaths.GetConfigFilePath();
            var config = configStore.Load(configPath);

            var removed = config.Lanes.RemoveAll(l => l.Id == id);
            if (removed == 0) return Results.NotFound();

            configStore.Save(config, configPath);
            return Results.NoContent();
        });
    }
}
