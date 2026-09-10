using System.Text.Json.Nodes;
using Compressarr.Core.Config;
using Compressarr.Core.Logging;

namespace Compressarr.Core.Arr;

/// <summary>What actually happened to the library rescan this call triggered (or didn't). Distinct
/// from a boolean "did it work" - a caller deciding whether it's safe to delete the now-supposedly-
/// empty source folder needs to know not just whether the unmonitor+rescan finished, but whether it
/// was ever POSITIVELY CONFIRMED to finish, since Sonarr/Radarr only clears an episode/movie's
/// tracking correctly if the source folder still physically exists at the moment the rescan actually
/// runs - see ICompanionFileService.CleanUpEmptySourceFolder.</summary>
public enum ArrRescanOutcome
{
    /// <summary>The matching service isn't enabled - no coordination was ever required, so there's
    /// nothing that could be left in flight.</summary>
    NotEnabled,

    /// <summary>No matching monitored item was found - no rescan was ever triggered.</summary>
    NoMatch,

    /// <summary>The rescan command reached "completed" - positively confirmed finished.</summary>
    Completed,

    /// <summary>The rescan command reached a terminal "failed" or "aborted" status - it DID finish,
    /// but not successfully, so Sonarr/Radarr's own view of the library after it is not trustworthy.</summary>
    Failed,

    /// <summary>The command never reached a terminal status within the configured poll window - it
    /// may still be running on Sonarr/Radarr's side.</summary>
    TimedOut,

    /// <summary>The wait was cut short by cancellation (e.g. Abort) before a terminal status was
    /// observed.</summary>
    Cancelled,
}

/// <summary>Message is the short human-readable status line callers log as before (null only when
/// the service isn't enabled). SafeToCleanUp tells the caller whether it's actually safe to remove
/// the now-supposedly-empty source folder: only true when arr coordination either wasn't required at
/// all (NotEnabled/NoMatch) or was positively confirmed complete (Completed) - a Failed/TimedOut/
/// Cancelled rescan leaves the caller unable to prove Sonarr/Radarr ever saw the folder while it
/// still existed, so cleanup must wait for a later retry instead.</summary>
public sealed record ArrUnmonitorResult(string? Message, ArrRescanOutcome Outcome)
{
    public bool SafeToCleanUp => Outcome is ArrRescanOutcome.NotEnabled or ArrRescanOutcome.NoMatch or ArrRescanOutcome.Completed;
}

public interface IArrUnmonitorService
{
    /// <summary>Dispatches to Sonarr (TV) or Radarr (Movie) based on isTv, only if that service
    /// is enabled. On a real match, this call polls Sonarr/Radarr's own /api/v3/command/{id} until
    /// the rescan it just triggered actually reaches a terminal status (completed/failed/aborted),
    /// instead of blindly waiting a fixed duration - see WaitForCommandAsync for the poll/timeout
    /// mechanics - and the returned ArrUnmonitorResult.Outcome reflects exactly what was observed,
    /// so callers can gate anything that depends on the rescan having genuinely finished (see
    /// SafeToCleanUp) rather than just having been triggered. cancellationToken (the run's Abort
    /// token) stops the WAIT early if triggered - it does not undo the unmonitor/rescan-trigger,
    /// which has already happened by that point. Throws if the service is enabled but not
    /// configured (blank URL/API key), or if the initial request itself fails — callers are
    /// expected to wrap this in their own try/catch (a broken/unreachable arr instance must never
    /// fail an otherwise-successful conversion).</summary>
    Task<ArrUnmonitorResult> UnmonitorAsync(CompressarrConfig config, string fileName, bool isTv, CancellationToken cancellationToken = default);
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

    public async Task<ArrUnmonitorResult> UnmonitorAsync(CompressarrConfig config, string fileName, bool isTv, CancellationToken cancellationToken = default)
    {
        var (svc, serviceName, itemWord) = isTv
            ? (config.Arrs.Sonarr, "Sonarr", "episode")
            : (config.Arrs.Radarr, "Radarr", "movie");

        if (!svc.Enabled) return new ArrUnmonitorResult(null, ArrRescanOutcome.NotEnabled);

        if (string.IsNullOrWhiteSpace(svc.Url) || string.IsNullOrWhiteSpace(svc.ApiKey))
        {
            throw new InvalidOperationException($"{serviceName} is enabled but its URL or API key is not configured.");
        }

        var (matched, changed, outcome) = isTv
            ? await UnmonitorSonarrAsync(svc.Url, svc.ApiKey, fileName, cancellationToken)
            : await UnmonitorRadarrAsync(svc.Url, svc.ApiKey, fileName, cancellationToken);

        if (!matched)
        {
            return new ArrUnmonitorResult($"{serviceName}: no matching monitored {itemWord} found for '{fileName}' - left unchanged.", ArrRescanOutcome.NoMatch);
        }
        var message = changed
            ? $"{serviceName}: unmonitored the matching {itemWord} and rescanned the library."
            : $"{serviceName}: already unmonitored - rescanned the library to clear its stale downloaded status.";
        return new ArrUnmonitorResult(message, outcome);
    }

