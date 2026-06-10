<#
.SYNOPSIS
    Sborka proshivki ESP32-S3 cherez Docker (PowerShell)

.PARAMETER Clean
    Ochistit sborku pered kompilyatsiey (idf.py fullclean)

.PARAMETER Verbose
    Podrobnyy vyvod kompilyatora (idf.py build -v)

.EXAMPLE
    .\scripts\docker-build.ps1
    .\scripts\docker-build.ps1 -Clean
    .\scripts\docker-build.ps1 -Clean -Verbose
#>

param(
    [switch]$Clean,
    [switch]$Verbose
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# --- Paths -------------------------------------------------------------------
$ScriptDir   = Split-Path -Parent $MyInvocation.MyCommand.Path
$ProjectDir  = Split-Path -Parent $ScriptDir
$DockerImage = 'espressif/idf:v5.3'

# --- Output helpers ----------------------------------------------------------
function Write-Info    { param($msg) Write-Host "  $msg" -ForegroundColor Cyan }
function Write-Ok      { param($msg) Write-Host "  OK  $msg" -ForegroundColor Green }
function Write-Warn    { param($msg) Write-Host "  !!  $msg" -ForegroundColor Yellow }
function Write-Err     { param($msg) Write-Host "  ERR $msg" -ForegroundColor Red }
function Write-Header  { param($msg)
    Write-Host ""
    Write-Host ("-" * 60) -ForegroundColor Blue
    Write-Host "  $msg"   -ForegroundColor Blue
    Write-Host ("-" * 60) -ForegroundColor Blue
}

# --- Check Docker ------------------------------------------------------------
function Assert-Docker {
    Write-Info "Checking Docker..."

    if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
        Write-Err "Docker not installed. Download Docker Desktop: https://www.docker.com/products/docker-desktop/"
        exit 1
    }

    docker info 2>$null | Out-Null
    if (-not $?) {
        Write-Err "Docker is not running or no access. Start Docker Desktop."
        exit 1
    }

    Write-Ok "Docker available"
}

# --- Check / pull image ------------------------------------------------------
function Assert-Image {
    Write-Info "Checking image $DockerImage..."

    docker image inspect $DockerImage 2>$null | Out-Null
    if (-not $?) {
        Write-Warn "Image not found locally -- pulling (may take several minutes)..."
        docker pull $DockerImage
        if (-not $?) {
            Write-Err "Failed to pull $DockerImage"
            exit 1
        }
    }

    Write-Ok "Image ready"
}

# --- Check project layout ----------------------------------------------------
function Assert-Project {
    Write-Info "Checking project structure..."

    if (-not (Test-Path $ProjectDir)) {
        Write-Err "Project directory not found: $ProjectDir"
        exit 1
    }
    if (-not (Test-Path (Join-Path $ProjectDir 'CMakeLists.txt'))) {
        Write-Err "CMakeLists.txt not found in $ProjectDir"
        exit 1
    }

    Write-Ok "Project structure OK"
}

# --- Shared docker run args --------------------------------------------------
function Get-DockerArgs {
    return @(
        'run', '--rm',
        '-v', "${ProjectDir}:/project",
        '-v', 'elgato-ccache:/root/.ccache',
        '-v', 'elgato-esp-config:/root/.espressif',
        '-w', '/project',
        '-e', 'IDF_TARGET=esp32s3',
        '-e', 'CCACHE_DIR=/root/.ccache',
        '-e', 'CCACHE_MAXSIZE=2G',
        $DockerImage
    )
}

# --- Clean -------------------------------------------------------------------
function Invoke-Clean {
    Write-Header "Cleaning build"

    $a = (Get-DockerArgs) + @('idf.py', 'fullclean')
    & docker @a
    if (-not $?) { Write-Err "Clean failed"; exit 1 }

    Write-Ok "Build cleaned"
}

# --- Build -------------------------------------------------------------------
function Invoke-Build {
    Write-Header "Building ESP32-S3 firmware"
    Write-Info "Project : $ProjectDir"
    Write-Info "Image   : $DockerImage"
    Write-Info "Target  : esp32s3"

    if ($Verbose) {
        $buildCmd = @('idf.py', '-v', 'build')
    } else {
        $buildCmd = @('idf.py', 'build')
    }

    $a = (Get-DockerArgs) + $buildCmd

    $t0 = Get-Date
    Write-Info "Starting build..."
    Write-Host ""

    & docker @a

    if (-not $?) {
        Write-Host ""
        Write-Err "Build failed!"
        Write-Info "Try: .\scripts\docker-build.ps1 -Clean"
        exit 1
    }

    $sec = [int]((Get-Date) - $t0).TotalSeconds
    Write-Host ""
    Write-Ok "Build finished in $sec sec"

    # Show output binaries
    $binPath = Join-Path $ProjectDir 'build\macro-keyboard.bin'
    if (Test-Path $binPath) {
        $kb = [int]((Get-Item $binPath).Length / 1024)
        Write-Header "Firmware files"
        Write-Info "App size : $kb KB"
        Write-Info "Location : $ProjectDir\build\"
        Write-Host ""
        Get-ChildItem (Join-Path $ProjectDir 'build') -Filter '*.bin' |
            ForEach-Object {
                Write-Host ("  {0,-45} {1,6} KB" -f $_.Name, [int]($_.Length / 1024)) -ForegroundColor Cyan
            }
    }

    Write-Host ""
    Write-Header "Next step -- flash"
    Write-Host "  cd $ProjectDir\build" -ForegroundColor White
    Write-Host '  $flashArgs = (Get-Content flash_args) -split "\s+"' -ForegroundColor White
    Write-Host "  & esptool.exe -p COM3 -b 460800 --before default-reset --after hard-reset write-flash @flashArgs" -ForegroundColor White
    Write-Host "  (replace COM3 with your port from Device Manager)" -ForegroundColor DarkGray
}

# --- ccache stats ------------------------------------------------------------
function Show-CcacheStats {
    Write-Header "ccache stats"
    & docker run --rm -v 'elgato-ccache:/root/.ccache' $DockerImage ccache -s
}

# --- Main --------------------------------------------------------------------
Write-Header "ESP32-S3 Macro Keyboard - Docker Build"

Assert-Docker
Assert-Image
Assert-Project

if ($Clean) { Invoke-Clean }

Invoke-Build
Show-CcacheStats
