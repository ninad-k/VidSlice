using VidSlice.Models;

namespace VidSlice.Services;

/// <summary>Options controlling a convert + split run for a single file.</summary>
public sealed class ConvertSplitOptions
{
    public required string InputPath { get; init; }
    public required string OutputFolder { get; init; }
    public required string BaseName { get; init; }

    public SplitMode Mode { get; init; } = SplitMode.BySize;

    /// <summary>Size ceiling per part (BySize mode).</summary>
    public long MaxBytes { get; init; }

    /// <summary>Number of equal parts (ByParts mode).</summary>
    public int PartCount { get; init; } = 4;

    /// <summary>Seconds per part (ByDuration mode).</summary>
    public double SegmentSeconds { get; init; } = 1800;

    public bool ConvertToMp4 { get; init; } = true;

    /// <summary>Allow re-encoding when the source can't be stream-copied to MP4.</summary>
    public bool AllowReencode { get; init; }

    /// <summary>
    /// Re-encode at segment boundaries so cuts are frame-accurate instead of
    /// snapping to the nearest existing keyframe (slower).
    /// </summary>
    public bool ExactSplit { get; init; }
}

/// <summary>Outcome of a full convert + split + verify run.</summary>
public sealed class RunResult
{
    public string? Mp4Path { get; init; }
    public IReadOnlyList<PartResult> Parts { get; init; } = [];
    public bool AllPartsUnderLimit { get; init; }
    public bool WasReencoded { get; init; }
    public int RetryCount { get; init; }
}

public interface IFfmpegService
{
    Task<MediaInfo> ProbeAsync(string path, CancellationToken ct = default);

    /// <summary>Remux/convert to MP4. Returns the output path actually written.</summary>
    Task<string> ConvertAsync(
        string inputPath, string outputMp4Path, MediaInfo info, bool allowReencode,
        IProgress<double>? progress = null, CancellationToken ct = default);

    /// <summary>Split a file into parts per the plan. Returns the created file paths in order.</summary>
    Task<IReadOnlyList<string>> SplitAsync(
        string inputPath, SplitPlan plan, string outputFolder, string baseName, string extension,
        double totalDurationSeconds, bool exactSplit = false,
        IProgress<double>? progress = null, CancellationToken ct = default);

    /// <summary>Probe each part and build verification results.</summary>
    Task<IReadOnlyList<PartResult>> VerifyAsync(
        IEnumerable<string> partPaths, long maxBytes, CancellationToken ct = default);

    /// <summary>End-to-end: convert (optional) → plan → split → verify, with one auto-retry on overshoot.</summary>
    Task<RunResult> RunAsync(
        ConvertSplitOptions options, MediaInfo info,
        IProgress<double>? progress = null, IProgress<string>? status = null, CancellationToken ct = default);
}
