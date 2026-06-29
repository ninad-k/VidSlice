using VidSlice.Services;

namespace VidSlice.Tests;

public class WhisperProgressParserTests
{
    [Theory]
    [InlineData("whisper_print_progress_callback: progress =  10%", 0.10)]
    [InlineData("progress = 0%", 0.0)]
    [InlineData("progress = 100%", 1.0)]
    [InlineData("...progress=55%...", 0.55)]
    public void ProgressLines_ParsedAsFraction(string line, double expected)
    {
        Assert.True(WhisperProgressParser.TryParseProgressFraction(line, out var f));
        Assert.Equal(expected, f, precision: 4);
    }

    [Theory]
    [InlineData("")]
    [InlineData("loading model...")]
    [InlineData("[00:00.000 --> 00:02.000]  hello")]
    [InlineData("progress = abc%")]
    public void NonProgressLines_ReturnFalse(string line)
    {
        Assert.False(WhisperProgressParser.TryParseProgressFraction(line, out var f));
        Assert.Equal(0, f);
    }

    [Theory]
    [InlineData("whisper_full_with_state: auto-detected language: hi (p = 0.98)", "hi")]
    [InlineData("auto-detected language: MR", "mr")]
    [InlineData("auto-detected language: en", "en")]
    public void DetectedLanguage_Parsed(string line, string expected)
    {
        Assert.True(WhisperProgressParser.TryParseDetectedLanguage(line, out var lang));
        Assert.Equal(expected, lang);
    }

    [Theory]
    [InlineData("")]
    [InlineData("progress = 50%")]
    [InlineData("no language here")]
    public void NonLanguageLines_ReturnFalse(string line)
    {
        Assert.False(WhisperProgressParser.TryParseDetectedLanguage(line, out var lang));
        Assert.Equal("", lang);
    }
}
