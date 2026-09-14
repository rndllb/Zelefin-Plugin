#!/usr/bin/env bash
# Copy the Zelefin plugin onto the Zima OS Jellyfin box with the same
# ownership Catalog uses (uid 1000). A root-owned folder or meta.json
# makes Jellyfin crash on startup.
set -euo pipefail

HOST="${ZELEFIN_DEPLOY_HOST:-zimaos}"
CONTAINER="${ZELEFIN_JELLYFIN_CONTAINER:-jellyfin}"
PLUGINS_HOST="${ZELEFIN_PLUGINS_DIR:-/DATA/AppData/jellyfin/config/data/plugins}"
OWNER_UID="${ZELEFIN_JELLYFIN_UID:-1000}"
OWNER_GID="${ZELEFIN_JELLYFIN_GID:-1000}"
RESTART=1

usage() {
  cat <<EOF
Usage: $(basename "$0") [--no-restart] [--host HOST]

Deploys Jellyfin.Plugin.Zelefin.dll to the Zima OS Jellyfin server.

  --no-restart   Copy the DLL but do not restart the jellyfin container
  --host HOST    SSH host (default: ${HOST})

Environment:
  ZELEFIN_DEPLOY_HOST          SSH host (default: zimaos)
  ZELEFIN_JELLYFIN_CONTAINER   Docker name (default: jellyfin)
  ZELEFIN_PLUGINS_DIR          Host plugins dir
  ZELEFIN_JELLYFIN_UID         File owner uid (default: 1000)
  ZELEFIN_JELLYFIN_GID         File owner gid (default: 1000)
EOF
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --no-restart) RESTART=0; shift ;;
    --host) HOST="$2"; shift 2 ;;
    -h|--help) usage; exit 0 ;;
    *) echo "Unknown option: $1" >&2; usage; exit 2 ;;
  esac
done

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
VERSION="$(awk -F '"' '/^version:/{print $2; exit}' "$ROOT/build.yaml")"
[[ -n "$VERSION" ]] || { echo "Could not read version from build.yaml" >&2; exit 1; }

echo "Building Zelefin ${VERSION}…"
dotnet build "$ROOT/Jellyfin.Plugin.Zelefin/Jellyfin.Plugin.Zelefin.csproj" \
  --configuration Release --nologo --verbosity quiet

DLL="$ROOT/Jellyfin.Plugin.Zelefin/bin/Release/net10.0/Jellyfin.Plugin.Zelefin.dll"
[[ -f "$DLL" ]] || { echo "Missing $DLL" >&2; exit 1; }

FOLDER="Zelefin_${VERSION}"
DEST="${PLUGINS_HOST}/${FOLDER}"
REMOTE_TMP="/tmp/Jellyfin.Plugin.Zelefin.dll"

echo "Copying DLL to ${HOST}:${DEST}"
scp -q "$DLL" "${HOST}:${REMOTE_TMP}"

ssh "$HOST" env \
  DEST="$DEST" \
  PLUGINS_HOST="$PLUGINS_HOST" \
  FOLDER="$FOLDER" \
  OWNER_UID="$OWNER_UID" \
  OWNER_GID="$OWNER_GID" \
  CONTAINER="$CONTAINER" \
  RESTART="$RESTART" \
  REMOTE_TMP="$REMOTE_TMP" \
  bash -s <<'REMOTE'
set -euo pipefail

sudo mkdir -p "$DEST"

# Unversioned folder was the crash: Jellyfin could not write meta.json.
if [[ -d "${PLUGINS_HOST}/Zelefin" ]]; then
  echo "Removing leftover ${PLUGINS_HOST}/Zelefin"
  sudo rm -rf "${PLUGINS_HOST}/Zelefin"
fi

# Keep a single version so PluginManager does not load two copies.
shopt -s nullglob
for dir in "${PLUGINS_HOST}"/Zelefin_*; do
  if [[ "$(basename "$dir")" != "$FOLDER" ]]; then
    echo "Removing old $(basename "$dir")"
    sudo rm -rf "$dir"
  fi
done

sudo install -m 644 -o "$OWNER_UID" -g "$OWNER_GID" \
  "$REMOTE_TMP" "$DEST/Jellyfin.Plugin.Zelefin.dll"
rm -f "$REMOTE_TMP"

# Drop a root-owned manifest so Jellyfin can write its own.
if [[ -e "$DEST/meta.json" ]]; then
  owner="$(stat -c %u "$DEST/meta.json")"
  if [[ "$owner" != "$OWNER_UID" ]]; then
    echo "Removing root-owned meta.json"
    sudo rm -f "$DEST/meta.json"
  fi
fi

sudo chown -R "${OWNER_UID}:${OWNER_GID}" "$DEST"
sudo chmod 755 "$DEST"
sudo find "$DEST" -type f -exec chmod 644 {} +

echo "Installed:"
ls -la "$DEST"

if [[ "$RESTART" == "1" ]]; then
  echo "Restarting ${CONTAINER}…"
  docker restart "$CONTAINER" >/dev/null
  echo "Jellyfin restarted."
fi
REMOTE

echo "Done. Dashboard → Plugins should list Zelefin ${VERSION}."
