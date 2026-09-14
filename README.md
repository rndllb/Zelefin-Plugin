# Zelefin Companion Plugin

Jellyfin plugin for the [Zelefin](https://github.com/rndllb/Zelefin) iOS app. Site: [zelefin.app](https://zelefin.app).

Admins set Seerr and Intro Skipper defaults once. Every Zelefin install on this server picks them up after sign-in. The same plugin sends iPhone notifications when titles are added, when sessions start, and when an account is locked. Jellyfin admins do not need an Apple Developer account.

The server is the source of truth. The app does not ask each user to wire plugins by hand.

## What it does

- **Seerr** — Dashboard → Plugins → Zelefin. Paste the Seerr URL and API key (Seerr → Settings → General). Zelefin signs each user in automatically.
- **Intro Skipper defaults** — Optional auto-skip for intros and credits, with a lock so devices cannot override them.
- **Push notifications** — Item added (movies and grouped episodes), session started (admins), playback started (admins), user locked out, plus a webhook for Seerr or the Jellyfin webhook plugin. Alerts go through [Zelefin’s push service](https://push.zelefin.app); you never paste an Apple key.

Requires Jellyfin 12 / .NET 10.

## Install

Dashboard → Plugins → Catalog → ⚙️ → Add repository:

```
https://raw.githubusercontent.com/rndllb/Zelefin-Plugin/main/manifest.json
```

Catalog → Zelefin → Install → restart Jellyfin.

## Configure

Dashboard → Plugins → Zelefin

**Application**

- Seerr / Jellyseerr URL and API key (Seerr → Settings → General)
- Auto-skip intros / credits, and whether those are locked
- iPhone notifications: no Apple setup. Phones register after sign-in.

**Notifications**

- Item added, optional library filter, episode grouping window
- Session started, playback started, user locked out
- Send a test alert to the signed-in admin's Zelefin install

[Notification setup](NOTIFICATIONS.md)

## App API

| Method | Path | Who |
| --- | --- | --- |
| `GET` | `/Zelefin/config` | Signed-in user |
| `POST` | `/Zelefin/device` | Signed-in user (APNs token) |
| `DELETE` | `/Zelefin/device/{deviceId}` | Signed-in user |
| `POST` | `/Zelefin/notification` | Signed-in user or API key |

`GET /Zelefin/config` never returns Apple Push credentials. The plugin does not ship an APNs `.p8`.

## Push service

Phones register an Apple Push token with this plugin. The plugin posts to `https://push.zelefin.app/v1/send`. Apple credentials stay with the Zelefin publisher.

## License

MIT
