using System.Text.Json;

namespace Compressarr.Core.Updates;

public sealed record UpdateCheckResult(
    bool CheckedOk,
    string? Error,
    string LatestVersion,
    string ReleaseUrl,
    bool HasUpdate,
    DateTimeOffset CheckedAt);

/// <summary>Drives the GitHub releases check on a background loop for the app's whole lifetime,
/// same always-on posture as IBackupScheduler/IDigestScheduler. Runs its first check immediately
/// on Start() rather than waiting out the check interval - the app restarting is itself the signal
/// that's most likely to have just resolved a previously-flagged update (a fresh install replaces
/// the running process), so a stale "update available" shouldn't need to wait up to a full day to
/// clear after that. LastResult is served directly by the /api/about/check-update endpoint instead
/// of that endpoint hitting GitHub itself on every request.</summary>
public interface IUpdateCheckService
{
    UpdateCheckResult? LastResult { get; }
    bool IsRunning { get; }

    /// <summary>Idempotent - a second call while already running is a no-op.</summary>
    void Start();

    Task StopAsync();

    /// <summary>Runs one check immediately regardless of the loop's own schedule and updates
    /// LastResult - used as a same-request fallback by the endpoint when no background check has
    /// completed yet (Start() not called, or its first check is still in flight).</summary>
    Task<UpdateCheckResult> CheckNowAsync();
}

public sealed class UpdateCheckService : IUpdateCheckService, IDisposable
{
    // Same repo AboutEndpoints already pointed at - see its own comment for why this is a plain
    // constant rather than derived from anything.
    private const string RepoOwner = "MrWizardCT";
    private const string RepoName = "Compressarr";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _checkInterval;

    private CancellationTokenSource? _cts;
    private Task? _loopTask;

    public UpdateCheckResult? LastResult { get; private set; }

    public UpdateCheckService(IHttpClientFactory httpClientFactory)
        : this(httpClientFactory, TimeProvider.System, TimeSpan.FromHours(24))
    {
    }

    // Internal ctor lets tests drive the loop with a tiny check interval and a fake clock instead
    // of waiting a real day between ticks - same pattern BackupScheduler/DigestScheduler use.
    internal UpdateCheckService(IHttpClientFactory httpClientFactory, TimeProvider timeProvider, TimeSpan checkInterval)
    {
        _httpClientFactory = httpClientFactory;
        _timeProvider = timeProvider;
        _checkInterval = checkInterval;
    }

    public bool IsRunning => _cts is not null;

    public void Start()
    {
        if (_cts is not null) return;

        _cts = new CancellationTokenSource();
        _loopTask = LoopAsync(_cts.Token);
    }

    public async Task StopAsync()
    {
        var cts = _cts;
        if (cts is null) return;

        cts.Cancel();
        try { await (_loopTask ?? Task.CompletedTask); }
        catch (OperationCanceledException) { }
        _cts = null;
    }

    private async Task LoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            await CheckNowAsync();

            try { await Task.Delay(_checkInterval, token); }
            catch (OperationCanceledException) { break; }
        }
    }

    public async Task<UpdateCheckResult> CheckNowAsync()
    {
        var installed = typeof(UpdateCheckService).Assembly.GetName().Version ?? new Version(0, 0, 0);
        var checkedAt = _timeProvider.GetUtcNow();

        try
        {
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(10);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Compressarr-UpdateChecker");

            var response = await client.GetAsync($"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases/latest");
            if (!response.IsSuccessStatusCode)
            {
                var failed = new UpdateCheckResult(false, $"GitHub returned HTTP {(int)response.StatusCode}.", "", "", false, checkedAt);
                LastResult = failed;
                return failed;
            }

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var tagName = doc.RootElement.TryGetProperty("tag_name", out var tagProp) ? tagProp.GetString() ?? "" : "";
            var releaseUrl = doc.RootElement.TryGetProperty("html_url", out var urlProp) ? urlProp.GetString() ?? "" : "";

            var latestVersionText = tagName.TrimStart('v', 'V');
            var hasUpdate = Version.TryParse(latestVersionText, out var latest) && latest > installed;

            var result = new UpdateCheckResult(true, null, tagName, releaseUrl, hasUpdate, checkedAt);
            LastResult = result;
            return result;
        }
        catch (Exception ex)
        {
            var failed = new UpdateCheckResult(false, ex.Message, "", "", false, checkedAt);
            LastResult = failed;
            return failed;
        }
    }

    public void Dispose() => _cts?.Cancel();
}
