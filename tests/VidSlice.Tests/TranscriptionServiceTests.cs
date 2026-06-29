using VidSlice.Services;

namespace VidSlice.Tests;

public class TranscriptionServiceTests
{
    [Fact]
    public void BuildExtractArgs_Produces16kMonoPcmWav()
    {
        var args = TranscriptionService.BuildExtractArgs("in.mkv", "out.wav");

        Assert.Contains("in.mkv", args);
        Assert.Contains("out.wav", args);
        // 16 kHz mono PCM is what whisper.cpp requires.
        AssertPair(args, "-ar", "16000");
        AssertPair(args, "-ac", "1");
        AssertPair(args, "-c:a", "pcm_s16le");
        Assert.Contains("-vn", args); // drop video
    }

    [Fact]
    public void BuildWhisperArgs_AutoLanguage_PassesAuto_AndRequestedFormats()
    {
        var args = TranscriptionService.BuildWhisperArgs(
            "model.bin", "clip.wav", "C:\\out\\Clip",
            TranscriptionLanguages.Auto, translate: false, writeTxt: true, writeSrt: true);

        AssertPair(args, "-m", "model.bin");
        AssertPair(args, "-f", "clip.wav");
        AssertPair(args, "-of", "C:\\out\\Clip");
        AssertPair(args, "-l", "auto");
        Assert.Contains("--output-txt", args);
        Assert.Contains("--output-srt", args);
        Assert.DoesNotContain("--translate", args);
    }

    [Fact]
    public void BuildWhisperArgs_ExplicitLanguage_AndTranslate()
    {
        var args = TranscriptionService.BuildWhisperArgs(
            "m.bin", "a.wav", "Base",
            TranscriptionLanguages.Hindi, translate: true, writeTxt: true, writeSrt: false);

        AssertPair(args, "-l", "hi");
        Assert.Contains("--translate", args);
        Assert.Contains("--output-txt", args);
        Assert.DoesNotContain("--output-srt", args);
    }

    [Fact]
    public void BuildWhisperArgs_SrtOnly_OmitsTxt()
    {
        var args = TranscriptionService.BuildWhisperArgs(
            "m.bin", "a.wav", "Base",
            TranscriptionLanguages.Marathi, translate: false, writeTxt: false, writeSrt: true);

        Assert.DoesNotContain("--output-txt", args);
        Assert.Contains("--output-srt", args);
    }

    /// <summary>Assert that <paramref name="key"/> is immediately followed by <paramref name="value"/>.</summary>
    private static void AssertPair(IReadOnlyList<string> args, string key, string value)
    {
        int i = args.ToList().IndexOf(key);
        Assert.True(i >= 0 && i + 1 < args.Count, $"missing flag {key}");
        Assert.Equal(value, args[i + 1]);
    }
}
