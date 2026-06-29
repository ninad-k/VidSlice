namespace VidSlice.Models;

/// <summary>
/// User preferences persisted between runs to
/// %AppData%\VidSlice\settings.json.
/// </summary>
public sealed class AppSettings
{
    /// <summary>Current settings schema version, for future migrations.</summary>
    public const int CurrentVersion = 2;

    /// <summary>Schema version of this instance (as loaded from disk).</summary>
    public int Version { get; set; } = CurrentVersion;

    public string? OutputFolder { get; set; }
    public double MaxSizeMb { get; set; } = 180;
    public int PartCount { get; set; } = 4;
    public double SegmentMinutes { get; set; } = 30;
    public SplitMode SplitMode { get; set; } = SplitMode.BySize;
    public bool ConvertToMp4 { get; set; } = true;
    public bool KeepOriginal { get; set; } = true;
    public bool AllowReencode { get; set; }
    public bool ExactSplit { get; set; }

    // ---- Transcription ----

    /// <summary>Also produce a .txt + .srt transcript via whisper.cpp.</summary>
    public bool Transcribe { get; set; }

    /// <summary>Language hint: "auto", "hi", "mr", "en".</summary>
    public string TranscribeLanguage { get; set; } = "auto";

    /// <summary>Translate the transcript to English instead of the native script.</summary>
    public bool TranslateToEnglish { get; set; }

    /// <summary>"Dark", "Light", or "System".</summary>
    public string Theme { get; set; } = "System";
}
