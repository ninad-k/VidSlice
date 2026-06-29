using VidSlice.Services;

namespace VidSlice.Tests;

/// <summary>
/// In-memory ITranscriptionService for view-model tests — no real whisper/ffmpeg.
/// Records the last call and returns a canned result (or throws via a factory).
/// </summary>
internal sealed class FakeTranscriptionService : ITranscriptionService
{
    public TranscriptionResult ResultToReturn { get; set; } =
        new() { TxtPath = "out.txt", SrtPath = "out.srt", DetectedLanguage = "hi" };

    public int Calls { get; private set; }
    public TranscriptionOptions? LastOptions { get; private set; }
    public Func<TranscriptionOptions, TranscriptionResult>? Factory { get; set; }

    public Task<TranscriptionResult> TranscribeAsync(
        TranscriptionOptions options,
        IProgress<double>? progress = null, IProgress<string>? status = null, CancellationToken ct = default)
    {
        Calls++;
        LastOptions = options;
        status?.Report("Transcribing…");
        progress?.Report(1);
        var result = Factory?.Invoke(options) ?? ResultToReturn;
        return Task.FromResult(result);
    }
}
