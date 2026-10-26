#!/usr/bin/env bash
set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
BUILD_DIR="$SCRIPT_DIR/Build"
PACKAGE_NAME="VibeSkua-linux-Portable"
DIST_DIR="$BUILD_DIR/$PACKAGE_NAME"
CURRENT_DIR="$DIST_DIR/current"
ASSEMBLIES_DIR="$CURRENT_DIR/Assemblies"
PLUGINS_DIR="$CURRENT_DIR/plugins"
FFDEC_DIR="$CURRENT_DIR/FFDec"

echo "=== Packaging VibeSkua Linux Portable ==="

# 1. Build the solution in Release mode
dotnet build "$SCRIPT_DIR/Skua.sln" -c Release

# 2. Prepare Directory Structure
rm -rf "$DIST_DIR"
mkdir -p "$DIST_DIR"
mkdir -p "$CURRENT_DIR"
mkdir -p "$ASSEMBLIES_DIR"
mkdir -p "$ASSEMBLIES_DIR/Skills"
mkdir -p "$ASSEMBLIES_DIR/runtimes/linux-x64/native"
mkdir -p "$PLUGINS_DIR"
mkdir -p "$FFDEC_DIR"

# 3. Create root portable marker
touch "$DIST_DIR/.portable"

# 4. Create root launcher scripts
cat << 'EOF' > "$DIST_DIR/VibeSkua-Manager.sh"
#!/usr/bin/env bash
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR/current"
exec "$SCRIPT_DIR/current/VibeSkua.Manager" "$@"
EOF
chmod +x "$DIST_DIR/VibeSkua-Manager.sh"
cp -f "$DIST_DIR/VibeSkua-Manager.sh" "$DIST_DIR/VibeSkua-Manager"
chmod +x "$DIST_DIR/VibeSkua-Manager"

cat << 'EOF' > "$DIST_DIR/VibeSkua.sh"
#!/usr/bin/env bash
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR/current"
exec "$SCRIPT_DIR/current/VibeSkua" "$@"
EOF
chmod +x "$DIST_DIR/VibeSkua.sh"
cp -f "$DIST_DIR/VibeSkua.sh" "$DIST_DIR/VibeSkua"
chmod +x "$DIST_DIR/VibeSkua"

# 5. Copy root application files into current/
APP_BIN="$SCRIPT_DIR/Skua.App.Avalonia/bin/Release/net10.0"
MGR_BIN="$SCRIPT_DIR/Skua.Manager.Avalonia/bin/Release/net10.0"

ROOT_FILES=(
    "VibeSkua"
    "VibeSkua.dll"
    "VibeSkua.runtimeconfig.json"
    "VibeSkua.deps.json"
    "AdvancedSkills.json"
    "QuestData.json"
    "ScriptDates.json"
)

for file in "${ROOT_FILES[@]}"; do
    if [ -f "$APP_BIN/$file" ]; then
        cp -f "$APP_BIN/$file" "$CURRENT_DIR/$file"
    elif [ -f "$SCRIPT_DIR/Skua.Core/$file" ]; then
        cp -f "$SCRIPT_DIR/Skua.Core/$file" "$CURRENT_DIR/$file"
    fi
done

MGR_ROOT_FILES=(
    "VibeSkua.Manager"
    "VibeSkua.Manager.dll"
    "VibeSkua.Manager.runtimeconfig.json"
    "VibeSkua.Manager.deps.json"
    "Velopack.dll"
)

for file in "${MGR_ROOT_FILES[@]}"; do
    if [ -f "$MGR_BIN/$file" ]; then
        cp -f "$MGR_BIN/$file" "$CURRENT_DIR/$file"
    fi
done

# Copy skua.swf
if [ -f "$SCRIPT_DIR/Skua.AS3/skua/bin/skua.swf" ]; then
    cp -f "$SCRIPT_DIR/Skua.AS3/skua/bin/skua.swf" "$CURRENT_DIR/skua.swf"
elif [ -f "$APP_BIN/skua.swf" ]; then
    cp -f "$APP_BIN/skua.swf" "$CURRENT_DIR/skua.swf"
fi

# Copy Mugetsu binary
MUGETSU_BIN="$SCRIPT_DIR/../mugetsu/source/build/out/Mugetsu"
if [ -f "$MUGETSU_BIN" ]; then
    cp -f "$MUGETSU_BIN" "$CURRENT_DIR/Mugetsu"
    chmod +x "$CURRENT_DIR/Mugetsu"
elif [ -f "$BUILD_DIR/Mugetsu" ]; then
    cp -f "$BUILD_DIR/Mugetsu" "$CURRENT_DIR/Mugetsu"
    chmod +x "$CURRENT_DIR/Mugetsu"
fi

