using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;
using Compressarr.Core.Arr;
using Compressarr.Web.Dtos;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace Compressarr.Web.Endpoints;

public static class ArrEndpoints
{
    public static void MapArrEndpoints(this IEndpointRouteBuilder app)
    {
        // Tests whatever URL/API key is currently typed into the form, not necessarily what's
        // saved - lets the user confirm it works before hitting Save, matching how Check/Install
        // and Install/Merge Presets already behave on this page.
        app.MapPost("/api/arr/test", async (ArrServiceDto dto, IArrClient client) =>
        {
            if (string.IsNullOrWhiteSpace(dto.Url) || string.IsNullOrWhiteSpace(dto.ApiKey))
            {
                return Results.Json(new { success = false, message = "URL and API key are both required." });
            }

            try
            {
                var status = await client.GetAsync(dto.Url, dto.ApiKey, "/api/v3/system/status");
                var version = (status as JsonObject)?["version"]?.GetValue<string>();
                return Results.Json(new
                {
                    success = true,
                    message = version is not null ? $"Connected - version {version}." : "Connected."
                });
            }
            catch (Exception ex)
            {
                // Wrong API key, unreachable host, wrong port, TLS mismatch, etc. all land here -
                // surfacing ex.Message (HttpClient/HttpRequestException already phrase these
                // reasonably, e.g. "401 Unauthorized" or "No connection could be made") rather than
                // a generic "failed" is exactly the point of a test-connection button.
                return Results.Json(new { success = false, message = $"Connection failed: {ex.Message}" });
            }
        });
    }
}
