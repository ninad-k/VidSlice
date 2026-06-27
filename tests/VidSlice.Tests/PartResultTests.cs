using VidSlice.Models;

namespace VidSlice.Tests;

public class PartResultTests
{
    private const long Mb = 1024 * 1024;

    private static PartResult Make(long size, long max, double duration = 0) => new()
    {
        FileName = "part.mp4",
        FilePath = @"C:\part.mp4",
        SizeBytes = size,
        MaxBytes = max,
        DurationSeconds = duration,
    };

    [Fact]
    public void UnderLimit_TrueWhenBelowMax()
    {
        var p = Make(100 * Mb, 180 * Mb);
        Assert.True(p.UnderLimit);
        Assert.Equal("✓ OK", p.StatusText);
    }

    [Fact]
    public void UnderLimit_FalseWhenAboveMax()
    {
        var p = Make(200 * Mb, 180 * Mb);
        Assert.False(p.UnderLimit);
        Assert.Equal("✗ Over limit", p.StatusText);
    }

    [Fact]
    public void UnderLimit_FalseWhenExactlyAtMax()
    {
        // Strictly-under semantics: equal to the limit is treated as not under.
        Assert.False(Make(180 * Mb, 180 * Mb).UnderLimit);
    }

    [Fact]
    public void SizeMb_RoundedToTwoDecimals()
    {
        Assert.Equal(1.5, Make((long)(1.5 * Mb), 180 * Mb).SizeMb, precision: 2);
    }

    [Fact]
    public void DurationMinutes_RoundedToOneDecimal()
    {
        Assert.Equal(1.5, Make(Mb, 180 * Mb, duration: 90).DurationMinutes, precision: 1);
    }
}
