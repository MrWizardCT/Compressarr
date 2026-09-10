using System.Text.Json.Nodes;
using Compressarr.Core.Config;
using Compressarr.Core.Logging;

namespace Compressarr.Core.Arr;

public interface IArrUnmonitorService
{
    /// <summary>Dispatches to Sonarr (TV) or Radarr (Movie) based on isTv, only if that service
    /// is enabled. Returns null (no-op, not an error) if the matching service isn't enabled;
    /// otherwise a short human-readable status string describing the outcome (unmonitored +
    /// rescanned / already unmonitored, rescanned anyway / no match found). On a real match, this
    /// call polls Sonarr/Radarr's own /api/v3/command/{id} until the rescan it just triggered
    /// actually reaches a terminal status (completed/failed/aborted), instead of blindly waiting a
    /// fixed duration - see WaitForCommandAsync for the poll/timeout mechanics. cancellationToken
    /// (the run's Abort token) stops the WAIT early if triggered - it does not undo the unmonitor/
    /// rescan-trigger, which has already happened by that point. Throws if the service is enabled
    /// but not configured (blank URL/API key), or if the initial request itself fails — callers
    /// are expected to wrap this in their own try/catch (a broken/unreachable arr instance must
    /// never fail an otherwise-successful conversion).</summary>
    Task<string?> UnmonitorAsync(CompressarrConfig config, string fileName, bool isTv, CancellationToken cancellationToken = default);
}

/// <summary>Ported from Invoke-CompressarrArrUnmonitor/Invoke-CompressarrSonarrUnmonitor/
/// Invoke-CompressarrRadarrUnmonitor. Deliberately does no fuzzy matching of its own — trusts
/// whatever Sonarr/Radarr's own /api/v3/parse endpoint returns (the same lookup those apps use
/// internally for manual imports); a parse miss is always "leave unchanged," never a guessed
/// match, since a wrong auto-match would unmonitor the wrong show/movie.</summary>
public sealed class ArrUnmonitorService : IArrUnmonitorService
{
    // Confirmed live against real Sonarr and Radarr instances (2026-09-10): the command shape is
    // identical between them - POST /api/v3/command returns the created command's own
    // representation immediately, including "id" and an initial "status" ("started" observed live;
    // "queued" is also a documented possibility under load). GET /api/v3/command/{id} returns that
    // same shape, "status" transitioning to "completed" (with "result":"successful") or "failed"
    // (with a populated "exception" field, confirmed live against a deliberately-bad movie id) -
    // "aborted" is a documented third terminal value, not directly observed, included for
    // completeness (a command manually cancelled from within Sonarr/Radarr's own UI while this is
    // polling).
    private static readonly string[] TerminalStatuses = { "completed", "failed", "aborted" };

    private readonly IArrClient _client;
    private readonly IRunLogger _logger;
    private readonly TimeSpan _pollInterval;
    private readonly TimeSpan _maxPollWait;

    // If the POST response is ever missing "id" (an API shape this hasn't seen, but not something
    // to leave completely unhandled) - falls back to this fixed wait rather than not waiting at
    // all.
    private readonly TimeSpan _fallbackSettleDelay;

    public ArrUnmonitorService(IArrClient client, IRunLogger logger)
        : this(client, logger, TimeSpan.FromSeconds(2), TimeSpan.FromMinutes(2), TimeSpan.FromSeconds(30))
    {
    }

    // Internal setter only so tests can use near-instant intervals instead of real multi-second/
    // multi-minute waits per case.
    internal ArrUnmonitorService(IArrClient client, IRunLogger logger, TimeSpan pollInterval, TimeSpan maxPollWait, TimeSpan fallbackSettleDelay)
    {
        _client = client;
        _logger = logger;
        _pollInterval = pollInterval;
        _maxPollWait = maxPollWait;
        _fallbackSettleDelay = fallbackSettleDelay;
    }

    public async Task<string?> UnmonitorAsync(CompressarrConfig config, string fileName, bool isTv, CancellationToken cancellationToken = default)
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
            ? await UnmonitorSonarrAsync(svc.Url, svc.ApiKey, fileName, cancellationToken)
            : await UnmonitorRadarrAsync(svc.Url, svc.ApiKey, fileName, cancellationToken);

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

    private async Task<(bool Matched, bool Changed)> UnmonitorSonarrAsync(string baseUrl, string apiKey, string fileName, CancellationToken cancellationToken)
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
        var commandResponse = await _client.PostAsync(baseUrl, apiKey, "/api/v3/command", command);
        await WaitForCommandAsync(baseUrl, apiKey, commandResponse, "Sonarr", cancellationToken);

        return (true, changedAny);
    }

    private async Task<(bool Matched, bool Changed)> UnmonitorRadarrAsync(string baseUrl, string apiKey, string fileName, CancellationToken cancellationToken)
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
        var commandResponse = await _client.PostAsync(baseUrl, apiKey, "/api/v3/command", command);
        await WaitForCommandAsync(baseUrl, apiKey, commandResponse, "Radarr", cancellationToken);

        return (true, changed);
    }

    /// <summary>Polls /api/v3/command/{id} on an interval until it reaches a terminal status or
    /// _maxPollWait elapses, whichever comes first - replaces a blind fixed wait with actually
    /// knowing the rescan finished (or gave up waiting for it to). A transient poll failure doesn't
    /// abandon the wait - it just tries again next tick, same "don't let a flaky arr instance fail
    /// an otherwise-successful conversion" posture as everything else in this integration. Logs a
    /// warning (not an error - the unmonitor+rescan-trigger already succeeded regardless) if the
    /// command ends in "failed", including Sonarr/Radarr's own exception message when present.
    /// cancellationToken stops the wait immediately without throwing - Abort should cut this short,
    /// not have it misread as "the unmonitor call itself failed" by the caller's own try/catch.</summary>
    private async Task WaitForCommandAsync(string baseUrl, string apiKey, JsonNode? commandResponse, string serviceName, CancellationToken cancellationToken)
    {
        var idNode = commandResponse?["id"];
        if (idNode is null)
        {
            try { await Task.Delay(_fallbackSettleDelay, cancellationToken); }
            catch (OperationCanceledException) { }
            return;
        }

        var id = idNode.GetValue<int>();
        var deadline = DateTime.UtcNow + _maxPollWait;

        while (DateTime.UtcNow < deadline)
        {
            if (cancellationToken.IsCancellationRequested) return;

            JsonNode? status;
            try
            {
                status = await _client.GetAsync(baseUrl, apiKey, $"/api/v3/command/{id}");
            }
            catch
            {
                status = null;
            }

            var statusText = status?["status"]?.GetValue<string>();
            if (statusText is not null && TerminalStatuses.Contains(statusText))
            {
                if (statusText == "failed")
                {
                    var exception = status?["exception"]?.GetValue<string>();
                    _logger.Log($"  {serviceName} rescan (command {id}) reported failed: {exception ?? "no details given"}", LogSeverity.Error);
                }
                return;
            }

            try { await Task.Delay(_pollInterval, cancellationToken); }
            catch (OperationCanceledException) { return; }
        }
    }
}
