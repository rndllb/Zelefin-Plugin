using System.Text.Json;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Zelefin.Storage;

public enum HomeSurface
{
    ContinueWatching,
    NextUp,
    RecentlyWatched
}

public static class HomeSurfaceParser
{
    public static bool TryParse(string? value, out HomeSurface surface)
    {
        switch (value?.Trim().ToLowerInvariant())
        {
            case "continuewatching":
                surface = HomeSurface.ContinueWatching;
                return true;
            case "nextup":
                surface = HomeSurface.NextUp;
                return true;
            case "recentlywatched":
                surface = HomeSurface.RecentlyWatched;
                return true;
            default:
                surface = default;
                return false;
        }
    }
}

/// <summary>
/// Titles a user hid from a Home row without clearing watch history.
/// </summary>
public sealed class HiddenStore
{
    private readonly string _path;
    private readonly Lock _gate = new();
    private Dictionary<Guid, UserHidden> _users = [];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public HiddenStore(string dataPath)
    {
        Directory.CreateDirectory(dataPath);
        _path = Path.Combine(dataPath, "zelefin-hidden.json");
        Load();
    }

    public HiddenSnapshot Snapshot(Guid userId)
    {
        lock (_gate)
        {
            return _users.TryGetValue(userId, out var row)
                ? row.Snapshot()
                : HiddenSnapshot.Empty;
        }
    }

    public bool Add(Guid userId, HomeSurface surface, Guid itemId)
    {
        lock (_gate)
        {
            var row = Row(userId);
            var changed = row.Ids(surface).Add(itemId);
            if (changed)
            {
                Save();
            }

            return changed;
        }
    }

    public bool Remove(Guid userId, HomeSurface surface, Guid itemId)
    {
        lock (_gate)
        {
            if (!_users.TryGetValue(userId, out var row))
            {
                return false;
            }

            var changed = row.Ids(surface).Remove(itemId);
            if (changed)
            {
                Save();
            }

            return changed;
        }
    }

    public bool Clear(Guid userId, HomeSurface surface)
    {
        lock (_gate)
        {
            if (!_users.TryGetValue(userId, out var row))
            {
                return false;
            }

            var ids = row.Ids(surface);
            if (ids.Count == 0)
            {
                return false;
            }

            ids.Clear();
            Save();
            return true;
        }
    }

    /// <summary>
    /// Drops the title from every row. Playback means the user wants it back.
    /// </summary>
    public bool RemoveAll(Guid userId, Guid itemId)
    {
        lock (_gate)
        {
            if (!_users.TryGetValue(userId, out var row))
            {
                return false;
            }

            var changed = row.ContinueWatching.Remove(itemId)
                | row.NextUp.Remove(itemId)
                | row.RecentlyWatched.Remove(itemId);
            if (changed)
            {
                Save();
            }

            return changed;
        }
    }

    private UserHidden Row(Guid userId)
    {
        if (!_users.TryGetValue(userId, out var row))
        {
            row = new UserHidden();
            _users[userId] = row;
        }

        return row;
    }

    private void Load()
    {
        if (!File.Exists(_path))
        {
            return;
        }

        try
        {
            var json = File.ReadAllText(_path);
            var file = JsonSerializer.Deserialize<HiddenFile>(json, JsonOptions);
            _users = [];
            if (file?.Users is null)
            {
                return;
            }

            foreach (var (key, value) in file.Users)
            {
                if (Guid.TryParse(key, out var userId))
                {
                    _users[userId] = value;
                }
            }
        }
        catch
        {
            _users = [];
        }
    }

    private void Save()
    {
        var file = new HiddenFile
        {
            Users = _users.ToDictionary(
                pair => pair.Key.ToString("D"),
                pair => pair.Value,
                StringComparer.OrdinalIgnoreCase)
        };
        File.WriteAllText(_path, JsonSerializer.Serialize(file, JsonOptions));
    }

    private sealed class HiddenFile
    {
        public Dictionary<string, UserHidden> Users { get; set; } = [];
    }

    private sealed class UserHidden
    {
        public HashSet<Guid> ContinueWatching { get; set; } = [];
        public HashSet<Guid> NextUp { get; set; } = [];
        public HashSet<Guid> RecentlyWatched { get; set; } = [];

        public HashSet<Guid> Ids(HomeSurface surface) => surface switch
        {
            HomeSurface.ContinueWatching => ContinueWatching,
            HomeSurface.NextUp => NextUp,
            HomeSurface.RecentlyWatched => RecentlyWatched,
            _ => ContinueWatching
        };

        public HiddenSnapshot Snapshot() => new()
        {
            ContinueWatching = Ids(ContinueWatching),
            NextUp = Ids(NextUp),
            RecentlyWatched = Ids(RecentlyWatched)
        };

        private static List<string> Ids(HashSet<Guid> ids) =>
            ids.Select(id => id.ToString("D")).Order(StringComparer.Ordinal).ToList();
    }
}

public sealed class HiddenSnapshot
{
    public static readonly HiddenSnapshot Empty = new();

    [JsonPropertyName("continueWatching")]
    public List<string> ContinueWatching { get; set; } = [];

    [JsonPropertyName("nextUp")]
    public List<string> NextUp { get; set; } = [];

    [JsonPropertyName("recentlyWatched")]
    public List<string> RecentlyWatched { get; set; } = [];
}
