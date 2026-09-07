using System.Net;
using Compressarr.Core.Updates;

namespace Compressarr.Core.Tests.Updates;

file sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
    public int CallCount { get; private set; }

    public FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        CallCount++;
        return Task.FromResult(_responder(request));
    }
}

file sealed class FakeHttpClientFactory : IHttpClientFactory
{
    private readonly HttpMessageHandler _handler;
    public FakeHttpClientFactory(HttpMessageHandler handler) => _handler = handler;
    public HttpClient CreateClient(string name) => new(_handler);
}

public class UpdateCheckServiceTests
{
    // Real, very short interval rather than a fake TimeProvider - same tradeoff
    // BackupSchedulerTests/RunLoopControllerTests make.
    private static readonly TimeSpan TinyInterval = TimeSpan.FromMilliseconds(20);

    private static async Task WaitUntil(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition()) return;
            await Task.Delay(5);
        }
        throw new TimeoutException("Condition was not met within the timeout.");
    }

    private static string ReleaseJson(string tag) => $$"""
        { "tag_name": "{{tag}}", "html_url": "https://github.com/MrWizardCT/Compressarr/releases/tag/{{tag}}" }
        """;

    // The installed version at test time is whatever this test assembly's own <Version> is
    // (Directory.Build.props, shared repo-wide) - a tag comfortably below any real release
    // guarantees HasUpdate stays false regardless of what version this repo is actually on, and a
    // tag far in the future guarantees it stays true.
    private const string OldTag = "v0.0.1";
    private const string FutureTag = "v999.0.0";

    [Fact]
    public async Task CheckNowAsync_NewerTagOnGitHub_ReportsHasUpdate()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(ReleaseJson(FutureTag))
        });
        var service = new UpdateCheckService(new FakeHttpClientFactory(handler));

        var result = await service.CheckNowAsync();

        Assert.True(result.CheckedOk);
        Assert.True(result.HasUpdate);
        Assert.Equal(FutureTag, result.LatestVersion);
        Assert.Equal($"https://github.com/MrWizardCT/Compressarr/releases/tag/{FutureTag}", result.ReleaseUrl);
    }

    [Fact]
    public async Task CheckNowAsync_OlderOrEqualTagOnGitHub_ReportsNoUpdate()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(ReleaseJson(OldTag))
        });
        var service = new UpdateCheckService(new FakeHttpClientFactory(handler));

        var result = await service.CheckNowAsync();

        Assert.True(result.CheckedOk);
        Assert.False(result.HasUpdate);
    }

    [Fact]
    public async Task CheckNowAsync_GitHubUnreachable_ReturnsNotCheckedOkRatherThanThrowing()
    {
        var handler = new FakeHttpMessageHandler(_ => throw new HttpRequestException("simulated network failure"));
        var service = new UpdateCheckService(new FakeHttpClientFactory(handler));

        var result = await service.CheckNowAsync();

        Assert.False(result.CheckedOk);
        Assert.NotNull(result.Error);
        Assert.False(result.HasUpdate);
    }

    [Fact]
    public async Task CheckNowAsync_NonSuccessStatus_ReturnsNotCheckedOk()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        var service = new UpdateCheckService(new FakeHttpClientFactory(handler));

        var result = await service.CheckNowAsync();

        Assert.False(result.CheckedOk);
        Assert.False(result.HasUpdate);
    }

    [Fact]
    public async Task Start_RunsFirstCheckImmediately_WithoutWaitingOutTheInterval()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(ReleaseJson(FutureTag))
        });
        // A long interval - if Start() waited it out before its first check, LastResult would
        // still be null when we assert a moment later.
        var service = new UpdateCheckService(new FakeHttpClientFactory(handler), TimeProvider.System, TimeSpan.FromHours(24));

        service.Start();
        await WaitUntil(() => service.LastResult is not null, TimeSpan.FromSeconds(2));

        Assert.True(service.LastResult!.HasUpdate);
        await service.StopAsync();
    }

    [Fact]
    public async Task Start_ChecksAgainAfterIntervalElapses()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(ReleaseJson(OldTag))
        });
        var service = new UpdateCheckService(new FakeHttpClientFactory(handler), TimeProvider.System, TinyInterval);

        service.Start();
        await WaitUntil(() => handler.CallCount >= 3, TimeSpan.FromSeconds(2));

        await service.StopAsync();
    }

    [Fact]
    public void Start_CalledTwice_DoesNotDoubleStart()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(ReleaseJson(OldTag))
        });
        var service = new UpdateCheckService(new FakeHttpClientFactory(handler), TimeProvider.System, TinyInterval);

        service.Start();
        service.Start();

        Assert.True(service.IsRunning);
    }

    [Fact]
    public async Task StopAsync_StopsTheLoop_NoMoreChecksAfterStopping()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(ReleaseJson(OldTag))
        });
        var service = new UpdateCheckService(new FakeHttpClientFactory(handler), TimeProvider.System, TinyInterval);

        service.Start();
        await WaitUntil(() => handler.CallCount >= 1, TimeSpan.FromSeconds(2));
        await service.StopAsync();

        var countAtStop = handler.CallCount;
        await Task.Delay(TinyInterval * 5);

        Assert.Equal(countAtStop, handler.CallCount);
        Assert.False(service.IsRunning);
    }
}
