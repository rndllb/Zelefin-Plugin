using MediaBrowser.Controller.Events;
using MediaBrowser.Controller.Events.Session;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Zelefin.Push.Events;

public sealed class SessionStartEvent : PluginEvent, IEventConsumer<SessionStartedEventArgs>
{
    public SessionStartEvent(ILogger<SessionStartEvent> logger, NotificationService notifications)
        : base(logger, notifications)
    {
    }

    public Task OnEvent(SessionStartedEventArgs? eventArgs)
    {
        var session = eventArgs?.Argument;
        if (session is not null)
        {
            ZelefinPlugin.Instance?.Devices.RememberSessionUser(session.Id, session.UserId);
            ZelefinPlugin.Instance?.Devices.RememberSession(session.Id, session.DeviceId);
        }

        if (session is null || Config is not { SessionStartedEnabled: true })
        {
            return Task.CompletedTask;
        }
        if (Throttled("session:" + session.DeviceId))
        {
            return Task.CompletedTask;
        }

        var name = string.IsNullOrWhiteSpace(session.UserName) ? "A user" : session.UserName;
        return Notifications.SendToAdminsAsync(
            MediaCopy.SessionStarted(name),
            excludedUserIds: [session.UserId]);
    }
}
