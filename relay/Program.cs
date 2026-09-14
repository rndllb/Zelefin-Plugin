using Jellyfin.Plugin.Zelefin.Api;
using Jellyfin.Plugin.Zelefin.Push;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Services.AddSingleton<ApnsClient>();

var app = builder.Build();
var credentials = ApnsEnvironment.Load(builder.Configuration);
if (credentials is null)
{
    app.Logger.LogWarning("APNs env vars are missing. Set APNS_KEY_ID, APNS_TEAM_ID, and APNS_PRIVATE_KEY or APNS_PRIVATE_KEY_FILE.");
}

app.MapGet("/healthz", () => credentials is null
    ? Results.Problem("APNs is not configured")
    : Results.Ok(new { ok = true }));

app.MapPost("/v1/send", async (RelayRequest request, ApnsClient apns, CancellationToken cancellationToken) =>
{
    if (credentials is null)
    {
        return Results.Problem("APNs is not configured");
    }

    var tokens = (request.Tokens ?? [])
        .Where(token => !string.IsNullOrWhiteSpace(token))
        .Select(token => token.Trim())
        .Distinct(StringComparer.Ordinal)
        .Take(200)
        .ToList();
    if (tokens.Count == 0 || string.IsNullOrWhiteSpace(request.Body))
    {
        return Results.BadRequest();
    }

    var message = new PushMessage
    {
        Title = string.IsNullOrWhiteSpace(request.Title) ? "Zelefin" : request.Title.Trim(),
        Subtitle = string.IsNullOrWhiteSpace(request.Subtitle) ? null : request.Subtitle.Trim(),
        Body = request.Body.Trim(),
        ItemId = EmptyToNull(request.ItemId),
        SeriesId = EmptyToNull(request.SeriesId),
        Type = EmptyToNull(request.Type)
    };

    var expired = new List<string>();
    foreach (var token in tokens)
    {
        var result = await apns.SendAsync(credentials, token, message, cancellationToken).ConfigureAwait(false);
        if (result.TokenExpired)
        {
            expired.Add(token);
        }
    }

    return Results.Ok(new RelayResponse { ExpiredTokens = expired });
});

app.Run();

static string? EmptyToNull(string? value)
{
    var trimmed = value?.Trim();
    return string.IsNullOrEmpty(trimmed) ? null : trimmed;
}

internal static class ApnsEnvironment
{
    public static ApnsCredentials? Load(IConfiguration config)
    {
        var keyId = config["APNS_KEY_ID"] ?? string.Empty;
        var teamId = config["APNS_TEAM_ID"] ?? string.Empty;
        var bundleId = config["APNS_BUNDLE_ID"] ?? "app.zelefin.client";
        var pem = config["APNS_PRIVATE_KEY"];
        var pemFile = config["APNS_PRIVATE_KEY_FILE"];
        if (string.IsNullOrWhiteSpace(pem) && !string.IsNullOrWhiteSpace(pemFile) && File.Exists(pemFile))
        {
            pem = File.ReadAllText(pemFile);
        }

        pem = UnescapePem(pem);
        if (string.IsNullOrWhiteSpace(keyId)
            || string.IsNullOrWhiteSpace(teamId)
            || string.IsNullOrWhiteSpace(bundleId)
            || string.IsNullOrWhiteSpace(pem)
            || (!pem.Contains("BEGIN PRIVATE KEY", StringComparison.Ordinal)
                && !pem.Contains("BEGIN EC PRIVATE KEY", StringComparison.Ordinal)))
        {
            return null;
        }

        var production = string.Equals(config["APNS_PRODUCTION"], "true", StringComparison.OrdinalIgnoreCase);
        return new ApnsCredentials(keyId.Trim(), teamId.Trim(), bundleId.Trim(), pem, production);
    }

    private static string UnescapePem(string? pem)
    {
        return (pem ?? string.Empty).Replace("\\n", "\n", StringComparison.Ordinal);
    }
}

internal sealed class RelayRequest
{
    public List<string>? Tokens { get; set; }

    public string? Title { get; set; }

    public string? Subtitle { get; set; }

    public string Body { get; set; } = string.Empty;

    public string? ItemId { get; set; }

    public string? SeriesId { get; set; }

    public string? Type { get; set; }
}

internal sealed class RelayResponse
{
    public List<string> ExpiredTokens { get; set; } = [];
}
