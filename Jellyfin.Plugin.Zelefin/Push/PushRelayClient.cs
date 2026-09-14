using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Jellyfin.Plugin.Zelefin.Api;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Zelefin.Push;

/// <summary>
/// Sends device tokens to the Zelefin publisher relay. Apple credentials stay there,
/// not on each Jellyfin.
/// </summary>
public sealed class PushRelayClient
{
    private readonly HttpClient _http;
    private readonly ILogger<PushRelayClient> _logger;

    public PushRelayClient(HttpClient http, ILogger<PushRelayClient> logger)
    {
        _http = http;
        _logger = logger;
        _http.Timeout = TimeSpan.FromSeconds(20);
    }

    public async Task<RelaySendResult> SendAsync(
        string relayUrl,
        IReadOnlyList<string> tokens,
        PushMessage message,
        CancellationToken cancellationToken)
    {
        if (tokens.Count == 0)
        {
            return new RelaySendResult(true, []);
        }

        if (!Uri.TryCreate(relayUrl.Trim(), UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            return new RelaySendResult(false, []);
        }

        using var response = await _http.PostAsJsonAsync(
            uri,
            Body(tokens, message),
            cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var detail = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogWarning("Push relay returned {Status}: {Body}", (int)response.StatusCode, detail);
            return new RelaySendResult(false, []);
        }

        var parsed = await response.Content
            .ReadFromJsonAsync<RelaySendResponse>(cancellationToken)
            .ConfigureAwait(false);
        return new RelaySendResult(true, parsed?.ExpiredTokens ?? []);
    }

    public static RelaySendRequest Body(IReadOnlyList<string> tokens, PushMessage message) => new()
    {
        Tokens = [.. tokens],
        Title = message.Title,
        Subtitle = message.Subtitle,
        Body = message.Body,
        ItemId = message.ItemId,
        SeriesId = message.SeriesId,
        Type = message.Type
    };
}

public sealed class RelaySendRequest
{
    [JsonPropertyName("tokens")]
    public List<string> Tokens { get; set; } = [];

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("subtitle")]
    public string? Subtitle { get; set; }

    [JsonPropertyName("body")]
    public string Body { get; set; } = string.Empty;

    [JsonPropertyName("itemId")]
    public string? ItemId { get; set; }

    [JsonPropertyName("seriesId")]
    public string? SeriesId { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }
}

public sealed class RelaySendResponse
{
    [JsonPropertyName("expiredTokens")]
    public List<string> ExpiredTokens { get; set; } = [];
}

public readonly record struct RelaySendResult(bool Success, IReadOnlyList<string> ExpiredTokens);
