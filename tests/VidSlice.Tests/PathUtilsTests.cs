using System.IO;
using VidSlice.Services;

namespace VidSlice.Tests;

public class PathUtilsTests
{
    [Theory]
    [InlineData("normal name", "normal name")]
    [InlineData("a/b\\c:d*e?f", "a_b_c_d_e_f")]
    [InlineData("trailing...", "trailing")]
    [InlineData("  spaced  ", "spaced")]
    public void SanitizeFileName_RemovesInvalidAndTrims(string input, string expected)
    {
        Assert.Equal(expected, PathUtils.SanitizeFileName(input));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("...")]
    [InlineData(null)]
    public void SanitizeFileName_FallsBackWhenEmpty(string? input)
    {
        Assert.Equal("output", PathUtils.SanitizeFileName(input));
    }

    [Fact]
    public void GetUniquePath_ReturnsSameWhenNotExisting()
    {
        var path = Path.Combine(Path.GetTempPath(), "vidslice_unique_" + Guid.NewGuid().ToString("N") + ".mp4");
        Assert.Equal(path, PathUtils.GetUniquePath(path));
    }

    [Fact]
    public void GetUniquePath_SuffixesWhenExisting()
    {
        var path = Path.Combine(Path.GetTempPath(), "vidslice_collide_" + Guid.NewGuid().ToString("N") + ".mp4");
        File.WriteAllText(path, "x");
        try
        {
            var unique = PathUtils.GetUniquePath(path);
            Assert.NotEqual(path, unique);
            Assert.False(File.Exists(unique));
            Assert.Contains("(1)", Path.GetFileName(unique));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void GetAvailableFreeBytes_ReturnsNonNegativeForRealFolder()
    {
        Assert.True(PathUtils.GetAvailableFreeBytes(Path.GetTempPath()) >= 0);
    }

    [Fact]
    public void GetAvailableFreeBytes_ReturnsMinusOneForGarbage()
    {
        Assert.Equal(-1, PathUtils.GetAvailableFreeBytes("\0:::invalid"));
    }
}
