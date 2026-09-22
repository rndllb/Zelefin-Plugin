using Jellyfin.Plugin.Zelefin.Configuration;
using Jellyfin.Plugin.Zelefin.Storage;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.Zelefin;

/// <summary>
/// Companion plugin for the Zelefin iOS app.
/// </summary>
public class ZelefinPlugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    public new const string Version = "1.1.0.0";

    public static readonly Guid PluginGuid = Guid.Parse("b18e5910-21dd-49fe-a150-bb21ec3755e8");

    public ZelefinPlugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
        Devices = new DeviceStore(applicationPaths.DataPath);
        Hidden = new HiddenStore(applicationPaths.DataPath);
    }

    public static ZelefinPlugin? Instance { get; private set; }

    public DeviceStore Devices { get; }

    public HiddenStore Hidden { get; }

    public override string Name => "Zelefin";

    public override string Description =>
        "Companion for Zelefin: notifications, home-row hiding, and library lookups.";

    public override Guid Id => PluginGuid;

    public IEnumerable<PluginPageInfo> GetPages()
    {
        var prefix = GetType().Namespace;
        yield return new PluginPageInfo
        {
            Name = Name,
            EmbeddedResourcePath = prefix + ".Pages.config.html"
        };
        yield return new PluginPageInfo
        {
            Name = "Zelefin.js",
            EmbeddedResourcePath = prefix + ".Pages.config.js"
        };
        yield return new PluginPageInfo
        {
            Name = "ZelefinNotifications",
            EmbeddedResourcePath = prefix + ".Pages.notifications.html"
        };
        yield return new PluginPageInfo
        {
            Name = "ZelefinNotifications.js",
            EmbeddedResourcePath = prefix + ".Pages.notifications.js"
        };
    }
}
