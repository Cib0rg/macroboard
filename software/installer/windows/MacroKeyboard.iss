; MacroKeyboard Inno Setup Installer Script
;
; Prerequisites:
;   Inno Setup 6.x  https://jrsoftware.org/isinfo.php
;   Published binaries in publish\win-x64\  (run build-windows.ps1 first)
;
; Build:
;   iscc MacroKeyboard.iss
;   iscc /DAppVersion=1.2.0 MacroKeyboard.iss      (override version)
;
; The build-windows.ps1 script calls ISCC automatically with the right version.

; Version can be overridden from the command line: /DAppVersion=x.y.z
#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif

#define AppName        "MacroKeyboard"
#define AppPublisher   "Elgato"
#define AppURL         "https://github.com/user/macrokeyboard"
#define AppExeName     "MacroKeyboard.UI.exe"
#define BackendExeName "MacroKeyboard.Backend.exe"
#define ServiceName    "MacroKeyboard.Backend"
#define ServiceDisplay "MacroKeyboard Backend"
#define PublishBase    "..\..\publish\win-x64"
#define DriverDir      "driver"

; ─────────────────────────────────────────────────────────────
; Setup section
; ─────────────────────────────────────────────────────────────
[Setup]
; AppId is the permanent identity of this product — NEVER change it between versions.
; Inno Setup uses it to detect an existing installation and offer an upgrade.
AppId={{B8A5C9D1-4F3A-4C8E-9F2A-1D3E5F7A9B1C}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppURL}
AppSupportURL={#AppURL}
AppUpdatesURL={#AppURL}
VersionInfoVersion={#AppVersion}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
AllowNoIcons=yes
OutputDir=output
OutputBaseFilename=MacroKeyboard-Setup-{#AppVersion}
SetupIconFile={#PublishBase}\ui\Assets\app-icon.ico
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
; Admin required: installs Windows service and driver
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\ui\{#AppExeName}
MinVersion=10.0
UsedUserAreasWarning=no
; Automatically close running instances before copying files (handles upgrades)
CloseApplications=yes
CloseApplicationsFilter={#AppExeName},{#BackendExeName}
RestartApplications=no

; ─────────────────────────────────────────────────────────────
; Languages
; ─────────────────────────────────────────────────────────────
[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"

; ─────────────────────────────────────────────────────────────
; Optional install tasks shown to the user
; ─────────────────────────────────────────────────────────────
; Flags reference:
;   checkedonce — checked on fresh install, preserves user choice on upgrade
;   unchecked   — unchecked by default
[Tasks]
; WinUSB driver for the vendor HID interface (on by default, remembers user choice)
Name: "installdriver"; Description: "Install WinUSB device driver"; GroupDescription: "Device Driver:"; Flags: checkedonce

; UI autostart on login (on by default, remembers user choice on upgrade)
Name: "autostart_ui"; Description: "Launch MacroKeyboard UI on Windows login"; GroupDescription: "Autostart:"; Flags: checkedonce

; Desktop shortcut (off by default — Start Menu entry is always created)
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

; ─────────────────────────────────────────────────────────────
; Files to install
; ─────────────────────────────────────────────────────────────
[Files]
; UI — Avalonia desktop app (self-contained, single file)
Source: "{#PublishBase}\ui\*"; DestDir: "{app}\ui"; Flags: ignoreversion recursesubdirs createallsubdirs

; Backend — Windows Service (self-contained, single file)
Source: "{#PublishBase}\backend\*"; DestDir: "{app}\backend"; Flags: ignoreversion recursesubdirs createallsubdirs

; WinUSB driver INF
Source: "{#DriverDir}\MacroKeyboard.inf"; DestDir: "{app}\driver"; Flags: ignoreversion

; ─────────────────────────────────────────────────────────────
; Shortcuts
; ─────────────────────────────────────────────────────────────
[Icons]
; Start Menu
Name: "{group}\{#AppName}"; Filename: "{app}\ui\{#AppExeName}"
Name: "{group}\{cm:UninstallProgram,{#AppName}}"; Filename: "{uninstallexe}"
; Optional desktop shortcut
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\ui\{#AppExeName}"; Tasks: desktopicon

; ─────────────────────────────────────────────────────────────
; Registry
; ─────────────────────────────────────────────────────────────
[Registry]
; UI autostart on user login (HKCU — applies to the installing user only)
; Tied to the autostart_ui task so the user can opt out during install/upgrade.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; \
    ValueType: string; ValueName: "{#AppName}"; \
    ValueData: """{app}\ui\{#AppExeName}"""; \
    Flags: uninsdeletevalue; Tasks: autostart_ui

; ─────────────────────────────────────────────────────────────
; Post-install steps
; ─────────────────────────────────────────────────────────────
[Run]
; Install WinUSB driver (silent, requires admin — already the case for this installer)
Filename: "pnputil.exe"; \
    Parameters: "/add-driver ""{app}\driver\MacroKeyboard.inf"" /install"; \
    StatusMsg: "Installing device driver..."; \
    Flags: runhidden waituntilterminated; Tasks: installdriver

; Optional "Launch now" checkbox on the Finish page
Filename: "{app}\ui\{#AppExeName}"; \
    Description: "{cm:LaunchProgram,{#StringChange(AppName, '&', '&&')}}"; \
    Flags: nowait postinstall skipifsilent

; ─────────────────────────────────────────────────────────────
; Uninstall steps
; ─────────────────────────────────────────────────────────────
[UninstallRun]
; Kill UI if running (backend service is stopped in [Code] CurUninstallStepChanged)
Filename: "taskkill.exe"; Parameters: "/F /IM {#AppExeName}"; Flags: runhidden; RunOnceId: "KillUI"

; Remove WinUSB driver
Filename: "pnputil.exe"; \
    Parameters: "/delete-driver ""{app}\driver\MacroKeyboard.inf"" /uninstall"; \
    Flags: runhidden; RunOnceId: "RemoveDriver"

; ─────────────────────────────────────────────────────────────
; Pascal script — service management and upgrade handling
; ─────────────────────────────────────────────────────────────
[Code]
const
  SVC_NAME    = '{#ServiceName}';
  SVC_DISPLAY = '{#ServiceDisplay}';

// Returns true if the Windows Service is registered (whether running or stopped).
function ServiceExists(): Boolean;
var
  ResultCode: Integer;
begin
  Exec('sc.exe', 'query "' + SVC_NAME + '"', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Result := (ResultCode = 0);
end;

// Stops the service and waits, then deletes it.
// Safe to call even if the service does not exist.
procedure StopAndDeleteService();
var
  ResultCode: Integer;
  Attempts: Integer;
begin
  if not ServiceExists() then Exit;

  // Stop — ignore result code (service may already be stopped)
  Exec('sc.exe', 'stop "' + SVC_NAME + '"', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);

  // Wait up to 5 s for the service to reach stopped state
  Attempts := 0;
  while Attempts < 10 do
  begin
    Sleep(500);
    Exec('sc.exe', 'query "' + SVC_NAME + '"', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    // If query succeeds but service is gone — exit loop
    Attempts := Attempts + 1;
  end;

  Exec('sc.exe', 'delete "' + SVC_NAME + '"', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);

  // Brief pause so SCM can process the deletion before we recreate the service
  Sleep(1500);
end;

// Creates and starts the backend Windows Service pointing at the just-installed binary.
procedure CreateAndStartService();
var
  ResultCode: Integer;
  BinPath: String;
begin
  BinPath := ExpandConstant('{app}\backend\{#BackendExeName}');

  Exec('sc.exe',
    'create "' + SVC_NAME + '"' +
    ' binPath= "' + BinPath + '"' +
    ' DisplayName= "' + SVC_DISPLAY + '"' +
    ' start= auto' +
    ' obj= LocalSystem',
    '', SW_HIDE, ewWaitUntilTerminated, ResultCode);

  if ResultCode <> 0 then
  begin
    MsgBox(
      'Warning: could not register the MacroKeyboard backend service (sc.exe returned ' +
      IntToStr(ResultCode) + '). ' + #13#10 +
      'The device will not work until the service is registered. ' +
      'Try re-running the installer.',
      mbError, MB_OK);
    Exit;
  end;

  // Set a human-readable description in the Services console
  Exec('sc.exe',
    'description "' + SVC_NAME + '" "MacroKeyboard USB device communication service"',
    '', SW_HIDE, ewWaitUntilTerminated, ResultCode);

  // Start the service
  Exec('sc.exe', 'start "' + SVC_NAME + '"', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

// Called at each step of the installer wizard.
procedure CurStepChanged(CurStep: TSetupStep);
var
  ResultCode: Integer;
begin
  case CurStep of
    ssInstall:
      begin
        // Before files are written: kill UI and cleanly remove the backend service.
        // This handles both fresh installs (service absent) and upgrades (service present).
        Exec('taskkill.exe', '/F /IM {#AppExeName}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
        StopAndDeleteService();
      end;

    ssPostInstall:
      begin
        // Files have been copied. Register and start the service with the new binary.
        CreateAndStartService();
      end;
  end;
end;

// Called at each step of the uninstaller.
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
    StopAndDeleteService();
end;
