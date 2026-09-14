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
        if (eventArgs?.Argument is null || Config is not { SessionStartedEnabled: true })
        {
            return Task.CompletedTask;
        }

        var session = eventArgs.Argument;
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
