Bundled ffmpeg location
=======================

Place ffmpeg.exe and ffprobe.exe in this folder to ship them with the app.

At runtime VidSlice resolves the tools in this order:
  1. This folder  (Resources\ffmpeg next to the app .exe)
  2. The app's own folder
  3. A winget install of Gyan.FFmpeg (auto-detected)
  4. The system PATH

Recommended build: the smaller "essentials" static build from
https://www.gyan.dev/ffmpeg/builds/  (ffmpeg-release-essentials) — ffmpeg.exe
is ~80 MB vs ~217 MB for the "full" build. Copy ffmpeg.exe and ffprobe.exe
from its bin\ folder into this directory.

If this folder is empty, the app still works as long as ffmpeg is installed
elsewhere (winget or PATH).
