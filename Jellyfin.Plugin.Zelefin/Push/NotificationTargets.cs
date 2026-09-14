using Jellyfin.Plugin.Zelefin.Api;
using Jellyfin.Plugin.Zelefin.Storage;

namespace Jellyfin.Plugin.Zelefin.Push;

public static class NotificationTargets
{
    public static IReadOnlyList<DeviceRecord> Resolve(
        PushNotificationRequest request,
        IReadOnlyList<DeviceRecord> all,
        IReadOnlySet<Guid> adminUserIds,
        Func<string, Guid?> userIdByName)
    {
        var tokens = new Dictionary<Guid, DeviceRecord>();

        if (request.UserId is { } userId)
        {
            Add(tokens, all.Where(record => record.UserId == userId));
        }
        else if (!string.IsNullOrWhiteSpace(request.Username))
        {
            var named = userIdByName(request.Username.Trim());
            if (named is { } namedId)
            {
                Add(tokens, all.Where(record => record.UserId == namedId));
            }
        }
        else if (!request.IsAdmin)
        {
            Add(tokens, all);
        }

        if (request.IsAdmin)
        {
            Add(tokens, all.Where(record => adminUserIds.Contains(record.UserId)));
        }

        return tokens.Values.ToList();
    }

    public static IReadOnlyList<DeviceRecord> ForUsers(
        IReadOnlyList<DeviceRecord> all,
        IEnumerable<Guid> userIds)
    {
        var wanted = userIds.ToHashSet();
        return all.Where(record => wanted.Contains(record.UserId)).ToList();
    }

    public static IReadOnlyList<DeviceRecord> ForAdmins(
        IReadOnlyList<DeviceRecord> all,
        IReadOnlySet<Guid> adminUserIds,
        IEnumerable<Guid>? excludedUserIds = null)
    {
        var excluded = excludedUserIds?.ToHashSet() ?? [];
        return all
            .Where(record => adminUserIds.Contains(record.UserId) && !excluded.Contains(record.UserId))
            .ToList();
    }

    private static void Add(IDictionary<Guid, DeviceRecord> tokens, IEnumerable<DeviceRecord> records)
    {
        foreach (var record in records)
        {
            tokens[record.DeviceId] = record;
        }
    }
}
