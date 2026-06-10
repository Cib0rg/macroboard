<#
.SYNOPSIS
    Flash ESP32-S3 firmware via esptool

.PARAMETER Port
    COM port the device is connected to (e.g. COM3)

.EXAMPLE
    .\scripts\flash.ps1 -Port COM3
    .\scripts\flash.ps1 COM3
#>

param(
    [Parameter(Mandatory = $true, Position = 0)]
    [string]$Port
)

$ErrorActionPreference = 'Stop'

$ScriptDir  = Split-Path -Parent $MyInvocation.MyCommand.Path
$BuildDir   = Join-Path (Split-Path -Parent $ScriptDir) 'build'
$FlashArgs  = Join-Path $BuildDir 'flash_args'

# --- Checks ------------------------------------------------------------------
if (-not (Get-Command esptool.exe -ErrorAction SilentlyContinue)) {
    Write-Host "ERR  esptool.exe not found. Install it: pip install esptool" -ForegroundColor Red
    exit 1
}

if (-not (Test-Path $FlashArgs)) {
    Write-Host "ERR  $FlashArgs not found -- run docker-build.ps1 first" -ForegroundColor Red
    exit 1
}

# --- Flash -------------------------------------------------------------------
Write-Host "  Port     : $Port"           -ForegroundColor Cyan
Write-Host "  Build dir: $BuildDir"       -ForegroundColor Cyan
Write-Host ""

Push-Location $BuildDir
try {
    $fa = (Get-Content flash_args -Raw).Trim() -split '\s+'
    & esptool.exe -p $Port -b 460800 --before default-reset --after hard-reset write-flash @fa
    if (-not $?) { exit 1 }
} finally {
    Pop-Location
}

Write-Host ""
Write-Host "  Done." -ForegroundColor Green
