using System.IO;
using System.Windows;
using System.Windows.Media;
using VidSlice.ViewModels;
using Wpf.Ui.Controls;

namespace VidSlice;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : FluentWindow
{
    public MainViewModel ViewModel { get; }

    public MainWindow(MainViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnFileDrop(object sender, DragEventArgs e)
    {
        ResetDropHighlight();
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] { Length: > 0 } files) return;

        ViewModel.AddFiles(files.Where(File.Exists));
    }

    private void OnDropZoneDragEnter(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop) && TryGetAccentBrush(out var brush))
            DropZone.BorderBrush = brush;
    }

    private void OnDropZoneDragLeave(object sender, DragEventArgs e) => ResetDropHighlight();

    private void ResetDropHighlight() => DropZone.BorderBrush = Brushes.Transparent;

    private static bool TryGetAccentBrush(out Brush brush)
    {
        // Use the app accent if available, else a sensible fallback blue.
        if (Application.Current.TryFindResource("SystemAccentColorPrimaryBrush") is Brush accent)
        {
            brush = accent;
            return true;
        }
        brush = new SolidColorBrush(Color.FromRgb(0x2D, 0x7F, 0xF9));
        return true;
    }

    private void OnCycleTheme(object sender, RoutedEventArgs e)
    {
        // Cycle System -> Light -> Dark -> System.
        var next = ViewModel.Theme switch
        {
            "System" => "Light",
            "Light" => "Dark",
            _ => "System",
        };
        ViewModel.Theme = next;
        App.ApplyTheme(next);
        ViewModel.SaveSettings();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        ViewModel.SaveSettings();
        base.OnClosing(e);
    }
}
