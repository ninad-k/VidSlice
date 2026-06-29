using System.Diagnostics;
using System.IO;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace VidSlice.Services;

/// <summary>
/// Produces a text transcript (and SRT subtitles) for a media file using local
/// whisper.cpp — fully offline. Pipeline: ffmpeg extracts 16 kHz mono PCM WAV →
/// whisper-cli transcribes and writes the .txt/.srt itself.
/// </summary>
public sealed class TranscriptionService : ITranscriptionService
{
    private readonly ILogger<TranscriptionService> _log;

    public TranscriptionService() : this(NullLogger<TranscriptionService>.Instance) { }

    public TranscriptionService(ILogger<TranscriptionService> log) => _log = log;

    public async Task<TranscriptionResult> TranscribeAsync(
        TranscriptionOptions options,
        IProgress<double>? progress = null, IProgress<string>? status = null, CancellationToken ct = default)
    {
        if (!File.Exists(options.InputPath))
            throw new FileNotFoundException("Input file not found.", options.InputPath);

        if (!WhisperLocator.IsAvailable)
            throw new InvalidOperationException(
                "Whisper isn't set up. Bundle whisper-cli.exe and a ggml-*.bin model in " +
                "Resources\\whisper (run build\\fetch-whisper.ps1), or place a model in " +
                $"{WhisperLocator.ModelsDir}.");

        Directory.CreateDirectory(options.OutputFolder);
        var outBase = Path.Combine(options.OutputFolder, options.BaseName);

        var wav = Path.Combine(Path.GetTempPath(), $"vidslice_{Guid.NewGuid():N}.wav");
        try
        {
            // --- Step 1: extract audio to a whisper-friendly WAV (small fraction of the bar). ---
            status?.Report("Extracting audio…");
            var extractArgs = BuildExtractArgs(options.InputPath, wav);
            var (exit, _, extractErr) = await RunCapturedAsync(FfmpegLocator.Ffmpeg, extractArgs, ct);
            if (exit != 0 || !File.Exists(wav))
                throw new InvalidOperationException($"Audio extraction failed (exit {exit}).{Environment.NewLine}{extractErr}");
            progress?.Report(0.1);

            // --- Step 2: transcribe. whisper writes <outBase>.txt / .srt directly. ---
            status?.Report("Transcribing…");
            var whisperArgs = BuildWhisperArgs(
                WhisperLocator.ModelPath!, wav, outBase,
                options.LanguageHint, options.Translate, options.WriteTxt, options.WriteSrt);

            string? detected = await RunWhisperAsync(
                WhisperLocator.WhisperCli, whisperArgs,
                f => progress?.Report(0.1 + f * 0.9), ct);

            progress?.Report(1);

            var txt = options.WriteTxt ? outBase + ".txt" : null;
            var srt = options.WriteSrt ? outBase + ".srt" : null;

            return new TranscriptionResult
            {
                TxtPath = txt is not null && File.Exists(txt) ? txt : null,
                SrtPath = srt is not null && File.Exists(srt) ? srt : null,
                DetectedLanguage = detected,
            };
        }
        finally
        {
            TryDelete(wav);
        }
    }

    // ---- argument building (static + testable) ----

    /// <summary>ffmpeg args to pull a 16 kHz mono PCM WAV — exactly what whisper.cpp expects.</summary>
    public static IReadOnlyList<string> BuildExtractArgs(string inputPath, string wavPath) =>
    [
        "-y",
        "-i", inputPath,
        "-vn",
        "-ar", "16000",
        "-ac", "1",
        "-c:a", "pcm_s16le",
        wavPath,
    ];

    /// <summary>whisper-cli args. "auto" omits the language flag so whisper detects it.</summary>
    public static IReadOnlyList<string> BuildWhisperArgs(
        string modelPath, string wavPath, string outputBase,
        string languageHint, bool translate, bool writeTxt, bool writeSrt)
    {
        var args = new List<string>
        {
            "-m", modelPath,
            "-f", wavPath,
            "-of", outputBase,
            "--print-progress",
        };

        if (!string.IsNullOrWhiteSpace(languageHint) &&
            !string.Equals(languageHint, TranscriptionLanguages.Auto, StringComparison.OrdinalIgnoreCase))
        {
            args.AddRange(["-l", languageHint]);
        }
        else
        {
            args.AddRange(["-l", "auto"]);
        }

        if (translate) args.Add("--translate");
        if (writeTxt) args.Add("--output-txt");
        if (writeSrt) args.Add("--output-srt");

        return args;
    }

    // ---- process helpers (mirror FfmpegService) ----

    /// <summary>Run whisper, mapping its "progress = NN%" lines to a 0..1 fraction. Returns detected language.</summary>
    private async Task<string?> RunWhisperAsync(
        string exe, IReadOnlyList<string> args, Action<double> onFraction, CancellationToken ct)
    {
        var psi = NewStartInfo(exe, args);
        using var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };

        var stderrTail = new StringBuilder();
        string? detected = null;

        void Handle(string? data)
        {
            if (data is null) return;
            if (WhisperProgressParser.TryParseProgressFraction(data, out var frac))
                onFraction(frac);
            if (detected is null && WhisperProgressParser.TryParseDetectedLanguage(data, out var lang))
                detected = lang;
        }

        // whisper prints transcript to stdout and progress/info to stderr; watch both.
        proc.OutputDataReceived += (_, e) => Handle(e.Data);
        proc.ErrorDataReceived += (_, e) =>
        {
            Handle(e.Data);
            if (e.Data is not null) AppendTail(stderrTail, e.Data);
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

        return detected;
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

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* best effort */ }
    }
}
