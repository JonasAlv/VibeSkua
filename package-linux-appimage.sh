#!/usr/bin/env bash
# ==============================================================================
# package-linux-appimage.sh
# Packages VibeSkua and VibeSkua Manager into standalone x86_64 AppImages
# ==============================================================================
set -Eeuo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
BUILD_DIR="$SCRIPT_DIR/Build"
PORTABLE_DIR="$BUILD_DIR/VibeSkua-linux-Portable"
CURRENT_DIR="$PORTABLE_DIR/current"
OUTPUT_DIR="$BUILD_DIR"

echo "=== Packaging VibeSkua Linux AppImages (Approach A) ==="

# 1. Ensure the portable build is present and up-to-date
if [ ! -d "$CURRENT_DIR" ] || [ ! -f "$CURRENT_DIR/VibeSkua.dll" ] || [ ! -f "$CURRENT_DIR/VibeSkua.Manager.dll" ]; then
    echo "Portable build not found. Running package-linux-portable.sh..."
    "$SCRIPT_DIR/package-linux-portable.sh"
fi

# Ensure appimagetool is available
if ! command -v appimagetool &>/dev/null; then
    echo "Error: appimagetool not found in PATH." >&2
    echo "Please install appimagetool or download it from https://github.com/AppImage/appimagetool/releases" >&2
    exit 1
fi

SKUA_APPDIR="$BUILD_DIR/VibeSkua.AppDir"
MGR_APPDIR="$BUILD_DIR/VibeSkua-Manager.AppDir"

rm -rf "$SKUA_APPDIR" "$MGR_APPDIR"
mkdir -p "$SKUA_APPDIR" "$MGR_APPDIR"

# Source icon assets
SKUA_ICON_SRC="$SCRIPT_DIR/Skua.Shared.Avalonia/Assets/SkuaImage.png"
MGR_ICON_SRC="$SCRIPT_DIR/Skua.Shared.Avalonia/Assets/SkuaShellImage.png"

# ==============================================================================
# Helper to generate hicolor icon hierarchy
# ==============================================================================
generate_icons() {
    local src_img="$1"
    local appdir="$2"
    local app_name="$3"

    python3 - <<EOF
from PIL import Image
import os

src = "$src_img"
appdir = "$appdir"
name = "$app_name"

img = Image.open(src)

# Save root 256x256 icon and .DirIcon
root_icon = os.path.join(appdir, f"{name}.png")
img_256 = img.resize((256, 256), Image.Resampling.LANCZOS)
img_256.save(root_icon)

# Generate standard hicolor sizes
sizes = [16, 32, 48, 64, 128, 256, 512]
for s in sizes:
    dir_path = os.path.join(appdir, "usr", "share", "icons", "hicolor", f"{s}x{s}", "apps")
    os.makedirs(dir_path, exist_ok=True)
    out_path = os.path.join(dir_path, f"{name}.png")
    img.resize((s, s), Image.Resampling.LANCZOS).save(out_path)
EOF

    ln -sf "${app_name}.png" "$appdir/.DirIcon"
}

# ==============================================================================
# 2. Build VibeSkua.AppDir
# ==============================================================================
echo "Creating VibeSkua.AppDir..."
mkdir -p "$SKUA_APPDIR/usr/bin" "$SKUA_APPDIR/usr/share/applications" "$SKUA_APPDIR/current"

# Copy full application payload into current/
cp -a "$CURRENT_DIR/." "$SKUA_APPDIR/current/"

# Generate icons
generate_icons "$SKUA_ICON_SRC" "$SKUA_APPDIR" "vibeskua"

# Desktop entry
cat << 'EOF' > "$SKUA_APPDIR/vibeskua.desktop"
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
cp "$SKUA_APPDIR/vibeskua.desktop" "$SKUA_APPDIR/usr/share/applications/vibeskua.desktop"

# AppRun launcher
cat << 'EOF' > "$SKUA_APPDIR/AppRun"
#!/usr/bin/env bash
set -e

APPDIR="$(dirname "$(readlink -f "${0}")")"
export PATH="$APPDIR/usr/bin:$APPDIR/current:$PATH"
export LD_LIBRARY_PATH="$APPDIR/usr/lib:$APPDIR/current:$APPDIR/current/runtimes/linux-x64/native:${LD_LIBRARY_PATH:-}"

exec "$APPDIR/current/VibeSkua" "$@"
EOF
chmod +x "$SKUA_APPDIR/AppRun"
ln -sf "../current/VibeSkua" "$SKUA_APPDIR/usr/bin/VibeSkua"

# ==============================================================================
# 3. Build VibeSkua-Manager.AppDir
# ==============================================================================
echo "Creating VibeSkua-Manager.AppDir..."
mkdir -p "$MGR_APPDIR/usr/bin" "$MGR_APPDIR/usr/share/applications" "$MGR_APPDIR/current"

# Copy full application payload into current/
cp -a "$CURRENT_DIR/." "$MGR_APPDIR/current/"

# Generate icons
generate_icons "$MGR_ICON_SRC" "$MGR_APPDIR" "vibeskua-manager"

# Desktop entry
cat << 'EOF' > "$MGR_APPDIR/vibeskua-manager.desktop"
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
cp "$MGR_APPDIR/vibeskua-manager.desktop" "$MGR_APPDIR/usr/share/applications/vibeskua-manager.desktop"

# AppRun launcher
cat << 'EOF' > "$MGR_APPDIR/AppRun"
#!/usr/bin/env bash
set -e

APPDIR="$(dirname "$(readlink -f "${0}")")"
export PATH="$APPDIR/usr/bin:$APPDIR/current:$PATH"
export LD_LIBRARY_PATH="$APPDIR/usr/lib:$APPDIR/current:$APPDIR/current/runtimes/linux-x64/native:${LD_LIBRARY_PATH:-}"

exec "$APPDIR/current/VibeSkua.Manager" "$@"
EOF
chmod +x "$MGR_APPDIR/AppRun"
ln -sf "../current/VibeSkua.Manager" "$MGR_APPDIR/usr/bin/VibeSkua-Manager"

# ==============================================================================
# 4. Generate AppImages with appimagetool
# ==============================================================================
echo "Generating VibeSkua-x86_64.AppImage..."
ARCH=x86_64 appimagetool -n "$SKUA_APPDIR" "$OUTPUT_DIR/VibeSkua-x86_64.AppImage"
chmod +x "$OUTPUT_DIR/VibeSkua-x86_64.AppImage"

echo "Generating VibeSkua-Manager-x86_64.AppImage..."
ARCH=x86_64 appimagetool -n "$MGR_APPDIR" "$OUTPUT_DIR/VibeSkua-Manager-x86_64.AppImage"
chmod +x "$OUTPUT_DIR/VibeSkua-Manager-x86_64.AppImage"

# Clean up temporary AppDir directories
rm -rf "$SKUA_APPDIR" "$MGR_APPDIR"

echo "=== Linux AppImages successfully built! ==="
echo "Client:  $OUTPUT_DIR/VibeSkua-x86_64.AppImage ($(du -h "$OUTPUT_DIR/VibeSkua-x86_64.AppImage" | cut -f1))"
echo "Manager: $OUTPUT_DIR/VibeSkua-Manager-x86_64.AppImage ($(du -h "$OUTPUT_DIR/VibeSkua-Manager-x86_64.AppImage" | cut -f1))"
