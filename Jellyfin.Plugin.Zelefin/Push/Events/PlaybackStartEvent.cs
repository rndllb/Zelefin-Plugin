using MediaBrowser.Controller.Events;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Zelefin.Push.Events;

public sealed class PlaybackStartEvent : PluginEvent, IEventConsumer<PlaybackStartEventArgs>
{
    public PlaybackStartEvent(ILogger<PlaybackStartEvent> logger, NotificationService notifications)
        : base(logger, notifications)
    {
    }

    public Task OnEvent(PlaybackStartEventArgs? eventArgs)
    {
        if (eventArgs is null || Config is not { PlaybackStartedEnabled: true })
        {
            return Task.CompletedTask;
        }

        if (eventArgs.Item is null || eventArgs.Item.IsThemeMedia || eventArgs.Users.Count == 0)
        {
            return Task.CompletedTask;
        }

        var title = eventArgs.Item.Name;
        var tasks = eventArgs.Users.Select(user =>
        {
            if (Throttled("playback:" + user.Id + ":" + eventArgs.Item.Id))
            {
                return Task.CompletedTask;
            }

            return Notifications.SendToAdminsAsync(
                MediaCopy.PlaybackStarted(user.Username, title),
                excludedUserIds: eventArgs.Users.Select(entry => entry.Id));
        });
        return Task.WhenAll(tasks);
    }
}
