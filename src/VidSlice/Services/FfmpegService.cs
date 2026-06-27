using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using VidSlice.Models;

namespace VidSlice.Services;

/// <summary>
/// Wraps the ffmpeg/ffprobe executables as external processes. Mirrors the
/// manual workflow: probe → stream-copy convert → segment split → verify.
/// </summary>
public sealed class FfmpegService : IFfmpegService
{
    private readonly ILogger<FfmpegService> _log;

    public FfmpegService() : this(NullLogger<FfmpegService>.Instance) { }

    public FfmpegService(ILogger<FfmpegService> log) => _log = log;

    public async Task<MediaInfo> ProbeAsync(string path, CancellationToken ct = default)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("Input file not found.", path);

        var args = new[]
        {
            "-v", "quiet",
            "-print_format", "json",
            "-show_format",
            "-show_streams",
            path,
        };

        var (exit, stdout, stderr) = await RunCapturedAsync(FfmpegLocator.Ffprobe, args, ct);
        if (exit != 0 || string.IsNullOrWhiteSpace(stdout))
            throw new InvalidOperationException($"ffprobe failed (exit {exit}). {stderr}");

        using var doc = JsonDocument.Parse(stdout);
        var rootEl = doc.RootElement;

        string formatName = "";
        double duration = 0;
        long bitRate = 0;
        if (rootEl.TryGetProperty("format", out var fmt))
        {
            formatName = fmt.TryGetProperty("format_name", out var fn) ? fn.GetString() ?? "" : "";
            duration = ParseDouble(fmt, "duration");
            bitRate = (long)ParseDouble(fmt, "bit_rate");
        }

        string? vCodec = null, aCodec = null;
        int width = 0, height = 0;
        if (rootEl.TryGetProperty("streams", out var streams))
        {
            foreach (var s in streams.EnumerateArray())
            {
                var type = s.TryGetProperty("codec_type", out var ct2) ? ct2.GetString() : null;
                if (type == "video" && vCodec is null)
                {
                    vCodec = s.TryGetProperty("codec_name", out var cn) ? cn.GetString() : null;
                    width = s.TryGetProperty("width", out var w) && w.TryGetInt32(out var wi) ? wi : 0;
                    height = s.TryGetProperty("height", out var h) && h.TryGetInt32(out var hi) ? hi : 0;
                }
                else if (type == "audio" && aCodec is null)
                {
                    aCodec = s.TryGetProperty("codec_name", out var cn) ? cn.GetString() : null;
                }
            }
        }

        var size = new FileInfo(path).Length;

