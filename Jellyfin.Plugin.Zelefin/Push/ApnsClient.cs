using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Jellyfin.Plugin.Zelefin.Push;

public static class ApnsJwt
{
    public static string Create(string keyId, string teamId, string privateKeyPem, long? issuedAt = null)
    {
        var header = Base64Url(JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, string>
        {
            ["alg"] = "ES256",
            ["kid"] = keyId.Trim()
        }));
        var iat = issuedAt ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var payload = Base64Url(JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object>
        {
            ["iss"] = teamId.Trim(),
            ["iat"] = iat
        }));
        var unsigned = $"{header}.{payload}";

        using var key = ECDsa.Create();
        key.ImportFromPem(privateKeyPem);
        var signature = key.SignData(Encoding.ASCII.GetBytes(unsigned), HashAlgorithmName.SHA256);
        return $"{unsigned}.{Base64Url(signature)}";
    }

    public static string Base64Url(byte[] data)
    {
        return Convert.ToBase64String(data)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
}

public sealed class ApnsClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly bool _ownsClient;
    private string? _cachedJwt;
    private long _jwtIssuedAt;

    public ApnsClient(HttpClient? http = null)
    {
        if (http is null)
        {
            _http = new HttpClient();
            _ownsClient = true;
        }
        else
        {
            _http = http;
            _ownsClient = false;
        }
    }

    public async Task<ApnsSendResult> SendAsync(
        ApnsCredentials credentials,
        string deviceToken,
        Api.PushMessage message,
        CancellationToken cancellationToken = default)
    {
        ApnsSendResult? last = null;
        foreach (var production in ApnsRouting.Hosts(credentials.PreferProduction))
        {
            var result = await SendToAsync(credentials, deviceToken, message, production, cancellationToken)
                .ConfigureAwait(false);
            last = result;
            if (result.Success)
            {
                return result;
            }

            if (!ApnsRouting.ShouldTryOtherHost(result.StatusCode, result.Body))
            {
                return result;
            }
        }

        var failed = last!.Value;
        return failed with
        {
            TokenExpired = ApnsRouting.TokenIsDead(failed.StatusCode, failed.Body)
                || ApnsRouting.IsBadDeviceToken(failed.StatusCode, failed.Body)
        };
    }

    private async Task<ApnsSendResult> SendToAsync(
        ApnsCredentials credentials,
        string deviceToken,
        Api.PushMessage message,
        bool production,
        CancellationToken cancellationToken)
    {
        var jwt = Jwt(credentials);
        var host = production ? "api.push.apple.com" : "api.sandbox.push.apple.com";
        var pathToken = deviceToken.Trim().Replace(" ", string.Empty, StringComparison.Ordinal);
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"https://{host}/3/device/{pathToken}")
        {
            Version = new Version(2, 0),
            VersionPolicy = HttpVersionPolicy.RequestVersionOrHigher,
            Content = new StringContent(Payload(message), Encoding.UTF8, "application/json")
        };
        request.Headers.TryAddWithoutValidation("authorization", "bearer " + jwt);
        request.Headers.TryAddWithoutValidation("apns-topic", credentials.BundleId.Trim());
        request.Headers.TryAddWithoutValidation("apns-push-type", "alert");
        request.Headers.TryAddWithoutValidation("apns-priority", "10");

        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var status = (int)response.StatusCode;
        return new ApnsSendResult(status, response.IsSuccessStatusCode, ApnsRouting.TokenIsDead(status, body), body);
    }

    private string Jwt(ApnsCredentials credentials)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (_cachedJwt is not null && now - _jwtIssuedAt < 3000)
        {
            return _cachedJwt;
        }

        _jwtIssuedAt = now;
        _cachedJwt = ApnsJwt.Create(credentials.KeyId, credentials.TeamId, credentials.PrivateKey, now);
        return _cachedJwt;
    }

    public static string Payload(Api.PushMessage message)
    {
        var aps = new Dictionary<string, object>
        {
            ["sound"] = "default",
            ["alert"] = Alert(message)
        };
        var root = new Dictionary<string, object?>
        {
            ["aps"] = aps,
            ["itemId"] = message.ItemId,
            ["seriesId"] = message.SeriesId,
            ["type"] = message.Type,
            ["kind"] = message.Type
        };
        return JsonSerializer.Serialize(root);
    }

    private static Dictionary<string, string> Alert(Api.PushMessage message)
    {
        var alert = new Dictionary<string, string>
        {
            ["title"] = message.Title,
            ["body"] = message.Body
        };
        if (!string.IsNullOrWhiteSpace(message.Subtitle))
        {
            alert["subtitle"] = message.Subtitle;
        }

        return alert;
    }

    public void Dispose()
    {
        if (_ownsClient)
        {
            _http.Dispose();
        }
    }
}

public static class ApnsRouting
{
    public static IEnumerable<bool> Hosts(bool preferProduction)
    {
        yield return preferProduction;
        yield return !preferProduction;
    }

    public static bool ShouldTryOtherHost(int statusCode, string body)
    {
        if (statusCode is >= 500 and <= 599)
        {
            return false;
        }

        return IsBadDeviceToken(statusCode, body);
    }

    public static bool IsBadDeviceToken(int statusCode, string body)
    {
        return statusCode == 400
            && body.Contains("BadDeviceToken", StringComparison.OrdinalIgnoreCase);
    }

    public static bool TokenIsDead(int statusCode, string body)
    {
        if (statusCode == 410)
        {
            return true;
        }

        return body.Contains("Unregistered", StringComparison.OrdinalIgnoreCase);
    }
}

public readonly record struct ApnsSendResult(int StatusCode, bool Success, bool TokenExpired, string Body);

public sealed record ApnsCredentials(
    string KeyId,
    string TeamId,
    string BundleId,
    string PrivateKey,
    bool PreferProduction);
