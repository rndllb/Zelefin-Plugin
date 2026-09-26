using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.Zelefin.Api;
using Jellyfin.Plugin.Zelefin.Storage;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Zelefin.Push;

public sealed class NotificationService
{
    private readonly ApnsClient _apns;
    private readonly PushRelayClient _relay;
    private readonly IUserManager _userManager;
    private readonly ILogger<NotificationService> _logger;

    public NotificationService(
        ApnsClient apns,
        PushRelayClient relay,
        IUserManager userManager,
        ILogger<NotificationService> logger)
    {
        _apns = apns;
        _relay = relay;
        _userManager = userManager;
        _logger = logger;
    }

    public Task SendToAllAsync(PushMessage message, CancellationToken cancellationToken = default)
    {
        var devices = ZelefinPlugin.Instance?.Devices.All() ?? [];
        return SendAsync(devices, message, cancellationToken);
    }

    public Task SendToAdminsAsync(
        PushMessage message,
        IEnumerable<Guid>? excludedUserIds = null,
        CancellationToken cancellationToken = default)
    {
        var devices = NotificationTargets.ForAdmins(
            ZelefinPlugin.Instance?.Devices.All() ?? [],
            AdminIds(),
            excludedUserIds);
        return SendAsync(devices, message, cancellationToken);
    }

    public Task SendToUserAsync(Guid userId, PushMessage message, CancellationToken cancellationToken = default)
    {
        var devices = ZelefinPlugin.Instance?.Devices.ForUser(userId) ?? [];
        return SendAsync(devices, message, cancellationToken);
    }

    public async Task<string?> SendTestAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var config = ZelefinPlugin.Instance?.Configuration;
        if (config is null || !config.CanSendPush)
        {
            return "Notifications are not available yet.";
        }

        var devices = ZelefinPlugin.Instance?.Devices.ForUser(userId) ?? [];
        if (devices.Count == 0)
        {
            return "Open Zelefin on your phone or tablet, sign in, and allow notifications. Then try again.";
        }

        await SendAsync(
            devices,
            new PushMessage
            {
                Title = "Zelefin",
                Body = "Notifications are working.",
                Type = "Test"
            },
            cancellationToken).ConfigureAwait(false);
        return null;
    }

    public async Task SendRequestsAsync(
        IReadOnlyList<PushNotificationRequest> requests,
        CancellationToken cancellationToken = default)
    {
        var all = ZelefinPlugin.Instance?.Devices.All() ?? [];
        if (all.Count == 0)
        {
            return;
        }

        var admins = AdminIds();
        foreach (var request in requests.Where(PushMessage.IsValid))
        {
            var targets = NotificationTargets.Resolve(
                request,
                all,
                admins,
                name => _userManager.GetUserByName(name)?.Id);
            await SendAsync(targets, PushMessage.From(request), cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task SendAsync(
        IReadOnlyList<DeviceRecord> devices,
        PushMessage message,
        CancellationToken cancellationToken)
    {
        var config = ZelefinPlugin.Instance?.Configuration;
        if (config is null || !config.CanSendPush || devices.Count == 0)
        {
            if (config is not null && !config.CanSendPush)
            {
                _logger.LogDebug("Skipping push; no relay URL and no local APNs key");
            }

            return;
        }

        var apple = devices.Where(device => !device.IsAndroid).ToList();
        var android = devices.Where(device => device.IsAndroid).ToList();

        var credentials = config.LocalApnsCredentials();
        if (credentials is not null)
        {
            await SendLocalApnsAsync(credentials, apple, message, cancellationToken).ConfigureAwait(false);
            apple = [];
        }

        // Android always goes through the relay; Firebase credentials never live on Jellyfin.
        if (!config.HasPushRelay || (apple.Count == 0 && android.Count == 0))
        {
            return;
        }

        var result = await _relay.SendAsync(
            config.PushRelayUrl,
            apple.Select(device => device.Token).Distinct().ToList(),
            android.Select(device => device.Token).Distinct().ToList(),
            message,
            cancellationToken).ConfigureAwait(false);
        foreach (var token in result.ExpiredTokens)
        {
            ZelefinPlugin.Instance?.Devices.RemoveToken(token);
        }
    }

    private async Task SendLocalApnsAsync(
        ApnsCredentials credentials,
        IReadOnlyList<DeviceRecord> devices,
        PushMessage message,
        CancellationToken cancellationToken)
    {
        foreach (var device in devices)
        {
            try
            {
                var result = await _apns.SendAsync(credentials, device.Token, message, cancellationToken)
                    .ConfigureAwait(false);
                if (result.TokenExpired)
                {
                    ZelefinPlugin.Instance?.Devices.RemoveToken(device.Token);
                    _logger.LogInformation("Removed expired APNs token for device {DeviceId}", device.DeviceId);
                }
                else if (!result.Success)
                {
                    _logger.LogWarning(
                        "APNs rejected token for device {DeviceId}: {Status} {Body}",
                        device.DeviceId,
                        result.StatusCode,
                        result.Body);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send APNs notification to {DeviceId}", device.DeviceId);
            }
        }
    }

    public HashSet<Guid> AdminIds()
    {
        return _userManager.GetUsers()
            .Where(user => user.Permissions.Any(permission =>
                permission.Kind == PermissionKind.IsAdministrator && permission.Value))
            .Select(user => user.Id)
            .ToHashSet();
    }
}
