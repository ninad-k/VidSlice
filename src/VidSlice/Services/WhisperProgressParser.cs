using System.Globalization;
using System.Text.RegularExpressions;

namespace VidSlice.Services;

/// <summary>
/// Parses whisper.cpp console output. We care about two things: the periodic
/// "progress = NN%" lines (for the progress bar) and the one-off
/// "auto-detected language: xx" line. Kept separate from the service so it's
/// unit-testable without invoking a binary.
/// </summary>
public static partial class WhisperProgressParser
{
    [GeneratedRegex(@"progress\s*=\s*(\d{1,3})\s*%", RegexOptions.IgnoreCase)]
    private static partial Regex ProgressRegex();

    [GeneratedRegex(@"auto-detected language:\s*([a-zA-Z]{2,3})", RegexOptions.IgnoreCase)]
    private static partial Regex LanguageRegex();

    /// <summary>
    /// Try to read a "progress = NN%" line as a 0..1 fraction. Returns false for
    /// lines that don't carry progress.
    /// </summary>
    public static bool TryParseProgressFraction(string line, out double fraction)
    {
        fraction = 0;
        if (string.IsNullOrEmpty(line)) return false;

        var m = ProgressRegex().Match(line);
        if (!m.Success) return false;
        if (!int.TryParse(m.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var pct))
            return false;

        fraction = Math.Clamp(pct / 100.0, 0, 1);
        return true;
    }

    /// <summary>Try to read the auto-detected language code (e.g. "hi") from a line.</summary>
    public static bool TryParseDetectedLanguage(string line, out string language)
    {
        language = "";
        if (string.IsNullOrEmpty(line)) return false;

        var m = LanguageRegex().Match(line);
        if (!m.Success) return false;

        language = m.Groups[1].Value.ToLowerInvariant();
        return true;
    }
}
