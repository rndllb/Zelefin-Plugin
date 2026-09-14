using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Zelefin.Push.Events;

public abstract class PluginEvent
{
    protected PluginEvent(ILogger logger, NotificationService notifications)
    {
        Logger = logger;
        Notifications = notifications;
    }

    protected ILogger Logger { get; }

    protected NotificationService Notifications { get; }

    protected static Configuration.PluginConfiguration? Config => ZelefinPlugin.Instance?.Configuration;

    protected bool Throttled(string key)
    {
        var threshold = Config?.EventThreshold ?? TimeSpan.FromSeconds(5);
        EventThrottle.Cleanup(threshold + TimeSpan.FromMinutes(5));
        return EventThrottle.HasRecentlyProcessed(key, threshold);
    }
}
