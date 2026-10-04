using Jellyfin.Data.Events.Users;
using Jellyfin.Plugin.Zelefin.Push;
using Jellyfin.Plugin.Zelefin.Push.Events;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Events;
using MediaBrowser.Controller.Events.Session;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Plugins;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.Zelefin;

public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddSingleton<ApnsClient>();
        serviceCollection.AddHttpClient<PushRelayClient>();
        serviceCollection.AddSingleton<NotificationService>();
        serviceCollection.AddTransient<IStartupFilter, DisplayMessageStartupFilter>();
        serviceCollection.AddHostedService<SessionDeviceBinder>();
        serviceCollection.AddScoped<IEventConsumer<SessionStartedEventArgs>, SessionStartEvent>();
        serviceCollection.AddScoped<IEventConsumer<PlaybackStartEventArgs>, PlaybackStartEvent>();
        serviceCollection.AddScoped<IEventConsumer<UserLockedOutEventArgs>, UserLockedOutEvent>();
        serviceCollection.AddHostedService<ItemAddedService>();
    }
}
