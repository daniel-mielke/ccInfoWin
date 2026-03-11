using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace CCInfoWindows.Converters;

/// <summary>
/// Converts a boolean value to Visibility (true = Collapsed, false = Visible).
/// Inverse of BoolToVisibilityConverter, used to show placeholder UI when a feature is inactive.
/// </summary>
public class InvertedBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is bool boolValue)
        {
            return boolValue ? Visibility.Collapsed : Visibility.Visible;
        }

        return Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        if (value is Visibility visibility)
        {
            return visibility == Visibility.Collapsed;
        }

        return true;
    }
}
