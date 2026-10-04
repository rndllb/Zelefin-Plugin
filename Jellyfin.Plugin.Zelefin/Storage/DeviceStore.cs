using System.Text.Json;
using System.Text.Json.Serialization;
using Jellyfin.Plugin.Zelefin.Push;

namespace Jellyfin.Plugin.Zelefin.Storage;

public class DeviceRecord
{
    [JsonPropertyName("token")]
    public string Token { get; set; } = string.Empty;

    [JsonPropertyName("deviceId")]
    public Guid DeviceId { get; set; }

    [JsonPropertyName("userId")]
    public Guid UserId { get; set; }

    [JsonPropertyName("updatedAt")]
    public long UpdatedAt { get; set; }

    /// <summary>
    /// "ios" for an APNs token, "android" for a Firebase token. Rows saved before
    /// Android support have no platform and are iOS.
    /// </summary>
    [JsonPropertyName("platform")]
    public string Platform { get; set; } = DevicePlatform.Ios;

    /// <summary>
    /// Last Jellyfin session id for this install, so a closed app can still be
    /// targeted after the WebSocket is gone.
    /// </summary>
    [JsonPropertyName("lastSessionId")]
    public string? LastSessionId { get; set; }

    [JsonIgnore]
    public bool IsAndroid => DevicePlatform.Normalize(Platform) == DevicePlatform.Android;
}

public static class DevicePlatform
{
    public const string Ios = "ios";
    public const string Android = "android";

    public static string Normalize(string? value)
    {
        return string.Equals(value?.Trim(), Android, StringComparison.OrdinalIgnoreCase) ? Android : Ios;
    }
}

/// <summary>
/// JSON file of push tokens (APNs for iOS, Firebase for Android), one row per Zelefin install.
/// </summary>
public sealed class DeviceStore : IDisposable
{
    private readonly string _path;
    private readonly string _sessionsPath;
    private readonly Lock _gate = new();
    private List<DeviceRecord> _records = [];
    private Dictionary<string, Guid> _sessionUsers = new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public DeviceStore(string dataPath)
    {
        Directory.CreateDirectory(dataPath);
        _path = Path.Combine(dataPath, "zelefin-devices.json");
        _sessionsPath = Path.Combine(dataPath, "zelefin-session-users.json");
        Load();
    }

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _records.Count;
            }
        }
    }

    public List<DeviceRecord> All()
    {
        lock (_gate)
        {
            return [.._records];
        }
    }

    public List<DeviceRecord> ForUser(Guid userId)
    {
        lock (_gate)
        {
            return _records.Where(record => record.UserId == userId).ToList();
        }
    }

    public List<DeviceRecord> ForDevice(Guid deviceId)
    {
        if (deviceId == Guid.Empty)
        {
            return [];
        }

        lock (_gate)
        {
            return _records.Where(record => record.DeviceId == deviceId).ToList();
        }
    }

    public List<DeviceRecord> ForSession(string? sessionId)
    {
        var normalized = DisplayMessageWatch.NormalizeId(sessionId);
        if (normalized.Length == 0)
        {
            return [];
        }

        lock (_gate)
        {
            return _records
                .Where(record => DisplayMessageWatch.SameId(record.LastSessionId, normalized))
                .ToList();
        }
    }

    public void RememberSessionUser(string? sessionId, Guid userId)
    {
        var normalized = DisplayMessageWatch.NormalizeId(sessionId);
        if (normalized.Length == 0 || userId == Guid.Empty)
        {
            return;
        }

        lock (_gate)
        {
            if (_sessionUsers.TryGetValue(normalized, out var existing) && existing == userId)
            {
                return;
            }

            _sessionUsers[normalized] = userId;
            SaveSessions();
        }
    }

    public Guid UserForSession(string? sessionId)
    {
        var normalized = DisplayMessageWatch.NormalizeId(sessionId);
        if (normalized.Length == 0)
        {
            return Guid.Empty;
        }

        lock (_gate)
        {
            return _sessionUsers.TryGetValue(normalized, out var userId) ? userId : Guid.Empty;
        }
    }

    public void RememberSession(string? sessionId, string? deviceId)
    {
        if (!Guid.TryParse(deviceId, out var id) || id == Guid.Empty)
        {
            return;
        }

        var normalized = DisplayMessageWatch.NormalizeId(sessionId);
        if (normalized.Length == 0)
        {
            return;
        }

        lock (_gate)
        {
            var record = _records.FirstOrDefault(existing => existing.DeviceId == id);
            if (record is null || DisplayMessageWatch.SameId(record.LastSessionId, normalized))
            {
                return;
            }

            record.LastSessionId = normalized;
            Save();
        }
    }

    public DeviceRecord Upsert(
        Guid deviceId,
        Guid userId,
        string token,
        string? platform = null,
        string? sessionId = null)
    {
        var trimmed = token.Trim();
        var incomingSession = DisplayMessageWatch.NormalizeId(sessionId);

        lock (_gate)
        {
            var existing = _records.FirstOrDefault(entry =>
                entry.DeviceId == deviceId || entry.Token == trimmed);
            var lastSessionId = incomingSession.Length > 0
                ? incomingSession
                : existing?.LastSessionId;
            _records.RemoveAll(entry => entry.DeviceId == deviceId || entry.Token == trimmed);
            var record = new DeviceRecord
            {
                DeviceId = deviceId,
                UserId = userId,
                Token = trimmed,
                UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                Platform = DevicePlatform.Normalize(platform ?? existing?.Platform),
                LastSessionId = lastSessionId
            };
            _records.Add(record);
            Save();
            return record;
        }
    }

    public void Remove(Guid deviceId)
    {
        lock (_gate)
        {
            _records.RemoveAll(record => record.DeviceId == deviceId);
            Save();
        }
    }

    public void RemoveToken(string token)
    {
        lock (_gate)
        {
            _records.RemoveAll(record => record.Token == token);
            Save();
        }
    }

    private void Load()
    {
        if (File.Exists(_path))
        {
            try
            {
                var json = File.ReadAllText(_path);
                _records = JsonSerializer.Deserialize<List<DeviceRecord>>(json, JsonOptions) ?? [];
            }
            catch
            {
                _records = [];
            }
        }

        if (!File.Exists(_sessionsPath))
        {
            return;
        }

        try
        {
            var json = File.ReadAllText(_sessionsPath);
            var parsed = JsonSerializer.Deserialize<Dictionary<string, Guid>>(json, JsonOptions) ?? [];
            _sessionUsers = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in parsed)
            {
                var key = DisplayMessageWatch.NormalizeId(pair.Key);
                if (key.Length > 0 && pair.Value != Guid.Empty)
                {
                    _sessionUsers[key] = pair.Value;
                }
            }
        }
        catch
        {
            _sessionUsers = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private void Save()
    {
        File.WriteAllText(_path, JsonSerializer.Serialize(_records, JsonOptions));
    }

    private void SaveSessions()
    {
        File.WriteAllText(_sessionsPath, JsonSerializer.Serialize(_sessionUsers, JsonOptions));
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
