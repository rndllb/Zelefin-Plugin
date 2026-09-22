using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text.Json;
using Jellyfin.Plugin.Zelefin.Library;
using Jellyfin.Plugin.Zelefin.Push;
using Jellyfin.Plugin.Zelefin.Storage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.Zelefin.Api;

[ApiController]
[Route("Zelefin")]
public class ZelefinController : ControllerBase
{
    private const int MaxOwnedLookup = 2_000;

    private readonly NotificationService _notifications;
    private readonly LibraryIndex _library;

    public ZelefinController(NotificationService notifications, LibraryIndex library)
    {
        _notifications = notifications;
        _library = library;
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

        var stored = ZelefinPlugin.Instance!.Devices.Upsert(deviceId, userId, body.Token);
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

    [HttpGet("hidden")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult GetHidden()
    {
        if (!TryCurrentUserId(out var userId))
        {
            return Unauthorized();
        }

        return new JsonResult(ZelefinPlugin.Instance?.Hidden.Snapshot(userId) ?? Storage.HiddenSnapshot.Empty);
    }

    [HttpPost("hidden/{surface}/{itemId}")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public ActionResult PostHidden([FromRoute] string surface, [FromRoute] string itemId)
    {
        if (!TryHidden(surface, itemId, out var userId, out var parsedSurface, out var id, out var error))
        {
            return error!;
        }

        ZelefinPlugin.Instance!.Hidden.Add(userId, parsedSurface, id);
        return NoContent();
    }

    [HttpDelete("hidden/{surface}/{itemId}")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public ActionResult DeleteHidden([FromRoute] string surface, [FromRoute] string itemId)
    {
        if (!TryHidden(surface, itemId, out var userId, out var parsedSurface, out var id, out var error))
        {
            return error!;
        }

        ZelefinPlugin.Instance!.Hidden.Remove(userId, parsedSurface, id);
        return NoContent();
    }

    [HttpDelete("hidden/{surface}")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public ActionResult DeleteHiddenSurface([FromRoute] string surface)
    {
        if (!TryCurrentUserId(out var userId))
        {
            return Unauthorized();
        }

        if (!HomeSurfaceParser.TryParse(surface, out var parsed))
        {
            return BadRequest("surface must be continueWatching, nextUp, or recentlyWatched");
        }

        ZelefinPlugin.Instance?.Hidden.Clear(userId, parsed);
        return NoContent();
    }

    [HttpGet("library/tmdb")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult GetTmdbCatalog()
    {
        if (!TryCurrentUserId(out var userId))
        {
            return Unauthorized();
        }

        return new JsonResult(_library.CatalogFor(userId));
    }

    [HttpPost("library/owned")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult PostOwned([FromBody] OwnedLookupRequest? body)
    {
        if (!TryCurrentUserId(out var userId))
        {
            return Unauthorized();
        }

        var ids = body?.Tmdb ?? [];
        if (ids.Count > MaxOwnedLookup)
        {
            return BadRequest($"tmdb accepts at most {MaxOwnedLookup} ids");
        }

        return new JsonResult(_library.Owned(userId, ids, body?.Kind));
    }

    [HttpGet("library/fingerprint")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult GetFingerprint()
    {
        if (!TryCurrentUserId(out var userId))
        {
            return Unauthorized();
        }

        return new JsonResult(_library.Fingerprint(userId));
    }

    [HttpGet("item/{itemId}/collections")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult GetCollections([FromRoute] string itemId)
    {
        if (!TryCurrentUserId(out var userId))
        {
            return Unauthorized();
        }

        if (!Guid.TryParse(itemId, out var id) || id == Guid.Empty)
        {
            return BadRequest("itemId must be a UUID");
        }

        return new JsonResult(new CollectionListDto
        {
            Collections = _library.CollectionsContaining(userId, id).ToList()
        });
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

    private bool TryHidden(
        string surface,
        string itemId,
        out Guid userId,
        out HomeSurface parsedSurface,
        out Guid id,
        out ActionResult? error)
    {
        parsedSurface = default;
        id = Guid.Empty;
        if (!TryCurrentUserId(out userId))
        {
            error = Unauthorized();
            return false;
        }

        if (!HomeSurfaceParser.TryParse(surface, out parsedSurface))
        {
            error = BadRequest("surface must be continueWatching, nextUp, or recentlyWatched");
            return false;
        }

        if (!Guid.TryParse(itemId, out id) || id == Guid.Empty)
        {
            error = BadRequest("itemId must be a UUID");
            return false;
        }

        if (ZelefinPlugin.Instance is null)
        {
            error = StatusCode(StatusCodes.Status503ServiceUnavailable);
            return false;
        }

        error = null;
        return true;
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
