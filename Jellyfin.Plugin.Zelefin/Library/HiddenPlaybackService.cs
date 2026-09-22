using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Hosting;

namespace Jellyfin.Plugin.Zelefin.Library;

/// <summary>
/// Playing a hidden title puts it back on the row. Hiding is "not now", not "forget this".
/// </summary>
public sealed class HiddenPlaybackService : IHostedService
{
    private readonly IUserDataManager _userData;

    public HiddenPlaybackService(IUserDataManager userData)
    {
        _userData = userData;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _userData.UserDataSaved += OnUserDataSaved;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _userData.UserDataSaved -= OnUserDataSaved;
        return Task.CompletedTask;
    }

    private static void OnUserDataSaved(object? sender, UserDataSaveEventArgs args)
    {
        if (args.SaveReason is not (
            UserDataSaveReason.PlaybackStart
            or UserDataSaveReason.PlaybackProgress
            or UserDataSaveReason.PlaybackFinished
            or UserDataSaveReason.TogglePlayed))
        {
            return;
        }

        if (args.Item is null)
        {
            return;
        }

        ZelefinPlugin.Instance?.Hidden.RemoveAll(args.UserId, args.Item.Id);
    }
}
