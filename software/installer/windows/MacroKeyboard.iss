; MacroKeyboard Inno Setup Installer Script
; Builds a Windows installer that includes:
;   - MacroKeyboard Backend (background service)
;   - MacroKeyboard UI (Avalonia desktop app)
;   - WinUSB driver for the device (INF file)
;
; Prerequisites:
;   - Inno Setup 6.x (https://jrsoftware.org/isinfo.php)
;   - .NET 10 publish output in {#PublishDir}
;
; Build:
;   1. Run build-windows.ps1 first to create publish output
;   2. Open this file in Inno Setup Compiler and click Build
;   Or: iscc MacroKeyboard.iss

#define AppName "MacroKeyboard"
#define AppVersion "1.0.0"
#define AppPublisher "Elgato"
#define AppURL "https://github.com/user/macrokeyboard"
#define AppExeName "MacroKeyboard.UI.exe"
#define BackendExeName "MacroKeyboard.Backend.exe"

; Paths relative to this .iss file
#define PublishBase "..\..\publish\win-x64"
#define DriverDir "driver"

[Setup]
AppId={{B8A5C9D1-4F3A-4C8E-9F2A-1D3E5F7A9B1C}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppURL}
AppSupportURL={#AppURL}
AppUpdatesURL={#AppURL}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
AllowNoIcons=yes
; Output installer to installer/windows/output/
OutputDir=output
OutputBaseFilename=MacroKeyboard-Setup-{#AppVersion}
SetupIconFile={#PublishBase}\ui\Assets\app-icon.ico
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\ui\{#AppExeName}
MinVersion=10.0

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "installdriver"; Description: "Install WinUSB driver for MacroKeyboard device"; GroupDescription: "Device Driver:"; Flags: checkedonce
Name: "autostart"; Description: "Start backend service automatically on login"; GroupDescription: "Startup:"; Flags: checkedonce

[Files]
; UI application
Source: "{#PublishBase}\ui\*"; DestDir: "{app}\ui"; Flags: ignoreversion recursesubdirs createallsubdirs

; Backend service
Source: "{#PublishBase}\backend\*"; DestDir: "{app}\backend"; Flags: ignoreversion recursesubdirs createallsubdirs

; WinUSB driver INF
Source: "{#DriverDir}\MacroKeyboard.inf"; DestDir: "{app}\driver"; Flags: ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\ui\{#AppExeName}"
Name: "{group}\{cm:UninstallProgram,{#AppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\ui\{#AppExeName}"; Tasks: desktopicon

[Run]
; Install WinUSB driver via pnputil (requires admin)
Filename: "pnputil.exe"; Parameters: "/add-driver ""{app}\driver\MacroKeyboard.inf"" /install"; \
    StatusMsg: "Installing WinUSB driver for MacroKeyboard..."; \
    Flags: runhidden waituntilterminated; Tasks: installdriver

; Launch the app after install
Filename: "{app}\ui\{#AppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(AppName, '&', '&&')}}"; \
    Flags: nowait postinstall skipifsilent

[UninstallRun]
; Stop backend before uninstall
Filename: "taskkill.exe"; Parameters: "/F /IM {#BackendExeName}"; Flags: runhidden

; Remove the driver on uninstall
Filename: "pnputil.exe"; Parameters: "/delete-driver ""{app}\driver\MacroKeyboard.inf"" /uninstall"; \
    Flags: runhidden

[Registry]
; Autostart backend on login
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; \
    ValueType: string; ValueName: "MacroKeyboard.Backend"; \
    ValueData: """{app}\backend\{#BackendExeName}"""; \
    Flags: uninsdeletevalue; Tasks: autostart

[Code]
// Check if .NET 10 runtime is installed
function IsDotNetInstalled(): Boolean;
var
  ResultCode: Integer;
begin
  Result := Exec('dotnet', '--list-runtimes', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) and (ResultCode = 0);
end;

procedure InitializeWizard();
begin
  // Could add custom pages here (e.g., driver installation warning)
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  
  if CurPageID = wpReady then
  begin
    // Stop running instances before install
    Exec('taskkill.exe', '/F /IM ' + '{#AppExeName}', '', SW_HIDE, ewWaitUntilTerminated, 0);
    Exec('taskkill.exe', '/F /IM ' + '{#BackendExeName}', '', SW_HIDE, ewWaitUntilTerminated, 0);
  end;
end;
