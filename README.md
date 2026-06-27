# VidSlice

A Windows desktop app (WPF, .NET 10) that **converts videos to MP4 and splits them into parts** — the manual ffmpeg chore, turned into a polished, batch-capable tool with a light/dark UI.

## Features

- **Batch queue** — add many files (drag-drop or picker); they process sequentially with per-file and overall progress.
- **Three split modes**
  - **By max size** — each part stays under a size limit (default **180 MB**), with a safety margin and an auto-retry if a part overshoots.
  - **By number of parts** — divide into N equal pieces.
  - **By duration** — fixed minutes per part.
- **Lossless when possible** — MP4-compatible sources (H.264/AAC, etc.) are stream-copied, so quality is identical and it's fast. Re-encode is opt-in.
- **Exact split** (optional) — re-encodes with forced keyframes so cuts are frame-accurate instead of snapping to the nearest keyframe.
- **Smart handling** — detects misnamed files (e.g. a video named `.m3u8`), avoids overwriting existing files (auto-suffix), checks free disk space first, and sanitizes output names.
- **Remembers your settings** — output folder, mode, sizes, theme — in `%AppData%\VidSlice\settings.json`.
- **Light / dark / system theme**, app icon, and a daily log file in `%AppData%\VidSlice\logs`.
- **Send to** integration — right-click a video in Explorer → Send to → VidSlice.

The original file is **never deleted** unless you untick "Keep original file".

## Requirements

- Windows 10/11
- [.NET 10 SDK](https://dotnet.microsoft.com/) (to build) or the published self-contained exe (to run)
- `ffmpeg` + `ffprobe` — auto-detected from a bundled copy, a winget install (`Gyan.FFmpeg`), or `PATH`.

## Run from source

```powershell
dotnet run --project src\VidSlice
```

## Test

```powershell
dotnet test                                            # fast unit suite (no ffmpeg)

# Opt-in end-to-end tests against real files:
$env:VIDSLICE_TEST_INPUT          = "C:\path\to\video.mp4"
$env:VIDSLICE_TEST_INPUT_MISNAMED = "C:\path\to\misnamed.m3u8"   # optional
dotnet test
```

## Bundle ffmpeg (optional, for a standalone exe)

```powershell
pwsh -File build\fetch-ffmpeg.ps1     # downloads the ~80 MB essentials build into Resources\ffmpeg
```

## Package a single-file exe

```powershell
pwsh -File build\publish.ps1          # -> build\publish\VidSlice.exe (self-contained win-x64)
pwsh -File build\install-sendto.ps1   # add Explorer "Send to" shortcut
```

## Releases (installer + portable zip)

GitHub Actions builds a Windows **installer** (Inno Setup) and a **portable zip**, both
self-contained (no .NET needed) with ffmpeg bundled, and attaches them to a GitHub Release.

Cut a release either way:

```powershell
# Option A — push a version tag (recommended)
git tag v1.0.0
git push origin v1.0.0

# Option B — run the "Release" workflow manually from the Actions tab and enter 1.0.0
```

The `.github/workflows/release.yml` job then produces, on the release:
- `VidSlice-Setup-1.0.0.exe` — installer (Start-menu shortcut, optional desktop icon, uninstaller)
- `VidSlice-1.0.0-portable-win-x64.zip` — unzip-and-run portable build

To compile the installer locally (requires [Inno Setup 6](https://jrsoftware.org/isdl.php)):

```powershell
pwsh -File build\fetch-ffmpeg.ps1
dotnet publish src\VidSlice -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o build\publish
& "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe" /DMyAppVersion=1.0.0 installer\VidSlice.iss
```

## Project layout

```
src\VidSlice\
  Models\        MediaInfo, SplitPlan, PartResult, SplitMode, AppSettings, BatchItem
  Services\      FfmpegService, FfmpegLocator, ProgressParser, SplitPlanner,
                 SettingsService, PathUtils, FileLogger
  ViewModels\    MainViewModel (batch queue, modes, settings)
  MainWindow.*   Fluent UI (queue, mode selector, options, per-file results)
  App.xaml.cs    Host/DI + logging + theme + command-line files
tests\VidSlice.Tests\   unit tests + opt-in integration tests
build\                  publish.ps1, fetch-ffmpeg.ps1, install-sendto.ps1
.github\workflows\      CI (build + unit tests on windows-latest)
```

## Tech

WPF · .NET 10 · [WPF-UI](https://github.com/lepoco/wpfui) · [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) · Microsoft.Extensions.Hosting (DI + logging) · ffmpeg/ffprobe as external processes.
