using VidSlice.Services;

namespace VidSlice.Tests;

public class WhisperLocatorTests
{
    [Fact]
    public void WhisperCli_ResolvesToAKnownExecutableName()
    {
        // Always returns at least a bare exe name ending in .exe.
        Assert.EndsWith(".exe", WhisperLocator.WhisperCli, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ModelsDir_PointsUnderVidSlice()
    {
        Assert.Contains("VidSlice", WhisperLocator.ModelsDir);
        Assert.EndsWith("models", WhisperLocator.ModelsDir);
    }

    [Fact]
    public void IsAvailable_ReturnsABoolean_WithoutThrowing()
    {
        // Smoke test: resolution + folder scans must not throw on any machine.
        var available = WhisperLocator.IsAvailable;
        Assert.True(available || !available);
    }
}
