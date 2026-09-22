namespace Jellyfin.Plugin.Zelefin.Library;

/// <summary>
/// Reads a TMDB id out of Jellyfin provider ids. Keys vary by metadata provider.
/// </summary>
public static class TmdbIdentifiers
{
    public static int? Parse(IEnumerable<KeyValuePair<string, string>>? providerIds)
    {
        if (providerIds is null)
        {
            return null;
        }

        foreach (var pair in providerIds)
        {
            if (!IsTmdbKey(pair.Key))
            {
                continue;
            }

            var trimmed = pair.Value?.Trim() ?? string.Empty;
            if (int.TryParse(trimmed, out var id) && id > 0)
            {
                return id;
            }
        }

        return null;
    }

    public static bool IsTmdbKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        var compact = new string(key.Where(char.IsLetter).Select(char.ToLowerInvariant).ToArray());
        return compact is "tmdb" or "tmdbid" or "themoviedb" or "moviedb" or "themoviedatabase";
    }
}
