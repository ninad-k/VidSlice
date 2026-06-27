using VidSlice.Services;

namespace VidSlice.Tests;

public class ProgressParserTests
{
    [Fact]
    public void OutTimeUs_ParsedAsMicroseconds()
    {
        Assert.True(ProgressParser.TryParseProcessedSeconds("out_time_us=2000000", out var s));
        Assert.Equal(2.0, s, precision: 6);
    }

    [Fact]
    public void OutTimeMs_IsAlsoMicroseconds_NotMilliseconds()
    {
        // ffmpeg's "_ms" suffix is historical and actually reports microseconds.
        Assert.True(ProgressParser.TryParseProcessedSeconds("out_time_ms=500000", out var s));
        Assert.Equal(0.5, s, precision: 6);
    }

    [Fact]
    public void OutTime_HmsFormat_Parsed()
    {
        Assert.True(ProgressParser.TryParseProcessedSeconds("out_time=00:00:10.000000", out var s));
        Assert.Equal(10.0, s, precision: 3);
    }

    [Fact]
    public void OutTime_WithMinutesAndHours_Parsed()
    {
        Assert.True(ProgressParser.TryParseProcessedSeconds("out_time=01:02:03.500000", out var s));
        Assert.Equal(3723.5, s, precision: 3);
    }

    [Theory]
    [InlineData("frame=123")]
    [InlineData("bitrate=  500.0kbits/s")]
    [InlineData("progress=continue")]
    [InlineData("")]
    [InlineData("no_equals_sign")]
    [InlineData("=leadingequals")]
    public void NonTimeOrMalformedLines_ReturnFalse(string line)
    {
        Assert.False(ProgressParser.TryParseProcessedSeconds(line, out var s));
        Assert.Equal(0, s);
    }

    [Theory]
    [InlineData("out_time_us=-5")]
    [InlineData("out_time_us=abc")]
    [InlineData("out_time=notatimespan")]
    public void InvalidTimeValues_ReturnFalse(string line)
    {
        Assert.False(ProgressParser.TryParseProcessedSeconds(line, out _));
    }

    [Fact]
    public void WhitespaceAroundKeyValue_StillParses()
    {
        Assert.True(ProgressParser.TryParseProcessedSeconds("  out_time_us = 1000000 ", out var s));
        Assert.Equal(1.0, s, precision: 6);
    }

    [Theory]
    [InlineData(5, 10, 0.5)]
    [InlineData(0, 10, 0.0)]
    [InlineData(10, 10, 1.0)]
    [InlineData(20, 10, 1.0)]   // clamped to 1
    [InlineData(5, 0, 0.0)]     // zero total guarded
    [InlineData(-5, 10, 0.0)]   // clamped to 0
    public void Fraction_ClampsToZeroOne(double processed, double total, double expected)
    {
        Assert.Equal(expected, ProgressParser.Fraction(processed, total), precision: 6);
    }
}
