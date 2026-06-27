using System.Globalization;

namespace VidSlice.Services;

/// <summary>
/// Parses ffmpeg's machine-readable "-progress pipe:1" output. Each progress
/// block is a series of key=value lines; we care about the processed timestamp
/// so we can express progress as a fraction of the known total duration.
/// </summary>
public static class ProgressParser
{
    /// <summary>
    /// Try to extract the processed time (in seconds) from a single
    /// "-progress" key=value line. Returns false for lines we don't use.
    /// </summary>
    public static bool TryParseProcessedSeconds(string line, out double seconds)
    {
        seconds = 0;
        if (string.IsNullOrEmpty(line)) return false;

        int eq = line.IndexOf('=');
        if (eq <= 0) return false;

        var key = line.AsSpan(0, eq).Trim();
        var value = line.AsSpan(eq + 1).Trim();

        // out_time_us / out_time_ms are reported in MICROSECONDS by ffmpeg
        // (the _ms suffix is historical and misleading).
        if (key.SequenceEqual("out_time_us") || key.SequenceEqual("out_time_ms"))
        {
            if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var us) && us >= 0)
            {
                seconds = us / 1_000_000.0;
                return true;
            }
            return false;
        }

        // out_time is HH:MM:SS.micro
        if (key.SequenceEqual("out_time"))
        {
            if (TimeSpan.TryParse(value.ToString(), CultureInfo.InvariantCulture, out var ts))
            {
                seconds = ts.TotalSeconds;
                return true;
            }
        }

        return false;
    }

    /// <summary>Convert processed seconds + total duration into a 0..1 fraction.</summary>
    public static double Fraction(double processedSeconds, double totalSeconds)
    {
        if (totalSeconds <= 0) return 0;
        return Math.Clamp(processedSeconds / totalSeconds, 0, 1);
    }
}
