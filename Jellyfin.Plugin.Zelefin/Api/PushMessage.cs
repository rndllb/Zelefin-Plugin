using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Zelefin.Api;

public class PushNotificationRequest
{
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("subtitle")]
    public string? Subtitle { get; set; }

    [JsonPropertyName("body")]
    public string? Body { get; set; }

    [JsonPropertyName("userId")]
    public Guid? UserId { get; set; }

    [JsonPropertyName("username")]
    public string? Username { get; set; }

    [JsonPropertyName("isAdmin")]
    public bool IsAdmin { get; set; }

    [JsonPropertyName("itemId")]
    public string? ItemId { get; set; }

    [JsonPropertyName("seriesId")]
    public string? SeriesId { get; set; }
}

public class PushMessage
{
    public string Title { get; set; } = string.Empty;

    public string? Subtitle { get; set; }

    public string Body { get; set; } = string.Empty;

    public string? ItemId { get; set; }

    public string? SeriesId { get; set; }

    public string? Type { get; set; }

    public static bool IsValid(PushNotificationRequest request)
    {
        var title = request.Title?.Trim() ?? string.Empty;
        var body = request.Body?.Trim() ?? string.Empty;
        if (!string.IsNullOrEmpty(title) && !string.IsNullOrEmpty(body))
        {
            return true;
        }

        return string.IsNullOrEmpty(title) && !string.IsNullOrEmpty(body);
    }

    public static PushMessage From(PushNotificationRequest request) => new()
    {
        Title = string.IsNullOrWhiteSpace(request.Title) ? "Zelefin" : request.Title.Trim(),
        Subtitle = string.IsNullOrWhiteSpace(request.Subtitle) ? null : request.Subtitle.Trim(),
        Body = request.Body?.Trim() ?? string.Empty,
        ItemId = EmptyToNull(request.ItemId),
        SeriesId = EmptyToNull(request.SeriesId)
    };

    private static string? EmptyToNull(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}
