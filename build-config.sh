#!/usr/bin/env bash

# Shared Linux build metadata. Keep the actual versions in SkuaPlayerVersion.props;
# this file only exposes them consistently to the packaging scripts.
if [[ -z "${SCRIPT_DIR:-}" ]]; then
    SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
fi

VERSION_PROPS="$SCRIPT_DIR/SkuaPlayerVersion.props"
if [[ ! -s "$VERSION_PROPS" ]]; then
    echo "Error: missing version properties: $VERSION_PROPS" >&2
    return 1 2>/dev/null || exit 1
fi

read_prop() {
    local name="$1"
    sed -n "s:.*<${name}>\\([^<]*\\)</${name}>.*:\\1:p" "$VERSION_PROPS" | head -n 1
}

SKUA_PLAYER_VERSION="$(read_prop SkuaPlayerVersion)"
SKUA_PLAYER_OS="$(read_prop SkuaPlayerOs)"
WATCHDOG_VERSION="$(read_prop WatchDogVersion)"

if [[ -z "$SKUA_PLAYER_VERSION" || -z "$SKUA_PLAYER_OS" || -z "$WATCHDOG_VERSION" ]]; then
    echo "Error: SkuaPlayerVersion.props is missing required version properties." >&2
    return 1 2>/dev/null || exit 1
fi

SKUA_PLAYER_CACHE="$SCRIPT_DIR/player/${SKUA_PLAYER_VERSION}-${SKUA_PLAYER_OS}"
SKUA_PLAYER_ARCHIVE="Mugetsu-${SKUA_PLAYER_VERSION}-${SKUA_PLAYER_OS}.tar.gz"
SKUA_PLAYER_URL="https://github.com/retr0gr4d3/skua-mugetsu/releases/download/v${SKUA_PLAYER_VERSION}/${SKUA_PLAYER_ARCHIVE}"
SKUA_PLAYER_V2_CACHE="$SCRIPT_DIR/player/${SKUA_PLAYER_VERSION}-${SKUA_PLAYER_OS}-x86-64-v2"
SKUA_PLAYER_V2_ARCHIVE="Mugetsu-${SKUA_PLAYER_VERSION}-${SKUA_PLAYER_OS}-x86-64-v2.tar.gz"
SKUA_PLAYER_V2_URL="https://github.com/retr0gr4d3/skua-mugetsu/releases/download/v${SKUA_PLAYER_VERSION}/${SKUA_PLAYER_V2_ARCHIVE}"

WATCHDOG_CACHE="$SCRIPT_DIR/Build/.cache/watchdog/v${WATCHDOG_VERSION}/WatchDog.dll"
WATCHDOG_URL="https://github.com/JonasAlv/Watch-Dog-linux/releases/download/v${WATCHDOG_VERSION}/WatchDog.dll"
