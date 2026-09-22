using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Zelefin.Library;

/// <summary>
/// TMDB ownership and collection membership, rebuilt lazily after the library changes.
/// Answers questions the app otherwise asks by paging the whole catalog.
/// </summary>
public sealed class LibraryIndex : IHostedService
{
    private readonly ILibraryManager _library;
    private readonly IUserManager _users;
    private readonly ILogger<LibraryIndex> _logger;
    private readonly Lock _gate = new();
    private readonly Dictionary<Guid, Snapshot> _byUser = [];
    private long _revision = 1;

    public LibraryIndex(ILibraryManager library, IUserManager users, ILogger<LibraryIndex> logger)
    {
        _library = library;
        _users = users;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _library.ItemAdded += OnLibraryChanged;
        _library.ItemUpdated += OnLibraryChanged;
        _library.ItemRemoved += OnLibraryChanged;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _library.ItemAdded -= OnLibraryChanged;
        _library.ItemUpdated -= OnLibraryChanged;
        _library.ItemRemoved -= OnLibraryChanged;
        return Task.CompletedTask;
    }

    public long Revision
    {
        get
        {
            lock (_gate)
            {
                return _revision;
            }
        }
    }

    public TmdbCatalogDto CatalogFor(Guid userId)
    {
        var snapshot = ForUser(userId);
        return new TmdbCatalogDto
        {
            Movies = snapshot.MovieTmdbIds.Order().ToList(),
            Series = snapshot.SeriesTmdbIds.Order().ToList()
        };
    }

    public OwnedTmdbDto Owned(Guid userId, IReadOnlyCollection<int> tmdbIds, string? kind)
    {
        var snapshot = ForUser(userId);
        var wantMovies = kind is null or "" or "all" or "movie";
        var wantSeries = kind is null or "" or "all" or "series" or "tv";
        var owned = new List<int>();
        foreach (var id in tmdbIds.Distinct())
        {
            if ((wantMovies && snapshot.MovieTmdbIds.Contains(id))
                || (wantSeries && snapshot.SeriesTmdbIds.Contains(id)))
            {
                owned.Add(id);
            }
        }

        return new OwnedTmdbDto { Owned = owned };
    }

    public LibraryFingerprintDto Fingerprint(Guid userId)
    {
        var snapshot = ForUser(userId);
        return new LibraryFingerprintDto
        {
            Revision = snapshot.Revision,
            ItemCount = snapshot.ItemCount
        };
    }

    public IReadOnlyList<CollectionRefDto> CollectionsContaining(Guid userId, Guid itemId)
    {
        var snapshot = ForUser(userId);
        return snapshot.CollectionsByItem.TryGetValue(itemId, out var list)
            ? list
            : [];
    }

    private Snapshot ForUser(Guid userId)
    {
        lock (_gate)
        {
            if (_byUser.TryGetValue(userId, out var cached) && cached.Revision == _revision)
            {
                return cached;
            }
        }

        var built = Build(userId);
        lock (_gate)
        {
            if (built.Revision != _revision)
            {
                built.Revision = _revision;
            }

            _byUser[userId] = built;
            return built;
        }
    }

    private Snapshot Build(Guid userId)
    {
        var user = _users.GetUserById(userId);
        var snapshot = new Snapshot { Revision = Revision };
        if (user is null)
        {
            return snapshot;
        }

        try
        {
            var titles = _library.GetItemList(new InternalItemsQuery
            {
                User = user,
                Recursive = true
            });
            foreach (var item in titles)
            {
                if (item is Movie)
                {
                    snapshot.ItemCount++;
                    if (TmdbIdentifiers.Parse(item.ProviderIds) is int movieTmdb)
                    {
                        snapshot.MovieTmdbIds.Add(movieTmdb);
                    }
                }
                else if (item is Series)
                {
                    snapshot.ItemCount++;
                    if (TmdbIdentifiers.Parse(item.ProviderIds) is int seriesTmdb)
                    {
                        snapshot.SeriesTmdbIds.Add(seriesTmdb);
                    }
                }
                else if (item is BoxSet box)
                {
                    var reference = new CollectionRefDto
                    {
                        Id = box.Id.ToString("D"),
                        Name = box.Name ?? string.Empty
                    };
                    foreach (var child in box.GetLinkedChildren())
                    {
                        if (!snapshot.CollectionsByItem.TryGetValue(child.Id, out var list))
                        {
                            list = [];
                            snapshot.CollectionsByItem[child.Id] = list;
                        }

                        if (list.All(existing => existing.Id != reference.Id))
                        {
                            list.Add(reference);
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Zelefin could not index the library for user {UserId}", userId);
        }

        return snapshot;
    }

    private void OnLibraryChanged(object? sender, ItemChangeEventArgs args)
    {
        lock (_gate)
        {
            _revision++;
        }
    }

    private sealed class Snapshot
    {
        public long Revision { get; set; }
        public int ItemCount { get; set; }
        public HashSet<int> MovieTmdbIds { get; } = [];
        public HashSet<int> SeriesTmdbIds { get; } = [];
        public Dictionary<Guid, List<CollectionRefDto>> CollectionsByItem { get; } = [];
    }
}

public sealed class TmdbCatalogDto
{
    public List<int> Movies { get; set; } = [];
    public List<int> Series { get; set; } = [];
}

public sealed class OwnedTmdbDto
{
    public List<int> Owned { get; set; } = [];
}

public sealed class OwnedLookupRequest
{
    public List<int> Tmdb { get; set; } = [];

    /// <summary>movie, series, tv, or all.</summary>
    public string Kind { get; set; } = "all";
}

public sealed class LibraryFingerprintDto
{
    public long Revision { get; set; }
    public int ItemCount { get; set; }
}

public sealed class CollectionRefDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}

public sealed class CollectionListDto
{
    public List<CollectionRefDto> Collections { get; set; } = [];
}