    private async Task<(bool Matched, bool Changed, ArrRescanOutcome Outcome)> UnmonitorSonarrAsync(string baseUrl, string apiKey, string fileName, CancellationToken cancellationToken)
    {
        var parsed = await _client.GetAsync(baseUrl, apiKey, "/api/v3/parse?title=" + Uri.EscapeDataString(fileName));

        var episodes = parsed?["series"] is not null ? parsed["episodes"] as JsonArray : null;
        if (episodes is null || episodes.Count == 0)
        {
            return (false, false, ArrRescanOutcome.NoMatch);
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
        var outcome = await WaitForCommandAsync(baseUrl, apiKey, commandResponse, "Sonarr", cancellationToken);

        return (true, changedAny, outcome);
    }

    private async Task<(bool Matched, bool Changed, ArrRescanOutcome Outcome)> UnmonitorRadarrAsync(string baseUrl, string apiKey, string fileName, CancellationToken cancellationToken)
    {
        var parsed = await _client.GetAsync(baseUrl, apiKey, "/api/v3/parse?title=" + Uri.EscapeDataString(fileName));

        var movie = parsed?["movie"] as JsonObject;
        if (movie is null || movie["id"] is null)
        {
            return (false, false, ArrRescanOutcome.NoMatch);
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
        var outcome = await WaitForCommandAsync(baseUrl, apiKey, commandResponse, "Radarr", cancellationToken);

        return (true, changed, outcome);
    }

    /// <summary>Polls /api/v3/command/{id} on an interval until it reaches a terminal status or
    /// _maxPollWait elapses, whichever comes first - replaces a blind fixed wait with actually
    /// knowing the rescan finished (or gave up waiting for it to), and returns exactly what was
    /// observed so the caller can decide whether it's safe to treat the source folder as having
    /// been seen by Sonarr/Radarr while it still existed (see ArrRescanOutcome). A transient poll
    /// failure doesn't abandon the wait - it just tries again next tick, same "don't let a flaky
    /// arr instance fail an otherwise-successful conversion" posture as everything else in this
    /// integration. Logs a warning (not an error - the unmonitor+rescan-trigger already succeeded
    /// regardless) if the command ends in "failed"/"aborted", including Sonarr/Radarr's own
    /// exception message when present, or if the wait gives up after _maxPollWait with no terminal
    /// status ever observed. cancellationToken stops the wait immediately without throwing - Abort
    /// should cut this short, not have it misread as "the unmonitor call itself failed" by the
    /// caller's own try/catch.</summary>
    private async Task<ArrRescanOutcome> WaitForCommandAsync(string baseUrl, string apiKey, JsonNode? commandResponse, string serviceName, CancellationToken cancellationToken)
    {
        var idNode = commandResponse?["id"];
        if (idNode is null)
        {
            // The API shape this hasn't seen before - no command id to poll, so there's no way to
            // confirm completion. Falls back to a fixed best-effort wait (matching the original,
            // pre-polling behavior) rather than not waiting at all, but the outcome is honestly
            // reported as TimedOut - unconfirmed - not Completed, since nothing was actually
            // observed to finish.
            try { await Task.Delay(_fallbackSettleDelay, cancellationToken); }
            catch (OperationCanceledException) { return ArrRescanOutcome.Cancelled; }
            return ArrRescanOutcome.TimedOut;
        }

        var id = idNode.GetValue<int>();
        var deadline = DateTime.UtcNow + _maxPollWait;

        while (DateTime.UtcNow < deadline)
        {
            if (cancellationToken.IsCancellationRequested) return ArrRescanOutcome.Cancelled;

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
                if (statusText == "completed") return ArrRescanOutcome.Completed;

                var exception = status?["exception"]?.GetValue<string>();
                _logger.Log($"  {serviceName} rescan (command {id}) reported {statusText}: {exception ?? "no details given"}", LogSeverity.Error);
                return ArrRescanOutcome.Failed;
            }

            try { await Task.Delay(_pollInterval, cancellationToken); }
            catch (OperationCanceledException) { return ArrRescanOutcome.Cancelled; }
        }

        _logger.Log($"  {serviceName} rescan (command {id}) did not finish within the wait window - source folder cleanup for this file will wait for a later retry.", LogSeverity.Error);
        return ArrRescanOutcome.TimedOut;
    }
}
