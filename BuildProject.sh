#!/usr/bin/env bash
set -Eeuo pipefail

# Optional: WATCHDOG_LOCAL may point to a locally built WatchDog.dll or its containing folder.

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
BUILD_DIR="$SCRIPT_DIR/Build"
WATCHDOG_VERSION="$(sed -n 's:.*<WatchDogVersion>\([^<]*\)</WatchDogVersion>.*:\1:p' "$SCRIPT_DIR/SkuaPlayerVersion.props")"
WATCHDOG_CACHE="$BUILD_DIR/.cache/watchdog/v${WATCHDOG_VERSION}/WatchDog.dll"
WATCHDOG_URL="https://github.com/JonasAlv/Watch-Dog-linux/releases/download/v${WATCHDOG_VERSION}/WatchDog.dll"
WATCHDOG_LOCAL="${WATCHDOG_LOCAL:-}"
SKUA_PLAYER_VERSION="$(sed -n 's:.*<SkuaPlayerVersion>\([^<]*\)</SkuaPlayerVersion>.*:\1:p' "$SCRIPT_DIR/SkuaPlayerVersion.props")"
SKUA_PLAYER_OS="$(sed -n 's:.*<SkuaPlayerOs>\([^<]*\)</SkuaPlayerOs>.*:\1:p' "$SCRIPT_DIR/SkuaPlayerVersion.props")"
SKUA_PLAYER_CACHE="$SCRIPT_DIR/player/${SKUA_PLAYER_VERSION}-${SKUA_PLAYER_OS}"
SKUA_PLAYER_ARCHIVE="Mugetsu-${SKUA_PLAYER_VERSION}-${SKUA_PLAYER_OS}.tar.gz"
SKUA_PLAYER_URL="https://github.com/retr0gr4d3/skua-mugetsu/releases/download/v${SKUA_PLAYER_VERSION}/${SKUA_PLAYER_ARCHIVE}"
SKUA_PLAYER_V2_LOCAL="${SKUA_PLAYER_V2_LOCAL:-}"
SWF_SOURCE="$SCRIPT_DIR/Skua.AS3/skua/bin/skua.swf"
APP_BIN="$SCRIPT_DIR/Skua.App.Avalonia/bin/Release/net10.0"
MGR_BIN="$SCRIPT_DIR/Skua.Manager.Avalonia/bin/Release/net10.0"

if [[ ! -s "$SWF_SOURCE" ]]; then
    echo "Error: Required game client was not found: $SWF_SOURCE" >&2
    echo "Run Skua.AS3/compile-as3.sh after installing Apache Flex mxmlc, then retry." >&2
    exit 1
fi

echo "=== Building VibeSkua (Linux Release) ==="
# The player is copied to a version-agnostic output path. Remove prior copies so an
# incremental build cannot leave an older Mugetsu version in the package inputs.
rm -rf "$APP_BIN/player" "$APP_BIN/player-x86-64-v2"
dotnet_args=("$SCRIPT_DIR/Skua.sln" -c Release)
if [[ -n "$SKUA_PLAYER_V2_LOCAL" ]]; then
    if [[ ! -x "$SKUA_PLAYER_V2_LOCAL/Mugetsu" ]]; then
        echo "Error: SKUA_PLAYER_V2_LOCAL must point to a folder containing an executable Mugetsu binary." >&2
        exit 1
    fi
    dotnet_args+=("-p:SkuaPlayerV2Local=$SKUA_PLAYER_V2_LOCAL")
    echo "Using local x86-64-v2 Mugetsu from $SKUA_PLAYER_V2_LOCAL"
fi
dotnet build "${dotnet_args[@]}"

for required in \
    "$APP_BIN/VibeSkua" \
    "$APP_BIN/VibeSkua.dll" \
    "$APP_BIN/VibeSkua.runtimeconfig.json" \
    "$MGR_BIN/VibeSkua.Manager" \
    "$MGR_BIN/VibeSkua.Manager.dll" \
    "$MGR_BIN/VibeSkua.Manager.runtimeconfig.json" \
    "$SCRIPT_DIR/Skua.Plugin.DailyTracker/bin/Release/net10.0/Skua.Plugin.DailyTracker.dll" \
    "$SCRIPT_DIR/Skua.Plugin.ButlerV5/bin/Release/net10.0/ButlerV5.dll" \
    "$SCRIPT_DIR/Skua.Plugin.Cosmetics/bin/Release/net10.0/Skua.Plugin.Cosmetics.dll"; do
    if [[ ! -s "$required" ]]; then
        echo "Error: Required build output is missing or empty: $required" >&2
        exit 1
    fi
done

echo "=== Preparing Build directory ==="
mkdir -p "$BUILD_DIR"
rm -rf "$BUILD_DIR/plugins" "$BUILD_DIR/player" "$BUILD_DIR/FFDec"
rm -rf "$BUILD_DIR/player-x86-64-v2"
mkdir -p "$BUILD_DIR/plugins" "$BUILD_DIR/player" "$BUILD_DIR/player-x86-64-v2" "$BUILD_DIR/FFDec"

