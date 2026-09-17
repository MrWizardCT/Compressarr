using System.Net;
using System.Text.Json.Nodes;
using Compressarr.Core.Arr;

namespace Compressarr.Core.Tests.Arr;

file sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
    public HttpRequestMessage? LastRequest { get; private set; }

    public FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        LastRequest = request;
        return Task.FromResult(_responder(request));
    }
}

file sealed class FakeHttpClientFactory : IHttpClientFactory
{
    private readonly HttpMessageHandler _handler;
    public FakeHttpClientFactory(HttpMessageHandler handler) => _handler = handler;
    public HttpClient CreateClient(string name) => new(_handler);
}

// Architecture-roadmap item 5: baseUrl.TrimEnd('/') + relativePath was plain string
// concatenation with no real URL validation - a trailing slash, stray whitespace, or a genuinely
// malformed URL either produced a subtly wrong request or failed with a confusing error deep
// inside HttpClient instead of a clear one here.
public class ArrClientTests
{
    private static HttpResponseMessage OkJson() => new(HttpStatusCode.OK)
    {
        Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json")
    };

    [Fact]
    public async Task GetAsync_BaseUrlWithNoTrailingSlash_BuildsCorrectUrl()
    {
        var handler = new FakeHttpMessageHandler(_ => OkJson());
        var client = new ArrClient(new FakeHttpClientFactory(handler));

        await client.GetAsync("http://localhost:8989", "key", "/api/v3/command/1");

        Assert.Equal("http://localhost:8989/api/v3/command/1", handler.LastRequest!.RequestUri!.AbsoluteUri);
    }

    [Fact]
    public async Task GetAsync_BaseUrlWithTrailingSlash_DoesNotDoubleTheSlash()
    {
        var handler = new FakeHttpMessageHandler(_ => OkJson());
        var client = new ArrClient(new FakeHttpClientFactory(handler));

        await client.GetAsync("http://localhost:8989/", "key", "/api/v3/command/1");

        Assert.Equal("http://localhost:8989/api/v3/command/1", handler.LastRequest!.RequestUri!.AbsoluteUri);
    }

    [Fact]
    public async Task GetAsync_BaseUrlWithMultipleTrailingSlashes_DoesNotDoubleTheSlash()
    {
        var handler = new FakeHttpMessageHandler(_ => OkJson());
        var client = new ArrClient(new FakeHttpClientFactory(handler));

        await client.GetAsync("http://localhost:8989///", "key", "/api/v3/command/1");

        Assert.Equal("http://localhost:8989/api/v3/command/1", handler.LastRequest!.RequestUri!.AbsoluteUri);
    }

    [Fact]
    public async Task GetAsync_BaseUrlWithStrayWhitespace_IsTrimmed()
    {
        var handler = new FakeHttpMessageHandler(_ => OkJson());
        var client = new ArrClient(new FakeHttpClientFactory(handler));

        await client.GetAsync("  http://localhost:8989  ", "key", "/api/v3/command/1");

        Assert.Equal("http://localhost:8989/api/v3/command/1", handler.LastRequest!.RequestUri!.AbsoluteUri);
    }

    [Fact]
    public async Task GetAsync_BaseUrlWithCustomUrlBase_PreservesThePathSegment()
    {
        // A real, supported Sonarr/Radarr configuration for a reverse-proxied instance - must not
        // be silently dropped by whatever URL-combining approach replaces the old string concat.
        var handler = new FakeHttpMessageHandler(_ => OkJson());
        var client = new ArrClient(new FakeHttpClientFactory(handler));

        await client.GetAsync("http://localhost:8989/sonarr", "key", "/api/v3/command/1");

        Assert.Equal("http://localhost:8989/sonarr/api/v3/command/1", handler.LastRequest!.RequestUri!.AbsoluteUri);
    }

    [Fact]
    public async Task GetAsync_RelativePathWithQueryString_IsPreserved()
    {
        var handler = new FakeHttpMessageHandler(_ => OkJson());
        var client = new ArrClient(new FakeHttpClientFactory(handler));

        await client.GetAsync("http://localhost:8989", "key", "/api/v3/parse?title=" + Uri.EscapeDataString("Some Show S01E01"));

        Assert.Equal("http://localhost:8989/api/v3/parse?title=Some%20Show%20S01E01", handler.LastRequest!.RequestUri!.AbsoluteUri);
    }

    [Fact]
    public async Task GetAsync_MalformedBaseUrl_ThrowsAClearException()
    {
        var handler = new FakeHttpMessageHandler(_ => OkJson());
        var client = new ArrClient(new FakeHttpClientFactory(handler));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.GetAsync("not a url", "key", "/api/v3/command/1"));
        Assert.Contains("not a url", ex.Message);
    }

    [Fact]
    public async Task GetAsync_SendsApiKeyHeader()
    {
        var handler = new FakeHttpMessageHandler(_ => OkJson());
        var client = new ArrClient(new FakeHttpClientFactory(handler));

        await client.GetAsync("http://localhost:8989", "my-api-key", "/api/v3/command/1");

        Assert.Equal("my-api-key", handler.LastRequest!.Headers.GetValues("X-Api-Key").Single());
    }

    [Fact]
    public async Task PutAsync_BuildsCorrectUrlAndBody()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var client = new ArrClient(new FakeHttpClientFactory(handler));
        var body = new JsonObject { ["monitored"] = false };

        await client.PutAsync("http://localhost:8989/", "key", "/api/v3/episode/42", body);

        Assert.Equal("http://localhost:8989/api/v3/episode/42", handler.LastRequest!.RequestUri!.AbsoluteUri);
        Assert.Equal(HttpMethod.Put, handler.LastRequest.Method);
    }

    // Architecture-roadmap item 4: GetAsync/PostAsync/PutAsync took no CancellationToken at all, so
    // Abort's token died before it ever reached a Sonarr/Radarr HTTP call. Decisive rather than just
    // checking a parameter exists: an already-cancelled token must actually abort the call - if the
    // token weren't really wired through to SendAsync, this would just succeed with the fake
    // handler's normal OK response instead of throwing.
    [Fact]
    public async Task GetAsync_CancelledToken_ThrowsInsteadOfCompleting()
    {
        var handler = new FakeHttpMessageHandler(_ => OkJson());
        var client = new ArrClient(new FakeHttpClientFactory(handler));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.GetAsync("http://localhost:8989", "key", "/api/v3/command/1", cts.Token));
    }

    [Fact]
    public async Task PutAsync_CancelledToken_ThrowsInsteadOfCompleting()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var client = new ArrClient(new FakeHttpClientFactory(handler));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.PutAsync("http://localhost:8989", "key", "/api/v3/episode/1", new JsonObject(), cts.Token));
    }

    [Fact]
    public async Task PostAsync_CancelledToken_ThrowsInsteadOfCompleting()
    {
        var handler = new FakeHttpMessageHandler(_ => OkJson());
        var client = new ArrClient(new FakeHttpClientFactory(handler));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.PostAsync("http://localhost:8989", "key", "/api/v3/command", new JsonObject(), cts.Token));
    }
}
