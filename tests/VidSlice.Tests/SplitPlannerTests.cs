using VidSlice.Models;
using VidSlice.Services;

namespace VidSlice.Tests;

public class SplitPlannerTests
{
    private const long Mb = 1024 * 1024;

    [Fact]
    public void FileUnderLimit_ProducesSinglePart_NoSplit()
    {
        var plan = SplitPlanner.Plan(totalBytes: 100 * Mb, durationSeconds: 600, maxBytes: 180 * Mb);

        Assert.Equal(1, plan.ExpectedParts);
        Assert.False(plan.SplitNeeded);
    }

    [Fact]
    public void FileExactlyAtLimit_NoSplit()
    {
        var plan = SplitPlanner.Plan(totalBytes: 180 * Mb, durationSeconds: 600, maxBytes: 180 * Mb);

        Assert.Equal(1, plan.ExpectedParts);
        Assert.False(plan.SplitNeeded);
    }

    [Fact]
    public void LargeFile_SplitsIntoMultipleParts()
    {
        // ~568 MB over 9566 s with a 180 MB limit — the real case from the manual run.
        var plan = SplitPlanner.Plan(totalBytes: 568L * Mb, durationSeconds: 9566, maxBytes: 180 * Mb);

        Assert.True(plan.SplitNeeded);
        Assert.True(plan.ExpectedParts >= 4, $"expected >= 4 parts, got {plan.ExpectedParts}");
        Assert.True(plan.SegmentSeconds > 0);
    }

    [Fact]
    public void EachExpectedPart_StaysUnderLimit_GivenConstantBitrate()
    {
        long total = 568L * Mb;
        double duration = 9566;
        long max = 180 * Mb;

        var plan = SplitPlanner.Plan(total, duration, max);

        // With constant bit rate, a segment of SegmentSeconds yields this many bytes.
        double bytesPerSecond = total / duration;
        double partBytes = plan.SegmentSeconds * bytesPerSecond;

        Assert.True(partBytes < max, $"part {partBytes:N0} should be under {max:N0}");
    }

    [Fact]
    public void Shrink_ProducesSmallerSegments_AndMoreParts()
    {
        var plan = SplitPlanner.Plan(568L * Mb, 9566, 180 * Mb);
        var shrunk = SplitPlanner.Shrink(plan, 9566);

        Assert.True(shrunk.SegmentSeconds < plan.SegmentSeconds);
        Assert.True(shrunk.ExpectedParts >= plan.ExpectedParts);
        Assert.Equal(plan.MaxBytes, shrunk.MaxBytes);
    }

    [Fact]
    public void ZeroDuration_DoesNotThrow_AndReturnsSinglePart()
    {
        var plan = SplitPlanner.Plan(500L * Mb, durationSeconds: 0, maxBytes: 180 * Mb);

        Assert.Equal(1, plan.ExpectedParts);
        Assert.False(plan.SplitNeeded);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void NonPositiveMaxBytes_Throws(long max)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SplitPlanner.Plan(100 * Mb, 600, max));
    }

    [Fact]
    public void SafetyMargin_OutOfRange_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SplitPlanner.Plan(100 * Mb, 600, 50 * Mb, safetyMargin: 1.5));
    }

    [Fact]
    public void SafetyMargin_NonPositive_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SplitPlanner.Plan(100 * Mb, 600, 50 * Mb, safetyMargin: 0));
    }

    [Fact]
    public void HugeFileVsTinyLimit_SegmentNeverBelowOneSecond()
    {
        // 10 GB over 60 s with a 1 MB limit would mathematically want sub-second
        // segments; the planner floors at 1 s to stay usable.
        var plan = SplitPlanner.Plan(10L * 1024 * Mb, durationSeconds: 60, maxBytes: 1 * Mb);

        Assert.True(plan.SegmentSeconds >= 1);
        Assert.True(plan.SplitNeeded);
    }

    [Fact]
    public void SmallerMargin_ProducesShorterSegments()
    {
        var loose = SplitPlanner.Plan(568L * Mb, 9566, 180 * Mb, safetyMargin: 0.95);
        var tight = SplitPlanner.Plan(568L * Mb, 9566, 180 * Mb, safetyMargin: 0.50);

        Assert.True(tight.SegmentSeconds < loose.SegmentSeconds);
        Assert.True(tight.ExpectedParts >= loose.ExpectedParts);
    }

    [Fact]
    public void Shrink_FloorsAtOneSecond()
    {
        var plan = new SplitPlan { SegmentSeconds = 1, ExpectedParts = 5, MaxBytes = 180 * Mb };
        var shrunk = SplitPlanner.Shrink(plan, durationSeconds: 100);

        Assert.True(shrunk.SegmentSeconds >= 1);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(1.5)]
    public void Shrink_InvalidFactor_Throws(double factor)
    {
        var plan = new SplitPlan { SegmentSeconds = 100, ExpectedParts = 5, MaxBytes = 180 * Mb };
        Assert.Throws<ArgumentOutOfRangeException>(() => { _ = SplitPlanner.Shrink(plan, 9566, shrinkFactor: factor); });
    }

    [Fact]
    public void SegmentSeconds_IsWholeNumber()
    {
        // ffmpeg's -segment_time is happiest with an integer-ish value; we floor it.
        var plan = SplitPlanner.Plan(568L * Mb, 9566, 180 * Mb);
        Assert.Equal(Math.Floor(plan.SegmentSeconds), plan.SegmentSeconds);
    }

    // ---- ByParts ----

    [Fact]
    public void PlanByParts_DividesDurationEvenly()
    {
        var plan = SplitPlanner.PlanByParts(durationSeconds: 1000, partCount: 4);
        Assert.Equal(250, plan.SegmentSeconds);
        Assert.Equal(4, plan.ExpectedParts);
        Assert.Equal(long.MaxValue, plan.MaxBytes);
    }

    [Fact]
    public void PlanByParts_OnePart_NoSplit()
    {
        var plan = SplitPlanner.PlanByParts(1000, 1);
        Assert.False(plan.SplitNeeded);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void PlanByParts_InvalidCount_Throws(int count)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SplitPlanner.PlanByParts(1000, count));
    }

    // ---- ByDuration ----

    [Fact]
    public void PlanByDuration_UsesGivenSegmentLength()
    {
        var plan = SplitPlanner.PlanByDuration(durationSeconds: 1000, segmentSeconds: 300);
        Assert.Equal(300, plan.SegmentSeconds);
        Assert.Equal(4, plan.ExpectedParts); // ceil(1000/300)
        Assert.Equal(long.MaxValue, plan.MaxBytes);
    }

    [Fact]
    public void PlanByDuration_SegmentLongerThanFile_NoSplit()
    {
        var plan = SplitPlanner.PlanByDuration(600, 1800);
        Assert.False(plan.SplitNeeded);
    }

    [Fact]
    public void PlanByDuration_SegmentBelowOneSecond_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SplitPlanner.PlanByDuration(1000, 0.5));
    }
}
