using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text.Json;
using Jellyfin.Plugin.Zelefin.Push;
using MediaBrowser.Controller.Session;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.Zelefin.Api;

[ApiController]
[Route("Zelefin")]
public class ZelefinController : ControllerBase
{
    private readonly NotificationService _notifications;
    private readonly ISessionManager _sessions;

    public ZelefinController(NotificationService notifications, ISessionManager sessions)
    {
        _notifications = notifications;
        _sessions = sessions;
    }

    [HttpGet("config")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult GetConfig()
    {
        var config = ZelefinPlugin.Instance?.Configuration ?? new Configuration.PluginConfiguration();
        return Content(JsonSerializer.Serialize(ClientConfig.From(config)), "application/json");
    }

    [HttpPost("device")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public ActionResult PostDevice([FromBody, Required] DeviceTokenRequest body)
    {
        if (string.IsNullOrWhiteSpace(body.Token))
        {
            return BadRequest("token is required");
        }

        if (!TryCurrentUserId(out var userId))
        {
            return Unauthorized();
        }

        if (!TryDeviceId(body.DeviceId, out var deviceId))
        {
            return BadRequest("deviceId must be a UUID");
        }

        var stored = ZelefinPlugin.Instance!.Devices.Upsert(
            deviceId,
            userId,
            body.Token,
            body.Platform,
            body.SessionId);
        BindLiveSession(deviceId, body.DeviceId, body.SessionId);
        return new JsonResult(stored);
    }

    [HttpDelete("device/{deviceId}")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult DeleteDevice([FromRoute] string deviceId)
    {
        if (!TryDeviceId(deviceId, out var id))
        {
            return BadRequest("deviceId must be a UUID");
        }

        ZelefinPlugin.Instance?.Devices.Remove(id);
        return Ok();
    }

    [HttpPost("notification")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    public async Task<ActionResult> PostNotification(
        [FromBody, Required] List<PushNotificationRequest> notifications,
        CancellationToken cancellationToken)
    {
        if (ZelefinPlugin.Instance?.Devices.Count == 0)
        {
            return Accepted();
        }

        var valid = notifications.Where(PushMessage.IsValid).ToList();
        if (valid.Count == 0)
        {
            return Accepted();
        }

        await _notifications.SendRequestsAsync(valid, cancellationToken).ConfigureAwait(false);
        return Ok();
    }

    [HttpPost("notification/test")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult> PostTestNotification(CancellationToken cancellationToken)
    {
        if (!TryCurrentUserId(out var userId))
        {
            return Unauthorized();
        }

        var error = await _notifications.SendTestAsync(userId, cancellationToken).ConfigureAwait(false);
        return Ok(new { ok = error is null, error });
    }

    private void BindLiveSession(Guid deviceId, string? reportedDeviceId, string? reportedSessionId)
    {
        var store = ZelefinPlugin.Instance?.Devices;
        if (store is null)
        {
            return;
        }

        store.RememberSession(reportedSessionId, reportedDeviceId);
        store.RememberSession(reportedSessionId, deviceId.ToString("D"));
        foreach (var session in _sessions.Sessions)
        {
            if (DisplayMessageWatch.SameId(session.DeviceId, reportedDeviceId)
                || DisplayMessageWatch.SameId(session.DeviceId, deviceId.ToString("D"))
                || DisplayMessageWatch.SameId(session.DeviceId, deviceId.ToString("N")))
            {
                store.RememberSession(session.Id, deviceId.ToString("D"));
            }
        }
    }

    private bool TryCurrentUserId(out Guid userId)
    {
        foreach (var claim in User.Claims)
        {
            if (!LooksLikeUserIdClaim(claim.Type))
            {
                continue;
            }

            if (Guid.TryParse(claim.Value, out userId) && userId != Guid.Empty)
            {
                return true;
            }
        }

        userId = Guid.Empty;
        return false;
    }

    private static bool LooksLikeUserIdClaim(string type)
    {
        return type.Equals(ClaimTypes.NameIdentifier, StringComparison.OrdinalIgnoreCase)
            || type.EndsWith("/userId", StringComparison.OrdinalIgnoreCase)
            || type.Equals("UserId", StringComparison.OrdinalIgnoreCase)
            || type.Equals("Jellyfin-UserId", StringComparison.OrdinalIgnoreCase);
    }

    public static bool TryDeviceId(string? value, out Guid deviceId)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        if (Guid.TryParse(trimmed, out deviceId) && deviceId != Guid.Empty)
        {
            return true;
        }

        deviceId = Guid.Empty;
        return false;
    }
}
