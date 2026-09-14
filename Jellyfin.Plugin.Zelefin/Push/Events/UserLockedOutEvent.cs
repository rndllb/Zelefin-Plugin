using Jellyfin.Data.Events.Users;
using MediaBrowser.Controller.Events;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Zelefin.Push.Events;

public sealed class UserLockedOutEvent : PluginEvent, IEventConsumer<UserLockedOutEventArgs>
{
    public UserLockedOutEvent(ILogger<UserLockedOutEvent> logger, NotificationService notifications)
        : base(logger, notifications)
    {
    }

    public async Task OnEvent(UserLockedOutEventArgs? eventArgs)
    {
        if (eventArgs?.Argument is null || Config is not { UserLockedOutEnabled: true })
        {
            return;
        }

        var message = MediaCopy.UserLockedOut(eventArgs.Argument.Username);
        await Notifications.SendToAdminsAsync(message).ConfigureAwait(false);
        await Notifications.SendToUserAsync(eventArgs.Argument.Id, message).ConfigureAwait(false);
    }
}
