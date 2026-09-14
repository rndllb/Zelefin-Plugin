using System.Collections.Concurrent;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Zelefin.Push.Events;

public sealed class ItemAddedService : PluginEvent, IHostedService
{
    private readonly ILibraryManager _libraryManager;
    private readonly ConcurrentDictionary<Guid, SeasonWatch> _seasons = new();

    public ItemAddedService(
        ILibraryManager libraryManager,
        ILogger<ItemAddedService> logger,
        NotificationService notifications)
        : base(logger, notifications)
    {
        _libraryManager = libraryManager;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _libraryManager.ItemAdded += OnItemAdded;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _libraryManager.ItemAdded -= OnItemAdded;
        foreach (var watch in _seasons.Values)
        {
            watch.Dispose();
        }

        _seasons.Clear();
        return Task.CompletedTask;
    }

    private void OnItemAdded(object? sender, ItemChangeEventArgs args)
    {
        var config = Config;
        if (config is null || !config.ItemAddedEnabled || args.Item.IsVirtualItem || args.Item.IsFolder)
        {
            return;
        }

        if (!LibraryAllowed(args.Item.Path, config.ParsedLibraryIds()))
        {
            return;
        }

        switch (args.Item)
        {
            case Movie movie:
                _ = Notifications.SendToAllAsync(MediaCopy.MovieAdded(movie.Name, movie.ProductionYear, movie.Id));
                break;
            case Episode episode:
                QueueEpisode(episode, config.ItemAddedGroupDelay);
                break;
        }
    }

    private void QueueEpisode(Episode episode, TimeSpan delay)
    {
        var seasonId = episode.SeasonId;
        if (seasonId == Guid.Empty)
        {
            seasonId = episode.Season?.Id ?? Guid.Empty;
        }

        if (seasonId == Guid.Empty)
        {
            return;
        }

        var capture = new EpisodeCapture(
            episode.Id,
            episode.Series?.Name ?? episode.SeriesName ?? "Series",
            episode.SeriesId,
            episode.Season?.IndexNumber ?? episode.ParentIndexNumber,
            episode.IndexNumber,
            episode.Name);

        var watch = _seasons.GetOrAdd(seasonId, id => new SeasonWatch(id, delay, FlushSeason));
        watch.Add(capture);
    }

    private void FlushSeason(Guid seasonId)
    {
        if (!_seasons.TryRemove(seasonId, out var watch))
        {
            return;
        }

        using (watch)
        {
            if (watch.Batch.Episodes.Count == 0)
            {
                return;
            }

            _ = Notifications.SendToAllAsync(MediaCopy.EpisodesAdded(watch.Batch));
        }
    }

    private bool LibraryAllowed(string? path, string[] libraryIds)
    {
        if (libraryIds.Length == 0 || string.IsNullOrEmpty(path))
        {
            return true;
        }

        var folder = _libraryManager.GetVirtualFolders()
            .FirstOrDefault(entry => entry.Locations.Any(location =>
                path.StartsWith(location, StringComparison.OrdinalIgnoreCase)));
        if (folder is null)
        {
            return true;
        }

        return libraryIds.Contains(folder.ItemId, StringComparer.OrdinalIgnoreCase);
    }

    private sealed class SeasonWatch : IDisposable
    {
        private readonly Timer _timer;
        private readonly TimeSpan _delay;
        private readonly Action<Guid> _flush;

        public SeasonWatch(Guid seasonId, TimeSpan delay, Action<Guid> flush)
        {
            Batch = new EpisodeBatch { SeasonId = seasonId };
            _delay = delay;
            _flush = flush;
            _timer = new Timer(_ => _flush(seasonId), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }

        public EpisodeBatch Batch { get; }

        public void Add(EpisodeCapture episode)
        {
            Batch.Add(episode);
            _timer.Change(_delay, Timeout.InfiniteTimeSpan);
        }

        public void Dispose() => _timer.Dispose();
    }
}
