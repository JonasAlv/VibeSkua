#!/usr/bin/env bash
set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
BUILD_DIR="$SCRIPT_DIR/Build"

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

# 4. Copy SWF client
if [ -f "$SCRIPT_DIR/Skua.AS3/skua/bin/skua.swf" ]; then
    cp -f "$SCRIPT_DIR/Skua.AS3/skua/bin/skua.swf" "$BUILD_DIR/skua.swf"
fi

# 5. Copy Mugetsu binary if present in mugetsu repo
MUGETSU_SRC="$SCRIPT_DIR/../mugetsu/source/build/out/Mugetsu"
if [ -f "$MUGETSU_SRC" ]; then
    cp -f "$MUGETSU_SRC" "$BUILD_DIR/Mugetsu"
    chmod +x "$BUILD_DIR/Mugetsu"
fi

# 6. Set executable permissions
chmod +x "$BUILD_DIR/VibeSkua" 2>/dev/null || true
chmod +x "$BUILD_DIR/VibeSkua.Manager" 2>/dev/null || true
chmod +x "$BUILD_DIR/Mugetsu" 2>/dev/null || true

echo "=== Build complete! Artifacts are in: $BUILD_DIR ==="
