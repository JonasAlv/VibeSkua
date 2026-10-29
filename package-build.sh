#!/usr/bin/env bash
# ==============================================================================
# package-build.sh
# Linux packaging script for VibeSkua (mirrors upstream package-build.ps1)
#
# Default: Packages standalone x86_64 AppImages (Client & Manager)
# Options:
#   (none)          : Build standalone AppImages (default)
#   --binary,   -b  : Build direct binary portable distribution
#   --appimage, -i  : Build standalone AppImages (explicit)
#   --all,      -a  : Build both AppImage and direct binary distributions
# ==============================================================================
set -Eeuo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
BUILD_DIR="$SCRIPT_DIR/Build"
BINARY_DIR="$BUILD_DIR/VibeSkua-linux-Binary"
CURRENT_DIR="$BUILD_DIR/payload/current"
OUTPUT_DIR="$BUILD_DIR"
SKUA_PLAYER_VERSION="$(sed -n 's:.*<SkuaPlayerVersion>\([^<]*\)</SkuaPlayerVersion>.*:\1:p' "$SCRIPT_DIR/SkuaPlayerVersion.props")"
SKUA_PLAYER_OS="$(sed -n 's:.*<SkuaPlayerOs>\([^<]*\)</SkuaPlayerOs>.*:\1:p' "$SCRIPT_DIR/SkuaPlayerVersion.props")"
SKUA_PLAYER_CACHE="$SCRIPT_DIR/player/${SKUA_PLAYER_VERSION}-${SKUA_PLAYER_OS}"
SKUA_PLAYER_ARCHIVE="Mugetsu-${SKUA_PLAYER_VERSION}-${SKUA_PLAYER_OS}.tar.gz"
SKUA_PLAYER_URL="https://github.com/retr0gr4d3/skua-mugetsu/releases/download/v${SKUA_PLAYER_VERSION}/${SKUA_PLAYER_ARCHIVE}"

MODE="appimage"

while [[ $# -gt 0 ]]; do
    case "$1" in
        --appimage|-i)
            MODE="appimage"
            shift
            ;;
        --binary|-b|--bin|--portable|-p)
            MODE="binary"
            shift
            ;;
        --all|-a)
            MODE="all"
            shift
            ;;
        --help|-h)
            echo "Usage: $0 [--binary | --appimage | --all]"
            echo "  (default)       Build standalone AppImages"
            echo "  --binary,   -b  Build direct binary portable distribution"
            echo "  --appimage, -i  Build standalone AppImages"
            echo "  --all,      -a  Build both AppImage and direct binary distributions"
            exit 0
            ;;
        *)
            echo "Unknown argument: $1"
            echo "Use $0 --help for options."
            exit 1
            ;;
    esac
done

echo "=== VibeSkua Packaging (Mode: $MODE) ==="

# 1. Ensure solution is built
echo "--- Ensuring Release binaries are built ---"
"$SCRIPT_DIR/BuildProject.sh"

# 2. Assemble the portable payload in current/ (used by both Portable and AppImage)
echo "--- Assembling application payload ---"
rm -rf "$CURRENT_DIR"
mkdir -p "$CURRENT_DIR"
mkdir -p "$CURRENT_DIR/Assemblies/Skills"
mkdir -p "$CURRENT_DIR/Assemblies/runtimes/linux-x64/native"
mkdir -p "$CURRENT_DIR/plugins"
mkdir -p "$CURRENT_DIR/FFDec"

APP_BIN="$SCRIPT_DIR/Skua.App.Avalonia/bin/Release/net10.0"
MGR_BIN="$SCRIPT_DIR/Skua.Manager.Avalonia/bin/Release/net10.0"

