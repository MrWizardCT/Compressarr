using Microsoft.AspNetCore.Http;
using Compressarr.Core.Config;
using Compressarr.Core.Diagnostics;
using Compressarr.Core.Orchestration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace Compressarr.Web.Endpoints;

public static class RunEndpoints
{
    public static void MapRunEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/run/once", async (IConfigStore configStore, IRunOrchestrator runOrchestrator) =>
        {
            var config = configStore.Load(AppPaths.GetConfigFilePath());
            var result = await runOrchestrator.RunOnceAsync(config);
            return result is null
                ? Results.BadRequest(new { message = "Run aborted - check the HandBrakeCLI and presets.json paths." })
                : Results.Json(new { totalFiles = result.TotalFiles });
        });

        app.MapPost("/api/run/start", (IConfigStore configStore, IRunLoopController loopController) =>
        {
            var config = configStore.Load(AppPaths.GetConfigFilePath());
            loopController.Start(config, TimeSpan.FromSeconds(Math.Max(5, config.Repeat.PollIntervalSeconds)));
            return Results.Ok();
        });

        app.MapPost("/api/run/stop", async (IRunLoopController loopController) =>
        {
            await loopController.StopAsync();
            return Results.Ok();
        });

        app.MapGet("/api/run/status", async (IRunLoopController loopController, CurrentRunStateService runState, ICpuUsageSampler cpuSampler) =>
        {
            var snapshot = runState.GetSnapshot();
            var cpu = await cpuSampler.SampleAsync();

            return Results.Json(new
            {
                isMonitoring = loopController.IsRunning,
                isRunning = snapshot.IsRunning,
                laneDisplayName = snapshot.LaneDisplayName,
                fileName = snapshot.FileName,
                fileIndex = snapshot.FileIndex,
                fileTotal = snapshot.FileTotal,
                recentLogLines = snapshot.RecentLogLines,
                cpuUsagePercent = cpu
            });
        });
    }
}