# 1. Copy Application output
shopt -s nullglob
app_files=("$APP_BIN"/VibeSkua* "$APP_BIN"/*.dll "$APP_BIN"/*.json)
if ((${#app_files[@]} == 0)); then
    echo "Error: No application outputs were found in $APP_BIN" >&2
    exit 1
fi
cp -f "${app_files[@]}" "$BUILD_DIR/"
if [[ -d "$APP_BIN/runtimes" ]]; then
    cp -a "$APP_BIN/runtimes" "$BUILD_DIR/"
fi

# 2. Copy Manager output
manager_files=("$MGR_BIN"/VibeSkua.Manager*)
cp -f "${manager_files[@]}" "$BUILD_DIR/"

# 3. Copy Plugins
cp -f "$SCRIPT_DIR/Skua.Plugin.DailyTracker/bin/Release/net10.0/Skua.Plugin.DailyTracker.dll" "$BUILD_DIR/plugins/"
cp -f "$SCRIPT_DIR/Skua.Plugin.ButlerV5/bin/Release/net10.0/ButlerV5.dll" "$BUILD_DIR/plugins/"
cp -f "$SCRIPT_DIR/Skua.Plugin.Cosmetics/bin/Release/net10.0/Skua.Plugin.Cosmetics.dll" "$BUILD_DIR/plugins/"
if [[ -n "$WATCHDOG_LOCAL" ]]; then
    if [[ -d "$WATCHDOG_LOCAL" ]]; then
        WATCHDOG_LOCAL="$WATCHDOG_LOCAL/WatchDog.dll"
    fi
    if [[ ! -s "$WATCHDOG_LOCAL" ]]; then
        echo "Error: WATCHDOG_LOCAL must point to a built WatchDog.dll or its containing folder." >&2
        exit 1
    fi
    echo "Using local Watch Dog: $WATCHDOG_LOCAL"
    cp -f "$WATCHDOG_LOCAL" "$BUILD_DIR/plugins/WatchDog.dll"
elif [[ ! -s "$WATCHDOG_CACHE" ]]; then
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
if [[ -z "$WATCHDOG_LOCAL" ]]; then
    cp -f "$WATCHDOG_CACHE" "$BUILD_DIR/plugins/WatchDog.dll"
fi

# 4. Copy SWF client
cp -f "$SWF_SOURCE" "$BUILD_DIR/skua.swf"

# 5. Copy official skua-mugetsu Flash runner (https://github.com/retr0gr4d3/skua-mugetsu)
if [[ -s "$APP_BIN/player/Mugetsu" ]]; then
    echo "Using bundled skua-mugetsu from $APP_BIN/player..."
    cp -a "$APP_BIN/player/." "$BUILD_DIR/player/"
    cp -f "$BUILD_DIR/player/Mugetsu" "$BUILD_DIR/Mugetsu"
    chmod +x "$BUILD_DIR/player/Mugetsu" "$BUILD_DIR/Mugetsu"
elif [[ -s "$SKUA_PLAYER_CACHE/Mugetsu/Mugetsu" ]]; then
    echo "Using cached skua-mugetsu from player/..."
    cp -a "$SKUA_PLAYER_CACHE/Mugetsu/." "$BUILD_DIR/player/"
    cp -f "$BUILD_DIR/player/Mugetsu" "$BUILD_DIR/Mugetsu"
    chmod +x "$BUILD_DIR/player/Mugetsu" "$BUILD_DIR/Mugetsu"
else
    echo "Fetching skua-mugetsu v${SKUA_PLAYER_VERSION} from GitHub Releases..."
    TMP_ARCHIVE="$BUILD_DIR/skua-mugetsu.tar.gz"
    if curl -sSL --fail "$SKUA_PLAYER_URL" -o "$TMP_ARCHIVE" && [ -s "$TMP_ARCHIVE" ]; then
        tar -xzf "$TMP_ARCHIVE" -C "$BUILD_DIR/player/" --strip-components=1
        rm -f "$TMP_ARCHIVE"
        if [[ ! -s "$BUILD_DIR/player/Mugetsu" ]]; then
            echo "Error: Mugetsu archive did not contain the expected executable." >&2
            exit 1
        fi
        cp -f "$BUILD_DIR/player/Mugetsu" "$BUILD_DIR/Mugetsu"
        chmod +x "$BUILD_DIR/player/Mugetsu" "$BUILD_DIR/Mugetsu"
    else
        rm -f "$TMP_ARCHIVE"
        echo "Error: Failed to download official skua-mugetsu from GitHub Releases!" >&2
        exit 1
    fi
fi

# 6. Copy FFDec runtime required by Linux map decompilation
if [[ ! -s "$SCRIPT_DIR/FFDec/ffdec.jar" || ! -x "$SCRIPT_DIR/FFDec/ffdec.sh" || ! -d "$SCRIPT_DIR/FFDec/lib" ]]; then
    echo "Error: Linux FFDec runtime is incomplete under $SCRIPT_DIR/FFDec" >&2
    exit 1
fi
cp -a "$SCRIPT_DIR/FFDec/." "$BUILD_DIR/FFDec/"

# 7. Set executable permissions
chmod +x "$BUILD_DIR/VibeSkua" "$BUILD_DIR/VibeSkua.Manager" "$BUILD_DIR/Mugetsu"
if [[ -s "$APP_BIN/player-x86-64-v2/Mugetsu" ]]; then
    cp -a "$APP_BIN/player-x86-64-v2/." "$BUILD_DIR/player-x86-64-v2/"
    chmod +x "$BUILD_DIR/player-x86-64-v2/Mugetsu"
fi

echo "=== Build complete! Artifacts are in: $BUILD_DIR ==="