# Copy root executables & core files
cp -f "$APP_BIN"/VibeSkua "$CURRENT_DIR/"
cp -f "$APP_BIN"/VibeSkua.dll "$CURRENT_DIR/"
cp -f "$APP_BIN"/VibeSkua.runtimeconfig.json "$CURRENT_DIR/"
cp -f "$APP_BIN"/VibeSkua.deps.json "$CURRENT_DIR/"
cp -f "$APP_BIN"/*.pdb "$CURRENT_DIR/" 2>/dev/null || true

cp -f "$MGR_BIN"/VibeSkua.Manager "$CURRENT_DIR/"
cp -f "$MGR_BIN"/VibeSkua.Manager.dll "$CURRENT_DIR/"
cp -f "$MGR_BIN"/VibeSkua.Manager.runtimeconfig.json "$CURRENT_DIR/"
cp -f "$MGR_BIN"/VibeSkua.Manager.deps.json "$CURRENT_DIR/"
cp -f "$MGR_BIN"/*.pdb "$CURRENT_DIR/" 2>/dev/null || true

# Copy JSON data assets
[ -f "$APP_BIN/QuestData.json" ] && cp -f "$APP_BIN/QuestData.json" "$CURRENT_DIR/"
[ -f "$APP_BIN/AdvancedSkills.json" ] && cp -f "$APP_BIN/AdvancedSkills.json" "$CURRENT_DIR/"
[ -f "$SCRIPT_DIR/Skua.Core/ScriptDates.json" ] && cp -f "$SCRIPT_DIR/Skua.Core/ScriptDates.json" "$CURRENT_DIR/"

# Copy all dependent DLLs to Assemblies/ and current/
cp -f "$APP_BIN"/*.dll "$CURRENT_DIR/Assemblies/"
cp -f "$MGR_BIN"/*.dll "$CURRENT_DIR/Assemblies/"
rm -f "$CURRENT_DIR/Assemblies/VibeSkua.dll" "$CURRENT_DIR/Assemblies/VibeSkua.Manager.dll"
cp -f "$CURRENT_DIR/Assemblies/"*.dll "$CURRENT_DIR/"

# Copy runtimes native libraries
if [ -d "$APP_BIN/runtimes" ]; then
    cp -rf "$APP_BIN/runtimes" "$CURRENT_DIR/"
    cp -rf "$APP_BIN/runtimes" "$CURRENT_DIR/Assemblies/"
fi

# Copy plugins
cp -f "$SCRIPT_DIR/Skua.Plugin.DailyTracker/bin/Release/net10.0"/Skua.Plugin.*.dll "$CURRENT_DIR/plugins/"
cp -f "$SCRIPT_DIR/Skua.Plugin.ButlerV5/bin/Release/net10.0"/ButlerV5.dll "$CURRENT_DIR/plugins/"
cp -f "$SCRIPT_DIR/Skua.Plugin.Cosmetics/bin/Release/net10.0"/Skua.Plugin.Cosmetics.dll "$CURRENT_DIR/plugins/"
cp -f "$BUILD_DIR/plugins/WatchDog.dll" "$CURRENT_DIR/plugins/WatchDog.dll"

# Copy skua.swf (Essential AS3 game client & bot API bridge)
if [[ -s "$SCRIPT_DIR/Skua.AS3/skua/bin/skua.swf" ]]; then
    cp -f "$SCRIPT_DIR/Skua.AS3/skua/bin/skua.swf" "$CURRENT_DIR/skua.swf"
elif [[ -s "$BUILD_DIR/skua.swf" ]]; then
    cp -f "$BUILD_DIR/skua.swf" "$CURRENT_DIR/skua.swf"
else
    echo "Error: Required skua.swf is missing. Run Skua.AS3/compile-as3.sh first." >&2
    exit 1
fi

# FFDec is used by Linux map decompilation; ship its launcher and Java libraries.
if [[ ! -s "$SCRIPT_DIR/FFDec/ffdec.jar" || ! -x "$SCRIPT_DIR/FFDec/ffdec.sh" || ! -d "$SCRIPT_DIR/FFDec/lib" ]]; then
    echo "Error: Linux FFDec runtime is incomplete under $SCRIPT_DIR/FFDec" >&2
    exit 1
fi
cp -a "$SCRIPT_DIR/FFDec/." "$CURRENT_DIR/FFDec/"

# Copy or download official Skua Mugetsu Flash runner (https://github.com/retr0gr4d3/skua-mugetsu)
mkdir -p "$CURRENT_DIR/player"
if [ -s "$APP_BIN/player/Mugetsu" ]; then
    cp -a "$APP_BIN/player/." "$CURRENT_DIR/player/"
    cp -f "$CURRENT_DIR/player/Mugetsu" "$CURRENT_DIR/Mugetsu"
    chmod +x "$CURRENT_DIR/player/Mugetsu" "$CURRENT_DIR/Mugetsu"
elif [ -s "$SKUA_PLAYER_CACHE/Mugetsu/Mugetsu" ]; then
    cp -a "$SKUA_PLAYER_CACHE/Mugetsu/." "$CURRENT_DIR/player/"
    cp -f "$CURRENT_DIR/player/Mugetsu" "$CURRENT_DIR/Mugetsu"
    chmod +x "$CURRENT_DIR/player/Mugetsu" "$CURRENT_DIR/Mugetsu"
elif [ -s "$BUILD_DIR/player/Mugetsu" ]; then
    cp -a "$BUILD_DIR/player/." "$CURRENT_DIR/player/"
    cp -f "$CURRENT_DIR/player/Mugetsu" "$CURRENT_DIR/Mugetsu"
    chmod +x "$CURRENT_DIR/player/Mugetsu" "$CURRENT_DIR/Mugetsu"
else
    echo "Mugetsu binary not found locally. Fetching official skua-mugetsu v${SKUA_PLAYER_VERSION} from GitHub Releases..."
    TMP_ARCHIVE="$CURRENT_DIR/skua-mugetsu.tar.gz"
    if curl -sSL --fail "$SKUA_PLAYER_URL" -o "$TMP_ARCHIVE" && [ -s "$TMP_ARCHIVE" ]; then
        tar -xzf "$TMP_ARCHIVE" -C "$CURRENT_DIR/player/" --strip-components=1
        rm -f "$TMP_ARCHIVE"
        cp -f "$CURRENT_DIR/player/Mugetsu" "$CURRENT_DIR/Mugetsu"
        chmod +x "$CURRENT_DIR/player/Mugetsu" "$CURRENT_DIR/Mugetsu"
        cp -f "$CURRENT_DIR/player/Mugetsu" "$BUILD_DIR/Mugetsu" 2>/dev/null || true
    else
        rm -f "$TMP_ARCHIVE"
        echo "Error: Failed to download official skua-mugetsu from GitHub Releases!" >&2
        exit 1
    fi
fi

chmod +x "$CURRENT_DIR/VibeSkua" "$CURRENT_DIR/VibeSkua.Manager"

# ==============================================================================
# 3. Build Direct Binary Distribution (if requested)
# ==============================================================================
build_binary() {
    echo "--- Building Direct Binary Distribution ---"
    rm -rf "$BINARY_DIR"
    mkdir -p "$BINARY_DIR"
    cp -a "$CURRENT_DIR/." "$BINARY_DIR/current/"
    touch "$BINARY_DIR/.portable"

    # Compile native root C launchers
    cat << 'EOF' > "$BUILD_DIR/launcher.c"
#define _GNU_SOURCE
#include <unistd.h>
#include <libgen.h>
#include <limits.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

int main(int argc, char *argv[]) {
    char exe_path[PATH_MAX];
    ssize_t len = readlink("/proc/self/exe", exe_path, sizeof(exe_path) - 1);
    if (len == -1) {
        perror("readlink");
        return 1;
    }
    exe_path[len] = '\0';

    char *dir = dirname(exe_path);
    char current_dir[PATH_MAX];
    snprintf(current_dir, sizeof(current_dir), "%s/current", dir);

    char target[PATH_MAX];
    snprintf(target, sizeof(target), "%s/%s", current_dir, TARGET_BIN);

    if (chdir(current_dir) != 0) {
        perror("chdir");
        return 1;
    }

    char *orig_ld = getenv("LD_LIBRARY_PATH");
    char new_ld[PATH_MAX * 4];
    if (orig_ld && strlen(orig_ld) > 0) {
        snprintf(new_ld, sizeof(new_ld), "%s:%s/runtimes/linux-x64/native:%s", current_dir, current_dir, orig_ld);
    } else {
        snprintf(new_ld, sizeof(new_ld), "%s:%s/runtimes/linux-x64/native", current_dir, current_dir);
    }
    setenv("LD_LIBRARY_PATH", new_ld, 1);

    char *orig_path = getenv("PATH");
    char new_path[PATH_MAX * 2];
    if (orig_path && strlen(orig_path) > 0) {
        snprintf(new_path, sizeof(new_path), "%s:%s", current_dir, orig_path);
    } else {
        snprintf(new_path, sizeof(new_path), "%s", current_dir);
    }
    setenv("PATH", new_path, 1);

    argv[0] = target;
    execv(target, argv);

    perror("execv failed");
    return 1;
}
EOF

    gcc -O2 -DTARGET_BIN=\"VibeSkua\" "$BUILD_DIR/launcher.c" -o "$BINARY_DIR/VibeSkua"
    gcc -O2 -DTARGET_BIN=\"VibeSkua.Manager\" "$BUILD_DIR/launcher.c" -o "$BINARY_DIR/VibeSkua.Manager"
    rm -f "$BUILD_DIR/launcher.c"

    # Shell script fallbacks
    cat << 'EOF' > "$BINARY_DIR/VibeSkua.sh"
#!/usr/bin/env bash
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
CURRENT_DIR="$SCRIPT_DIR/current"
cd "$CURRENT_DIR"
export LD_LIBRARY_PATH="$CURRENT_DIR:$CURRENT_DIR/runtimes/linux-x64/native:${LD_LIBRARY_PATH:-}"
export PATH="$CURRENT_DIR:$PATH"
exec "$CURRENT_DIR/VibeSkua" "$@"
EOF
    chmod +x "$BINARY_DIR/VibeSkua.sh"

    cat << 'EOF' > "$BINARY_DIR/VibeSkua.Manager.sh"
#!/usr/bin/env bash
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
CURRENT_DIR="$SCRIPT_DIR/current"
cd "$CURRENT_DIR"
export LD_LIBRARY_PATH="$CURRENT_DIR:$CURRENT_DIR/runtimes/linux-x64/native:${LD_LIBRARY_PATH:-}"
export PATH="$CURRENT_DIR:$PATH"
exec "$CURRENT_DIR/VibeSkua.Manager" "$@"
EOF
    chmod +x "$BINARY_DIR/VibeSkua.Manager.sh"

    # Create root desktop files and icons
    cp -f "$SCRIPT_DIR/Skua.Shared.Avalonia/Assets/SkuaImage.png" "$BINARY_DIR/vibeskua.png"
    cp -f "$SCRIPT_DIR/Skua.Shared.Avalonia/Assets/SkuaImage.png" "$BINARY_DIR/vibeskua-manager.png"

    # Provide VibeSkua-linux-Portable symlink for backwards compatibility
    ln -sfn "VibeSkua-linux-Binary" "$BUILD_DIR/VibeSkua-linux-Portable"

    echo "Compressing binary release archives..."
    (
        cd "$BUILD_DIR"
        rm -f "VibeSkua-linux-Binary.zip" "VibeSkua-linux-Binary.tar.gz" "VibeSkua-linux-Portable.zip" "VibeSkua-linux-Portable.tar.gz"
        zip -q -r "VibeSkua-linux-Binary.zip" "VibeSkua-linux-Binary"
        tar -czf "VibeSkua-linux-Binary.tar.gz" "VibeSkua-linux-Binary"
        ln -sf "VibeSkua-linux-Binary.zip" "VibeSkua-linux-Portable.zip"
        ln -sf "VibeSkua-linux-Binary.tar.gz" "VibeSkua-linux-Portable.tar.gz"
    )
}

# ==============================================================================
# 4. Build Standalone AppImages (Default)
# ==============================================================================
build_appimage() {
    echo "--- Building Standalone AppImages ---"

    local appimagetool_cmd="appimagetool"
    if ! command -v appimagetool &>/dev/null; then
        if [ -x "$BUILD_DIR/tools/appimagetool" ]; then
            appimagetool_cmd="$BUILD_DIR/tools/appimagetool"
        else
            echo "appimagetool not found in PATH. Downloading standalone appimagetool..."
            mkdir -p "$BUILD_DIR/tools"
            curl -fsSL "https://github.com/AppImage/appimagetool/releases/download/continuous/appimagetool-x86_64.AppImage" -o "$BUILD_DIR/tools/appimagetool"
            test -s "$BUILD_DIR/tools/appimagetool"
            chmod +x "$BUILD_DIR/tools/appimagetool"
            appimagetool_cmd="$BUILD_DIR/tools/appimagetool"
        fi
    fi

    local skua_appdir="$BUILD_DIR/VibeSkua.AppDir"
    local mgr_appdir="$BUILD_DIR/VibeSkua-Manager.AppDir"
    rm -rf "$skua_appdir" "$mgr_appdir"
    mkdir -p "$skua_appdir/usr/bin" "$skua_appdir/usr/share/applications" "$skua_appdir/current"
    mkdir -p "$mgr_appdir/usr/bin" "$mgr_appdir/usr/share/applications" "$mgr_appdir/current"

    # Copy payloads
    cp -a "$CURRENT_DIR/." "$skua_appdir/current/"
    cp -a "$CURRENT_DIR/." "$mgr_appdir/current/"

    # Icon generator
    local skua_icon="$SCRIPT_DIR/Skua.Shared.Avalonia/Assets/SkuaImage.png"
    local mgr_icon="$SCRIPT_DIR/Skua.Shared.Avalonia/Assets/SkuaImage.png"

    if python3 -c "from PIL import Image" 2>/dev/null; then
        python3 - <<EOF
from PIL import Image
import os

def gen(src, appdir, name):
    img = Image.open(src)
    root_icon = os.path.join(appdir, f"{name}.png")
    img_256 = img.resize((256, 256), Image.Resampling.LANCZOS)
    img_256.save(root_icon)
    img_256.save(os.path.join(appdir, ".DirIcon"), format="PNG")
    for s in [16, 32, 48, 64, 128, 256, 512]:
        p = os.path.join(appdir, "usr", "share", "icons", "hicolor", f"{s}x{s}", "apps")
        os.makedirs(p, exist_ok=True)
        img.resize((s, s), Image.Resampling.LANCZOS).save(os.path.join(p, f"{name}.png"))

gen("$skua_icon", "$skua_appdir", "vibeskua")
gen("$mgr_icon", "$mgr_appdir", "vibeskua-manager")
EOF
    else
        echo "Note: python3 PIL not found, copying raw icon assets..."
        for target in "$skua_appdir:vibeskua" "$mgr_appdir:vibeskua-manager"; do
            appdir="${target%%:*}"
            name="${target##*:}"
            cp -f "$skua_icon" "$appdir/${name}.png"
            cp -f "$skua_icon" "$appdir/.DirIcon"
            mkdir -p "$appdir/usr/share/icons/hicolor/256x256/apps"
            cp -f "$skua_icon" "$appdir/usr/share/icons/hicolor/256x256/apps/${name}.png"
        done
    fi

    # Desktop entries
    cat << 'EOF' > "$skua_appdir/vibeskua.desktop"
[Desktop Entry]
Type=Application
Name=VibeSkua
GenericName=AQW Bot & Game Client
Comment=Cross-platform AQW client with advanced scripting and multi-account tabs
Exec=AppRun %U
Icon=vibeskua
Terminal=false
Categories=Game;Utility;
StartupWMClass=VibeSkua
EOF
    cp "$skua_appdir/vibeskua.desktop" "$skua_appdir/usr/share/applications/vibeskua.desktop"

    cat << 'EOF' > "$mgr_appdir/vibeskua-manager.desktop"
[Desktop Entry]
Type=Application
Name=VibeSkua Manager
GenericName=VibeSkua Account & Script Manager
Comment=Account manager, script updater, and launcher for VibeSkua
Exec=AppRun %U
Icon=vibeskua-manager
Terminal=false
Categories=Game;Utility;Settings;
StartupWMClass=VibeSkua-Manager
EOF
    cp "$mgr_appdir/vibeskua-manager.desktop" "$mgr_appdir/usr/share/applications/vibeskua-manager.desktop"

    # AppRun launchers
    cat << 'EOF' > "$skua_appdir/AppRun"
#!/usr/bin/env bash
set -e
APPDIR="$(dirname "$(readlink -f "${0}")")"
export PATH="$APPDIR/usr/bin:$APPDIR/current:$PATH"
export LD_LIBRARY_PATH="$APPDIR/usr/lib:$APPDIR/current:$APPDIR/current/runtimes/linux-x64/native:${LD_LIBRARY_PATH:-}"
exec "$APPDIR/current/VibeSkua" "$@"
EOF
    chmod +x "$skua_appdir/AppRun"
    ln -sf "../current/VibeSkua" "$skua_appdir/usr/bin/VibeSkua"

    cat << 'EOF' > "$mgr_appdir/AppRun"
#!/usr/bin/env bash
set -e
APPDIR="$(dirname "$(readlink -f "${0}")")"
export PATH="$APPDIR/usr/bin:$APPDIR/current:$PATH"
export LD_LIBRARY_PATH="$APPDIR/usr/lib:$APPDIR/current:$APPDIR/current/runtimes/linux-x64/native:${LD_LIBRARY_PATH:-}"
exec "$APPDIR/current/VibeSkua.Manager" "$@"
EOF
    chmod +x "$mgr_appdir/AppRun"
    ln -sf "../current/VibeSkua.Manager" "$mgr_appdir/usr/bin/VibeSkua-Manager"

    # Build AppImages
    echo "Running appimagetool for VibeSkua..."
    ARCH=x86_64 "$appimagetool_cmd" -n "$skua_appdir" "$OUTPUT_DIR/VibeSkua-x86_64.AppImage"
    chmod +x "$OUTPUT_DIR/VibeSkua-x86_64.AppImage"

    echo "Running appimagetool for VibeSkua-Manager..."
    ARCH=x86_64 "$appimagetool_cmd" -n "$mgr_appdir" "$OUTPUT_DIR/VibeSkua-Manager-x86_64.AppImage"
    chmod +x "$OUTPUT_DIR/VibeSkua-Manager-x86_64.AppImage"

    rm -rf "$skua_appdir" "$mgr_appdir"

    # Bundle AppImages into zip/tarball
    local bundle_dir="$BUILD_DIR/VibeSkua-linux-AppImage"
    rm -rf "$bundle_dir"
    mkdir -p "$bundle_dir"
    cp -f "$OUTPUT_DIR/VibeSkua-x86_64.AppImage" "$bundle_dir/"
    cp -f "$OUTPUT_DIR/VibeSkua-Manager-x86_64.AppImage" "$bundle_dir/"

    echo "Compressing AppImage release archives..."
    (
        cd "$BUILD_DIR"
        rm -f "VibeSkua-linux-AppImage.zip" "VibeSkua-linux-AppImage.tar.gz"
        zip -q -r "VibeSkua-linux-AppImage.zip" "VibeSkua-linux-AppImage"
        tar -czf "VibeSkua-linux-AppImage.tar.gz" "VibeSkua-linux-AppImage"
    )
}

# Execute based on MODE
case "$MODE" in
    appimage)
        build_appimage
        ;;
    binary)
        build_binary
        ;;
    all)
        build_binary
        build_appimage
        ;;
esac

echo "=== Packaging Complete! ==="
if [ "$MODE" = "appimage" ] || [ "$MODE" = "all" ]; then
    echo "AppImage Client:  $OUTPUT_DIR/VibeSkua-x86_64.AppImage ($(du -h "$OUTPUT_DIR/VibeSkua-x86_64.AppImage" | cut -f1))"
    echo "AppImage Manager: $OUTPUT_DIR/VibeSkua-Manager-x86_64.AppImage ($(du -h "$OUTPUT_DIR/VibeSkua-Manager-x86_64.AppImage" | cut -f1))"
    echo "AppImage ZIP:     $BUILD_DIR/VibeSkua-linux-AppImage.zip ($(du -h "$BUILD_DIR/VibeSkua-linux-AppImage.zip" | cut -f1))"
    echo "AppImage Tarball: $BUILD_DIR/VibeSkua-linux-AppImage.tar.gz ($(du -h "$BUILD_DIR/VibeSkua-linux-AppImage.tar.gz" | cut -f1))"
fi
if [ "$MODE" = "binary" ] || [ "$MODE" = "all" ]; then
    echo "Binary Folder:    $BINARY_DIR"
    echo "Binary ZIP:       $BUILD_DIR/VibeSkua-linux-Binary.zip ($(du -h "$BUILD_DIR/VibeSkua-linux-Binary.zip" | cut -f1))"
    echo "Binary Tarball:   $BUILD_DIR/VibeSkua-linux-Binary.tar.gz ($(du -h "$BUILD_DIR/VibeSkua-linux-Binary.tar.gz" | cut -f1))"
fi
