using CCInfoWindows.Helpers;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;

namespace CCInfoWindows.Converters;

/// <summary>
/// Converts a utilization value (0.0-1.0) to a SolidColorBrush from theme resources.
/// Uses ColorThresholds to determine the appropriate brush key.
/// </summary>
public class PercentageToColorConverter : IValueConverter
{
    private static readonly SolidColorBrush DefaultBrush = new(Colors.Gray);

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is not double utilization)
            return DefaultBrush;

        var brushKey = ColorThresholds.GetThresholdKey(utilization);

        if (Application.Current.Resources.TryGetValue(brushKey, out var resource) && resource is SolidColorBrush brush)
            return brush;

        return DefaultBrush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotSupportedException();
    }
}
