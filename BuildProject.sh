#!/usr/bin/env bash
set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
BUILD_DIR="$SCRIPT_DIR/Build"
WATCHDOG_VERSION="$(sed -n 's:.*<WatchDogVersion>\([^<]*\)</WatchDogVersion>.*:\1:p' "$SCRIPT_DIR/SkuaPlayerVersion.props")"
WATCHDOG_CACHE="$BUILD_DIR/.cache/watchdog/v${WATCHDOG_VERSION}/WatchDog.dll"
WATCHDOG_URL="https://github.com/JonasAlv/Watch-Dog-linux/releases/download/v${WATCHDOG_VERSION}/WatchDog.dll"
SKUA_PLAYER_VERSION="$(sed -n 's:.*<SkuaPlayerVersion>\([^<]*\)</SkuaPlayerVersion>.*:\1:p' "$SCRIPT_DIR/SkuaPlayerVersion.props")"
SKUA_PLAYER_OS="$(sed -n 's:.*<SkuaPlayerOs>\([^<]*\)</SkuaPlayerOs>.*:\1:p' "$SCRIPT_DIR/SkuaPlayerVersion.props")"
SKUA_PLAYER_CACHE="$SCRIPT_DIR/player/${SKUA_PLAYER_VERSION}-${SKUA_PLAYER_OS}"
SKUA_PLAYER_ARCHIVE="Mugetsu-${SKUA_PLAYER_VERSION}-${SKUA_PLAYER_OS}.tar.gz"
SKUA_PLAYER_URL="https://github.com/retr0gr4d3/skua-mugetsu/releases/download/v${SKUA_PLAYER_VERSION}/${SKUA_PLAYER_ARCHIVE}"

echo "=== Building VibeSkua (Linux Release) ==="
dotnet build "$SCRIPT_DIR/Skua.sln" -c Release

echo "=== Preparing Build directory ==="
mkdir -p "$BUILD_DIR"
mkdir -p "$BUILD_DIR/plugins"

# 1. Copy Application output
APP_BIN="$SCRIPT_DIR/Skua.App.Avalonia/bin/Release/net10.0"
cp -f "$APP_BIN"/VibeSkua* "$BUILD_DIR/" 2>/dev/null || true
cp -f "$APP_BIN"/*.dll "$BUILD_DIR/" 2>/dev/null || true
cp -f "$APP_BIN"/*.json "$BUILD_DIR/" 2>/dev/null || true
cp -rf "$APP_BIN/runtimes" "$BUILD_DIR/" 2>/dev/null || true

# 2. Copy Manager output
MGR_BIN="$SCRIPT_DIR/Skua.Manager.Avalonia/bin/Release/net10.0"
cp -f "$MGR_BIN"/VibeSkua.Manager* "$BUILD_DIR/" 2>/dev/null || true

# 3. Copy Plugins
cp -f "$SCRIPT_DIR/Skua.Plugin.DailyTracker/bin/Release/net10.0"/Skua.Plugin.*.dll "$BUILD_DIR/plugins/" 2>/dev/null || true
cp -f "$SCRIPT_DIR/Skua.Plugin.ButlerV5/bin/Release/net10.0"/ButlerV5.dll "$BUILD_DIR/plugins/" 2>/dev/null || true
cp -f "$SCRIPT_DIR/Skua.Plugin.Cosmetics/bin/Release/net10.0"/Skua.Plugin.Cosmetics.dll "$BUILD_DIR/plugins/" 2>/dev/null || true
if [ ! -f "$WATCHDOG_CACHE" ]; then
    mkdir -p "$(dirname "$WATCHDOG_CACHE")"
    tmp_watchdog="$WATCHDOG_CACHE.tmp"
    echo "Fetching Watch Dog v${WATCHDOG_VERSION} from GitHub Releases..."
    if curl -fsSL "$WATCHDOG_URL" -o "$tmp_watchdog" && [ -s "$tmp_watchdog" ]; then
        mv -f "$tmp_watchdog" "$WATCHDOG_CACHE"
    else
        rm -f "$tmp_watchdog"
        echo "Error: Failed to download Watch Dog v${WATCHDOG_VERSION}." >&2
        exit 1
    fi
fi
cp -f "$WATCHDOG_CACHE" "$BUILD_DIR/plugins/WatchDog.dll"

# 4. Copy SWF client
if [ -f "$SCRIPT_DIR/Skua.AS3/skua/bin/skua.swf" ]; then
    cp -f "$SCRIPT_DIR/Skua.AS3/skua/bin/skua.swf" "$BUILD_DIR/skua.swf"
fi

# 5. Copy official skua-mugetsu Flash runner (https://github.com/retr0gr4d3/skua-mugetsu)
mkdir -p "$BUILD_DIR/player"
if [ -d "$APP_BIN/player" ] && [ -f "$APP_BIN/player/Mugetsu" ]; then
    echo "Using bundled skua-mugetsu from $APP_BIN/player..."
    cp -rf "$APP_BIN/player"/* "$BUILD_DIR/player/"
    cp -f "$BUILD_DIR/player/Mugetsu" "$BUILD_DIR/Mugetsu"
    chmod +x "$BUILD_DIR/player/Mugetsu" "$BUILD_DIR/Mugetsu"
elif [ -f "$SKUA_PLAYER_CACHE/Mugetsu/Mugetsu" ]; then
    echo "Using cached skua-mugetsu from player/..."
    cp -f "$SKUA_PLAYER_CACHE/Mugetsu/Mugetsu" "$BUILD_DIR/player/Mugetsu"
    cp -f "$BUILD_DIR/player/Mugetsu" "$BUILD_DIR/Mugetsu"
    chmod +x "$BUILD_DIR/player/Mugetsu" "$BUILD_DIR/Mugetsu"
else
    echo "Fetching skua-mugetsu v${SKUA_PLAYER_VERSION} from GitHub Releases..."
    TMP_ARCHIVE="$BUILD_DIR/skua-mugetsu.tar.gz"
    if curl -sSL --fail "$SKUA_PLAYER_URL" -o "$TMP_ARCHIVE" && [ -s "$TMP_ARCHIVE" ]; then
        tar -xzf "$TMP_ARCHIVE" -C "$BUILD_DIR/player/" --strip-components=1
        rm -f "$TMP_ARCHIVE"
        cp -f "$BUILD_DIR/player/Mugetsu" "$BUILD_DIR/Mugetsu"
        chmod +x "$BUILD_DIR/player/Mugetsu" "$BUILD_DIR/Mugetsu"
    else
        rm -f "$TMP_ARCHIVE"
        echo "Error: Failed to download official skua-mugetsu from GitHub Releases!" >&2
        exit 1
    fi
fi

# 6. Set executable permissions
chmod +x "$BUILD_DIR/VibeSkua" 2>/dev/null || true
chmod +x "$BUILD_DIR/VibeSkua.Manager" 2>/dev/null || true
chmod +x "$BUILD_DIR/Mugetsu" 2>/dev/null || true

echo "=== Build complete! Artifacts are in: $BUILD_DIR ==="
