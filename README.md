# Zelefin Companion Plugin

Jellyfin plugin for the [Zelefin](https://github.com/rndllb/Zelefin) iOS and Android apps. Site: [zelefin.app](https://zelefin.app).

Admins set Seerr and Intro Skipper defaults once. Every Zelefin install on this server picks them up after sign-in. The same plugin sends iPhone, iPad, and Android notifications when titles are added, when sessions start, and when an account is locked. Jellyfin admins do not need an Apple Developer account or a Firebase project.

The server is the source of truth. The app does not ask each user to wire plugins by hand.

## What it does

- **Seerr** — Dashboard → Plugins → Zelefin. Paste the Seerr URL and API key (Seerr → Settings → General). Zelefin signs each user in automatically.
- **Intro Skipper defaults** — Optional auto-skip for intros and credits, with a lock so devices cannot override them.
- **Push notifications** — Item added (movies and grouped episodes), session started (admins), playback started (admins), user locked out, plus a webhook for Seerr or the Jellyfin webhook plugin. Alerts go through [Zelefin’s push service](https://push.zelefin.app); you never paste an Apple key or Firebase credentials.

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
- Phone notifications: no Apple or Firebase setup. iOS and Android devices register after sign-in.

**Notifications**

- Item added, optional library filter, episode grouping window
- Session started, playback started, user locked out
- Send a test alert to the signed-in admin's Zelefin install

[Notification setup](NOTIFICATIONS.md)

## App API

| Method | Path | Who |
| --- | --- | --- |
| `GET` | `/Zelefin/config` | Signed-in user |
| `POST` | `/Zelefin/device` | Signed-in user (`token`, `deviceId`, `platform`: `ios` for APNs or `android` for Firebase; defaults to `ios`) |
| `DELETE` | `/Zelefin/device/{deviceId}` | Signed-in user |
| `POST` | `/Zelefin/notification` | Signed-in user or API key |

`GET /Zelefin/config` never returns push credentials. The plugin does not ship an APNs `.p8` or a Firebase service account.

## Push service

iPhones and iPads register an Apple Push token; Android devices register a Firebase Cloud Messaging token. The plugin posts both to `https://push.zelefin.app/v1/send` (`tokens` for APNs, `fcmTokens` for Firebase). Apple and Firebase credentials stay with the Zelefin publisher.

## License

MIT
