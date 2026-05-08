using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace CCInfoWindows.Converters;

/// <summary>
/// Returns Visible when the bound integer value is zero, Collapsed otherwise.
/// Used in the Settings Sessions tab empty-state placeholder (SessionRenameItems.Count == 0).
/// </summary>
public class ZeroToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        return value is int count && count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotSupportedException();
    }
}
