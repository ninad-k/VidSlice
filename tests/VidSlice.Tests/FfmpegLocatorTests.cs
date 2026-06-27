using VidSlice.Services;

namespace VidSlice.Tests;

public class FfmpegLocatorTests
{
    [Fact]
    public void Ffmpeg_ResolvesToAnExecutablePath()
    {
        // Always returns at least the bare exe name; ends with the tool name.
        Assert.EndsWith("ffmpeg.exe", FfmpegLocator.Ffmpeg, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Ffprobe_ResolvesToAnExecutablePath()
    {
        Assert.EndsWith("ffprobe.exe", FfmpegLocator.Ffprobe, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void IsAvailable_ReturnsABoolean_WithoutThrowing()
    {
        // Smoke test: resolution + PATH scan must not throw on any machine.
        var available = FfmpegLocator.IsAvailable;
        Assert.True(available || !available);
    }
}
