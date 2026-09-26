using Jellyfin.Plugin.Zelefin.Push;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.Zelefin.Configuration;

/// <summary>
/// Server-wide defaults for the Zelefin iOS and Android apps. Apple Push and Firebase
/// credentials belong on the publisher relay, not in this dashboard. Local APNs fields
/// remain for an already-saved key; Android devices always go through the relay.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    public string SeerrUrl { get; set; } = string.Empty;

    /// <summary>
    /// Seerr Settings → General API key. Lets Zelefin sign users in without their password.
    /// </summary>
    public string SeerrApiKey { get; set; } = string.Empty;

    public bool AutoSkipIntro { get; set; }

    public bool AutoSkipCredits { get; set; }

    /// <summary>
    /// When true, Zelefin applies skip defaults from this server and hides the toggles.
    /// </summary>
    public bool LockAutoSkip { get; set; }

    public bool ItemAddedEnabled { get; set; } = true;

    /// <summary>
    /// Comma-separated library IDs. Empty means every library.
    /// </summary>
    public string ItemAddedLibraryIds { get; set; } = string.Empty;

    public int ItemAddedGroupSeconds { get; set; } = 60;

    public bool SessionStartedEnabled { get; set; } = true;

    public bool PlaybackStartedEnabled { get; set; } = true;

    public bool UserLockedOutEnabled { get; set; } = true;

    public int EventThresholdSeconds { get; set; } = 5;

    public string ApnsKeyId { get; set; } = string.Empty;

    public string ApnsTeamId { get; set; } = string.Empty;

    public string ApnsBundleId { get; set; } = "app.zelefin.client";

    public string ApnsPrivateKey { get; set; } = string.Empty;

    public bool ApnsProduction { get; set; }

    /// <summary>
    /// Publisher-operated push relay for APNs and Firebase. Jellyfin admins do not paste
    /// an Apple key or a Firebase service account.
    /// </summary>
    public string PushRelayUrl { get; set; } = DefaultPushRelayUrl;

    public const string DefaultPushRelayUrl = "https://push.zelefin.app/v1/send";

    public bool IsApnsConfigured =>
        !string.IsNullOrWhiteSpace(ApnsKeyId)
        && !string.IsNullOrWhiteSpace(ApnsTeamId)
        && !string.IsNullOrWhiteSpace(ApnsBundleId)
        && ApnsPrivateKeyLooksValid(ApnsPrivateKey);

    public bool HasPushRelay
    {
        get
        {
            var url = PushRelayUrl?.Trim() ?? string.Empty;
            return url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                || url.StartsWith("http://", StringComparison.OrdinalIgnoreCase);
        }
    }

    public bool CanSendPush => IsApnsConfigured || HasPushRelay;

    public ApnsCredentials? LocalApnsCredentials()
    {
        if (!IsApnsConfigured)
        {
            return null;
        }

        return new ApnsCredentials(
            ApnsKeyId,
            ApnsTeamId,
            ApnsBundleId,
            ApnsPrivateKey,
            ApnsProduction);
    }

    public string[] ParsedLibraryIds()
    {
        if (string.IsNullOrWhiteSpace(ItemAddedLibraryIds))
        {
            return [];
        }

        return ItemAddedLibraryIds
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public TimeSpan ItemAddedGroupDelay =>
        TimeSpan.FromSeconds(Math.Clamp(ItemAddedGroupSeconds <= 0 ? 60 : ItemAddedGroupSeconds, 5, 600));

    public TimeSpan EventThreshold =>
        TimeSpan.FromSeconds(Math.Clamp(EventThresholdSeconds <= 0 ? 5 : EventThresholdSeconds, 1, 3600));

    public static string NormalizeUrl(string? value)
    {
        var trimmed = (value ?? string.Empty).Trim().TrimEnd('/');
        if (string.IsNullOrEmpty(trimmed))
        {
            return string.Empty;
        }

        if (!trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            && !trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = "https://" + trimmed;
        }

        return trimmed;
    }

    private static bool ApnsPrivateKeyLooksValid(string? pem)
    {
        if (string.IsNullOrWhiteSpace(pem))
        {
            return false;
        }

        return pem.Contains("BEGIN PRIVATE KEY", StringComparison.Ordinal)
            || pem.Contains("BEGIN EC PRIVATE KEY", StringComparison.Ordinal);
    }
}
