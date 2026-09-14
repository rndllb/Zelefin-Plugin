# Zelefin push relay

Publisher-operated APNs service. Jellyfin plugins POST device tokens here. The Apple `.p8` lives only as a secret on this host — never in the plugin DLL, git, or the Jellyfin dashboard.

You (the App Store account) hold Apple once. Other Jellyfin admins only install the plugin.

## Environment

| Variable | Required | Meaning |
| --- | --- | --- |
| `APNS_KEY_ID` | yes | Key ID from Apple Developer → Keys |
| `APNS_TEAM_ID` | yes | Apple Team ID |
| `APNS_BUNDLE_ID` | no | Defaults to `app.zelefin.client` |
| `APNS_PRIVATE_KEY` | one of these | PEM contents. Use `\n` for newlines if the host cannot take a multiline value |
| `APNS_PRIVATE_KEY_FILE` | one of these | Path to the `.p8` on the host |
| `APNS_PRODUCTION` | no | `true` to try production APNs first. The sender still falls back to sandbox |

Do not commit the `.p8`.

## Run locally

```bash
export APNS_KEY_ID=...
export APNS_TEAM_ID=...
export APNS_PRIVATE_KEY_FILE=/path/to/AuthKey.p8
dotnet run --project relay/Zelefin.PushRelay.csproj
```

`GET /healthz` is ready when the key loaded. `POST /v1/send` body:

```json
{
  "tokens": ["apns-device-token"],
  "title": "Zelefin",
  "subtitle": "optional",
  "body": "Notifications are working.",
  "itemId": "optional",
  "seriesId": "optional",
  "type": "Test"
}
```

Response: `{ "expiredTokens": [] }`. The plugin drops those tokens.

## Docker

Build from the plugin repo root so the Dockerfile can reach `ApnsClient.cs`:

```bash
docker build -f relay/Dockerfile -t zelefin-push-relay .
docker run --rm -p 8080:8080 \
  -e APNS_KEY_ID \
  -e APNS_TEAM_ID \
  -e APNS_BUNDLE_ID=app.zelefin.client \
  -e APNS_PRIVATE_KEY_FILE=/secrets/AuthKey.p8 \
  -v /path/to/AuthKey.p8:/secrets/AuthKey.p8:ro \
  zelefin-push-relay
```

Point a public HTTPS hostname at this process. Production for Zelefin is the Node app (`relay-node/`) at `https://push.zelefin.app/v1/send`. Keep this .NET project for a VPS or Docker host. A Jellyfin that already has a `.p8` in plugin config still talks to Apple directly.