        return new MediaInfo
        {
            FilePath = path,
            FormatName = formatName,
            DurationSeconds = duration,
            SizeBytes = size,
            BitRate = bitRate,
            VideoCodec = vCodec,
            AudioCodec = aCodec,
            Width = width,
            Height = height,
        };
    }

    public async Task<string> ConvertAsync(
        string inputPath, string outputMp4Path, MediaInfo info, bool allowReencode,
        IProgress<double>? progress = null, CancellationToken ct = default)
    {
        bool streamCopy = info.CanStreamCopyToMp4;
        if (!streamCopy && !allowReencode)
            throw new InvalidOperationException(
                $"Source streams ({info.VideoCodec}/{info.AudioCodec}) aren't MP4-compatible. " +
                "Enable re-encoding to convert this file.");

        // Don't clobber an existing unrelated file.
        outputMp4Path = PathUtils.GetUniquePath(outputMp4Path);

        // Re-encoding can need roughly the source size again; copy needs ~1x.
        EnsureDiskSpace(outputMp4Path, info.SizeBytes);

        var args = new List<string>
        {
            "-y",
            "-i", inputPath,
            "-map", "0",
        };

        if (streamCopy)
        {
            args.AddRange(["-c", "copy"]);
        }
        else
        {
            args.AddRange([
                "-c:v", "libx264", "-preset", "medium", "-crf", "18",
                "-c:a", "aac", "-b:a", "192k",
            ]);
        }

        args.AddRange(["-movflags", "+faststart"]);
        args.AddRange(["-progress", "pipe:1", "-nostats"]);
        args.Add(outputMp4Path);

        _log.LogInformation("Converting {Input} -> {Output} (streamCopy={Copy})", inputPath, outputMp4Path, streamCopy);
        await RunWithProgressAsync(FfmpegLocator.Ffmpeg, args, info.DurationSeconds, progress, ct);

        if (!File.Exists(outputMp4Path))
            throw new InvalidOperationException("Conversion finished but the output file is missing.");

        return outputMp4Path;
    }

    public async Task<IReadOnlyList<string>> SplitAsync(
        string inputPath, SplitPlan plan, string outputFolder, string baseName, string extension,
        double totalDurationSeconds, bool exactSplit = false,
        IProgress<double>? progress = null, CancellationToken ct = default)
    {
        Directory.CreateDirectory(outputFolder);

        // Clear any stale parts from a previous run so verification is accurate.
        foreach (var stale in EnumerateParts(outputFolder, baseName, extension))
            TryDelete(stale);

        var pattern = Path.Combine(outputFolder, $"{baseName} - Part %03d{extension}");
        var seg = plan.SegmentSeconds.ToString(CultureInfo.InvariantCulture);

        var args = new List<string>
        {
            "-y",
            "-i", inputPath,
        };

        if (exactSplit)
        {
            // Force keyframes exactly at each segment boundary, then re-encode so
            // cuts are frame-accurate rather than snapping to existing keyframes.
            args.AddRange([
                "-force_key_frames", $"expr:gte(t,n_forced*{seg})",
                "-c:v", "libx264", "-preset", "veryfast", "-crf", "18",
                "-c:a", "aac", "-b:a", "192k",
            ]);
        }
        else
        {
            args.AddRange(["-c", "copy"]);
        }

        args.AddRange([
            "-map", "0",
            "-f", "segment",
            "-segment_time", seg,
            "-reset_timestamps", "1",
            "-progress", "pipe:1", "-nostats",
            pattern,
        ]);

        _log.LogInformation("Splitting {Input} every {Seg}s (exact={Exact})", inputPath, seg, exactSplit);
        await RunWithProgressAsync(FfmpegLocator.Ffmpeg, args, totalDurationSeconds, progress, ct);

        return EnumerateParts(outputFolder, baseName, extension)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<IReadOnlyList<PartResult>> VerifyAsync(
        IEnumerable<string> partPaths, long maxBytes, CancellationToken ct = default)
    {
        var results = new List<PartResult>();
        foreach (var p in partPaths)
        {
            ct.ThrowIfCancellationRequested();
            double dur = 0;
            try
            {
                var info = await ProbeAsync(p, ct);
                dur = info.DurationSeconds;
            }
            catch
            {
                // A part we can't probe still gets reported with its size.
            }

            results.Add(new PartResult
            {
                FileName = Path.GetFileName(p),
                FilePath = p,
                SizeBytes = new FileInfo(p).Length,
                DurationSeconds = dur,
                MaxBytes = maxBytes,
            });
        }
        return results;
    }

    public async Task<RunResult> RunAsync(
        ConvertSplitOptions options, MediaInfo info,
        IProgress<double>? progress = null, IProgress<string>? status = null, CancellationToken ct = default)
    {
        Directory.CreateDirectory(options.OutputFolder);

        bool reencoded = !info.CanStreamCopyToMp4;

        // Up-front disk-space check. Re-encoding can grow the output, so budget
        // generously: convert phase + split phase, each ~1x (×1.5 when re-encoding).
        double convertFactor = options.ConvertToMp4 ? (reencoded ? 1.5 : 1.0) : 0;
        double splitFactor = options.ExactSplit ? 1.5 : 1.0;
        EnsureDiskSpace(options.OutputFolder, (long)(info.SizeBytes * (convertFactor + splitFactor)));

        string sourceForSplit;
        string? mp4Path = null;
        string extension;

        try
        {
            // --- Phase 1: convert (or use the input as-is) ---
            if (options.ConvertToMp4)
            {
                status?.Report("Converting to MP4…");
                var target = Path.Combine(options.OutputFolder, $"{options.BaseName}.mp4");

                var convProgress = new Progress<double>(f => progress?.Report(f * 0.4));
                mp4Path = await ConvertAsync(options.InputPath, target, info, options.AllowReencode, convProgress, ct);

                sourceForSplit = mp4Path;
                extension = ".mp4";
            }
            else
            {
                sourceForSplit = options.InputPath;
                extension = Path.GetExtension(options.InputPath);
                if (string.IsNullOrEmpty(extension)) extension = ".mp4";
            }

            // --- Phase 2: plan + split ---
            var plan = BuildPlan(options, new FileInfo(sourceForSplit).Length, info.DurationSeconds);
            long verifyCeiling = options.Mode == SplitMode.BySize ? options.MaxBytes : long.MaxValue;

            if (!plan.SplitNeeded)
            {
                status?.Report("File already fits — no split needed.");
                progress?.Report(1);
                var single = await VerifyAsync([sourceForSplit], verifyCeiling, ct);
                return new RunResult
                {
                    Mp4Path = mp4Path,
                    Parts = single,
                    AllPartsUnderLimit = single.All(p => p.UnderLimit),
                    WasReencoded = reencoded,
                };
            }

            int retries = 0;
            IReadOnlyList<PartResult> parts;
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                status?.Report($"Splitting into ~{plan.ExpectedParts} parts ({plan.SegmentSeconds:0}s each)…");

                double phaseStart = options.ConvertToMp4 ? 0.4 : 0.0;
                double phaseSpan = options.ConvertToMp4 ? 0.55 : 0.95;
                var splitProgress = new Progress<double>(f => progress?.Report(phaseStart + f * phaseSpan));

                var paths = await SplitAsync(
                    sourceForSplit, plan, options.OutputFolder, options.BaseName, extension,
                    info.DurationSeconds, options.ExactSplit, splitProgress, ct);

                status?.Report("Verifying parts…");
                parts = await VerifyAsync(paths, verifyCeiling, ct);

                // Only the size mode has a meaningful overshoot to retry.
                bool needRetry = options.Mode == SplitMode.BySize && parts.Any(p => !p.UnderLimit);
                if (!needRetry || retries >= 1)
                    break;

                retries++;
                status?.Report("A part exceeded the limit — retrying with smaller segments…");
                plan = SplitPlanner.Shrink(plan, info.DurationSeconds);
            }

            progress?.Report(1);
            return new RunResult
            {
                Mp4Path = mp4Path,
                Parts = parts,
                AllPartsUnderLimit = parts.All(p => p.UnderLimit),
                WasReencoded = reencoded,
                RetryCount = retries,
            };
        }
        catch (OperationCanceledException)
        {
            // Best-effort cleanup so a cancelled run doesn't leave half-written files.
            if (mp4Path is not null) TryDelete(mp4Path);
            foreach (var p in EnumerateParts(options.OutputFolder, options.BaseName, ".mp4")) TryDelete(p);
            var inputExt = Path.GetExtension(options.InputPath);
            if (!string.IsNullOrEmpty(inputExt) && inputExt != ".mp4")
                foreach (var p in EnumerateParts(options.OutputFolder, options.BaseName, inputExt)) TryDelete(p);
            throw;
        }
    }

    // ---- helpers ----

    private static SplitPlan BuildPlan(ConvertSplitOptions o, long sourceBytes, double duration) => o.Mode switch
    {
        SplitMode.ByParts => SplitPlanner.PlanByParts(duration, o.PartCount),
        SplitMode.ByDuration => SplitPlanner.PlanByDuration(duration, o.SegmentSeconds),
        _ => SplitPlanner.Plan(sourceBytes, duration, o.MaxBytes),
    };

    private static void EnsureDiskSpace(string targetPathOrFolder, long requiredBytes)
    {
        var folder = Directory.Exists(targetPathOrFolder)
            ? targetPathOrFolder
            : Path.GetDirectoryName(targetPathOrFolder) ?? targetPathOrFolder;

        long free = PathUtils.GetAvailableFreeBytes(folder);
        if (free >= 0 && free < requiredBytes)
        {
            static double Gb(long b) => b / 1024d / 1024d / 1024d;
            throw new IOException(
                $"Not enough free disk space. Need ~{Gb(requiredBytes):0.1} GB but only {Gb(free):0.1} GB is available.");
        }
    }

    private static IEnumerable<string> EnumerateParts(string folder, string baseName, string extension)
    {
        if (!Directory.Exists(folder)) return [];

        // Match only our own "<base> - Part NNN.ext" outputs, not arbitrary files
        // that happen to contain "Part" — the wildcard pre-filters, the regex confirms.
        var rx = new System.Text.RegularExpressions.Regex(
            $"^{System.Text.RegularExpressions.Regex.Escape(baseName)} - Part \\d+{System.Text.RegularExpressions.Regex.Escape(extension)}$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        return Directory.EnumerateFiles(folder, $"{baseName} - Part *{extension}")
            .Where(p => rx.IsMatch(Path.GetFileName(p)));
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch { /* best effort */ }
    }

    private static double ParseDouble(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) &&
        double.TryParse(v.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d)
            ? d : 0;

    private async Task RunWithProgressAsync(
        string exe, IReadOnlyList<string> args, double totalSeconds,
        IProgress<double>? progress, CancellationToken ct)
    {
        var psi = NewStartInfo(exe, args);
        using var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };

        var stderrTail = new StringBuilder();

        proc.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            if (ProgressParser.TryParseProcessedSeconds(e.Data, out var secs))
                progress?.Report(ProgressParser.Fraction(secs, totalSeconds));
        };
        proc.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            AppendTail(stderrTail, e.Data);
        };

        if (!proc.Start())
            throw new InvalidOperationException($"Failed to start {Path.GetFileName(exe)}.");

        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        try
        {
            await proc.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            TryKill(proc);
            throw;
        }

        if (proc.ExitCode != 0)
        {
            var tail = stderrTail.ToString().Trim();
            _log.LogError("{Exe} failed (exit {Code}): {Tail}", Path.GetFileName(exe), proc.ExitCode, tail);
            throw new InvalidOperationException(
                $"{Path.GetFileName(exe)} failed (exit {proc.ExitCode}).{Environment.NewLine}{tail}");
        }
    }

    private static async Task<(int exit, string stdout, string stderr)> RunCapturedAsync(
        string exe, IReadOnlyList<string> args, CancellationToken ct)
    {
        var psi = NewStartInfo(exe, args);
        using var proc = new Process { StartInfo = psi };

        if (!proc.Start())
            throw new InvalidOperationException($"Failed to start {Path.GetFileName(exe)}.");

        var stdoutTask = proc.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = proc.StandardError.ReadToEndAsync(ct);

        try
        {
            await proc.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            TryKill(proc);
            // Observe the read tasks so they don't surface as unobserved exceptions.
            await ObserveQuietly(stdoutTask);
            await ObserveQuietly(stderrTask);
            throw;
        }

        return (proc.ExitCode, await stdoutTask, await stderrTask);
    }

    private static async Task ObserveQuietly(Task task)
    {
        try { await task; } catch { /* deliberately swallowed after cancellation */ }
    }

    private static ProcessStartInfo NewStartInfo(string exe, IReadOnlyList<string> args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = exe,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var a in args)
            psi.ArgumentList.Add(a);
        return psi;
    }

    private static void AppendTail(StringBuilder sb, string line)
    {
        const int maxChars = 4000;
        sb.AppendLine(line);
        if (sb.Length > maxChars)
            sb.Remove(0, sb.Length - maxChars);
    }

    private static void TryKill(Process proc)
    {
        try { if (!proc.HasExited) proc.Kill(entireProcessTree: true); }
        catch { /* already gone */ }
    }
}
