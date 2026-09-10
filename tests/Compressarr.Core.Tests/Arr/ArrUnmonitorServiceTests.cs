using System.Text.Json.Nodes;
using Compressarr.Core.Arr;
using Compressarr.Core.Config;
using Compressarr.Core.Logging;

namespace Compressarr.Core.Tests.Arr;

/// <summary>Fakes at the IArrClient seam (rather than HttpMessageHandler) since that's the
/// boundary ArrUnmonitorService actually depends on — equivalent coverage with less plumbing.
/// Distinguishes the /api/v3/command/{id} polling GET from the /api/v3/parse lookup GET by path,
/// since WaitForCommandAsync now polls the former after the rescan POST. Defaults to a command
/// that reports "completed" on the very first poll, so tests that don't care about polling
/// specifics (most of them) see zero extra delay even using ArrUnmonitorService's real public
/// constructor and its real (multi-second/multi-minute) default timing.</summary>
file sealed class FakeArrClient : IArrClient
{
    public JsonNode? ParseResponse { get; set; }
    public JsonNode? CommandPostResponse { get; set; } = JsonNode.Parse("""{"id": 1}""");
    public Queue<JsonNode?> CommandStatusResponses { get; } = new(new[] { JsonNode.Parse("""{"id": 1, "status": "completed", "result": "successful"}""") });
    public List<(string Method, string Path)> Calls { get; } = new();

    public Task<JsonNode?> GetAsync(string baseUrl, string apiKey, string relativePath)
    {
        Calls.Add(("GET", relativePath));
        if (relativePath.StartsWith("/api/v3/command/", StringComparison.Ordinal))
        {
            return Task.FromResult(CommandStatusResponses.Count > 0 ? CommandStatusResponses.Dequeue() : null);
        }
        return Task.FromResult(ParseResponse);
    }

    public Task PutAsync(string baseUrl, string apiKey, string relativePath, JsonNode body)
    {
        Calls.Add(("PUT", relativePath));
        return Task.CompletedTask;
    }

    public Task<JsonNode?> PostAsync(string baseUrl, string apiKey, string relativePath, JsonNode body)
    {
        Calls.Add(("POST", relativePath));
        return Task.FromResult(CommandPostResponse);
    }
}

file sealed class NoOpRunLogger : IRunLogger
{
    public event Action<string, LogSeverity>? LineWritten;
    public List<(string Message, LogSeverity Severity)> Logs { get; } = new();
    public bool HasLoggedError => Logs.Any(l => l.Severity == LogSeverity.Error);
    public string Initialize(string logFilePath, string timestamp) => "";
    public void Log(string message, LogSeverity severity = LogSeverity.Info) => Logs.Add((message, severity));
    public void LogProblem(string key, string message) { }
    public void ClearProblem(string key) { }
    public bool HasLaneProblemsChanged(string laneId, IReadOnlyCollection<string> problemCodes) => true;
    public void FileStart(string laneDisplayName, int index, int total, string fileName, double sizeGb, string contentType, string preset) { }
    public void FileComplete(string fileName, double beginSizeGb, double endSizeGb, TimeSpan duration, bool success, string? detailLogFile) { }
}

public class ArrUnmonitorServiceTests
{
    private static CompressarrConfig ConfigWith(ArrServiceSettings sonarr) =>
        new() { Arrs = new ArrSettings { Sonarr = sonarr, Radarr = new ArrServiceSettings() } };

    // Tiny but real (not zero) intervals - matches this codebase's usual "real tiny interval, no
    // fake clock" precedent for anything that goes through a real Task.Delay.
    private static readonly TimeSpan TinyInterval = TimeSpan.FromMilliseconds(10);
    private static readonly TimeSpan TinyMaxWait = TimeSpan.FromMilliseconds(100);

    [Fact]
    public async Task UnmonitorAsync_ServiceDisabled_ReturnsNullWithoutCallingClient()
    {
        var client = new FakeArrClient();
        var service = new ArrUnmonitorService(client, new NoOpRunLogger(), TinyInterval, TinyMaxWait, TinyInterval);
        var config = ConfigWith(new ArrServiceSettings { Enabled = false });

        var result = await service.UnmonitorAsync(config, "Show.S01E01.mkv", isTv: true);

        Assert.Null(result);
        Assert.Empty(client.Calls);
    }

