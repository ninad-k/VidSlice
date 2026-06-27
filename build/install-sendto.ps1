# Adds a "VidSlice" shortcut to the Windows "Send to" menu so you can right-click
# a video in Explorer -> Send to -> VidSlice and have it open pre-loaded.
#
# Usage:
#   pwsh -File build\install-sendto.ps1                       # uses build\publish\VidSlice.exe
#   pwsh -File build\install-sendto.ps1 -ExePath C:\path\VidSlice.exe
#   pwsh -File build\install-sendto.ps1 -Uninstall

param(
    [string]$ExePath,
    [switch]$Uninstall
)

$ErrorActionPreference = "Stop"

$sendTo = [Environment]::GetFolderPath('SendTo')
$linkPath = Join-Path $sendTo "VidSlice.lnk"

if ($Uninstall) {
    if (Test-Path $linkPath) { Remove-Item $linkPath -Force; Write-Host "Removed Send To shortcut." }
    else { Write-Host "No Send To shortcut found." }
    return
}

if (-not $ExePath) {
    $root = Split-Path -Parent $PSScriptRoot
    $ExePath = Join-Path $root "build\publish\VidSlice.exe"
}

if (-not (Test-Path $ExePath)) {
    throw "VidSlice.exe not found at '$ExePath'. Build it first (build\publish.ps1) or pass -ExePath."
}

$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($linkPath)
$shortcut.TargetPath = $ExePath
$shortcut.WorkingDirectory = Split-Path -Parent $ExePath
$shortcut.IconLocation = $ExePath
$shortcut.Description = "Convert & split videos with VidSlice"
$shortcut.Save()

Write-Host "Installed Send To shortcut -> $linkPath" -ForegroundColor Green
Write-Host "Right-click any video in Explorer -> Send to -> VidSlice."
