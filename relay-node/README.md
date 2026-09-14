# Zelefin push (Node)

Hosted Apple Push sender for Zelefin. Jellyfin plugins POST to `https://push.zelefin.app/v1/send`.

This is what runs in production. Do not put a `.p8` in git. Set environment variables on the host:

| Variable | Required | Meaning |
| --- | --- | --- |
| `APNS_KEY_ID` | yes | Key ID from Apple Developer → Keys |
| `APNS_TEAM_ID` | yes | Apple Team ID |
| `APNS_BUNDLE_ID` | no | Defaults to `app.zelefin.client` |
| `APNS_PRIVATE_KEY` | yes | PEM contents. `\n` is fine for newlines |
| `APNS_PRODUCTION` | no | `true` to try production APNs first. Sandbox is still tried |

`GET /healthz` returns `{ "ok": true }` when the key loaded. The process must bind `process.env.PORT`.
