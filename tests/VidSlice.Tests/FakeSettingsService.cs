using VidSlice.Models;
using VidSlice.Services;

namespace VidSlice.Tests;

/// <summary>In-memory ISettingsService for view-model tests.</summary>
internal sealed class FakeSettingsService : ISettingsService
{
    private AppSettings _settings;
    public int SaveCount { get; private set; }

    public FakeSettingsService(AppSettings? initial = null) => _settings = initial ?? new AppSettings();

    public string SettingsPath => "(in-memory)";
    public AppSettings Load() => _settings;
    public void Save(AppSettings settings)
    {
        _settings = settings;
        SaveCount++;
    }

    public AppSettings Current => _settings;
}
