# Downloads the prebuilt whisper.cpp Windows binary and a GGML speech model into
# src\VidSlice\Resources\whisper so they get bundled with the app (offline transcription).
#
# By default it fetches a CPU build and the multilingual large-v3 model, which has
# strong Hindi/Marathi/English coverage. The model is large (~3 GB); pass -Model to
# pick a smaller one (e.g. medium ~1.5 GB) or -SkipModel to bundle only the binary
# and let the app download a model to %AppData%\VidSlice\models on first use.
#
# Usage:
#   pwsh -File build\fetch-whisper.ps1
#   pwsh -File build\fetch-whisper.ps1 -Model medium
#   pwsh -File build\fetch-whisper.ps1 -SkipModel

param(
    [string]$Model = "large-v3",
    [switch]$SkipModel
)

$ErrorActionPreference = "Stop"
# Invoke-WebRequest's progress bar makes large downloads crawl on Windows
# PowerShell 5.1; silencing it restores full speed.
$ProgressPreference = "SilentlyContinue"

$root = Split-Path -Parent $PSScriptRoot
$dest = Join-Path $root "src\VidSlice\Resources\whisper"
$tmp  = Join-Path $env:TEMP ("vidslice_whisper_" + [Guid]::NewGuid().ToString("N"))

New-Item -ItemType Directory -Force -Path $tmp, $dest | Out-Null

# --- 1. whisper.cpp prebuilt Windows binary (CPU build) ---
# Latest release asset that contains whisper-cli.exe for Windows x64.
$binUrl = "https://github.com/ggerganov/whisper.cpp/releases/latest/download/whisper-bin-x64.zip"
$binZip = Join-Path $tmp "whisper-bin.zip"

Write-Host "Downloading whisper.cpp Windows binary…" -ForegroundColor Cyan
Invoke-WebRequest -Uri $binUrl -OutFile $binZip

Write-Host "Extracting…" -ForegroundColor Cyan
Expand-Archive -Path $binZip -DestinationPath $tmp -Force

# Copy the CLI and any runtime DLLs it needs next to it.
$cli = Get-ChildItem -Path $tmp -Recurse -File -Include "whisper-cli.exe", "main.exe" | Select-Object -First 1
if (-not $cli) { throw "Could not find whisper-cli.exe in the downloaded archive." }

Copy-Item $cli.FullName (Join-Path $dest $cli.Name) -Force
Get-ChildItem -Path $cli.DirectoryName -Filter "*.dll" | ForEach-Object {
    Copy-Item $_.FullName (Join-Path $dest $_.Name) -Force
}
Write-Host "Copied $($cli.Name) (+ DLLs) -> $dest"

# --- 2. GGML model ---
if ($SkipModel) {
    Write-Host "`nSkipping model download (-SkipModel). The app will fetch one on first use." -ForegroundColor Yellow
} else {
    $modelName = "ggml-$Model.bin"
    $modelUrl  = "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/$modelName"
    $modelOut  = Join-Path $dest $modelName

    Write-Host "`nDownloading model $modelName (this can be large)…" -ForegroundColor Cyan
    Invoke-WebRequest -Uri $modelUrl -OutFile $modelOut
    Write-Host "Copied $modelName -> $dest"
}

Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
Write-Host "`nDone. Whisper transcription will now be available in the app." -ForegroundColor Green
