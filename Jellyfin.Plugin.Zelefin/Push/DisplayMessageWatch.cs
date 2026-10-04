using System.Text.Json;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.Zelefin.Api;

namespace Jellyfin.Plugin.Zelefin.Push;

/// <summary>
/// Parses Jellyfin session Message/Command POSTs so a closed Zelefin install
/// can still get the lock-screen push. Live WebSocket delivery stays with the app.
/// </summary>
public static class DisplayMessageWatch
{
    public const string PushType = "sessionMessage";

    private static readonly Regex PathPattern = new(
        @"/Sessions/([^/]+)/(Message|Command)/?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static bool TryMatchPath(string? path, out string sessionId, out bool isCommand)
    {
        sessionId = string.Empty;
        isCommand = false;
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var match = PathPattern.Match(path);
        if (!match.Success)
        {
            return false;
        }

        sessionId = match.Groups[1].Value;
        isCommand = match.Groups[2].Value.Equals("Command", StringComparison.OrdinalIgnoreCase);
        return !string.IsNullOrWhiteSpace(sessionId);
    }

    public static string NormalizeId(string? value)
    {
        return (value ?? string.Empty).Replace("-", "", StringComparison.Ordinal).Trim();
    }

    public static bool SameId(string? left, string? right)
    {
        var a = NormalizeId(left);
        var b = NormalizeId(right);
        return a.Length > 0 && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }

    public static bool TryParse(string? json, bool isCommand, out string header, out string text)
    {
        header = "Message";
        text = string.Empty;
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (isCommand)
            {
                var name = ReadString(root, "Name") ?? ReadString(root, "name");
                if (!string.Equals(name, "DisplayMessage", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                if (!TryProperty(root, "Arguments", out var arguments)
                    && !TryProperty(root, "arguments", out arguments))
                {
                    return false;
                }

                return ReadHeaderText(arguments, out header, out text);
            }

            return ReadHeaderText(root, out header, out text);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static PushMessage ToPush(string header, string text)
    {
        var title = string.IsNullOrWhiteSpace(header) ? "Message" : header.Trim();
        var body = string.IsNullOrWhiteSpace(text) ? title : text.Trim();
        return new PushMessage
        {
            Title = title,
            Body = body,
            Type = PushType
        };
    }

    private static bool ReadHeaderText(JsonElement root, out string header, out string text)
    {
        header = ReadString(root, "Header", "header", "Title", "title") ?? "Message";
        text = ReadString(root, "Text", "text", "Message", "message") ?? string.Empty;
        if (string.IsNullOrWhiteSpace(header))
        {
            header = "Message";
        }

        if (string.IsNullOrWhiteSpace(text) && string.Equals(header, "Message", StringComparison.Ordinal))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            text = header;
        }

        return true;
    }

    private static string? ReadString(JsonElement root, params string[] names)
    {
        foreach (var name in names)
        {
            if (!TryProperty(root, name, out var property))
            {
                continue;
            }

            var value = property.ValueKind switch
            {
                JsonValueKind.String => property.GetString(),
                JsonValueKind.Number => property.GetRawText(),
                _ => null
            };
            var trimmed = value?.Trim();
            if (!string.IsNullOrEmpty(trimmed))
            {
                return trimmed;
            }
        }

        return null;
    }

    private static bool TryProperty(JsonElement root, string name, out JsonElement property)
    {
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out property))
        {
            return true;
        }

        property = default;
        return false;
    }
}
