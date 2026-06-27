using System.IO;

namespace VidSlice.Services;

/// <summary>
/// Resolves the ffmpeg/ffprobe executables. Search order:
/// 1. Bundled copies under the app's Resources\ffmpeg folder.
/// 2. The app base directory itself.
/// 3. The system PATH (and known winget install location as a fallback).
/// </summary>
public static class FfmpegLocator
{
    private static string? _ffmpeg;
    private static string? _ffprobe;

    public static string Ffmpeg => _ffmpeg ??= Resolve("ffmpeg.exe");
    public static string Ffprobe => _ffprobe ??= Resolve("ffprobe.exe");

    /// <summary>True if both tools were found.</summary>
    public static bool IsAvailable =>
        File.Exists(Ffmpeg) || PathHas("ffmpeg.exe");

    private static string Resolve(string exe)
    {
        var baseDir = AppContext.BaseDirectory;

        var candidates = new[]
        {
            Path.Combine(baseDir, "Resources", "ffmpeg", exe),
            Path.Combine(baseDir, exe),
        };

        foreach (var c in candidates)
            if (File.Exists(c))
                return c;

        // Known winget install location (Gyan.FFmpeg) as a convenience fallback.
        var winget = FindWingetFfmpeg(exe);
        if (winget is not null)
            return winget;

        // Fall back to the bare name and let the OS resolve via PATH.
        return exe;
    }

    private static string? FindWingetFfmpeg(string exe)
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var pkgRoot = Path.Combine(local, "Microsoft", "WinGet", "Packages");
        if (!Directory.Exists(pkgRoot)) return null;

        try
        {
            foreach (var dir in Directory.EnumerateDirectories(pkgRoot, "Gyan.FFmpeg*"))
            {
                var hit = Directory
                    .EnumerateFiles(dir, exe, SearchOption.AllDirectories)
                    .FirstOrDefault();
                if (hit is not null) return hit;
            }
        }
        catch
        {
            // Ignore enumeration errors and fall through to PATH resolution.
        }

        return null;
    }

    private static bool PathHas(string exe)
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                if (File.Exists(Path.Combine(dir, exe))) return true;
            }
            catch
            {
                // Skip malformed PATH entries.
            }
        }
        return false;
    }
}
