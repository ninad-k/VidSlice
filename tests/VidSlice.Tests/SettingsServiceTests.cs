using System.IO;
using VidSlice.Models;
using VidSlice.Services;

namespace VidSlice.Tests;

public class SettingsServiceTests : IDisposable
{
    private readonly string _dir;

    public SettingsServiceTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "vidslice_settings_" + Guid.NewGuid().ToString("N"));
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); } catch { }
    }

    [Fact]
    public void Load_ReturnsDefaultsWhenNoFile()
    {
        var svc = new SettingsService(_dir);
        var s = svc.Load();
        Assert.Equal(180, s.MaxSizeMb);
        Assert.Equal(SplitMode.BySize, s.SplitMode);
        Assert.True(s.KeepOriginal);
    }

    [Fact]
    public void Save_ThenLoad_RoundTrips()
    {
        var svc = new SettingsService(_dir);
        svc.Save(new AppSettings
        {
            MaxSizeMb = 99,
            PartCount = 6,
            SplitMode = SplitMode.ByParts,
            Theme = "Light",
            KeepOriginal = false,
            ExactSplit = true,
        });

        var loaded = new SettingsService(_dir).Load();
        Assert.Equal(99, loaded.MaxSizeMb);
        Assert.Equal(6, loaded.PartCount);
        Assert.Equal(SplitMode.ByParts, loaded.SplitMode);
        Assert.Equal("Light", loaded.Theme);
        Assert.False(loaded.KeepOriginal);
        Assert.True(loaded.ExactSplit);
    }

    [Fact]
    public void Load_ReturnsDefaults_WhenFileCorrupt()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "settings.json"), "{ this is not valid json");

        var loaded = new SettingsService(_dir).Load();
        Assert.Equal(180, loaded.MaxSizeMb); // fell back to defaults
    }

    [Fact]
    public void SettingsPath_IsUnderProvidedDirectory()
    {
        var svc = new SettingsService(_dir);
        Assert.StartsWith(_dir, svc.SettingsPath);
        Assert.EndsWith("settings.json", svc.SettingsPath);
    }

    [Fact]
    public void Load_MigratesOlderVersionToCurrent()
    {
        Directory.CreateDirectory(_dir);
        // An older file with Version 0 and a missing field.
        File.WriteAllText(Path.Combine(_dir, "settings.json"),
            "{ \"Version\": 0, \"MaxSizeMb\": 64 }");

        var loaded = new SettingsService(_dir).Load();

        Assert.Equal(AppSettings.CurrentVersion, loaded.Version);
        Assert.Equal(64, loaded.MaxSizeMb);   // preserved
        Assert.True(loaded.KeepOriginal);     // missing field took its default
    }
}
