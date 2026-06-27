using System.IO;
using VidSlice.Models;
using VidSlice.Services;

namespace VidSlice.Tests;

/// <summary>
/// Shared, opt-in fixture that runs the real ffmpeg/ffprobe once: it probes the
/// input and converts it to a temp MP4 that the integration tests reuse. Heavy
/// I/O, so it only activates when VIDSLICE_TEST_INPUT points at a real video:
///   $env:VIDSLICE_TEST_INPUT = "C:\Users\Ninad\Downloads\Video\Class 5.mp4"
/// Optionally set VIDSLICE_TEST_INPUT_MISNAMED to a video with a wrong extension
/// (e.g. the original .m3u8) to exercise container auto-detection.
/// </summary>
public sealed class RealMediaFixture : IDisposable
{
    public FfmpegService Service { get; } = new();
    public string? InputPath { get; }
    public string? MisnamedInputPath { get; }
    public bool Enabled { get; }
    public string TempDir { get; }
    public MediaInfo? Info { get; }
    public string? ConvertedMp4 { get; }

    public RealMediaFixture()
    {
        InputPath = Environment.GetEnvironmentVariable("VIDSLICE_TEST_INPUT");
        MisnamedInputPath = Environment.GetEnvironmentVariable("VIDSLICE_TEST_INPUT_MISNAMED");
        Enabled = !string.IsNullOrWhiteSpace(InputPath) && File.Exists(InputPath);
        TempDir = Path.Combine(Path.GetTempPath(), "VidSliceIT_" + Guid.NewGuid().ToString("N"));

        if (!Enabled) return;

        Directory.CreateDirectory(TempDir);
        Info = Service.ProbeAsync(InputPath!).GetAwaiter().GetResult();
        ConvertedMp4 = Path.Combine(TempDir, "converted.mp4");
        Service.ConvertAsync(InputPath!, ConvertedMp4, Info, allowReencode: false)
               .GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        try { if (Directory.Exists(TempDir)) Directory.Delete(TempDir, recursive: true); }
        catch { /* best effort */ }
    }
}

public class FfmpegServiceIntegrationTests : IClassFixture<RealMediaFixture>
{
    private const long Mb = 1024 * 1024;
    private readonly RealMediaFixture _fx;

    public FfmpegServiceIntegrationTests(RealMediaFixture fx) => _fx = fx;

    [SkippableFact]
    public void Probe_RealFile_ReturnsStreamsAndDuration()
    {
        Skip.IfNot(_fx.Enabled, "Set VIDSLICE_TEST_INPUT to a real video to run.");

        Assert.True(_fx.Info!.DurationSeconds > 0);
        Assert.True(_fx.Info.SizeBytes > 0);
        Assert.True(_fx.Info.HasVideo);
    }

    [SkippableFact]
    public async Task Convert_ProducesPlayableMp4_PreservingDuration()
    {
        Skip.IfNot(_fx.Enabled, "Set VIDSLICE_TEST_INPUT to a real video to run.");

        Assert.True(File.Exists(_fx.ConvertedMp4));
        var converted = await _fx.Service.ProbeAsync(_fx.ConvertedMp4!);

        Assert.Contains("mp4", converted.FormatName);
        Assert.InRange(converted.DurationSeconds, _fx.Info!.DurationSeconds * 0.99, _fx.Info.DurationSeconds * 1.01);
    }

    [SkippableFact]
    public void MisnamedExtension_StillDetectedAsRealContainer()
    {
        Skip.If(string.IsNullOrWhiteSpace(_fx.MisnamedInputPath) || !File.Exists(_fx.MisnamedInputPath),
            "Set VIDSLICE_TEST_INPUT_MISNAMED to a misnamed video (e.g. a video named .m3u8).");

        var info = _fx.Service.ProbeAsync(_fx.MisnamedInputPath!).GetAwaiter().GetResult();
        Assert.True(info.HasVideo);
        Assert.True(info.DurationSeconds > 0);
    }

