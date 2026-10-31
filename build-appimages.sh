#!/usr/bin/env bash
# ==============================================================================
# build-appimages.sh
# Builds VibeSkua Standalone AppImages + .zip + .tar.gz
# Usage: ./build-appimages.sh [--local-mugetsu /path/to/mugetsu] [--local-mugetsu-v2 /path] [--local-watchdog /path]
# ==============================================================================
set -Eeuo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
source "$SCRIPT_DIR/build-config.sh"

LOCAL_MUGETSU="${SKUA_PLAYER_LOCAL:-}"
LOCAL_MUGETSU_V2="${SKUA_PLAYER_V2_LOCAL:-}"
LOCAL_WATCHDOG="${WATCHDOG_LOCAL:-}"

while [[ $# -gt 0 ]]; do
    case "$1" in
        --local-mugetsu)
            [[ $# -ge 2 ]] || { echo "Error: --local-mugetsu requires a path." >&2; exit 1; }
            LOCAL_MUGETSU="$2"
            shift 2
            ;;
        --local-mugetsu-v2)
            [[ $# -ge 2 ]] || { echo "Error: --local-mugetsu-v2 requires a path." >&2; exit 1; }
            LOCAL_MUGETSU_V2="$2"
            shift 2
            ;;
        --local-watchdog)
            [[ $# -ge 2 ]] || { echo "Error: --local-watchdog requires a path." >&2; exit 1; }
            LOCAL_WATCHDOG="$2"
            shift 2
            ;;
        --help|-h)
            echo "Usage: $0 [--local-mugetsu PATH] [--local-mugetsu-v2 PATH] [--local-watchdog PATH]"
            echo "  --local-mugetsu PATH     Use a local standard Mugetsu binary or its folder."
            echo "  --local-mugetsu-v2 PATH  Use a local x86-64-v2 Mugetsu binary or its folder."
            echo "  --local-watchdog PATH    Use a local WatchDog.dll or its folder."
            exit 0
            ;;
        *)
            echo "Unknown argument: $1" >&2
            echo "Use $0 --help for options." >&2
            exit 1
            ;;
    esac
done

if [[ -n "$LOCAL_MUGETSU" ]]; then export SKUA_PLAYER_LOCAL="$LOCAL_MUGETSU"; fi
if [[ -n "$LOCAL_MUGETSU_V2" ]]; then export SKUA_PLAYER_V2_LOCAL="$LOCAL_MUGETSU_V2"; fi
if [[ -n "$LOCAL_WATCHDOG" ]]; then export WATCHDOG_LOCAL="$LOCAL_WATCHDOG"; fi

echo "=== Building Standalone AppImages ==="

# Build solution and assemble current/ payload
assemble_payload

appimagetool_cmd="appimagetool"
if ! command -v appimagetool &>/dev/null; then
    if [[ -x "$BUILD_DIR/tools/appimagetool" ]]; then
        appimagetool_cmd="$BUILD_DIR/tools/appimagetool"
    else
        echo "appimagetool not found in PATH. Downloading standalone appimagetool..."
        mkdir -p "$BUILD_DIR/tools"
        curl -fsSL "https://github.com/AppImage/appimagetool/releases/download/continuous/appimagetool-x86_64.AppImage" -o "$BUILD_DIR/tools/appimagetool"
        chmod +x "$BUILD_DIR/tools/appimagetool"
        appimagetool_cmd="$BUILD_DIR/tools/appimagetool"
    fi
fi

skua_appdir="$BUILD_DIR/VibeSkua.AppDir"
mgr_appdir="$BUILD_DIR/VibeSkua-Manager.AppDir"
rm -rf "$skua_appdir" "$mgr_appdir"
mkdir -p "$skua_appdir/usr/bin" "$skua_appdir/usr/share/applications" "$skua_appdir/current"
mkdir -p "$mgr_appdir/usr/bin" "$mgr_appdir/usr/share/applications" "$mgr_appdir/current"

cp -a "$CURRENT_DIR/." "$skua_appdir/current/"
cp -a "$CURRENT_DIR/." "$mgr_appdir/current/"

# Icon generation
skua_icon="$SKUA_ICON"
mgr_icon="$SKUA_ICON"

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

# Files copied from dotnet output can retain owner-only execute bits (0744).
chmod -R a+rX "$skua_appdir" "$mgr_appdir"

export APPIMAGE_EXTRACT_AND_RUN=1
ARCH=x86_64 "$appimagetool_cmd" -n "$skua_appdir" "$BUILD_DIR/VibeSkua-x86_64.AppImage"
ARCH=x86_64 "$appimagetool_cmd" -n "$mgr_appdir" "$BUILD_DIR/VibeSkua-Manager-x86_64.AppImage"
chmod +x "$BUILD_DIR/VibeSkua-x86_64.AppImage" "$BUILD_DIR/VibeSkua-Manager-x86_64.AppImage"

rm -rf "$skua_appdir" "$mgr_appdir"

rm -rf "$APPIMAGE_BUNDLE_DIR"
mkdir -p "$APPIMAGE_BUNDLE_DIR"
cp -f "$BUILD_DIR/VibeSkua-x86_64.AppImage" "$APPIMAGE_BUNDLE_DIR/"
cp -f "$BUILD_DIR/VibeSkua-Manager-x86_64.AppImage" "$APPIMAGE_BUNDLE_DIR/"

echo "Compressing AppImage release archives..."
(
    cd "$BUILD_DIR"
    rm -f "VibeSkua-linux-AppImage.zip" "VibeSkua-linux-AppImage.tar.gz"
    if command -v zip &>/dev/null; then
        zip -q -r "VibeSkua-linux-AppImage.zip" "VibeSkua-linux-AppImage"
    elif command -v bsdtar &>/dev/null; then
        bsdtar -a -cf "VibeSkua-linux-AppImage.zip" "VibeSkua-linux-AppImage"
    fi
    tar -czf "VibeSkua-linux-AppImage.tar.gz" "VibeSkua-linux-AppImage"
)

echo "=== AppImage Packaging Complete! ==="
