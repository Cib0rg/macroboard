#!/bin/bash
# MacroKeyboard Linux .deb Package Builder
#
# Prerequisites:
#   - .NET 10 SDK
#   - dpkg-deb (usually pre-installed on Debian/Ubuntu)
#
# Usage:
#   ./build-deb.sh                    # Build .deb for linux-x64
#   ./build-deb.sh --arch arm64       # Build for ARM64
#   ./build-deb.sh --no-publish       # Skip dotnet publish (use existing)
#
# Output:
#   installer/linux/output/macrokeyboard_1.0.0_amd64.deb

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SOFTWARE_DIR="$(cd "$SCRIPT_DIR/../.." && pwd)"
VERSION="1.0.0"
ARCH="amd64"
RUNTIME="linux-x64"
SKIP_PUBLISH=false
CONFIGURATION="Release"

# Parse arguments
while [[ $# -gt 0 ]]; do
    case $1 in
        --arch)
            ARCH="$2"
            if [ "$ARCH" = "arm64" ]; then
                RUNTIME="linux-arm64"
            fi
            shift 2
            ;;
        --version)
            VERSION="$2"
            shift 2
            ;;
        --no-publish)
            SKIP_PUBLISH=true
            shift
            ;;
        *)
            echo "Unknown option: $1"
            exit 1
            ;;
    esac
done

PUBLISH_DIR="$SOFTWARE_DIR/publish/$RUNTIME"
PKG_NAME="macrokeyboard"
PKG_DIR="$SCRIPT_DIR/build/${PKG_NAME}_${VERSION}_${ARCH}"
OUTPUT_DIR="$SCRIPT_DIR/output"

echo "=== MacroKeyboard .deb Package Builder ==="
echo "Version:  $VERSION"
echo "Arch:     $ARCH ($RUNTIME)"
echo "Publish:  $PUBLISH_DIR"
echo "Package:  $PKG_DIR"
echo ""

# Step 1: Publish .NET applications
if [ "$SKIP_PUBLISH" = false ]; then
    echo "=== Publishing Backend ==="
    dotnet publish "$SOFTWARE_DIR/src/MacroKeyboard.Backend/MacroKeyboard.Backend.csproj" \
        -c "$CONFIGURATION" \
        -r "$RUNTIME" \
        --self-contained true \
        -p:PublishSingleFile=true \
        -p:IncludeNativeLibrariesForSelfExtract=true \
        -o "$PUBLISH_DIR/backend"

    echo ""
    echo "=== Publishing UI ==="
    dotnet publish "$SOFTWARE_DIR/src/MacroKeyboard.UI/MacroKeyboard.UI.csproj" \
        -c "$CONFIGURATION" \
        -r "$RUNTIME" \
        --self-contained true \
        -p:PublishSingleFile=true \
        -p:IncludeNativeLibrariesForSelfExtract=true \
        -o "$PUBLISH_DIR/ui"
    echo ""
fi

# Verify publish output exists
if [ ! -d "$PUBLISH_DIR/backend" ] || [ ! -d "$PUBLISH_DIR/ui" ]; then
    echo "ERROR: Publish output not found at $PUBLISH_DIR"
    echo "Run without --no-publish first."
    exit 1
fi

# Step 2: Clean and create package directory structure
echo "=== Building .deb package structure ==="
rm -rf "$PKG_DIR"
mkdir -p "$PKG_DIR/DEBIAN"
mkdir -p "$PKG_DIR/opt/macrokeyboard/backend"
mkdir -p "$PKG_DIR/opt/macrokeyboard/ui"
mkdir -p "$PKG_DIR/etc/udev/rules.d"
mkdir -p "$PKG_DIR/usr/share/applications"
mkdir -p "$PKG_DIR/usr/share/icons/hicolor/256x256/apps"
mkdir -p "$PKG_DIR/usr/share/icons/hicolor/scalable/apps"
mkdir -p "$PKG_DIR/usr/bin"
mkdir -p "$PKG_DIR/usr/lib/systemd/user"

# Step 3: Copy application files
echo "Copying backend..."
cp -r "$PUBLISH_DIR/backend/"* "$PKG_DIR/opt/macrokeyboard/backend/"
chmod +x "$PKG_DIR/opt/macrokeyboard/backend/MacroKeyboard.Backend"

echo "Copying UI..."
cp -r "$PUBLISH_DIR/ui/"* "$PKG_DIR/opt/macrokeyboard/ui/"
chmod +x "$PKG_DIR/opt/macrokeyboard/ui/MacroKeyboard.UI"

# Step 4: Copy udev rules
echo "Installing udev rules..."
cp "$SOFTWARE_DIR/scripts/99-macrokeyboard.rules" "$PKG_DIR/etc/udev/rules.d/"

# Step 5: Copy icons
if [ -f "$SOFTWARE_DIR/src/MacroKeyboard.UI/Assets/app-icon.png" ]; then
    cp "$SOFTWARE_DIR/src/MacroKeyboard.UI/Assets/app-icon.png" \
       "$PKG_DIR/usr/share/icons/hicolor/256x256/apps/macrokeyboard.png"
fi
if [ -f "$SOFTWARE_DIR/src/MacroKeyboard.UI/Assets/app-icon.svg" ]; then
    cp "$SOFTWARE_DIR/src/MacroKeyboard.UI/Assets/app-icon.svg" \
       "$PKG_DIR/usr/share/icons/hicolor/scalable/apps/macrokeyboard.svg"
fi

# Step 6: Create symlinks in /usr/bin
ln -sf /opt/macrokeyboard/ui/MacroKeyboard.UI "$PKG_DIR/usr/bin/macrokeyboard"
ln -sf /opt/macrokeyboard/backend/MacroKeyboard.Backend "$PKG_DIR/usr/bin/macrokeyboard-backend"

