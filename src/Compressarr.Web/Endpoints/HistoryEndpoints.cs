using Microsoft.AspNetCore.Http;
using Compressarr.Core.Config;
using Compressarr.Core.Logging;
using Compressarr.Core.Reporting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace Compressarr.Web.Endpoints;

public static class HistoryEndpoints
{
    public static void MapHistoryEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/history", (
            IConfigStore configStore,
            IPathExpander pathExpander,
            IWebHistoryRollupCalculator rollupCalculator,
            IRunHistoryStore historyStore) =>
        {
            var config = configStore.Load(AppPaths.GetConfigFilePath());
            var logFilePath = pathExpander.Expand(config.Logging.LogFilePath);

            var rollups = rollupCalculator.Calculate(logFilePath);
            var runCount = historyStore.GetRunCount(AppPaths.GetRunCountFilePath());

            return Results.Json(new
            {
                rollups.Today,
                rollups.Last7Days,
                rollups.Last30Days,
                rollups.LastYear,
                rollups.AllTime,
                totalRunCount = runCount
            });
        });

        app.MapGet("/api/history/reports", (
            IConfigStore configStore,
            IPathExpander pathExpander,
            IRunHistoryStore historyStore) =>
        {
            var config = configStore.Load(AppPaths.GetConfigFilePath());
            var logFilePath = pathExpander.Expand(config.Logging.LogFilePath);
            var reportPath = pathExpander.Expand(config.Report.ReportPath);

            var history = historyStore.GetHistory(logFilePath);
            var entries = ReportListBuilder.Build(
                history,
                config.Logging.RetentionDays,
                DateTime.Now,
                fileName => File.Exists(Path.Combine(reportPath, fileName)));

            return Results.Json(entries);
        });

        // The sidebar's error/warning badges only count runs newer than this watermark.
        app.MapGet("/api/history/viewed-through", (IConfigStore configStore) =>
        {
            var config = configStore.Load(AppPaths.GetConfigFilePath());
            return Results.Json(new { runNumber = config.UiState.HistoryViewedThroughRunNumber });
        });

        // Called once the History page has actually rendered its Reports list - advances the
        // watermark above to "every run that exists right now", clearing the sidebar badges.
        app.MapPost("/api/history/mark-viewed", (IConfigStore configStore, IRunHistoryStore historyStore) =>
        {
            var runNumber = configStore.Update(AppPaths.GetConfigFilePath(), config =>
            {
                config.UiState.HistoryViewedThroughRunNumber = historyStore.GetRunCount(AppPaths.GetRunCountFilePath());
                return config.UiState.HistoryViewedThroughRunNumber;
            });
            return Results.Json(new { runNumber });
        });

        // Serves a report HTML file by name only (never a full/relative path) resolved against
        // the *current* Report.ReportPath, so a later change to that setting doesn't strand
        // already-generated links - Path.GetFileName strips any directory component an attacker
        // (or a stale link) might try to smuggle in, so this can never escape the reports folder.
        // Serves a run's text log (the Compressarr log, or a HandBrake/ffmpeg detail log) by name only, from the Logs
        // folder, for the "Full Details" links on a report viewed through the app. Only .log/.txt files, and
        // Path.GetFileName strips any folder component, so it can't reach anything else on disk. Opened with shared
        // access because the current run's log may still be being written.
        app.MapGet("/api/logs/{fileName}", (string fileName, IConfigStore configStore, IPathExpander pathExpander) =>
        {
            var safeName = Path.GetFileName(fileName);
            var extension = Path.GetExtension(safeName);
            if (!extension.Equals(".log", StringComparison.OrdinalIgnoreCase) && !extension.Equals(".txt", StringComparison.OrdinalIgnoreCase))
            {
                return Results.NotFound();
            }

            var config = configStore.Load(AppPaths.GetConfigFilePath());
            var fullPath = Path.Combine(pathExpander.Expand(config.Logging.LogFilePath), safeName);
            if (!File.Exists(fullPath)) return Results.NotFound();

            return Results.Stream(new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete), "text/plain; charset=utf-8");
        });

        app.MapGet("/api/reports/{fileName}", (string fileName, IConfigStore configStore, IPathExpander pathExpander) =>
        {
            var config = configStore.Load(AppPaths.GetConfigFilePath());
            var reportPath = pathExpander.Expand(config.Report.ReportPath);
            var safeName = Path.GetFileName(fileName);
            var fullPath = Path.Combine(reportPath, safeName);

            return File.Exists(fullPath) ? Results.File(fullPath, "text/html") : Results.NotFound();
        });
    }
}
