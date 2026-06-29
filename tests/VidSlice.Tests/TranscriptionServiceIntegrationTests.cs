using System.IO;
using VidSlice.Services;

namespace VidSlice.Tests;

/// <summary>
/// Opt-in, heavy tests that run real ffmpeg + whisper.cpp. They only activate when
/// VIDSLICE_TEST_WHISPER is set, a real input is given, and whisper is installed:
///   $env:VIDSLICE_TEST_WHISPER = "1"
///   $env:VIDSLICE_TEST_INPUT   = "C:\path\to\clip.mp4"
/// (whisper-cli.exe + a ggml-*.bin must be bundled in Resources\whisper or on PATH/%AppData%.)
/// </summary>
public class TranscriptionServiceIntegrationTests : IDisposable
{
    private readonly bool _enabled;
    private readonly string? _input;
    private readonly string _tempDir;

    public TranscriptionServiceIntegrationTests()
    {
        var optIn = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("VIDSLICE_TEST_WHISPER"));
        _input = Environment.GetEnvironmentVariable("VIDSLICE_TEST_INPUT");
        _enabled = optIn && !string.IsNullOrWhiteSpace(_input) && File.Exists(_input) && WhisperLocator.IsAvailable;
        _tempDir = Path.Combine(Path.GetTempPath(), "VidSliceWhisperIT_" + Guid.NewGuid().ToString("N"));
    }

    [SkippableFact]
    public async Task Transcribe_RealClip_WritesTxtAndSrt()
    {
        Skip.IfNot(_enabled,
            "Set VIDSLICE_TEST_WHISPER=1, VIDSLICE_TEST_INPUT to a real clip, and bundle whisper to run.");

        Directory.CreateDirectory(_tempDir);
        var service = new TranscriptionService();
        var options = new TranscriptionOptions
        {
            InputPath = _input!,
            OutputFolder = _tempDir,
            BaseName = "Clip",
        };

        var result = await service.TranscribeAsync(options);

        Assert.NotNull(result.TxtPath);
        Assert.True(File.Exists(result.TxtPath!));
        Assert.NotNull(result.SrtPath);
        Assert.True(File.Exists(result.SrtPath!));
        // A non-trivial clip should yield some text.
        Assert.False(string.IsNullOrWhiteSpace(await File.ReadAllTextAsync(result.TxtPath!)));
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); }
        catch { /* best effort */ }
    }
}
