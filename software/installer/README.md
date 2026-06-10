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
- [Inno Setup 6.x](https://jrsoftware.org/isinfo.php) (needed only for `-Installer`)

### Version

The version number is read from `installer/windows/version.txt`.  
Edit that file before building to set the version for both the installer filename
and the embedded assembly metadata.

```
1.0.0
```

You can also pass it explicitly:

```powershell
.\build-windows.ps1 -Installer -Version 1.2.0
```

### Build & Package

```powershell
cd software/installer/windows

# Publish only (no installer — useful to test the binaries)
.\build-windows.ps1

# Publish + create the .exe installer
.\build-windows.ps1 -Installer

# Cross-compile for Windows ARM64
.\build-windows.ps1 -Installer -Runtime win-arm64
```

Output:
- `software/publish/win-x64/` — self-contained published binaries
- `software/installer/windows/output/MacroKeyboard-Setup-1.0.0.exe` — installer

### Upgrading to a new version

Just run the new installer over the existing installation.  
The installer automatically:
1. Stops and unregisters the old backend service
2. Copies new files
3. Registers and starts the new backend service

No manual uninstall required.

### What the installer does

Both components — UI and Backend — are installed and set up for autostart.

| Component | How it starts | When |
|-----------|--------------|------|
| **Backend** (`MacroKeyboard.Backend.exe`) | Windows Service (`LocalSystem`) | At system boot, before login |
| **UI** (`MacroKeyboard.UI.exe`) | Registry `HKCU\Run` key | When the installing user logs in |

Additional install steps:

| Step | Default |
|------|---------|
| WinUSB driver via `pnputil` | On (remembered on upgrades) |
| UI autostart on login | On (remembered on upgrades) |
| Desktop shortcut | Off |
| Start Menu shortcuts | Always |
| Launch UI immediately after install | Optional checkbox on finish page |

### Uninstall

Use **Add or Remove Programs** → MacroKeyboard.  
The uninstaller stops the backend service, removes it from SCM, removes the driver, and cleans up registry keys.

### Backend Windows Service

The backend runs as a **Windows Service** (`LocalSystem` account) so it starts
automatically at boot even before a user logs in. You can manage it in
`services.msc` or via PowerShell:

```powershell
Get-Service MacroKeyboard.Backend        # check status
Restart-Service MacroKeyboard.Backend    # restart
Stop-Service MacroKeyboard.Backend       # stop
```

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
