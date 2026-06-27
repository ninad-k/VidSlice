using VidSlice.Models;

namespace VidSlice.Services;

/// <summary>
/// Pure logic that turns a target part size into a per-segment duration for
/// ffmpeg. Kept free of I/O so it can be unit-tested in isolation.
/// </summary>
public static class SplitPlanner
{
    /// <summary>
    /// Safety margin applied to the theoretical max segment length. Stream-copy
    /// segments can only split on keyframes and bit rate varies (VBR), so the
    /// real part may overshoot the average — aiming below the limit absorbs that.
    /// </summary>
    public const double DefaultSafetyMargin = 0.85;

    /// <summary>
    /// Build a split plan for a file of the given size/duration so that every
    /// resulting part stays under <paramref name="maxBytes"/>.
    /// </summary>
    public static SplitPlan Plan(long totalBytes, double durationSeconds, long maxBytes, double safetyMargin = DefaultSafetyMargin)
    {
        if (maxBytes <= 0) throw new ArgumentOutOfRangeException(nameof(maxBytes), "Max size must be positive.");
        if (safetyMargin is <= 0 or > 1) throw new ArgumentOutOfRangeException(nameof(safetyMargin), "Margin must be in (0, 1].");

        // Already fits, or we have no duration to split on → single part, no split.
        if (totalBytes <= maxBytes || durationSeconds <= 0)
        {
            return new SplitPlan
            {
                SegmentSeconds = durationSeconds > 0 ? durationSeconds : 0,
                ExpectedParts = 1,
                MaxBytes = maxBytes,
            };
        }

        double effectiveLimit = maxBytes * safetyMargin;
        double bytesPerSecond = totalBytes / durationSeconds;

        // How many seconds of content fit under the (margin-adjusted) limit.
        double segmentSeconds = effectiveLimit / bytesPerSecond;

        // Guard against absurdly small segments if the file is enormous vs the limit.
        if (segmentSeconds < 1) segmentSeconds = 1;

        int expectedParts = (int)Math.Ceiling(durationSeconds / segmentSeconds);

        return new SplitPlan
        {
            SegmentSeconds = Math.Floor(segmentSeconds),
            ExpectedParts = expectedParts,
            MaxBytes = maxBytes,
        };
    }

    /// <summary>
    /// Plan a split into a fixed number of equal-duration parts. Size is not
    /// constrained, so the verification ceiling is effectively unlimited.
    /// </summary>
    public static SplitPlan PlanByParts(double durationSeconds, int partCount)
    {
        if (partCount < 1) throw new ArgumentOutOfRangeException(nameof(partCount), "Part count must be >= 1.");

        if (partCount == 1 || durationSeconds <= 0)
            return new SplitPlan { SegmentSeconds = durationSeconds, ExpectedParts = 1, MaxBytes = long.MaxValue };

        double segmentSeconds = Math.Max(1, Math.Floor(durationSeconds / partCount));
        return new SplitPlan
        {
            SegmentSeconds = segmentSeconds,
            ExpectedParts = (int)Math.Ceiling(durationSeconds / segmentSeconds),
            MaxBytes = long.MaxValue,
        };
    }

    /// <summary>
    /// Plan a split into fixed-duration parts. Size is not constrained.
    /// </summary>
    public static SplitPlan PlanByDuration(double durationSeconds, double segmentSeconds)
    {
        if (segmentSeconds < 1) throw new ArgumentOutOfRangeException(nameof(segmentSeconds), "Segment length must be >= 1 second.");

        if (durationSeconds <= 0 || segmentSeconds >= durationSeconds)
            return new SplitPlan { SegmentSeconds = durationSeconds, ExpectedParts = 1, MaxBytes = long.MaxValue };

        return new SplitPlan
        {
            SegmentSeconds = Math.Floor(segmentSeconds),
            ExpectedParts = (int)Math.Ceiling(durationSeconds / Math.Floor(segmentSeconds)),
            MaxBytes = long.MaxValue,
        };
    }

    /// <summary>
    /// Produce a tighter plan after a part overshot the limit. Shrinks the
    /// segment length by <paramref name="shrinkFactor"/> so the retry lands under.
    /// </summary>
    public static SplitPlan Shrink(SplitPlan previous, double durationSeconds, double shrinkFactor = 0.8)
    {
        if (shrinkFactor is <= 0 or >= 1) throw new ArgumentOutOfRangeException(nameof(shrinkFactor));

        double segmentSeconds = Math.Max(1, Math.Floor(previous.SegmentSeconds * shrinkFactor));
        int expectedParts = durationSeconds > 0
            ? (int)Math.Ceiling(durationSeconds / segmentSeconds)
            : previous.ExpectedParts + 1;

        return new SplitPlan
        {
            SegmentSeconds = segmentSeconds,
            ExpectedParts = expectedParts,
            MaxBytes = previous.MaxBytes,
        };
    }
}
