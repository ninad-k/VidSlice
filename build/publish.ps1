# Publishes VidSlice as a self-contained, single-file Windows x64 executable.
# Output: build\publish\VidSlice.exe
#
# Usage:  pwsh -File build\publish.ps1   (or run from the repo root)

$ErrorActionPreference = "Stop"

$root    = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "src\VidSlice\VidSlice.csproj"
$outDir  = Join-Path $PSScriptRoot "publish"

Write-Host "Publishing VidSlice -> $outDir" -ForegroundColor Cyan

dotnet publish $project `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -o $outDir

if ($LASTEXITCODE -ne 0) { throw "Publish failed with exit code $LASTEXITCODE" }

Write-Host "`nDone. Executable:" -ForegroundColor Green
Get-ChildItem (Join-Path $outDir "VidSlice.exe") | Select-Object FullName, @{n='MB';e={[math]::Round($_.Length/1MB,1)}}

Write-Host "`nReminder: drop ffmpeg.exe/ffprobe.exe into src\VidSlice\Resources\ffmpeg" -ForegroundColor Yellow
Write-Host "before publishing if you want them bundled into the output folder." -ForegroundColor Yellow
