using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace Compressarr.Web.Endpoints;

public static class AboutEndpoints
{
    // The original v1.1 PowerShell tool's repo - this is what v2 is a rewrite of, and where its
    // own GitHub Releases currently live. Once v2 has a real published home this may need to
    // change; there is no way to derive that automatically, so it's a plain constant here.
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

        app.MapGet("/api/about/check-update", async (IHttpClientFactory httpClientFactory) =>
        {
            var installed = typeof(AboutEndpoints).Assembly.GetName().Version ?? new Version(0, 0, 0);

            try
            {
                var client = httpClientFactory.CreateClient();
                client.Timeout = TimeSpan.FromSeconds(10);
                client.DefaultRequestHeaders.UserAgent.ParseAdd("Compressarr-UpdateChecker");

                var response = await client.GetAsync($"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases/latest");
                if (!response.IsSuccessStatusCode)
                {
                    return Results.Json(new { checkedOk = false, error = $"GitHub returned HTTP {(int)response.StatusCode}." });
                }

                using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                var tagName = doc.RootElement.TryGetProperty("tag_name", out var tagProp) ? tagProp.GetString() ?? "" : "";
                var releaseUrl = doc.RootElement.TryGetProperty("html_url", out var urlProp) ? urlProp.GetString() ?? "" : "";

                var latestVersionText = tagName.TrimStart('v', 'V');
                var hasUpdate = Version.TryParse(latestVersionText, out var latest) && latest > installed;

                return Results.Json(new
                {
                    checkedOk = true,
                    latestVersion = tagName,
                    releaseUrl,
                    hasUpdate
                });
            }
            catch (Exception ex)
            {
                return Results.Json(new { checkedOk = false, error = ex.Message });
            }
        });
    }

    private static string FormatVersion(Version? version) =>
        version is null ? "unknown" : $"{version.Major}.{version.Minor}.{version.Build}";
}
