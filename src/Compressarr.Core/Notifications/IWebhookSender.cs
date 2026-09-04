namespace Compressarr.Core.Notifications;

/// <summary>Shared HTTP boundary every INotifier depends on instead of touching HttpClient
/// directly - centralizes the actual request/timeout/error-shaping once (mirroring how ArrClient
/// centralizes the X-Api-Key header for Sonarr/Radarr) and gives every notifier's tests one fake
/// to depend on instead of one per channel type.</summary>
public interface IWebhookSender
{
    Task<NotifyResult> PostAsync(string url, HttpMethod method, IReadOnlyDictionary<string, string> headers, string body, string contentType, CancellationToken ct);
}

public sealed class WebhookSender : IWebhookSender
{
    private readonly IHttpClientFactory _httpClientFactory;

    public WebhookSender(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public async Task<NotifyResult> PostAsync(string url, HttpMethod method, IReadOnlyDictionary<string, string> headers, string body, string contentType, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return new NotifyResult(false, "URL is required.");
        }

        try
        {
            using var request = new HttpRequestMessage(method, url)
            {
                Content = new StringContent(body, System.Text.Encoding.UTF8, contentType)
            };
            foreach (var (key, value) in headers)
            {
                if (!string.IsNullOrWhiteSpace(key)) request.Headers.TryAddWithoutValidation(key, value);
            }

            var client = _httpClientFactory.CreateClient(nameof(WebhookSender));
            client.Timeout = TimeSpan.FromSeconds(15);

            using var response = await client.SendAsync(request, ct);
            if (response.IsSuccessStatusCode)
            {
                return new NotifyResult(true, $"Sent ({(int)response.StatusCode}).");
            }

            var responseBody = await response.Content.ReadAsStringAsync(ct);
            var detail = string.IsNullOrWhiteSpace(responseBody) ? "" : $" - {Truncate(responseBody, 200)}";
            return new NotifyResult(false, $"{(int)response.StatusCode} {response.ReasonPhrase}{detail}");
        }
        catch (Exception ex)
        {
            return new NotifyResult(false, $"Failed: {ex.Message}");
        }
    }

    private static string Truncate(string text, int maxLength) =>
        text.Length <= maxLength ? text : text[..maxLength] + "...";
}
