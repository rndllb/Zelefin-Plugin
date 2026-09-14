using System.Collections.Concurrent;

namespace Jellyfin.Plugin.Zelefin.Push;

public static class EventThrottle
{
    private static readonly ConcurrentDictionary<string, DateTime> Recent = new();

    public static bool HasRecentlyProcessed(string key, TimeSpan threshold)
    {
        var now = DateTime.UtcNow;
        if (Recent.TryGetValue(key, out var last) && now - last < threshold)
        {
            return true;
        }

        Recent[key] = now;
        return false;
    }

    public static void Cleanup(TimeSpan maxAge)
    {
        var cutoff = DateTime.UtcNow - maxAge;
        foreach (var pair in Recent)
        {
            if (pair.Value < cutoff)
            {
                Recent.TryRemove(pair.Key, out _);
            }
        }
    }

    public static void Reset() => Recent.Clear();
}

public sealed class EpisodeBatch
{
    public Guid SeasonId { get; init; }

    public string SeriesName { get; set; } = string.Empty;

    public int? SeasonNumber { get; set; }

    public Guid SeriesId { get; set; }

    public List<EpisodeCapture> Episodes { get; } = [];

    public void Add(EpisodeCapture episode)
    {
        var index = Episodes.FindIndex(existing => existing.EpisodeId == episode.EpisodeId);
        if (index >= 0)
        {
            Episodes[index] = episode;
        }
        else
        {
            Episodes.Add(episode);
        }

        SeriesName = episode.SeriesName;
        SeasonNumber = episode.SeasonNumber ?? SeasonNumber;
        SeriesId = episode.SeriesId;
    }
}

public readonly record struct EpisodeCapture(
    Guid EpisodeId,
    string SeriesName,
    Guid SeriesId,
    int? SeasonNumber,
    int? EpisodeNumber,
    string EpisodeName);

public static class MediaCopy
{
    public static Api.PushMessage MovieAdded(string name, int? year, Guid itemId)
    {
        var cleaned = StripYear(name);
        var title = year is > 0 ? $"{cleaned} ({year})" : cleaned;
        return new Api.PushMessage
        {
            Title = $"{title} added",
            Body = "Watch now",
            ItemId = itemId.ToString("N"),
            Type = "Movie"
        };
    }

    public static Api.PushMessage EpisodesAdded(EpisodeBatch batch)
    {
        if (batch.Episodes.Count == 1)
        {
            var episode = batch.Episodes[0];
            return new Api.PushMessage
            {
                Title = "New episode",
                Body = EpisodeLine(episode),
                ItemId = episode.EpisodeId.ToString("N"),
                SeriesId = episode.SeriesId.ToString("N"),
                Type = "Episode"
            };
        }

        var season = batch.SeasonNumber is { } number ? $"Season {number}" : "this season";
        return new Api.PushMessage
        {
            Title = "New episodes",
            Body = $"{batch.Episodes.Count} episodes of {batch.SeriesName} added to {season}",
            SeriesId = batch.SeriesId.ToString("N"),
            Type = "Season"
        };
    }

    public static Api.PushMessage SessionStarted(string userName) => new()
    {
        Title = "Session started",
        Body = $"{userName} is online"
    };

    public static Api.PushMessage PlaybackStarted(string userName, string title) => new()
    {
        Title = "Playback started",
        Body = $"{userName} is watching {title}"
    };

    public static Api.PushMessage UserLockedOut(string userName) => new()
    {
        Title = "Account locked",
        Body = $"{userName} has been locked out"
    };

    public static string StripYear(string name)
    {
        var trimmed = name.Trim();
        if (trimmed.Length < 7 || trimmed[^1] != ')')
        {
            return trimmed;
        }

        var open = trimmed.LastIndexOf(" (", StringComparison.Ordinal);
        if (open < 0 || trimmed.Length - open != 7)
        {
            return trimmed;
        }

        return int.TryParse(trimmed.AsSpan(open + 2, 4), out _)
            ? trimmed[..open].Trim()
            : trimmed;
    }

    private static string EpisodeLine(EpisodeCapture episode)
    {
        if (episode.SeasonNumber is { } season && episode.EpisodeNumber is { } number)
        {
            return $"{episode.SeriesName} S{season}:E{number} — {episode.EpisodeName}";
        }

        if (episode.EpisodeNumber is { } onlyEpisode)
        {
            return $"{episode.SeriesName} episode {onlyEpisode} — {episode.EpisodeName}";
        }

        return $"{episode.SeriesName} — {episode.EpisodeName}";
    }
}
