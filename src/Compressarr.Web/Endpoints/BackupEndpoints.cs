using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;
using Compressarr.Core.Backup;
using Compressarr.Core.Config;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace Compressarr.Web.Endpoints;

public static class BackupEndpoints
{
    private static readonly JsonSerializerOptions ExportOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static void MapBackupEndpoints(this IEndpointRouteBuilder app)
    {
        // Exports the fully-merged config (every field populated, not just whatever overrides
        // happen to be in the on-disk file) so the download is a complete, self-contained backup
        // regardless of how partial compressarr.settings.json currently is.
        app.MapGet("/api/settings/export", (IConfigStore configStore) =>
        {
            var config = configStore.Load(AppPaths.GetConfigFilePath());
            var json = JsonSerializer.Serialize(config, ExportOptions);
            var bytes = Encoding.UTF8.GetBytes(json);
            return Results.File(bytes, "application/json", $"compressarr-config-{DateTime.Now:yyyy-MM-dd}.json");
        });

        app.MapPost("/api/settings/import", async (HttpRequest request, IConfigStore configStore) =>
        {
            using var reader = new StreamReader(request.Body);
            var raw = await reader.ReadToEndAsync();

            // Validate + merge-over-defaults the uploaded file the exact same way
            // JsonConfigStore.Load already does for the real config, by round-tripping it through
            // a throwaway temp file - reuses that logic instead of duplicating it, and means an
            // older/partial export still imports cleanly (missing fields fall back to defaults
            // rather than null or a crash).
            var tempPath = Path.Combine(Path.GetTempPath(), $"compressarr-import-{Guid.NewGuid():N}.json");
            try
            {
                await File.WriteAllTextAsync(tempPath, raw);

                CompressarrConfig imported;
                try
                {
                    imported = configStore.Load(tempPath);
                }
                catch (Exception ex)
                {
                    return Results.BadRequest(new { message = $"That file isn't a valid Compressarr config: {ex.Message}" });
                }

                configStore.Save(imported, AppPaths.GetConfigFilePath());
                return Results.Ok();
            }
            finally
            {
                try { File.Delete(tempPath); } catch { }
            }
        });

        // Distinct from /api/settings/export above - this is the automated multi-file backup
        // (settings/lanes, run counter, resume state, history CSV) written to the configured
        // Backups folder, not a one-shot settings-only download. Same trigger both the scheduled
        // loop and this manual button call into.
        app.MapPost("/api/backups/run", async (IBackupService backupService) =>
        {
            var result = await backupService.RunBackupAsync();
            return result.Success
                ? Results.Ok(new { fileName = result.FileName })
                : Results.Json(new { message = result.Error }, statusCode: 500);
        });
    }
}
