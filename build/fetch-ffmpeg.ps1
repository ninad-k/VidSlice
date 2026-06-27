# Downloads the smaller ffmpeg "essentials" static build and copies ffmpeg.exe /
# ffprobe.exe into src\VidSlice\Resources\ffmpeg so they get bundled with the app.
#
# Usage:  pwsh -File build\fetch-ffmpeg.ps1

$ErrorActionPreference = "Stop"

$root   = Split-Path -Parent $PSScriptRoot
$dest   = Join-Path $root "src\VidSlice\Resources\ffmpeg"
$tmp    = Join-Path $env:TEMP ("vidslice_ffmpeg_" + [Guid]::NewGuid().ToString("N"))
$zip    = Join-Path $tmp "ffmpeg.zip"
$url    = "https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip"

New-Item -ItemType Directory -Force -Path $tmp, $dest | Out-Null

Write-Host "Downloading ffmpeg essentials build…" -ForegroundColor Cyan
Invoke-WebRequest -Uri $url -OutFile $zip

Write-Host "Extracting…" -ForegroundColor Cyan
Expand-Archive -Path $zip -DestinationPath $tmp -Force

$bin = Get-ChildItem -Path $tmp -Recurse -Directory -Filter "bin" | Select-Object -First 1
if (-not $bin) { throw "Could not find bin\ folder in the extracted archive." }

foreach ($exe in "ffmpeg.exe", "ffprobe.exe") {
    Copy-Item (Join-Path $bin.FullName $exe) (Join-Path $dest $exe) -Force
    Write-Host "Copied $exe -> $dest"
}

Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
Write-Host "`nDone. ffmpeg will now be bundled into builds." -ForegroundColor Green
