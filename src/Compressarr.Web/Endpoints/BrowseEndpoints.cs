using Microsoft.AspNetCore.Http;
using Compressarr.Core.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace Compressarr.Web.Endpoints;

public static class BrowseEndpoints
{
    public static void MapBrowseEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/browse", (string? path, IFileSystemBrowser browser) =>
        {
            return Results.Json(browser.Browse(path));
        });
    }
}
