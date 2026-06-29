namespace VidSlice.Services;

/// <summary>
/// Language hint passed to whisper. "auto" lets whisper detect the dominant
/// language (still transcribing interleaved phrases in their own script), which
/// is the right default for code-switched Hindi/Marathi/English audio.
/// </summary>
public static class TranscriptionLanguages
{
    public const string Auto = "auto";
    public const string Hindi = "hi";
    public const string Marathi = "mr";
    public const string English = "en";
}

/// <summary>Options controlling a single-file transcription run.</summary>
public sealed class TranscriptionOptions
{
    /// <summary>Source media to transcribe (audio is extracted from it with ffmpeg).</summary>
    public required string InputPath { get; init; }

    public required string OutputFolder { get; init; }

    /// <summary>Output file stem; whisper appends ".txt"/".srt".</summary>
    public required string BaseName { get; init; }

    /// <summary>"auto", "hi", "mr", "en", … (see <see cref="TranscriptionLanguages"/>).</summary>
    public string LanguageHint { get; init; } = TranscriptionLanguages.Auto;

    /// <summary>Translate the speech to English instead of transcribing in the native script.</summary>
    public bool Translate { get; init; }

    public bool WriteTxt { get; init; } = true;
    public bool WriteSrt { get; init; } = true;
}

/// <summary>Outcome of a transcription run.</summary>
public sealed class TranscriptionResult
{
    /// <summary>Path to the produced .txt, or null if not written.</summary>
    public string? TxtPath { get; init; }

    /// <summary>Path to the produced .srt, or null if not written.</summary>
    public string? SrtPath { get; init; }

    /// <summary>Language whisper auto-detected, if it reported one (e.g. "hi").</summary>
    public string? DetectedLanguage { get; init; }
}

public interface ITranscriptionService
{
    /// <summary>
    /// Extract audio from the input, run whisper, and write a .txt and/or .srt
    /// transcript next to the other outputs. Reports a 0..1 fraction and status text.
    /// </summary>
    Task<TranscriptionResult> TranscribeAsync(
        TranscriptionOptions options,
        IProgress<double>? progress = null, IProgress<string>? status = null, CancellationToken ct = default);
}
