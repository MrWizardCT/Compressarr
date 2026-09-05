using System.Net.Http.Json;
using System.Text;

namespace Compressarr.Core.Notifications;

/// <summary>Shared HTTP boundary for the fixed-destination notifiers (Discord, Slack, Telegram,
/// Pushover, ntfy, Gotify, Notifiarr, IFTTT) - deliberately narrow instead of a single universal
/// method/headers/content-type-parameterized sender. Always POST; content-type is fixed per
/// method rather than passed in. Headers are optional and additive, for the handful of providers
/// whose own documented API requires exactly one (ntfy's Title/Priority/Tags, Gotify's
/// X-Gotify-Key) - not a general escape hatch for arbitrary request shaping. The Generic Webhook
/// notifier is deliberately NOT built on this - it genuinely needs a configurable method and
/// arbitrary headers, so it keeps its own separate IWebhookSender rather than widening this
/// interface back into the thing it's replacing.</summary>
public interface INotificationHttpClient
{
    Task<HttpResponseMessage> PostJsonAsync(Uri destination, object payload, IReadOnlyDictionary<string, string>? headers, CancellationToken ct);
    Task<HttpResponseMessage> PostFormAsync(Uri destination, IReadOnlyDictionary<string, string> fields, CancellationToken ct);
    Task<HttpResponseMessage> PostTextAsync(Uri destination, string text, IReadOnlyDictionary<string, string>? headers, CancellationToken ct);
}

public sealed class NotificationHttpClient : INotificationHttpClient
{
    private readonly IHttpClientFactory _httpClientFactory;

    public NotificationHttpClient(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public Task<HttpResponseMessage> PostJsonAsync(Uri destination, object payload, IReadOnlyDictionary<string, string>? headers, CancellationToken ct)
    {
        var client = CreateClient();
        if (headers is null || headers.Count == 0)
        {
            return client.PostAsJsonAsync(destination, payload, ct);
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, destination) { Content = JsonContent.Create(payload) };
        ApplyHeaders(request, headers);
        return client.SendAsync(request, ct);
    }

    public Task<HttpResponseMessage> PostFormAsync(Uri destination, IReadOnlyDictionary<string, string> fields, CancellationToken ct)
    {
        var client = CreateClient();
        // WebUtility.UrlEncode (space -> '+'), not Uri.EscapeDataString (space -> %20) - matches
        // real application/x-www-form-urlencoded semantics, same reasoning as the prior
        // hand-built Pushover body this replaces.
        var encoded = string.Join('&', fields.Select(f => $"{System.Net.WebUtility.UrlEncode(f.Key)}={System.Net.WebUtility.UrlEncode(f.Value)}"));
        using var content = new StringContent(encoded, Encoding.UTF8, "application/x-www-form-urlencoded");
        return client.PostAsync(destination, content, ct);
    }

    public Task<HttpResponseMessage> PostTextAsync(Uri destination, string text, IReadOnlyDictionary<string, string>? headers, CancellationToken ct)
    {
        var client = CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, destination)
        {
            // The mediaType parameter must be the bare type - it appends the charset itself from
            // the Encoding argument, so "text/plain; charset=utf-8" here would throw.
            Content = new StringContent(text, Encoding.UTF8, "text/plain")
        };
        if (headers is not null) ApplyHeaders(request, headers);
        return client.SendAsync(request, ct);
    }

    private HttpClient CreateClient()
    {
        var client = _httpClientFactory.CreateClient(nameof(NotificationHttpClient));
        client.Timeout = TimeSpan.FromSeconds(15);
        return client;
    }

    private static void ApplyHeaders(HttpRequestMessage request, IReadOnlyDictionary<string, string> headers)
    {
        foreach (var (key, value) in headers)
        {
            if (!string.IsNullOrWhiteSpace(key)) request.Headers.TryAddWithoutValidation(key, value);
        }
    }
}
