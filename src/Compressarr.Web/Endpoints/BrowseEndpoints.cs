using Microsoft.AspNetCore.Http;
using Compressarr.Core.Config;
using Compressarr.Core.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace Compressarr.Web.Endpoints;

public static class BrowseEndpoints
{
    public static void MapBrowseEndpoints(this IEndpointRouteBuilder app)
    {
        // files=true also lists the files (tool-path pickers). The path is expanded first so a field holding
        // %CompressarrAppData%... opens in the real folder.
        app.MapGet("/api/browse", (string? path, bool? files, string? ext, IFileSystemBrowser browser, IPathExpander pathExpander) =>
        {
            var start = string.IsNullOrWhiteSpace(path) ? path : pathExpander.Expand(path);
            return Results.Json(browser.Browse(start, files == true, ext));
        });
    }
}
