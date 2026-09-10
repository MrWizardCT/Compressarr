using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace Compressarr.Core.Arr;

public interface IArrClient
{
    Task<JsonNode?> GetAsync(string baseUrl, string apiKey, string relativePath);
    Task PutAsync(string baseUrl, string apiKey, string relativePath, JsonNode body);

    /// <summary>Returns the response body - for a command POST (e.g. /api/v3/command), this is
    /// the created command's own representation, including its "id", needed to poll
    /// /api/v3/command/{id} afterward for real completion status instead of guessing with a fixed
    /// wait.</summary>
    Task<JsonNode?> PostAsync(string baseUrl, string apiKey, string relativePath, JsonNode body);
}

/// <summary>Thin wrapper around HttpClient with the X-Api-Key header, so callers don't repeat
/// that everywhere. Ported from Invoke-CompressarrArrRequest.</summary>
public sealed class ArrClient : IArrClient
{
    private readonly IHttpClientFactory _httpClientFactory;

    public ArrClient(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public async Task<JsonNode?> GetAsync(string baseUrl, string apiKey, string relativePath)
    {
        using var request = CreateRequest(HttpMethod.Get, baseUrl, apiKey, relativePath);
        using var client = CreateClient();
        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonNode>();
    }

    public async Task PutAsync(string baseUrl, string apiKey, string relativePath, JsonNode body)
    {
        using var request = CreateRequest(HttpMethod.Put, baseUrl, apiKey, relativePath);
        request.Content = JsonContent.Create(body);
        using var client = CreateClient();
        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }

    public async Task<JsonNode?> PostAsync(string baseUrl, string apiKey, string relativePath, JsonNode body)
    {
        using var request = CreateRequest(HttpMethod.Post, baseUrl, apiKey, relativePath);
        request.Content = JsonContent.Create(body);
        using var client = CreateClient();
        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonNode>();
    }

    private HttpClient CreateClient()
    {
        var client = _httpClientFactory.CreateClient(nameof(ArrClient));
        client.Timeout = TimeSpan.FromSeconds(15);
        return client;
    }

    private static HttpRequestMessage CreateRequest(HttpMethod method, string baseUrl, string apiKey, string relativePath)
    {
        var uri = baseUrl.TrimEnd('/') + relativePath;
        var request = new HttpRequestMessage(method, uri);
        request.Headers.Add("X-Api-Key", apiKey);
        return request;
    }
}
