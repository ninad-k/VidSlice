using System.IO;

namespace VidSlice.Services;

/// <summary>
/// Resolves the whisper.cpp CLI and a GGML speech model. Search order mirrors
/// <see cref="FfmpegLocator"/>:
/// 1. Bundled copies under the app's Resources\whisper folder.
/// 2. The app base directory itself.
/// 3. A user model folder under %AppData%\VidSlice\models (first-run downloads).
/// 4. The system PATH (CLI only).
///
/// The CLI is shipped as "whisper-cli.exe" in current whisper.cpp releases; the
/// older "main.exe" name is accepted as a fallback. A model is any "ggml-*.bin".
/// </summary>
public static class WhisperLocator
{
    private static string? _cli;
    private static string? _model;

    private static readonly string[] CliNames = ["whisper-cli.exe", "main.exe"];

    /// <summary>User model folder, also where first-run downloads land.</summary>
    public static string ModelsDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VidSlice", "models");

    /// <summary>Path to the whisper CLI (falls back to the bare name for PATH resolution).</summary>
    public static string WhisperCli => _cli ??= ResolveCli();

    /// <summary>Path to a GGML model file, or null if none was found.</summary>
    public static string? ModelPath => _model ??= ResolveModel();

    /// <summary>True only when both the CLI and a model are present — transcription needs both.</summary>
    public static bool IsAvailable => CliExists() && ModelPath is not null;

    private static string ResolveCli()
    {
        var baseDir = AppContext.BaseDirectory;
        foreach (var name in CliNames)
        {
            var candidates = new[]
            {
                Path.Combine(baseDir, "Resources", "whisper", name),
                Path.Combine(baseDir, name),
            };
            foreach (var c in candidates)
                if (File.Exists(c))
                    return c;
        }

        // Fall back to the canonical name and let the OS resolve via PATH.
        return CliNames[0];
    }

    private static string? ResolveModel()
    {
        var baseDir = AppContext.BaseDirectory;
        var folders = new[]
        {
            Path.Combine(baseDir, "Resources", "whisper"),
            baseDir,
            ModelsDir,
        };

        foreach (var folder in folders)
        {
            var hit = FirstGgmlModel(folder);
            if (hit is not null) return hit;
        }
        return null;
    }

    private static string? FirstGgmlModel(string folder)
    {
        try
        {
            if (!Directory.Exists(folder)) return null;
            return Directory.EnumerateFiles(folder, "ggml-*.bin")
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
        }
        catch
        {
            // Ignore enumeration errors (permissions, malformed path).
            return null;
        }
    }

    private static bool CliExists()
    {
        if (File.Exists(WhisperCli)) return true;
        return CliNames.Any(PathHas);
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
