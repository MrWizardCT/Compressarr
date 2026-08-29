using System.Text.Json.Nodes;
using Compressarr.Core.Arr;
using Compressarr.Core.Config;

namespace Compressarr.Core.Tests.Arr;

/// <summary>Fakes at the IArrClient seam (rather than HttpMessageHandler) since that's the
/// boundary ArrUnmonitorService actually depends on — equivalent coverage with less plumbing.</summary>
file sealed class FakeArrClient : IArrClient
{
    public JsonNode? ParseResponse { get; set; }
    public List<(string Method, string Path)> Calls { get; } = new();

    public Task<JsonNode?> GetAsync(string baseUrl, string apiKey, string relativePath)
    {
        Calls.Add(("GET", relativePath));
        return Task.FromResult(ParseResponse);
    }

    public Task PutAsync(string baseUrl, string apiKey, string relativePath, JsonNode body)
    {
        Calls.Add(("PUT", relativePath));
        return Task.CompletedTask;
    }

    public Task PostAsync(string baseUrl, string apiKey, string relativePath, JsonNode body)
    {
        Calls.Add(("POST", relativePath));
        return Task.CompletedTask;
    }
}

public class ArrUnmonitorServiceTests
{
    private static CompressarrConfig ConfigWith(ArrServiceSettings sonarr) =>
        new() { Arrs = new ArrSettings { Sonarr = sonarr, Radarr = new ArrServiceSettings() } };

    [Fact]
    public async Task UnmonitorAsync_ServiceDisabled_ReturnsNullWithoutCallingClient()
    {
        var client = new FakeArrClient();
        var service = new ArrUnmonitorService(client);
        var config = ConfigWith(new ArrServiceSettings { Enabled = false });

        var result = await service.UnmonitorAsync(config, "Show.S01E01.mkv", isTv: true);

        Assert.Null(result);
        Assert.Empty(client.Calls);
    }

    [Fact]
    public async Task UnmonitorAsync_EnabledButUnconfigured_Throws()
    {
        var client = new FakeArrClient();
        var service = new ArrUnmonitorService(client);
        var config = ConfigWith(new ArrServiceSettings { Enabled = true, Url = "", ApiKey = "" });

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.UnmonitorAsync(config, "Show.S01E01.mkv", isTv: true));
    }

    [Fact]
    public async Task UnmonitorAsync_NoMatch_ReturnsUnchangedStatusAndDoesNotCallPutOrPost()
    {
        var client = new FakeArrClient { ParseResponse = JsonNode.Parse("""{"series":null,"episodes":[]}""") };
        var service = new ArrUnmonitorService(client);
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
        var service = new ArrUnmonitorService(client);
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
        var service = new ArrUnmonitorService(client);
        var config = ConfigWith(new ArrServiceSettings { Enabled = true, Url = "http://sonarr:8989", ApiKey = "key" });

        var result = await service.UnmonitorAsync(config, "Show.S01E01.mkv", isTv: true);

        Assert.Contains("unmonitored the matching episode", result);
        Assert.Contains(client.Calls, c => c.Method == "PUT" && c.Path == "/api/v3/episode/7");
        Assert.Contains(client.Calls, c => c.Method == "POST" && c.Path == "/api/v3/command");
    }
}
