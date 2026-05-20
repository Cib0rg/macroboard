# MacroKeyboard Installers

Platform-specific installers and packaging scripts for MacroKeyboard.

## Directory Structure

```
installer/
├── README.md                  # This file
├── windows/
│   ├── build-windows.ps1      # PowerShell build & package script
│   ├── MacroKeyboard.iss      # Inno Setup installer script
│   ├── driver/
│   │   └── MacroKeyboard.inf  # WinUSB driver INF file
│   └── output/                # Generated installer (git-ignored)
└── linux/
    ├── build-deb.sh           # Debian package build script
    └── output/                # Generated .deb package (git-ignored)
```

---

## Windows

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Inno Setup 6.x](https://jrsoftware.org/isinfo.php) (for installer only)

### Build & Install

```powershell
# Build self-contained publish (no installer)
cd software/installer/windows
.\build-windows.ps1

# Build with Inno Setup installer
.\build-windows.ps1 -Installer
```

Output:
- `software/publish/win-x64/` — published application files
- `software/installer/windows/output/MacroKeyboard-Setup-1.0.0.exe` — installer

### What the installer does

1. Installs MacroKeyboard UI and Backend to `C:\Program Files\MacroKeyboard\`
2. Installs the **WinUSB driver** for the device via `pnputil` (optional, requires admin)
3. Creates Start Menu shortcuts
4. Optionally adds backend to Windows autostart (Registry)
5. On uninstall: stops backend, removes driver, cleans up

### WinUSB Driver (Manual Install)

If you don't use the installer, you can install the driver manually:

1. **Option A — INF file:**
   ```cmd
   pnputil /add-driver driver\MacroKeyboard.inf /install
   ```

2. **Option B — Zadig:**
   - Download [Zadig](https://zadig.akeo.ie/)
   - Plug in the device
   - Select "MacroKeyboard (Interface 1)" in Zadig
   - Choose "WinUSB" driver
   - Click "Install Driver"

> **Note:** The INF targets `USB\VID_1209&PID_0001&MI_01` — only the Vendor interface (Interface 1).
> The HID Keyboard interface (Interface 0) continues to use the built-in Windows HID driver.

---

## Linux (Debian/Ubuntu)

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- `dpkg-deb` (pre-installed on Debian/Ubuntu)
- `libusb-1.0-0` (runtime dependency, installed automatically by the .deb)

### Build & Install

```bash
# Build .deb package
cd software/installer/linux
chmod +x build-deb.sh
./build-deb.sh

# Install
sudo dpkg -i output/macrokeyboard_1.0.0_amd64.deb
sudo apt-get install -f  # fix dependencies if needed
```

### What the .deb package includes

| Component | Installed to |
|-----------|-------------|
| Backend binary | `/opt/macrokeyboard/backend/` |
| UI binary | `/opt/macrokeyboard/ui/` |
| CLI symlinks | `/usr/bin/macrokeyboard`, `/usr/bin/macrokeyboard-backend` |
| udev rules | `/etc/udev/rules.d/99-macrokeyboard.rules` |
| Desktop entry | `/usr/share/applications/macrokeyboard.desktop` |
| App icon | `/usr/share/icons/hicolor/` |
| systemd service | `/usr/lib/systemd/user/macrokeyboard-backend.service` |

### Post-install

```bash
# Enable and start the backend service (per-user)
systemctl --user enable --now macrokeyboard-backend

# Launch the UI
macrokeyboard

# If device was already plugged in, replug it for udev rules to take effect
```

### udev Rules

The package installs `/etc/udev/rules.d/99-macrokeyboard.rules` which grants
read/write access to the device (VID `0x1209`, PID `0x0001`) for users in the
`plugdev` group. The rule also uses `TAG+="uaccess"` for systemd-logind
integration.

### Dependencies

- `libusb-1.0-0` (>= 1.0.24) — USB device communication
- `libx11-6` — X11 display (for Avalonia UI)
- `libfontconfig1` — Font rendering

---

## Cross-compilation

Both scripts support cross-compilation via .NET RID:

```bash
# Linux ARM64 (e.g., Raspberry Pi)
./build-deb.sh --arch arm64

# Windows from Linux (publish only, no installer)
# Use the PowerShell script on Windows for the full installer
dotnet publish -r win-x64 --self-contained true
```
