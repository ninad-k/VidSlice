using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Win32;
using VidSlice.Models;
using VidSlice.Services;

namespace VidSlice.ViewModels;

public sealed record SplitModeOption(string Label, SplitMode Value);

public partial class MainViewModel : ObservableObject
{
    private readonly IFfmpegService _ffmpeg;
    private readonly ISettingsService _settings;
    private readonly ILogger<MainViewModel> _log;
    private CancellationTokenSource? _cts;

    public MainViewModel()
        : this(new FfmpegService(), new SettingsService(), NullLogger<MainViewModel>.Instance) { }

    public MainViewModel(
        IFfmpegService ffmpeg, ISettingsService settings, ILogger<MainViewModel> log,
        bool? ffmpegAvailable = null)
    {
        _ffmpeg = ffmpeg;
        _settings = settings;
        _log = log;
        FfmpegAvailable = ffmpegAvailable ?? FfmpegLocator.IsAvailable;

        LoadFromSettings(_settings.Load());

        if (!FfmpegAvailable)
            StatusText = "⚠ ffmpeg was not found. Install ffmpeg or bundle it in Resources\\ffmpeg.";
    }

    // ---- Queue ----

    public ObservableCollection<BatchItem> Queue { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RemoveSelectedCommand))]
    private BatchItem? _selectedItem;

    // ---- Options ----

    public IReadOnlyList<SplitModeOption> Modes { get; } =
    [
        new("By max size (MB)", SplitMode.BySize),
        new("By number of parts", SplitMode.ByParts),
        new("By duration (minutes)", SplitMode.ByDuration),
    ];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBySize), nameof(IsByParts), nameof(IsByDuration))]
    private SplitMode _selectedMode = SplitMode.BySize;

    public bool IsBySize => SelectedMode == SplitMode.BySize;
    public bool IsByParts => SelectedMode == SplitMode.ByParts;
    public bool IsByDuration => SelectedMode == SplitMode.ByDuration;

    [ObservableProperty] private string? _outputFolder;
    [ObservableProperty] private double _maxSizeMb = 180;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PartCountValue))]
    private int _partCount = 4;

    /// <summary>
    /// Double-typed view of <see cref="PartCount"/> for binding to NumberBox
    /// (whose Value is double?), avoiding lossy/nullable int binding pitfalls.
    /// </summary>
    public double PartCountValue
    {
        get => PartCount;
        set => PartCount = (int)Math.Round(value);
    }

    [ObservableProperty] private double _segmentMinutes = 30;
    [ObservableProperty] private bool _convertToMp4 = true;
    [ObservableProperty] private bool _keepOriginal = true;
    [ObservableProperty] private bool _allowReencode;
    [ObservableProperty] private bool _exactSplit;
    [ObservableProperty] private string _theme = "System";

    // ---- State ----

    [ObservableProperty] private string _statusText = "Drop video files or click Add to begin.";
    [ObservableProperty] private double _overallProgress;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunAllCommand), nameof(CancelCommand), nameof(ClearCommand))]
    private bool _isBusy;

    public bool FfmpegAvailable { get; }

    // ---- Settings mapping ----

    private void LoadFromSettings(AppSettings s)
    {
        OutputFolder = s.OutputFolder;
        MaxSizeMb = s.MaxSizeMb;
        PartCount = s.PartCount;
        SegmentMinutes = s.SegmentMinutes;
        SelectedMode = s.SplitMode;
        ConvertToMp4 = s.ConvertToMp4;
        KeepOriginal = s.KeepOriginal;
        AllowReencode = s.AllowReencode;
        ExactSplit = s.ExactSplit;
        Theme = s.Theme;
    }

    public AppSettings ToSettings() => new()
    {
        OutputFolder = OutputFolder,
        MaxSizeMb = MaxSizeMb,
        PartCount = PartCount,
        SegmentMinutes = SegmentMinutes,
        SplitMode = SelectedMode,
        ConvertToMp4 = ConvertToMp4,
        KeepOriginal = KeepOriginal,
        AllowReencode = AllowReencode,
        ExactSplit = ExactSplit,
        Theme = Theme,
    };

    public void SaveSettings() => _settings.Save(ToSettings());

    // ---- Adding files ----

    private bool CanAdd() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanAdd))]
    private void AddFiles()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Select video files",
            Multiselect = true,
            Filter = "Video files|*.mp4;*.mkv;*.mov;*.avi;*.m4v;*.ts;*.m3u8;*.webm;*.flv;*.wmv|All files|*.*",
        };
        if (dlg.ShowDialog() == true)
            AddFiles(dlg.FileNames);
    }

    /// <summary>Add one or more input files to the queue and analyze each.</summary>
    public void AddFiles(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            if (!File.Exists(path)) continue;
            if (Queue.Any(q => string.Equals(q.InputPath, path, StringComparison.OrdinalIgnoreCase))) continue;

            var item = new BatchItem(path);
            Queue.Add(item);
            SelectedItem ??= item;
            OutputFolder ??= Path.GetDirectoryName(path);

            _ = AnalyzeItemAsync(item);
        }
        RunAllCommand.NotifyCanExecuteChanged();
        ClearCommand.NotifyCanExecuteChanged();
    }

    private async Task AnalyzeItemAsync(BatchItem item)
    {
        if (!FfmpegAvailable) return;
        try
        {
            item.Status = BatchStatus.Analyzing;
            item.StatusDetail = "Analyzing…";
            item.MediaInfo = await _ffmpeg.ProbeAsync(item.InputPath);
            var mi = item.MediaInfo;
            item.Status = BatchStatus.Pending;
            item.StatusDetail = $"{mi.ResolutionText} · {mi.DurationText} · {mi.SizeMb:0.0} MB";
        }
        catch (Exception ex)
        {
            item.Status = BatchStatus.Error;
            item.StatusDetail = $"Analyze failed: {ex.Message}";
            _log.LogWarning(ex, "Analyze failed for {File}", item.InputPath);
        }
    }

    // ---- Run ----

    private bool CanRun() => !IsBusy && FfmpegAvailable && Queue.Count > 0;

    private static bool IsRunnable(BatchItem i) => i.Status is BatchStatus.Pending or BatchStatus.Error;

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task RunAllAsync()
    {
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        SaveSettings();

        // If nothing is runnable, treat Run as "re-run everything".
        if (!Queue.Any(IsRunnable))
            foreach (var i in Queue) i.Status = BatchStatus.Pending;

        // Track items attempted this run so a failed file isn't retried forever
        // (Error stays "runnable" for a manual retry, but not within the same run).
        var processed = new HashSet<BatchItem>();

        try
        {
            IsBusy = true;
            int done = 0;
            OverallProgress = 0;

            // Re-scan each iteration so files dropped mid-run are picked up,
            // skipping anything already attempted in this run.
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                var item = Queue.FirstOrDefault(i => IsRunnable(i) && !processed.Contains(i));
                if (item is null) break;
                processed.Add(item);

                int remaining = Queue.Count(i => IsRunnable(i) && !processed.Contains(i));
                int total = done + 1 + remaining;
                int captured = done;
                SelectedItem = item;
                await ProcessItemAsync(item, ct, fraction =>
                    OverallProgress = total > 0 ? (captured + fraction) / total * 100 : 0);
                done++;
            }

            OverallProgress = 100;
            int ok = Queue.Count(q => q.Status == BatchStatus.Done);
            StatusText = $"Finished {ok}/{Queue.Count} file(s).";
        }
        catch (OperationCanceledException)
        {
            StatusText = "Cancelled.";
        }
        finally
        {
            IsBusy = false;
            _cts?.Dispose();
            _cts = null;
        }
    }

    private async Task ProcessItemAsync(BatchItem item, CancellationToken ct, Action<double> onFraction)
    {
        try
        {
            item.Status = BatchStatus.Running;
            item.ProgressValue = 0;

            var info = item.MediaInfo ?? await _ffmpeg.ProbeAsync(item.InputPath, ct);
            item.MediaInfo = info;

            var baseName = PathUtils.SanitizeFileName(Path.GetFileNameWithoutExtension(item.InputPath));
            var outFolder = string.IsNullOrWhiteSpace(OutputFolder)
                ? Path.GetDirectoryName(item.InputPath)!
                : OutputFolder!;

            var options = new ConvertSplitOptions
            {
                InputPath = item.InputPath,
                OutputFolder = outFolder,
                BaseName = baseName,
                Mode = SelectedMode,
                MaxBytes = (long)(MaxSizeMb * 1024 * 1024),
                PartCount = PartCount,
                SegmentSeconds = SegmentMinutes * 60,
                ConvertToMp4 = ConvertToMp4,
                AllowReencode = AllowReencode,
                ExactSplit = ExactSplit,
            };

            var progress = new Progress<double>(f =>
            {
                item.ProgressValue = f * 100;
                onFraction(f);
            });
            var status = new Progress<string>(s => item.StatusDetail = s);

            var result = await _ffmpeg.RunAsync(options, info, progress, status, ct);
            item.SetParts(result.Parts);

            if (!KeepOriginal && result.Mp4Path is not null &&
                !string.Equals(Path.GetFullPath(result.Mp4Path), Path.GetFullPath(item.InputPath), StringComparison.OrdinalIgnoreCase))
            {
                try { File.Delete(item.InputPath); } catch { /* leave original if locked */ }
            }

            item.Status = BatchStatus.Done;
            var enc = result.WasReencoded ? " (re-encoded)" : " (lossless)";
            var lim = result.AllPartsUnderLimit ? "all under limit" : "⚠ some over limit";
            item.StatusDetail = $"Done{enc} · {result.Parts.Count} part(s) · {lim}";
            item.ProgressValue = 100;
        }
        catch (OperationCanceledException)
        {
            item.Status = BatchStatus.Cancelled;
            item.StatusDetail = "Cancelled";
            throw;
        }
        catch (Exception ex)
        {
            item.Status = BatchStatus.Error;
            item.StatusDetail = $"Error: {ex.Message}";
            _log.LogError(ex, "Processing failed for {File}", item.InputPath);
        }
    }

    private bool CanCancel() => IsBusy && _cts is not null;

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel() => _cts?.Cancel();

    private bool CanClear() => !IsBusy && Queue.Count > 0;

    [RelayCommand(CanExecute = nameof(CanClear))]
    private void Clear()
    {
        Queue.Clear();
        SelectedItem = null;
        OverallProgress = 0;
        RunAllCommand.NotifyCanExecuteChanged();
        ClearCommand.NotifyCanExecuteChanged();
    }

    private bool CanRemoveSelected() => !IsBusy && SelectedItem is not null;

    [RelayCommand(CanExecute = nameof(CanRemoveSelected))]
    private void RemoveSelected()
    {
        if (SelectedItem is null) return;
        Queue.Remove(SelectedItem);
        SelectedItem = Queue.FirstOrDefault();
        RunAllCommand.NotifyCanExecuteChanged();
        ClearCommand.NotifyCanExecuteChanged();
    }

    // ---- Per-item actions ----

    private bool CanEditQueue() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanEditQueue))]
    private void RemoveItem(BatchItem? item)
    {
        if (item is null) return;
        bool wasSelected = ReferenceEquals(item, SelectedItem);
        Queue.Remove(item);
        if (wasSelected) SelectedItem = Queue.FirstOrDefault();
        RunAllCommand.NotifyCanExecuteChanged();
        ClearCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanEditQueue))]
    private void MoveUp(BatchItem? item)
    {
        if (item is null) return;
        int i = Queue.IndexOf(item);
        if (i > 0) Queue.Move(i, i - 1);
    }

    [RelayCommand(CanExecute = nameof(CanEditQueue))]
    private void MoveDown(BatchItem? item)
    {
        if (item is null) return;
        int i = Queue.IndexOf(item);
        if (i >= 0 && i < Queue.Count - 1) Queue.Move(i, i + 1);
    }

    [RelayCommand]
    private void OpenItemSource(BatchItem? item)
    {
        if (item is null || !File.Exists(item.InputPath)) return;
        // Open Explorer with the source file selected.
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"/select,\"{item.InputPath}\"",
            UseShellExecute = true,
        });
    }

    private bool CanRetryItem(BatchItem? item) => !IsBusy && FfmpegAvailable && item is not null;

    [RelayCommand(CanExecute = nameof(CanRetryItem))]
    private async Task RetryItemAsync(BatchItem? item)
    {
        if (item is null) return;
        _cts = new CancellationTokenSource();
        try
        {
            IsBusy = true;
            item.Status = BatchStatus.Pending;
            SelectedItem = item;
            OverallProgress = 0;
            await ProcessItemAsync(item, _cts.Token, f => OverallProgress = f * 100);
            OverallProgress = 100;
            StatusText = $"Retried “{item.FileName}”.";
        }
        catch (OperationCanceledException)
        {
            StatusText = "Cancelled.";
        }
        finally
        {
            IsBusy = false;
            _cts?.Dispose();
            _cts = null;
        }
    }

    [RelayCommand]
    private void BrowseOutput()
    {
        var dlg = new OpenFolderDialog { Title = "Select output folder" };
        if (!string.IsNullOrEmpty(OutputFolder)) dlg.InitialDirectory = OutputFolder;
        if (dlg.ShowDialog() == true)
            OutputFolder = dlg.FolderName;
    }

    [RelayCommand]
    private void OpenOutputFolder()
    {
        var folder = OutputFolder;
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return;
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = folder,
            UseShellExecute = true,
        });
    }

    // Keep CanExecute fresh as busy state changes.
    partial void OnIsBusyChanged(bool value)
    {
        RemoveSelectedCommand.NotifyCanExecuteChanged();
        AddFilesCommand.NotifyCanExecuteChanged();
        RemoveItemCommand.NotifyCanExecuteChanged();
        MoveUpCommand.NotifyCanExecuteChanged();
        MoveDownCommand.NotifyCanExecuteChanged();
        RetryItemCommand.NotifyCanExecuteChanged();
    }
}
