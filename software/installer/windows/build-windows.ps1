#!/usr/bin/env pwsh
# MacroKeyboard Windows Build & Package Script
#
# Prerequisites:
#   .NET 10 SDK     https://dotnet.microsoft.com/download
#   Inno Setup 6.x  https://jrsoftware.org/isinfo.php  (needed only for -Installer)
#
# Usage:
#   .\build-windows.ps1                             # publish only
#   .\build-windows.ps1 -Installer                  # publish + create .exe installer
#   .\build-windows.ps1 -Installer -Version 1.2.0   # explicit version number
#   .\build-windows.ps1 -Runtime win-arm64           # cross-compile for ARM64

param(
    [switch]$Installer,
    [string]$Version    = "",
    [string]$Runtime    = "win-x64",
    [string]$Configuration = "Release"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$ScriptDir   = Split-Path -Parent $MyInvocation.MyCommand.Path
$SoftwareDir = Resolve-Path "$ScriptDir\..\.."
$PublishDir  = "$SoftwareDir\publish\$Runtime"

# ── Version resolution ────────────────────────────────────────────────────────
# Priority: -Version argument > version.txt next to this script > "1.0.0"
if (-not $Version) {
    $VersionFile = "$ScriptDir\version.txt"
    if (Test-Path $VersionFile) {
        $Version = (Get-Content $VersionFile -First 1).Trim()
        Write-Host "Version read from version.txt: $Version" -ForegroundColor DarkGray
    } else {
        $Version = "1.0.0"
        Write-Host "No -Version or version.txt found, defaulting to $Version" -ForegroundColor Yellow
    }
}

if ($Version -notmatch '^\d+\.\d+\.\d+') {
    throw "Version '$Version' is not in the expected format (Major.Minor.Patch)"
}

# ── Summary ───────────────────────────────────────────────────────────────────
Write-Host ""
Write-Host "=== MacroKeyboard Build Script ===" -ForegroundColor Cyan
Write-Host "  Version:       $Version"
Write-Host "  Runtime:       $Runtime"
Write-Host "  Configuration: $Configuration"
Write-Host "  Publish dir:   $PublishDir"
Write-Host "  Build installer: $Installer"
Write-Host ""

# ── Clean previous publish ────────────────────────────────────────────────────
if (Test-Path $PublishDir) {
    Write-Host "Cleaning $PublishDir ..." -ForegroundColor Yellow
    Remove-Item -Recurse -Force $PublishDir
}

# ── Helper: dotnet publish ────────────────────────────────────────────────────
function Publish-Project {
    param([string]$ProjectPath, [string]$OutDir)

    Write-Host "Publishing $(Split-Path $ProjectPath -Leaf) -> $OutDir" -ForegroundColor Green

    dotnet publish $ProjectPath `
        -c $Configuration `
        -r $Runtime `
        --self-contained true `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:Version=$Version `
        -p:AssemblyVersion=$Version `
        -p:FileVersion=$Version `
        -o $OutDir

    if ($LASTEXITCODE -ne 0) { throw "Publish failed for $ProjectPath" }
}

# ── Publish Backend ───────────────────────────────────────────────────────────
Publish-Project "$SoftwareDir\src\MacroKeyboard.Backend\MacroKeyboard.Backend.csproj" "$PublishDir\backend"

# ── Publish UI ────────────────────────────────────────────────────────────────
Publish-Project "$SoftwareDir\src\MacroKeyboard.UI\MacroKeyboard.UI.csproj" "$PublishDir\ui"

# Ensure Assets directory exists and copy the icon so Inno Setup can find it
$AssetsDir = "$PublishDir\ui\Assets"
New-Item -ItemType Directory -Force -Path $AssetsDir | Out-Null

$IconSource = "$SoftwareDir\src\MacroKeyboard.UI\Assets\app-icon.ico"
if (Test-Path $IconSource) {
    Copy-Item $IconSource $AssetsDir -Force
}

Write-Host ""
Write-Host "Publish complete" -ForegroundColor Green
Write-Host "  Backend: $PublishDir\backend"
Write-Host "  UI:      $PublishDir\ui"

# ── Installer ─────────────────────────────────────────────────────────────────
if (-not $Installer) {
    Write-Host ""
    Write-Host "Tip: add -Installer to also build the .exe installer." -ForegroundColor DarkGray
    exit 0
}

Write-Host ""
Write-Host "=== Building Installer ===" -ForegroundColor Cyan

# Locate ISCC.exe
$IsccCandidates = @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles}\Inno Setup 6\ISCC.exe",
    "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
    "C:\Program Files\Inno Setup 6\ISCC.exe",
    (Get-Command "iscc" -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source -ErrorAction SilentlyContinue)
) | Where-Object { $_ -and (Test-Path $_) }

$IsccPath = $IsccCandidates | Select-Object -First 1

if (-not $IsccPath) {
    Write-Host ""
    Write-Host "ERROR: Inno Setup 6 not found." -ForegroundColor Red
    Write-Host "  Install from: https://jrsoftware.org/isinfo.php"   -ForegroundColor Red
    Write-Host "  Published files are ready in: $PublishDir"          -ForegroundColor Yellow
    exit 1
}

Write-Host "  ISCC: $IsccPath"

New-Item -ItemType Directory -Force -Path "$ScriptDir\output" | Out-Null

& $IsccPath /DAppVersion=$Version "$ScriptDir\MacroKeyboard.iss"

if ($LASTEXITCODE -ne 0) { throw "Inno Setup build failed (exit code $LASTEXITCODE)" }

Write-Host ""
Write-Host "Installer created:" -ForegroundColor Green
Get-ChildItem "$ScriptDir\output\MacroKeyboard-Setup-$Version*.exe" | ForEach-Object {
    $SizeMB = [math]::Round($_.Length / 1MB, 1)
    Write-Host "  $($_.Name)  ($SizeMB MB)"
}

Write-Host ""
Write-Host "Done." -ForegroundColor Green
