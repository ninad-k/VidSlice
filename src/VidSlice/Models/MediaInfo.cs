namespace VidSlice.Models;

/// <summary>
/// Result of probing a media file with ffprobe. Describes the container and
/// streams as they actually are on disk (not as the file extension claims).
/// </summary>
public sealed class MediaInfo
{
    public required string FilePath { get; init; }

    /// <summary>ffprobe format_name, e.g. "mov,mp4,m4a,3gp,3g2,mj2".</summary>
    public string FormatName { get; init; } = "";

    /// <summary>Total duration in seconds.</summary>
    public double DurationSeconds { get; init; }

    /// <summary>File size in bytes (from the OS, authoritative).</summary>
    public long SizeBytes { get; init; }

    /// <summary>Overall bit rate in bits/second. Falls back to size/duration when unknown.</summary>
    public long BitRate { get; init; }

    public string? VideoCodec { get; init; }
    public string? AudioCodec { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }

    public bool HasVideo => !string.IsNullOrEmpty(VideoCodec);
    public bool HasAudio => !string.IsNullOrEmpty(AudioCodec);

    /// <summary>
    /// True when the existing streams can be placed into an MP4 container with a
    /// plain stream copy (no re-encode) — i.e. identical quality, fast.
    /// </summary>
    public bool CanStreamCopyToMp4
    {
        get
        {
            var v = (VideoCodec ?? "").ToLowerInvariant();
            var a = (AudioCodec ?? "").ToLowerInvariant();
            bool videoOk = !HasVideo || v is "h264" or "hevc" or "h265" or "mpeg4" or "av1";
            bool audioOk = !HasAudio || a is "aac" or "mp3" or "ac3" or "eac3";
            return videoOk && audioOk;
        }
    }

    /// <summary>Effective bit rate, never zero (so size math is safe).</summary>
    public long EffectiveBitRate =>
        BitRate > 0
            ? BitRate
            : DurationSeconds > 0
                ? (long)(SizeBytes * 8 / DurationSeconds)
                : 0;

    public string ResolutionText => Width > 0 && Height > 0 ? $"{Width}x{Height}" : "—";

    public string DurationText
    {
        get
        {
            var t = TimeSpan.FromSeconds(DurationSeconds);
            return t.TotalHours >= 1
                ? $"{(int)t.TotalHours}h {t.Minutes}m {t.Seconds}s"
                : $"{t.Minutes}m {t.Seconds}s";
        }
    }

    public double SizeMb => SizeBytes / 1024d / 1024d;
}
