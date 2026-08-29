using System.Text.Json.Nodes;
using Compressarr.Core.Config;

namespace Compressarr.Core.Arr;

public interface IArrUnmonitorService
{
    /// <summary>Dispatches to Sonarr (TV) or Radarr (Movie) based on isTv, only if that service
    /// is enabled. Returns null (no-op, not an error) if the matching service isn't enabled;
    /// otherwise a short human-readable status string describing the outcome (unmonitored +
    /// rescanned / already unmonitored, rescanned anyway / no match found). Throws if the service
    /// is enabled but not configured (blank URL/API key), or if the request itself fails —
    /// callers are expected to wrap this in their own try/catch (a broken/unreachable arr
    /// instance must never fail an otherwise-successful conversion).</summary>
    Task<string?> UnmonitorAsync(CompressarrConfig config, string fileName, bool isTv);
}

/// <summary>Ported from Invoke-CompressarrArrUnmonitor/Invoke-CompressarrSonarrUnmonitor/
/// Invoke-CompressarrRadarrUnmonitor. Deliberately does no fuzzy matching of its own — trusts
/// whatever Sonarr/Radarr's own /api/v3/parse endpoint returns (the same lookup those apps use
/// internally for manual imports); a parse miss is always "leave unchanged," never a guessed
/// match, since a wrong auto-match would unmonitor the wrong show/movie.</summary>
public sealed class ArrUnmonitorService : IArrUnmonitorService
{
    private readonly IArrClient _client;

    public ArrUnmonitorService(IArrClient client)
    {
        _client = client;
    }

    public async Task<string?> UnmonitorAsync(CompressarrConfig config, string fileName, bool isTv)
    {
        var (svc, serviceName, itemWord) = isTv
            ? (config.Arrs.Sonarr, "Sonarr", "episode")
            : (config.Arrs.Radarr, "Radarr", "movie");

        if (!svc.Enabled) return null;

        if (string.IsNullOrWhiteSpace(svc.Url) || string.IsNullOrWhiteSpace(svc.ApiKey))
        {
            throw new InvalidOperationException($"{serviceName} is enabled but its URL or API key is not configured.");
        }

        var (matched, changed) = isTv
            ? await UnmonitorSonarrAsync(svc.Url, svc.ApiKey, fileName)
            : await UnmonitorRadarrAsync(svc.Url, svc.ApiKey, fileName);

        if (!matched)
        {
            return $"{serviceName}: no matching monitored {itemWord} found for '{fileName}' - left unchanged.";
        }
        if (changed)
        {
            return $"{serviceName}: unmonitored the matching {itemWord} and rescanned the library.";
        }
        return $"{serviceName}: already unmonitored - rescanned the library to clear its stale downloaded status.";
    }

    private async Task<(bool Matched, bool Changed)> UnmonitorSonarrAsync(string baseUrl, string apiKey, string fileName)
    {
        var parsed = await _client.GetAsync(baseUrl, apiKey, "/api/v3/parse?title=" + Uri.EscapeDataString(fileName));

        var episodes = parsed?["series"] is not null ? parsed["episodes"] as JsonArray : null;
        if (episodes is null || episodes.Count == 0)
        {
            return (false, false);
        }

        var changedAny = false;
        foreach (var episodeNode in episodes)
        {
            var episode = episodeNode as JsonObject;
            if (episode is null) continue;
            if (episode["monitored"]?.GetValue<bool>() == false) continue;

            episode["monitored"] = false;
            var episodeId = episode["id"]!.GetValue<int>();
            await _client.PutAsync(baseUrl, apiKey, $"/api/v3/episode/{episodeId}", episode);
            changedAny = true;
        }

        var seriesId = parsed!["series"]!["id"]!.GetValue<int>();
        var command = new JsonObject { ["name"] = "RescanSeries", ["seriesId"] = seriesId };
        await _client.PostAsync(baseUrl, apiKey, "/api/v3/command", command);

        return (true, changedAny);
    }

    private async Task<(bool Matched, bool Changed)> UnmonitorRadarrAsync(string baseUrl, string apiKey, string fileName)
    {
        var parsed = await _client.GetAsync(baseUrl, apiKey, "/api/v3/parse?title=" + Uri.EscapeDataString(fileName));

        var movie = parsed?["movie"] as JsonObject;
        if (movie is null || movie["id"] is null)
        {
            return (false, false);
        }

        var changed = false;
        if (movie["monitored"]?.GetValue<bool>() != false)
        {
            movie["monitored"] = false;
            var movieId = movie["id"]!.GetValue<int>();
            await _client.PutAsync(baseUrl, apiKey, $"/api/v3/movie/{movieId}", movie);
            changed = true;
        }

        var movieIdForCommand = movie["id"]!.GetValue<int>();
        var command = new JsonObject { ["name"] = "RescanMovie", ["movieId"] = movieIdForCommand };
        await _client.PostAsync(baseUrl, apiKey, "/api/v3/command", command);

        return (true, changed);
    }
}
