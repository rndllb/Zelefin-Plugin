using System.Text.Json;
using System.Text.Json.Serialization;

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
}

/// <summary>
/// JSON file of APNs device tokens, one row per Zelefin install.
/// </summary>
public sealed class DeviceStore : IDisposable
{
    private readonly string _path;
    private readonly Lock _gate = new();
    private List<DeviceRecord> _records = [];
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

    public DeviceRecord Upsert(Guid deviceId, Guid userId, string token)
    {
        var record = new DeviceRecord
        {
            DeviceId = deviceId,
            UserId = userId,
            Token = token.Trim(),
            UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };

        lock (_gate)
        {
            _records.RemoveAll(existing => existing.DeviceId == deviceId || existing.Token == record.Token);
            _records.Add(record);
            Save();
        }

        return record;
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
        if (!File.Exists(_path))
        {
            return;
        }

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

    private void Save()
    {
        File.WriteAllText(_path, JsonSerializer.Serialize(_records, JsonOptions));
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
