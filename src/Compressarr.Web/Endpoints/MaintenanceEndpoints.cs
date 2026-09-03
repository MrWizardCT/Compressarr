using Microsoft.AspNetCore.Http;
using Compressarr.Core.Config;
using Compressarr.Core.Conversion;
using Compressarr.Core.Diagnostics;
using Compressarr.Core.Orchestration;
using Compressarr.Core.Routing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace Compressarr.Web.Endpoints;

public static class MaintenanceEndpoints
{
    public static void MapMaintenanceEndpoints(this IEndpointRouteBuilder app)
    {
        // Every handler here just writes a file (or deletes old ones already past retention) -
        // nothing is cached in memory anywhere that would need an app restart to pick it up.
        // /api/settings, /api/run/status, etc. all re-read their file from disk on every request,
        // so the effect is visible immediately on the very next poll/load.

        app.MapPost("/api/maintenance/reset-resume", (IResumeStateStore resumeStore) =>
        {
            var path = Path.Combine(AppPaths.GetAppDataDirectory(), "compressarr.resume.json");
            resumeStore.Save(new List<ResumeEntry>(), path);
            return Results.Ok();
        });

        app.MapPost("/api/maintenance/clear-config", (IConfigStore configStore) =>
        {
            configStore.Save(DefaultConfigFactory.Create(), AppPaths.GetConfigFilePath());
            return Results.Ok();
        });

        // Forces the same retention cleanup RunOrchestrator already does at the end of every real
        // pass (RetentionCleaner.CleanUp against Logging.RetentionDays) right now, instead of
        // waiting for the next one - files go to the Recycle Bin (DeleteAfterConvertMode.Recycle),
        // same as that automatic pass, not a permanent delete.
        app.MapPost("/api/maintenance/cleanup-now", (IConfigStore configStore, IPathExpander pathExpander, ITrashService trash) =>
        {
            var config = configStore.Load(AppPaths.GetConfigFilePath());
            var logPath = pathExpander.Expand(config.Logging.LogFilePath);
            var reportPath = pathExpander.Expand(config.Report.ReportPath);

            RetentionCleaner.CleanUp(trash, logPath, new[] { ".log", ".txt" }, config.Logging.RetentionDays, "log");
            RetentionCleaner.CleanUp(trash, reportPath, new[] { ".html" }, config.Logging.RetentionDays, "report");

            return Results.Ok();
        });
    }
}