    [SkippableFact]
    public async Task Split_ProducesMultipleParts_EachUnderLimit()
    {
        Skip.IfNot(_fx.Enabled, "Set VIDSLICE_TEST_INPUT to a real video to run.");

        long max = 180 * Mb;
        var plan = SplitPlanner.Plan(new FileInfo(_fx.ConvertedMp4!).Length, _fx.Info!.DurationSeconds, max);
        var outDir = Path.Combine(_fx.TempDir, "split_" + Guid.NewGuid().ToString("N"));

        var paths = await _fx.Service.SplitAsync(
            _fx.ConvertedMp4!, plan, outDir, "Part", ".mp4", _fx.Info.DurationSeconds);

        Assert.True(paths.Count > 1, "expected more than one part");

        var verified = await _fx.Service.VerifyAsync(paths, max);
        Assert.All(verified, p => Assert.True(p.UnderLimit, $"{p.FileName} = {p.SizeMb} MB"));
    }

    [SkippableFact]
    public async Task Run_ConvertAndSplit_AllPartsUnderLimit_AndDurationsReconstruct()
    {
        Skip.IfNot(_fx.Enabled, "Set VIDSLICE_TEST_INPUT to a real video to run.");

        long max = 180 * Mb;
        var outDir = Path.Combine(_fx.TempDir, "run_" + Guid.NewGuid().ToString("N"));
        var options = new ConvertSplitOptions
        {
            InputPath = _fx.InputPath!,
            OutputFolder = outDir,
            BaseName = "RunClass",
            MaxBytes = max,
            ConvertToMp4 = true,
        };

        var result = await _fx.Service.RunAsync(options, _fx.Info!);

        Assert.NotEmpty(result.Parts);
        Assert.All(result.Parts, p => Assert.True(p.SizeBytes < max, $"{p.FileName} = {p.SizeMb} MB"));
        Assert.True(result.AllPartsUnderLimit);

        double partsTotal = result.Parts.Sum(p => p.DurationSeconds);
        Assert.InRange(partsTotal, _fx.Info!.DurationSeconds * 0.97, _fx.Info.DurationSeconds * 1.03);
    }

    [SkippableFact]
    public async Task Split_ByParts_ProducesApproxRequestedCount()
    {
        Skip.IfNot(_fx.Enabled, "Set VIDSLICE_TEST_INPUT to a real video to run.");

        int requested = 5;
        var plan = SplitPlanner.PlanByParts(_fx.Info!.DurationSeconds, requested);
        var outDir = Path.Combine(_fx.TempDir, "byparts_" + Guid.NewGuid().ToString("N"));

        var paths = await _fx.Service.SplitAsync(
            _fx.ConvertedMp4!, plan, outDir, "Part", ".mp4", _fx.Info.DurationSeconds);

        // Stream-copy cuts land on keyframes, so the count can differ by one.
        Assert.InRange(paths.Count, requested - 1, requested + 1);
    }

    [SkippableFact]
    public async Task Split_ByDuration_PartsMatchRequestedLength()
    {
        Skip.IfNot(_fx.Enabled, "Set VIDSLICE_TEST_INPUT to a real video to run.");

        double segment = 1800; // 30 min
        var plan = SplitPlanner.PlanByDuration(_fx.Info!.DurationSeconds, segment);
        var outDir = Path.Combine(_fx.TempDir, "bydur_" + Guid.NewGuid().ToString("N"));

        var paths = await _fx.Service.SplitAsync(
            _fx.ConvertedMp4!, plan, outDir, "Part", ".mp4", _fx.Info.DurationSeconds);

        var verified = await _fx.Service.VerifyAsync(paths, long.MaxValue);
        // Every part except possibly the last should be close to the requested length.
        foreach (var p in verified.Take(verified.Count - 1))
            Assert.InRange(p.DurationSeconds, segment * 0.5, segment * 1.5);
    }

    [SkippableFact]
    public async Task Run_WhenUnderLimit_NoSplit_SinglePart()
    {
        Skip.IfNot(_fx.Enabled, "Set VIDSLICE_TEST_INPUT to a real video to run.");

        // A limit larger than the whole file → no split, just one verified part.
        long hugeMax = (new FileInfo(_fx.ConvertedMp4!).Length) + 1024L * Mb;
        var outDir = Path.Combine(_fx.TempDir, "nosplit_" + Guid.NewGuid().ToString("N"));
        var options = new ConvertSplitOptions
        {
            InputPath = _fx.ConvertedMp4!,
            OutputFolder = outDir,
            BaseName = "Whole",
            MaxBytes = hugeMax,
            ConvertToMp4 = false,
        };

        var result = await _fx.Service.RunAsync(options, _fx.Info!);

        Assert.Single(result.Parts);
        Assert.True(result.AllPartsUnderLimit);
    }
}