# 6. Copy Assemblies (excluding root DLLs)
EXCLUDED_DLLS=("VibeSkua.dll" "VibeSkua.Manager.dll" "Velopack.dll")

for dll in "$APP_BIN"/*.dll "$MGR_BIN"/*.dll; do
    [ -f "$dll" ] || continue
    name="$(basename "$dll")"
    skip=0
    for exc in "${EXCLUDED_DLLS[@]}"; do
        if [ "$name" == "$exc" ]; then
            skip=1
            break
        fi
    done
    if [ $skip -eq 0 ]; then
        cp -f "$dll" "$ASSEMBLIES_DIR/$name"
    fi
done

# Copy xml docs to Assemblies
for xml in "$APP_BIN"/*.xml "$MGR_BIN"/*.xml; do
    [ -f "$xml" ] || continue
    cp -f "$xml" "$ASSEMBLIES_DIR/" 2>/dev/null || true
done

# Copy native runtimes (SkiaSharp, HarfBuzzSharp) to Assemblies/runtimes/linux-x64/native
if [ -d "$APP_BIN/runtimes/linux-x64/native" ]; then
    cp -f "$APP_BIN/runtimes/linux-x64/native"/* "$ASSEMBLIES_DIR/runtimes/linux-x64/native/" 2>/dev/null || true
fi
# Also ensure current/runtimes exists for maximum compatibility
mkdir -p "$CURRENT_DIR/runtimes/linux-x64/native"
cp -f "$ASSEMBLIES_DIR/runtimes/linux-x64/native"/* "$CURRENT_DIR/runtimes/linux-x64/native/" 2>/dev/null || true

# 7. Copy Skills source files to Assemblies/Skills
if [ -d "$SCRIPT_DIR/Skua.Core/Skills" ]; then
    cp -f "$SCRIPT_DIR/Skua.Core/Skills"/*.cs "$ASSEMBLIES_DIR/Skills/" 2>/dev/null || true
fi

# 8. Copy Plugins to current/plugins
if [ -d "$SCRIPT_DIR/Skua.Plugin.DailyTracker/bin/Release/net10.0" ]; then
    cp -f "$SCRIPT_DIR/Skua.Plugin.DailyTracker/bin/Release/net10.0"/Skua.Plugin.*.dll "$PLUGINS_DIR/" 2>/dev/null || true
fi
if [ -d "$SCRIPT_DIR/Skua.Plugin.ButlerV5/bin/Release/net10.0" ]; then
    cp -f "$SCRIPT_DIR/Skua.Plugin.ButlerV5/bin/Release/net10.0"/ButlerV5.dll "$PLUGINS_DIR/" 2>/dev/null || true
fi
if [ -d "$SCRIPT_DIR/Skua.Plugin.Cosmetics/bin/Release/net10.0" ]; then
    cp -f "$SCRIPT_DIR/Skua.Plugin.Cosmetics/bin/Release/net10.0"/Skua.Plugin.Cosmetics.dll "$PLUGINS_DIR/" 2>/dev/null || true
fi

# 9. Copy FFDec to current/FFDec
if [ -d "$SCRIPT_DIR/FFDec" ]; then
    cp -rf "$SCRIPT_DIR/FFDec"/* "$FFDEC_DIR/" 2>/dev/null || true
    cat << 'EOF' > "$FFDEC_DIR/ffdec.sh"
#!/usr/bin/env bash
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
java -jar "$SCRIPT_DIR/ffdec.jar" "$@"
EOF
    chmod +x "$FFDEC_DIR/ffdec.sh"
fi

# 10. Set permissions
chmod +x "$CURRENT_DIR/VibeSkua" 2>/dev/null || true
chmod +x "$CURRENT_DIR/VibeSkua.Manager" 2>/dev/null || true
if [ -f "$CURRENT_DIR/Mugetsu" ]; then
    chmod +x "$CURRENT_DIR/Mugetsu"
fi

# 11. Create ZIP and Tarball distributions in Build/
echo "=== Compressing portable release archives ==="
cd "$BUILD_DIR"
rm -f "$PACKAGE_NAME.zip" "$PACKAGE_NAME.tar.gz"
zip -q -r "$PACKAGE_NAME.zip" "$PACKAGE_NAME"
tar -czf "$PACKAGE_NAME.tar.gz" "$PACKAGE_NAME"

echo "=== Portable Linux Package created successfully! ==="
echo "Directory: $DIST_DIR"
echo "ZIP:       $BUILD_DIR/$PACKAGE_NAME.zip ($(du -h "$BUILD_DIR/$PACKAGE_NAME.zip" | cut -f1))"
echo "Tarball:   $BUILD_DIR/$PACKAGE_NAME.tar.gz ($(du -h "$BUILD_DIR/$PACKAGE_NAME.tar.gz" | cut -f1))"
