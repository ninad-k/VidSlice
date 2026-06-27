using System.IO;
using Microsoft.Extensions.Logging;
using VidSlice.Services;

namespace VidSlice.Tests;

public class FileLoggerTests : IDisposable
{
    private readonly string _dir;

    public FileLoggerTests()
        => _dir = Path.Combine(Path.GetTempPath(), "vidslice_logs_" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); } catch { }
    }

    [Fact]
    public void Writes_LogLine_ToDatedFile()
    {
        using var provider = new FileLoggerProvider(_dir);
        var logger = provider.CreateLogger("Test");
        logger.LogInformation("hello world");

        var files = Directory.GetFiles(_dir, "vidslice-*.log");
        Assert.Single(files);
        Assert.Contains("hello world", File.ReadAllText(files[0]));
    }

    [Fact]
    public void Prunes_LogFilesOlderThanRetention()
    {
        Directory.CreateDirectory(_dir);
        var oldFile = Path.Combine(_dir, "vidslice-2000-01-01.log");
        File.WriteAllText(oldFile, "ancient");
        File.SetLastWriteTime(oldFile, new DateTime(2000, 1, 1));

        var recentFile = Path.Combine(_dir, "vidslice-recent.log");
        File.WriteAllText(recentFile, "fresh");

        // Construction triggers pruning.
        using var provider = new FileLoggerProvider(_dir);

        Assert.False(File.Exists(oldFile), "old log should be pruned");
        Assert.True(File.Exists(recentFile), "recent log should remain");
    }

    [Fact]
    public void RespectsLogLevel_SkipsDebug()
    {
        using var provider = new FileLoggerProvider(_dir);
        var logger = provider.CreateLogger("Test");
        logger.LogDebug("should not appear");

        var files = Directory.GetFiles(_dir, "vidslice-*.log");
        // No Information-or-above entry written, so no file content.
        Assert.True(files.Length == 0 || !File.ReadAllText(files[0]).Contains("should not appear"));
    }
}
