using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace CCInfoWindows.Converters;

/// <summary>
/// Converts a boolean value to Visibility (true = Collapsed, false = Visible).
/// Inverse of BoolToVisibilityConverter, used to show placeholder UI when a feature is inactive.
///
/// Implemented by negating and delegating rather than by restating the Visible/Collapsed literals:
/// the two converters are used as a matched pair on sibling elements bound to the same flag (a
/// statistics value and its shimmer placeholder, a subagent bar and its workflow label), so their
/// answers — including the one they give for a null or non-bool value — have to stay exact
/// complements. Delegation makes that structural instead of coincidental.
/// </summary>
public class InvertedBoolToVisibilityConverter : IValueConverter
{
    private static readonly BoolToVisibilityConverter Inner = new();

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        // A non-bool value maps to true here, which the inner converter turns into Visible — the
        // fallback this converter has always had, and the complement of the inner one's Collapsed.
        return Inner.Convert(value is bool boolValue ? !boolValue : true, targetType, parameter, language);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        return !(bool)Inner.ConvertBack(value, targetType, parameter, language);
    }
}
