using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Compressarr.Core.Updates;

namespace Compressarr.Web.Endpoints;

public static class AboutEndpoints
{
    private const string RepoOwner = "MrWizardCT";
    private const string RepoName = "Compressarr";

    private static string InstalledVersionString => FormatVersion(typeof(AboutEndpoints).Assembly.GetName().Version);

    public static void MapAboutEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/about", () => Results.Json(new
        {
            version = InstalledVersionString,
            // The "Original v1.1" link specifically - v1.1 (PowerShell) lives on the 1.x branch
            // now that main hosts v2's own source.
            repoUrl = $"https://github.com/{RepoOwner}/{RepoName}/tree/1.x"
        }));

        // The toolbar's ambient indicator (nav.js, on every page load) - purely reads
        // IUpdateCheckService's own background-checked cache (refreshed immediately on app
        // startup, then roughly daily), no network call of its own. Falls back to one inline live
        // check only if the service hasn't completed one yet at all (e.g. this request landed
        // before Start()'s first check finished).
        app.MapGet("/api/about/update-status", async (IUpdateCheckService updateCheckService) =>
            Results.Json(ToDto(updateCheckService.LastResult ?? await updateCheckService.CheckNowAsync())));

        // The About page's explicit "Check for Updates" button - always performs a real,
        // on-demand GitHub check (not the cache above), since that's what clicking a button
        // labeled "Check for Updates" should actually do. Also refreshes IUpdateCheckService's
        // cache as a side effect, so the toolbar indicator picks up the same fresh result too.
        app.MapGet("/api/about/check-update", async (IUpdateCheckService updateCheckService) =>
            Results.Json(ToDto(await updateCheckService.CheckNowAsync())));
    }

    private static string FormatVersion(Version? version) =>
        version is null ? "unknown" : $"{version.Major}.{version.Minor}.{version.Build}";

    private static object ToDto(UpdateCheckResult result) => new
    {
        checkedOk = result.CheckedOk,
        error = result.Error,
        latestVersion = result.LatestVersion,
        releaseUrl = result.ReleaseUrl,
        hasUpdate = result.HasUpdate
    };
}
