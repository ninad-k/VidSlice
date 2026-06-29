using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace VidSlice.Converters;

/// <summary>
/// True → Collapsed, False → Visible. Used to show a hint only when a feature
/// (e.g. whisper) is unavailable.
/// </summary>
public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b && b ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is Visibility v && v != Visibility.Visible;
}
