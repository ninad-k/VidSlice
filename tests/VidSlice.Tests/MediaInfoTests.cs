using VidSlice.Models;

namespace VidSlice.Tests;

public class MediaInfoTests
{
    private static MediaInfo Make(
        string? video = "h264", string? audio = "aac",
        long size = 0, double duration = 0, long bitRate = 0,
        int width = 0, int height = 0) => new()
    {
        FilePath = @"C:\x.mp4",
        VideoCodec = video,
        AudioCodec = audio,
        SizeBytes = size,
        DurationSeconds = duration,
        BitRate = bitRate,
        Width = width,
        Height = height,
    };

    [Theory]
    [InlineData("h264", "aac", true)]
    [InlineData("hevc", "ac3", true)]
    [InlineData("h265", "eac3", true)]
    [InlineData("av1", "mp3", true)]
    [InlineData("mpeg4", "aac", true)]
    [InlineData("vp9", "opus", false)]   // vp9 not MP4-copy-friendly here
    [InlineData("mpeg2video", "aac", false)]
    [InlineData("h264", "flac", false)]  // flac audio not in allow-list
    public void CanStreamCopyToMp4_DependsOnCodecs(string v, string a, bool expected)
    {
        Assert.Equal(expected, Make(v, a).CanStreamCopyToMp4);
    }

    [Fact]
    public void CanStreamCopy_AudioOnly_OkWhenAudioCompatible()
    {
        Assert.True(Make(video: null, audio: "aac").CanStreamCopyToMp4);
    }

    [Fact]
    public void CanStreamCopy_VideoOnly_OkWhenVideoCompatible()
    {
        Assert.True(Make(video: "h264", audio: null).CanStreamCopyToMp4);
    }

    [Fact]
    public void HasVideoAndAudio_ReflectCodecPresence()
    {
        var info = Make(video: "h264", audio: null);
        Assert.True(info.HasVideo);
        Assert.False(info.HasAudio);
    }

    [Fact]
    public void EffectiveBitRate_PrefersReportedBitRate()
    {
        Assert.Equal(1000, Make(bitRate: 1000, size: 999, duration: 1).EffectiveBitRate);
    }

    [Fact]
    public void EffectiveBitRate_FallsBackToSizeOverDuration()
    {
        // 1,000,000 bytes over 8 s => 1,000,000 bits/s.
        Assert.Equal(1_000_000, Make(bitRate: 0, size: 1_000_000, duration: 8).EffectiveBitRate);
    }

    [Fact]
    public void EffectiveBitRate_ZeroWhenNoDataAvailable()
    {
        Assert.Equal(0, Make(bitRate: 0, size: 1000, duration: 0).EffectiveBitRate);
    }

    [Fact]
    public void DurationText_FormatsHoursWhenLong()
    {
        Assert.Equal("1h 1m 1s", Make(duration: 3661).DurationText);
    }

    [Fact]
    public void DurationText_OmitsHoursWhenShort()
    {
        Assert.Equal("1m 30s", Make(duration: 90).DurationText);
    }

    [Theory]
    [InlineData(1920, 1080, "1920x1080")]
    [InlineData(0, 0, "—")]
    [InlineData(1280, 0, "—")]
    public void ResolutionText_HandlesMissingDimensions(int w, int h, string expected)
    {
        Assert.Equal(expected, Make(width: w, height: h).ResolutionText);
    }

    [Fact]
    public void SizeMb_ConvertsBytes()
    {
        Assert.Equal(1.0, Make(size: 1024 * 1024).SizeMb, precision: 6);
    }
}
