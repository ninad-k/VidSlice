using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;

namespace VidSlice.Models;

public enum BatchStatus { Pending, Analyzing, Running, Done, Error, Cancelled }

/// <summary>One queued input file, with its own status, progress and results.</summary>
public partial class BatchItem : ObservableObject
{
    public BatchItem(string inputPath)
    {
        InputPath = inputPath;
        FileName = Path.GetFileName(inputPath);
    }

    public string InputPath { get; }
    public string FileName { get; }

    [ObservableProperty] private MediaInfo? _mediaInfo;
    [ObservableProperty] private BatchStatus _status = BatchStatus.Pending;
    [ObservableProperty] private double _progressValue;
    [ObservableProperty] private string _statusDetail = "Pending";

    public ObservableCollection<PartResult> Parts { get; } = [];

    public bool AllPartsUnderLimit => Parts.Count > 0 && Parts.All(p => p.UnderLimit);

    public void SetParts(IEnumerable<PartResult> parts)
    {
        Parts.Clear();
        foreach (var p in parts) Parts.Add(p);
        OnPropertyChanged(nameof(AllPartsUnderLimit));
    }

    /// <summary>
    /// Marshal change notifications to the UI thread. This makes the item safe to
    /// mutate from a background thread regardless of how the service awaits, so a
    /// future ConfigureAwait(false) can't trigger cross-thread binding exceptions.
    /// </summary>
    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
            dispatcher.Invoke(() => base.OnPropertyChanged(e));
        else
            base.OnPropertyChanged(e);
    }
}