    [Fact]
    public async Task UnmonitorAsync_EnabledButUnconfigured_Throws()
    {
        var client = new FakeArrClient();
        var service = new ArrUnmonitorService(client, new NoOpRunLogger(), TinyInterval, TinyMaxWait, TinyInterval);
        var config = ConfigWith(new ArrServiceSettings { Enabled = true, Url = "", ApiKey = "" });

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.UnmonitorAsync(config, "Show.S01E01.mkv", isTv: true));
    }

    [Fact]
    public async Task UnmonitorAsync_NoMatch_ReturnsUnchangedStatusAndDoesNotCallPutOrPost()
    {
        var client = new FakeArrClient { ParseResponse = JsonNode.Parse("""{"series":null,"episodes":[]}""") };
        var service = new ArrUnmonitorService(client, new NoOpRunLogger(), TinyInterval, TinyMaxWait, TinyInterval);
        var config = ConfigWith(new ArrServiceSettings { Enabled = true, Url = "http://sonarr:8989", ApiKey = "key" });

        var result = await service.UnmonitorAsync(config, "Unmatched.mkv", isTv: true);

        Assert.Contains("no matching monitored episode", result);
        Assert.DoesNotContain(client.Calls, c => c.Method is "PUT" or "POST");
    }

    [Fact]
    public async Task UnmonitorAsync_MatchedAlreadyUnmonitored_StillRescans()
    {
        var client = new FakeArrClient
        {
            ParseResponse = JsonNode.Parse("""
                {
                  "series": { "id": 42 },
                  "episodes": [ { "id": 7, "monitored": false } ]
                }
                """)
        };
        var service = new ArrUnmonitorService(client, new NoOpRunLogger(), TinyInterval, TinyMaxWait, TinyInterval);
        var config = ConfigWith(new ArrServiceSettings { Enabled = true, Url = "http://sonarr:8989", ApiKey = "key" });

        var result = await service.UnmonitorAsync(config, "Show.S01E01.mkv", isTv: true);

        Assert.Contains("already unmonitored", result);
        Assert.DoesNotContain(client.Calls, c => c.Method == "PUT"); // episode already unmonitored, no flip needed
        Assert.Contains(client.Calls, c => c.Method == "POST" && c.Path == "/api/v3/command"); // rescan still fires
    }

    [Fact]
    public async Task UnmonitorAsync_MatchedAndMonitored_UnmonitorsAndRescans()
    {
        var client = new FakeArrClient
        {
            ParseResponse = JsonNode.Parse("""
                {
                  "series": { "id": 42 },
                  "episodes": [ { "id": 7, "monitored": true } ]
                }
                """)
        };
        var service = new ArrUnmonitorService(client, new NoOpRunLogger(), TinyInterval, TinyMaxWait, TinyInterval);
        var config = ConfigWith(new ArrServiceSettings { Enabled = true, Url = "http://sonarr:8989", ApiKey = "key" });

        var result = await service.UnmonitorAsync(config, "Show.S01E01.mkv", isTv: true);

        Assert.Contains("unmonitored the matching episode", result);
        Assert.Contains(client.Calls, c => c.Method == "PUT" && c.Path == "/api/v3/episode/7");
        Assert.Contains(client.Calls, c => c.Method == "POST" && c.Path == "/api/v3/command");
    }

    // Real API shapes confirmed live against Sonarr/Radarr 2026-09-10 - see ArrUnmonitorService's
    // own doc comments for the exact verification. These tests exercise the actual polling
    // mechanics (WaitForCommandAsync) that replaced the old fixed 30-second wait.

    [Fact]
    public async Task UnmonitorAsync_CommandStaysStartedThenCompletes_PollsMultipleTimesBeforeReturning()
    {
        var client = new FakeArrClient
        {
            ParseResponse = JsonNode.Parse("""{ "movie": { "id": 99, "monitored": true } }"""),
            CommandPostResponse = JsonNode.Parse("""{ "id": 5, "status": "started", "result": "unknown" }""")
        };
        client.CommandStatusResponses.Clear();
        client.CommandStatusResponses.Enqueue(JsonNode.Parse("""{ "id": 5, "status": "started", "result": "unknown" }"""));
        client.CommandStatusResponses.Enqueue(JsonNode.Parse("""{ "id": 5, "status": "started", "result": "unknown" }"""));
        client.CommandStatusResponses.Enqueue(JsonNode.Parse("""{ "id": 5, "status": "completed", "result": "successful" }"""));

        var service = new ArrUnmonitorService(client, new NoOpRunLogger(), TinyInterval, TinyMaxWait, TinyInterval);
        var config = ConfigWith(new ArrServiceSettings());
        config.Arrs.Radarr = new ArrServiceSettings { Enabled = true, Url = "http://radarr:7878", ApiKey = "key" };

        var result = await service.UnmonitorAsync(config, "Movie (2026).mkv", isTv: false);

        Assert.Contains("unmonitored the matching movie", result);
        Assert.Equal(3, client.Calls.Count(c => c.Method == "GET" && c.Path == "/api/v3/command/5"));
    }

    [Fact]
    public async Task UnmonitorAsync_CommandNeverReachesTerminalStatus_GivesUpAfterMaxWaitInsteadOfHanging()
    {
        // No CommandStatusResponses queued at all - GetAsync's fallback returns null forever,
        // matching what would happen if a status poll consistently failed/returned no usable data.
        var client = new FakeArrClient
        {
            ParseResponse = JsonNode.Parse("""{ "movie": { "id": 99, "monitored": true } }"""),
            CommandPostResponse = JsonNode.Parse("""{ "id": 5, "status": "started" }""")
        };
        client.CommandStatusResponses.Clear();

        var service = new ArrUnmonitorService(client, new NoOpRunLogger(), TinyInterval, TinyMaxWait, TinyInterval);
        var config = ConfigWith(new ArrServiceSettings());
        config.Arrs.Radarr = new ArrServiceSettings { Enabled = true, Url = "http://radarr:7878", ApiKey = "key" };

        // The real assertion is that this call returns AT ALL within a sane test timeout, instead
        // of hanging indefinitely - TinyMaxWait (100ms) bounds it. Reference-compares which task
        // actually won the race, rather than type-checking the result (an async method's returned
        // Task isn't literally typeof(Task<string?>) at the implementation level).
        var unmonitorTask = service.UnmonitorAsync(config, "Movie (2026).mkv", isTv: false);
        var completed = await Task.WhenAny(unmonitorTask, Task.Delay(TimeSpan.FromSeconds(5)));

        Assert.Same(unmonitorTask, completed);
    }

    [Fact]
    public async Task UnmonitorAsync_CommandFails_LogsWarningWithException()
    {
        var client = new FakeArrClient
        {
            ParseResponse = JsonNode.Parse("""{ "movie": { "id": 99, "monitored": true } }"""),
            CommandPostResponse = JsonNode.Parse("""{ "id": 5, "status": "started" }""")
        };
        client.CommandStatusResponses.Clear();
        client.CommandStatusResponses.Enqueue(JsonNode.Parse("""{ "id": 5, "status": "failed", "exception": "Movie with ID 99 does not exist" }"""));

        var logger = new NoOpRunLogger();
        var service = new ArrUnmonitorService(client, logger, TinyInterval, TinyMaxWait, TinyInterval);
        var config = ConfigWith(new ArrServiceSettings());
        config.Arrs.Radarr = new ArrServiceSettings { Enabled = true, Url = "http://radarr:7878", ApiKey = "key" };

        // The unmonitor+rescan-trigger itself still succeeded (matched, PUT/POST both fired) -
        // only the rescan's OWN outcome failed, which is a warning, not something that should make
        // UnmonitorAsync itself throw or report failure.
        var result = await service.UnmonitorAsync(config, "Movie (2026).mkv", isTv: false);
        Assert.Contains("unmonitored the matching movie", result);

        Assert.Contains(logger.Logs, l => l.Severity == LogSeverity.Error && l.Message.Contains("Movie with ID 99 does not exist"));
    }

    [Fact]
    public async Task UnmonitorAsync_PostResponseMissingCommandId_FallsBackToFixedDelayInsteadOfNotWaitingAtAll()
    {
        var client = new FakeArrClient
        {
            ParseResponse = JsonNode.Parse("""{ "movie": { "id": 99, "monitored": true } }"""),
            CommandPostResponse = JsonNode.Parse("""{}""") // no "id" at all - an API shape this hasn't seen
        };

        var fallbackDelay = TimeSpan.FromMilliseconds(30);
        var service = new ArrUnmonitorService(client, new NoOpRunLogger(), TinyInterval, TinyMaxWait, fallbackDelay);
        var config = ConfigWith(new ArrServiceSettings());
        config.Arrs.Radarr = new ArrServiceSettings { Enabled = true, Url = "http://radarr:7878", ApiKey = "key" };

        var sw = System.Diagnostics.Stopwatch.StartNew();
        await service.UnmonitorAsync(config, "Movie (2026).mkv", isTv: false);
        sw.Stop();

        Assert.True(sw.Elapsed >= fallbackDelay, $"Expected to wait at least {fallbackDelay}, only waited {sw.Elapsed}");
        // Never even tried to poll a command status - there was no id to poll with.
        Assert.DoesNotContain(client.Calls, c => c.Method == "GET" && c.Path.StartsWith("/api/v3/command/"));
    }
}
