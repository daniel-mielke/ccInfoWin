using Microsoft.UI.Xaml.Data;

namespace CCInfoWindows.Converters;

/// <summary>
/// Returns 0.5 opacity for orphan rows (IsOrphan == true) and 1.0 for live sessions.
/// Used in the Settings Sessions tab to visually distinguish orphan custom names (D-08 / RENAME-06).
/// </summary>
public class OrphanOpacityConverter : IValueConverter
{
    private const double OrphanOpacity = 0.5;
    private const double NormalOpacity = 1.0;

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        return value is bool isOrphan && isOrphan ? OrphanOpacity : NormalOpacity;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotSupportedException();
    }
}
