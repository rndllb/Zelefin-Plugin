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

1. Dashboard → Plugins → Catalog → ⚙️ → Add repository:

   ```
   https://raw.githubusercontent.com/rndllb/Zelefin-Plugin/main/manifest.json
   ```

2. Catalog → Zelefin → Install → restart Jellyfin.

The catalog card uses [`thumb.png`](thumb.png).

Manual install: copy **only** `Jellyfin.Plugin.Zelefin.dll` into a **versioned** folder. Do not copy `meta.json` — Jellyfin writes that file itself, and a root-owned copy will crash startup.

Use the same versioned folder layout as other Jellyfin plugins:

- Linux: `/var/lib/jellyfin/plugins/Zelefin_1.0.0.0/`
- Docker: `/config/data/plugins/Zelefin_1.0.0.0/`
- Windows: `%AppData%\Jellyfin\Server\plugins\Zelefin_1.0.0.0\`

The plugins directory must be writable by the Jellyfin process. If you copied files as root:

```bash
chown -R 1000:1000 /config/data/plugins/Zelefin_1.0.0.0
```

If a previous `plugins/Zelefin` folder exists, delete it before restarting. Do not copy `Jellyfin.Controller.dll` or anything else from `bin/`.

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

The hosted sender is [`relay-node/`](relay-node/README.md). [`relay/`](relay/README.md) is an optional .NET process for a VPS or Docker host. Do not put a `.p8` in this repository.

## Build

```bash
dotnet test
dotnet build Jellyfin.Plugin.Zelefin --configuration Release
```

The release artifact is only `Jellyfin.Plugin.Zelefin.dll`. Jellyfin 12 already ships the controller assemblies. The Apple Push key is not in this DLL.

```bash
make zip
# dist/zelefin-1.0.0.0.zip
```

## Deploy to a local Docker host

`scripts/deploy-zimaos.sh` builds the DLL, copies **only** that file into a versioned plugin folder owned by uid `1000` (the Jellyfin container user), and restarts the `jellyfin` container. It does not copy `meta.json`.

```bash
make deploy
# or: ./scripts/deploy-zimaos.sh
```

Defaults: SSH host `zimaos`, plugins at `/DATA/AppData/jellyfin/config/data/plugins`, owner `1000:1000`. Override with `--host` or `ZELEFIN_*` environment variables. `./scripts/deploy-zimaos.sh --help` lists them.

## License

MIT
