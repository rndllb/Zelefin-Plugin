using MediaBrowser.Controller.Session;
using Microsoft.Extensions.Hosting;

namespace Jellyfin.Plugin.Zelefin.Push;

/// <summary>
/// Snapshots live Jellyfin sessions onto registered Zelefin devices so a later
/// DisplayMessage can target that install after the socket drops.
/// </summary>
public sealed class SessionDeviceBinder : IHostedService
{
    private readonly ISessionManager _sessions;

    public SessionDeviceBinder(ISessionManager sessions)
    {
        _sessions = sessions;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        Remember(_sessions);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public static void Remember(ISessionManager sessions)
    {
        var store = ZelefinPlugin.Instance?.Devices;
        if (store is null)
        {
            return;
        }

        foreach (var session in sessions.Sessions)
        {
            store.RememberSessionUser(session.Id, session.UserId);
            store.RememberSession(session.Id, session.DeviceId);
        }
    }
}