# Step 7: Create .desktop file
cat > "$PKG_DIR/usr/share/applications/macrokeyboard.desktop" << 'DESKTOP'
[Desktop Entry]
Name=MacroKeyboard
Comment=Configure your MacroKeyboard (Stream Deck) device
Exec=/usr/bin/macrokeyboard
Icon=macrokeyboard
Terminal=false
Type=Application
Categories=Utility;Settings;HardwareSettings;
Keywords=macro;keyboard;stream;deck;elgato;
StartupNotify=true
DESKTOP

# Step 8: Create systemd user service for backend
cat > "$PKG_DIR/usr/lib/systemd/user/macrokeyboard-backend.service" << 'SERVICE'
[Unit]
Description=MacroKeyboard Backend Service
Documentation=https://github.com/user/macrokeyboard
After=graphical-session.target

[Service]
Type=simple
ExecStart=/opt/macrokeyboard/backend/MacroKeyboard.Backend
Restart=on-failure
RestartSec=5
Environment=DOTNET_ENVIRONMENT=Production

[Install]
WantedBy=default.target
SERVICE

# Step 9: Create DEBIAN/control
INSTALLED_SIZE=$(du -sk "$PKG_DIR" | cut -f1)
cat > "$PKG_DIR/DEBIAN/control" << CONTROL
Package: $PKG_NAME
Version: $VERSION
Section: utils
Priority: optional
Architecture: $ARCH
Installed-Size: $INSTALLED_SIZE
Depends: libusb-1.0-0 (>= 1.0.24), libx11-6, libfontconfig1
Recommends: libicu72 | libicu74 | libicu76
Maintainer: Elgato <support@elgato.com>
Homepage: https://github.com/user/macrokeyboard
Description: MacroKeyboard (Stream Deck) Configuration Software
 Desktop application and background service for configuring
 MacroKeyboard (Elgato Stream Deck compatible) devices.
 .
 Features:
  - Configure button actions (keyboard shortcuts, shell commands, etc.)
  - Upload custom images to device buttons
  - LED color and effect configuration
  - Profile management with folder support
  - Plugin system for extensibility
CONTROL

# Step 10: Create DEBIAN/postinst
cat > "$PKG_DIR/DEBIAN/postinst" << 'POSTINST'
#!/bin/bash
set -e

# Reload udev rules to pick up the new device rule
if command -v udevadm &> /dev/null; then
    udevadm control --reload-rules 2>/dev/null || true
    udevadm trigger 2>/dev/null || true
fi

# Update icon cache
if command -v gtk-update-icon-cache &> /dev/null; then
    gtk-update-icon-cache -f -t /usr/share/icons/hicolor 2>/dev/null || true
fi

# Update desktop database
if command -v update-desktop-database &> /dev/null; then
    update-desktop-database /usr/share/applications 2>/dev/null || true
fi

echo ""
echo "=== MacroKeyboard installed successfully ==="
echo ""
echo "To start the backend service:"
echo "  systemctl --user enable --now macrokeyboard-backend"
echo ""
echo "To launch the UI:"
echo "  macrokeyboard"
echo ""
echo "NOTE: If your device is already plugged in, unplug and replug it"
echo "      for the udev rules to take effect."
echo ""

exit 0
POSTINST
chmod 755 "$PKG_DIR/DEBIAN/postinst"

# Step 11: Create DEBIAN/prerm
cat > "$PKG_DIR/DEBIAN/prerm" << 'PRERM'
#!/bin/bash
set -e

# Stop the backend service if running
if command -v systemctl &> /dev/null; then
    systemctl --user stop macrokeyboard-backend 2>/dev/null || true
    systemctl --user disable macrokeyboard-backend 2>/dev/null || true
fi

exit 0
PRERM
chmod 755 "$PKG_DIR/DEBIAN/prerm"

# Step 12: Create DEBIAN/postrm
cat > "$PKG_DIR/DEBIAN/postrm" << 'POSTRM'
#!/bin/bash
set -e

if [ "$1" = "purge" ] || [ "$1" = "remove" ]; then
    # Reload udev rules
    if command -v udevadm &> /dev/null; then
        udevadm control --reload-rules 2>/dev/null || true
    fi

    # Update icon cache
    if command -v gtk-update-icon-cache &> /dev/null; then
        gtk-update-icon-cache -f -t /usr/share/icons/hicolor 2>/dev/null || true
    fi

    # Clean up user data (only on purge)
    if [ "$1" = "purge" ]; then
        rm -rf /opt/macrokeyboard 2>/dev/null || true
    fi
fi

exit 0
POSTRM
chmod 755 "$PKG_DIR/DEBIAN/postrm"

# Step 13: Build the .deb package
echo ""
echo "=== Building .deb package ==="
mkdir -p "$OUTPUT_DIR"
DEB_FILE="$OUTPUT_DIR/${PKG_NAME}_${VERSION}_${ARCH}.deb"
dpkg-deb --build --root-owner-group "$PKG_DIR" "$DEB_FILE"

# Step 14: Verify
echo ""
echo "=== Package info ==="
dpkg-deb --info "$DEB_FILE"
echo ""
echo "=== Package contents (top-level) ==="
dpkg-deb --contents "$DEB_FILE" | head -30
echo "..."

echo ""
echo "=== Build complete ==="
DEB_SIZE=$(du -h "$DEB_FILE" | cut -f1)
echo "Output: $DEB_FILE ($DEB_SIZE)"
echo ""
echo "Install with:"
echo "  sudo dpkg -i $DEB_FILE"
echo "  sudo apt-get install -f  # fix dependencies if needed"
echo ""
