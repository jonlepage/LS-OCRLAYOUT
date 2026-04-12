param(
    [switch]$Release,  # Build + create GitHub release
    [switch]$Run       # Build + run
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$csproj = Join-Path $root "ScreenSearchOverlay.csproj"

# Read version from .csproj
[xml]$proj = Get-Content $csproj
$version = $proj.Project.PropertyGroup.Version
if (-not $version) { throw "No <Version> found in .csproj" }

$publishDir = Join-Path $root "bin\Release\net10.0-windows10.0.19041.0\win-x64\publish"
$distDir = Join-Path $root "dist"
$exeName = "ScreenSearchOverlay.exe"

Write-Host ""
Write-Host "  ScreenSearchOverlay v$version" -ForegroundColor Cyan
Write-Host "  =========================" -ForegroundColor DarkGray
Write-Host ""

# ── Step 1: Kill running instance ──
$proc = Get-Process -Name "ScreenSearchOverlay" -ErrorAction SilentlyContinue
if ($proc) {
    Write-Host "[1/4] Killing running instance..." -ForegroundColor Yellow
    $proc | Stop-Process -Force
    Start-Sleep -Milliseconds 500
} else {
    Write-Host "[1/4] No running instance" -ForegroundColor DarkGray
}

# ── Step 2: Build ──
Write-Host "[2/4] Building Release..." -ForegroundColor Yellow
dotnet publish $csproj -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    --verbosity quiet
if ($LASTEXITCODE -ne 0) { throw "Build failed" }

# ── Step 3: Copy to dist ──
Write-Host "[3/4] Copying to dist/" -ForegroundColor Yellow
if (!(Test-Path $distDir)) { New-Item -ItemType Directory -Path $distDir | Out-Null }
Copy-Item (Join-Path $publishDir $exeName) (Join-Path $distDir $exeName) -Force
$size = [math]::Round((Get-Item (Join-Path $distDir $exeName)).Length / 1MB, 1)
Write-Host "      dist/$exeName ($size MB)" -ForegroundColor Green

# ── Step 4: Release or Run ──
if ($Release) {
    Write-Host "[4/4] Creating GitHub release v$version..." -ForegroundColor Yellow

    # Check if release already exists. PowerShell 7.4+ promotes native non-zero
    # exits to terminating errors via $PSNativeCommandUseErrorActionPreference,
    # so we wrap gh in try/catch and only inspect $LASTEXITCODE.
    $releaseExists = $false
    try {
        $null = gh release view "v$version" 2>&1
        $releaseExists = ($LASTEXITCODE -eq 0)
    } catch {
        $releaseExists = $false
    }
    $global:LASTEXITCODE = 0

    if ($releaseExists) {
        Write-Host "      Deleting existing v$version..." -ForegroundColor DarkYellow
        gh release delete "v$version" --yes
        if ($LASTEXITCODE -ne 0) { throw "Failed to delete existing release" }
    }

    gh release create "v$version" (Join-Path $distDir $exeName) `
        --title "ScreenSearchOverlay v$version" `
        --notes @"
## ScreenSearchOverlay v$version

Self-contained portable .exe — no .NET install needed.

See [README](../../blob/main/README.md) for features and usage.
"@

    if ($LASTEXITCODE -ne 0) { throw "Release failed" }
    Write-Host ""
    Write-Host "  Released v$version" -ForegroundColor Green
}
elseif ($Run) {
    Write-Host "[4/4] Launching..." -ForegroundColor Yellow
    Start-Process (Join-Path $distDir $exeName)
}
else {
    Write-Host "[4/4] Done. Run with:" -ForegroundColor DarkGray
    Write-Host "      .\build.ps1 -Run       # build + run" -ForegroundColor DarkGray
    Write-Host "      .\build.ps1 -Release   # build + GitHub release" -ForegroundColor DarkGray
}

Write-Host ""
