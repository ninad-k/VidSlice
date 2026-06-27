using VidSlice.Models;
using VidSlice.Services;

namespace VidSlice.Tests;

/// <summary>
/// In-memory IFfmpegService for view-model tests — no real ffmpeg involved.
/// Records the last call arguments and returns canned results.
/// </summary>
internal sealed class FakeFfmpegService : IFfmpegService
{
    public MediaInfo? InfoToReturn { get; set; }
    public RunResult RunResultToReturn { get; set; } = new();

    public int ProbeCalls { get; private set; }
    public int RunCalls { get; private set; }
    public ConvertSplitOptions? LastRunOptions { get; private set; }
    public Func<ConvertSplitOptions, MediaInfo, RunResult>? RunFactory { get; set; }

    public Task<MediaInfo> ProbeAsync(string path, CancellationToken ct = default)
    {
        ProbeCalls++;
        var info = InfoToReturn ?? new MediaInfo
        {
            FilePath = path,
            VideoCodec = "h264",
            AudioCodec = "aac",
            DurationSeconds = 600,
            SizeBytes = 500L * 1024 * 1024,
            Width = 1920,
            Height = 1080,
        };
        return Task.FromResult(info);
    }

    public Task<string> ConvertAsync(string inputPath, string outputMp4Path, MediaInfo info, bool allowReencode,
        IProgress<double>? progress = null, CancellationToken ct = default)
    {
        progress?.Report(1);
        return Task.FromResult(outputMp4Path);
    }

    public Task<IReadOnlyList<string>> SplitAsync(string inputPath, SplitPlan plan, string outputFolder,
        string baseName, string extension, double totalDurationSeconds, bool exactSplit = false,
        IProgress<double>? progress = null, CancellationToken ct = default)
    {
        progress?.Report(1);
        return Task.FromResult<IReadOnlyList<string>>([]);
    }

    public Task<IReadOnlyList<PartResult>> VerifyAsync(IEnumerable<string> partPaths, long maxBytes,
        CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<PartResult>>([]);

    public Task<RunResult> RunAsync(ConvertSplitOptions options, MediaInfo info,
        IProgress<double>? progress = null, IProgress<string>? status = null, CancellationToken ct = default)
    {
        RunCalls++;
        LastRunOptions = options;
        status?.Report("Working…");
        progress?.Report(1);
        var result = RunFactory?.Invoke(options, info) ?? RunResultToReturn;
        return Task.FromResult(result);
    }
}
