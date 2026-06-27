namespace VidSlice.Models;

/// <summary>
/// A plan for splitting a file into parts that each stay under a size limit,
/// expressed as a per-segment duration for ffmpeg's segment muxer.
/// </summary>
public sealed class SplitPlan
{
    /// <summary>Target seconds per segment passed to ffmpeg's -segment_time.</summary>
    public double SegmentSeconds { get; init; }

    /// <summary>Expected number of parts (estimate; actual may differ by keyframe placement).</summary>
    public int ExpectedParts { get; init; }

    /// <summary>The size ceiling each part must stay under, in bytes.</summary>
    public long MaxBytes { get; init; }

    /// <summary>True when the whole file already fits under the limit — no split needed.</summary>
    public bool SplitNeeded => ExpectedParts > 1;
}
