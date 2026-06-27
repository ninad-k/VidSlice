using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using VidSlice.Models;

namespace VidSlice.Services;

public interface ISettingsService
{
    AppSettings Load();
    void Save(AppSettings settings);
    string SettingsPath { get; }
}

/// <summary>Loads/saves <see cref="AppSettings"/> as JSON under %AppData%\VidSlice.</summary>
public sealed class SettingsService : ISettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public string SettingsPath { get; }

    public SettingsService(string? overrideDir = null)
    {
        var dir = overrideDir ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VidSlice");
        Directory.CreateDirectory(dir);
        SettingsPath = Path.Combine(dir, "settings.json");
    }

    public AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                var s = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
                if (s is not null) return Migrate(s);
            }
        }
        catch
        {
            // Corrupt/unreadable settings → fall back to defaults.
        }
        return new AppSettings();
    }

    /// <summary>
    /// Bring older settings up to the current schema. Missing JSON fields already
    /// take their property defaults; this hook is where future field renames or
    /// value remaps would live. For now it just stamps the current version.
    /// </summary>
    private static AppSettings Migrate(AppSettings s)
    {
        if (s.Version < AppSettings.CurrentVersion)
            s.Version = AppSettings.CurrentVersion;
        return s;
    }

    public void Save(AppSettings settings)
    {
        try
        {
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, JsonOptions));
        }
        catch
        {
            // Persisting preferences is best-effort; ignore IO failures.
        }
    }
}
