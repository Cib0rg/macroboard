#!/usr/bin/env pwsh
# MacroKeyboard Windows Build & Package Script
# 
# Prerequisites:
#   - .NET 10 SDK
#   - Inno Setup 6.x (optional, for installer)
#
# Usage:
#   .\build-windows.ps1              # Build only (self-contained publish)
#   .\build-windows.ps1 -Installer   # Build + create installer
#   .\build-windows.ps1 -Runtime linux-x64  # Cross-compile for Linux

param(
    [switch]$Installer,
    [string]$Runtime = "win-x64",
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$SoftwareDir = Resolve-Path "$ScriptDir\..\.."
$PublishDir = "$SoftwareDir\publish\$Runtime"

Write-Host "=== MacroKeyboard Build Script ===" -ForegroundColor Cyan
Write-Host "Runtime:       $Runtime"
Write-Host "Configuration: $Configuration"
Write-Host "Publish dir:   $PublishDir"
Write-Host ""

# Clean previous publish
if (Test-Path $PublishDir) {
    Write-Host "Cleaning previous publish..." -ForegroundColor Yellow
    Remove-Item -Recurse -Force $PublishDir
}

# Publish Backend
Write-Host "Publishing Backend..." -ForegroundColor Green
dotnet publish "$SoftwareDir\src\MacroKeyboard.Backend\MacroKeyboard.Backend.csproj" `
    -c $Configuration `
    -r $Runtime `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -o "$PublishDir\backend"

if ($LASTEXITCODE -ne 0) { throw "Backend publish failed" }

# Publish UI
Write-Host "Publishing UI..." -ForegroundColor Green
dotnet publish "$SoftwareDir\src\MacroKeyboard.UI\MacroKeyboard.UI.csproj" `
    -c $Configuration `
    -r $Runtime `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -o "$PublishDir\ui"

if ($LASTEXITCODE -ne 0) { throw "UI publish failed" }

# Copy icon for installer
if (Test-Path "$SoftwareDir\src\MacroKeyboard.UI\Assets\app-icon.ico") {
    Copy-Item "$SoftwareDir\src\MacroKeyboard.UI\Assets\app-icon.ico" "$PublishDir\ui\Assets\" -Force -ErrorAction SilentlyContinue
}

Write-Host ""
Write-Host "=== Publish complete ===" -ForegroundColor Green
Write-Host "Backend: $PublishDir\backend"
Write-Host "UI:      $PublishDir\ui"

# Build installer if requested
if ($Installer) {
    Write-Host ""
    Write-Host "=== Building Installer ===" -ForegroundColor Cyan
    
    $IsccPath = $null
    $PossiblePaths = @(
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles}\Inno Setup 6\ISCC.exe",
        "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
        "C:\Program Files\Inno Setup 6\ISCC.exe"
    )
    
    foreach ($path in $PossiblePaths) {
        if (Test-Path $path) {
            $IsccPath = $path
            break
        }
    }
    
    if (-not $IsccPath) {
        Write-Host "ERROR: Inno Setup not found. Install from https://jrsoftware.org/isinfo.php" -ForegroundColor Red
        Write-Host "Skipping installer build. Published files are in: $PublishDir" -ForegroundColor Yellow
        exit 0
    }
    
    Write-Host "Using Inno Setup: $IsccPath"
    
    # Create output directory
    New-Item -ItemType Directory -Force -Path "$ScriptDir\output" | Out-Null
    
    & $IsccPath "$ScriptDir\MacroKeyboard.iss"
    
    if ($LASTEXITCODE -ne 0) { throw "Installer build failed" }
    
    Write-Host ""
    Write-Host "=== Installer created ===" -ForegroundColor Green
    Write-Host "Output: $ScriptDir\output\"
    Get-ChildItem "$ScriptDir\output\*.exe" | ForEach-Object { Write-Host "  $($_.Name) ($([math]::Round($_.Length / 1MB, 1)) MB)" }
}

Write-Host ""
Write-Host "Done!" -ForegroundColor Green
