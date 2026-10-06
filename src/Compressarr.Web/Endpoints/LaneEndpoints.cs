using Microsoft.AspNetCore.Http;
using Compressarr.Core.Config;
using Compressarr.Core.Conversion;
using Compressarr.Core.Presets;
using Compressarr.Web.Dtos;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace Compressarr.Web.Endpoints;

public static class LaneEndpoints
{
    private static List<ValidationIssueDto> Validate(LaneConfig lane, CompressarrConfig config, IPathExpander pathExpander, IEncoderPresetService presets)
    {
        return LaneValidator.Validate(lane, config, pathExpander, presets)
            .Select(i => new ValidationIssueDto(i.Field, i.Message)).ToList();
    }

    public static void MapLaneEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/lanes", (IConfigStore configStore, IPathExpander pathExpander, IEncoderPresetService presets) =>
        {
            var config = configStore.Load(AppPaths.GetConfigFilePath());
            return Results.Json(config.Lanes.Select(lane => ConfigMapping.ToLaneDto(lane, Validate(lane, config, pathExpander, presets))).ToList());
        });

        app.MapPost("/api/lanes", (IConfigStore configStore, IPathExpander pathExpander, IEncoderPresetService presets) =>
        {
            var dto = configStore.Update(AppPaths.GetConfigFilePath(), config =>
            {
                var lane = new LaneConfig
                {
                    DisplayName = $"New Lane {config.Lanes.Count + 1}",
                    Enabled = true
                };
                config.Lanes.Add(lane);
                return ConfigMapping.ToLaneDto(lane, Validate(lane, config, pathExpander, presets));
            });

            return Results.Json(dto);
        });

        app.MapPut("/api/lanes/{id}", (string id, LaneDto dto, IConfigStore configStore, IPathExpander pathExpander, IEncoderPresetService presets) =>
        {
            var result = configStore.Update(AppPaths.GetConfigFilePath(), config =>
            {
                var lane = config.Lanes.FirstOrDefault(l => l.Id == id);
                if (lane is null) return null;

                ConfigMapping.ApplyLaneDto(lane, dto);
                return ConfigMapping.ToLaneDto(lane, Validate(lane, config, pathExpander, presets));
            });

            return result is null ? Results.NotFound() : Results.Json(result);
        });

        // How many queued files in OTHER lanes are set to land in this lane's library (see
        // ResumeEntry.DestinationLaneId) - asked before a lane is deleted so the warning can say what would
        // happen to them: each waits in Output until the user picks another destination.
        app.MapGet("/api/lanes/{id}/redirected-files", (string id, IResumeStateStore resumeStore) =>
        {
            var waiting = resumeStore.Load(AppPaths.GetResumeFilePath()).Count(e =>
                e.DestinationLaneId == id &&
                e.LaneId != id &&
                !e.Removed &&
                e.Status is ResumeStatus.Pending or ResumeStatus.MoveFailed);
            return Results.Json(new { waitingFiles = waiting });
        });

        app.MapDelete("/api/lanes/{id}"
, (string id, IConfigStore configStore) =>
        {
            var removed = configStore.Update(AppPaths.GetConfigFilePath(), config => config.Lanes.RemoveAll(l => l.Id == id));
            return removed == 0 ? Results.NotFound() : Results.NoContent();
        });
    }
}
