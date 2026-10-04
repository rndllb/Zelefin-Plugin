using System.Text;
using MediaBrowser.Controller.Session;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Zelefin.Push;

/// <summary>
/// After a session DisplayMessage, push every Zelefin device for that user.
/// Jellyfin 500s when the target socket is gone; the user push still goes out.
/// </summary>
public sealed class DisplayMessagePushMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<DisplayMessagePushMiddleware> _logger;

    public DisplayMessagePushMiddleware(RequestDelegate next, ILogger<DisplayMessagePushMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(
        HttpContext context,
        ISessionManager sessions,
        NotificationService notifications)
    {
        if (!HttpMethods.IsPost(context.Request.Method)
            || !DisplayMessageWatch.TryMatchPath(context.Request.Path.Value, out var sessionId, out var isCommand))
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        context.Request.EnableBuffering();
        string body;
        using (var reader = new StreamReader(context.Request.Body, Encoding.UTF8, leaveOpen: true))
        {
            body = await reader.ReadToEndAsync().ConfigureAwait(false);
        }

        context.Request.Body.Position = 0;

        var parsed = DisplayMessageWatch.TryParse(body, isCommand, out var header, out var text);
        var userId = parsed ? ResolveUser(sessions, sessionId) : Guid.Empty;
        if (userId != Guid.Empty)
        {
            context.Response.OnStarting(() =>
            {
                if (context.Response.StatusCode >= 500)
                {
                    context.Response.StatusCode = StatusCodes.Status204NoContent;
                }

                return Task.CompletedTask;
            });
        }

        await _next(context).ConfigureAwait(false);

        if (context.Response.StatusCode is StatusCodes.Status401Unauthorized or StatusCodes.Status403Forbidden)
        {
            return;
        }

        if (!parsed
            || userId == Guid.Empty
            || ZelefinPlugin.Instance?.Configuration is not { DisplayMessageEnabled: true })
        {
            return;
        }

        try
        {
            await notifications.SendToUserAsync(userId, DisplayMessageWatch.ToPush(header, text))
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to push DisplayMessage for session {SessionId}", sessionId);
        }
    }

    private static Guid ResolveUser(ISessionManager sessions, string sessionId)
    {
        var store = ZelefinPlugin.Instance?.Devices;
        SessionDeviceBinder.Remember(sessions);
        var live = sessions.Sessions.FirstOrDefault(entry =>
            DisplayMessageWatch.SameId(entry.Id, sessionId)
            || DisplayMessageWatch.SameId(entry.DeviceId, sessionId));
        if (live is not null && live.UserId != Guid.Empty)
        {
            store?.RememberSessionUser(live.Id, live.UserId);
            return live.UserId;
        }

        return store?.UserForSession(sessionId) ?? Guid.Empty;
    }
}
