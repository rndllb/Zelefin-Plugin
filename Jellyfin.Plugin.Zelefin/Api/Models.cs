using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Zelefin.Api;

/// <summary>
/// Public config the Zelefin app fetches after sign-in. Apple Push secrets stay on the
/// publisher relay, never in this payload.
/// </summary>
public class ClientConfig
{
    [JsonPropertyName("seerrUrl")]
    public string SeerrUrl { get; set; } = string.Empty;

    [JsonPropertyName("seerrApiKey")]
    public string SeerrApiKey { get; set; } = string.Empty;

    [JsonPropertyName("seerrSignInReady")]
    public bool SeerrSignInReady { get; set; }

    [JsonPropertyName("autoSkipIntro")]
    public bool AutoSkipIntro { get; set; }

    [JsonPropertyName("autoSkipCredits")]
    public bool AutoSkipCredits { get; set; }

    [JsonPropertyName("lockAutoSkip")]
    public bool LockAutoSkip { get; set; }

    [JsonPropertyName("notificationsReady")]
    public bool NotificationsReady { get; set; }

    public static ClientConfig From(Configuration.PluginConfiguration config) => new()
    {
        SeerrUrl = Configuration.PluginConfiguration.NormalizeUrl(config.SeerrUrl),
        SeerrApiKey = config.SeerrApiKey?.Trim() ?? string.Empty,
        SeerrSignInReady = !string.IsNullOrWhiteSpace(Configuration.PluginConfiguration.NormalizeUrl(config.SeerrUrl))
            && !string.IsNullOrWhiteSpace(config.SeerrApiKey),
        AutoSkipIntro = config.AutoSkipIntro,
        AutoSkipCredits = config.AutoSkipCredits,
        LockAutoSkip = config.LockAutoSkip,
        // The app should always register. Sending goes through the publisher relay.
        NotificationsReady = true
    };
}

public class DeviceTokenRequest
{
    [JsonPropertyName("token")]
    public string Token { get; set; } = string.Empty;

    [JsonPropertyName("deviceId")]
    public string DeviceId { get; set; } = string.Empty;
}

