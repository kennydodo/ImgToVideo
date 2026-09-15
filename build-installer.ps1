# Builds the ImgToVideo installer end to end:
#   1. dotnet publish (self-contained, single exe, win-x64)
#   2. bundles ffmpeg/ffprobe next to the exe (found via PATH / winget)
#   3. compiles installer\imgtovideo.iss with Inno Setup (ISCC.exe)
# Output: dist\installer\ImgToVideo-Setup-<version>.exe

$ErrorActionPreference = "Stop"
$repoRoot = $PSScriptRoot
Set-Location $repoRoot

$version = "1.0.0"
$publishDir = Join-Path $repoRoot "dist\publish"
$ffmpegDir = Join-Path $publishDir "ffmpeg"

Write-Host "== Publishing ImgToVideo (self-contained win-x64) ==" -ForegroundColor Cyan
dotnet publish src/ImgToVideo.App/ImgToVideo.App.csproj `
    -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=none -p:DebugSymbols=false `
    -o $publishDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed." }

Write-Host "== Bundling ffmpeg ==" -ForegroundColor Cyan
$ffmpegSource = @()
foreach ($name in @("ffmpeg", "ffprobe")) {
    $cmd = Get-Command $name -ErrorAction SilentlyContinue
    if ($cmd) { $ffmpegSource += $cmd.Source }
}
if ($ffmpegSource.Count -lt 2) {
    throw "ffmpeg/ffprobe not found on PATH. Install with: winget install Gyan.FFmpeg"
}

New-Item -ItemType Directory -Path $ffmpegDir -Force | Out-Null
foreach ($source in $ffmpegSource) {
    $real = $source
    if ($source -match '\.exe$') {
        # Resolve winget/shim indirection to the real binary where possible.
        $target = (Get-Item $source).Target
        if ($target -is [array]) { $target = $target[0] }
        if ($target -and (Test-Path $target)) { $real = $target }
    }
    Copy-Item $real (Join-Path $ffmpegDir (Split-Path $real -Leaf)) -Force
    Write-Host "  bundled $(Split-Path $real -Leaf)"
}

Write-Host "== Locating Inno Setup ==" -ForegroundColor Cyan
$isccPaths = @(
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
)
$iscc = $isccPaths | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) {
    Write-Host "Inno Setup not found - installing via winget..." -ForegroundColor Yellow
    winget install --id JRSoftware.InnoSetup -e --accept-source-agreements --accept-package-agreements
    if ($LASTEXITCODE -ne 0) { throw "winget install of Inno Setup failed." }
    $iscc = $isccPaths | Where-Object { Test-Path $_ } | Select-Object -First 1
}
if (-not $iscc) { throw "ISCC.exe not found after installing Inno Setup." }

Write-Host "== Compiling installer ==" -ForegroundColor Cyan
& $iscc "installer\imgtovideo.iss"
if ($LASTEXITCODE -ne 0) { throw "Inno Setup compilation failed." }

$setup = Join-Path $repoRoot "dist\installer\ImgToVideo-Setup-$version.exe"
Write-Host "Installer ready: $setup" -ForegroundColor Green
Write-Host ("Size: {0:N1} MB" -f ((Get-Item $setup).Length / 1MB))
