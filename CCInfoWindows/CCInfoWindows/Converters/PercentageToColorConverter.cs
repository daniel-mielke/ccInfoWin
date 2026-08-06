using CCInfoWindows.Helpers;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;

namespace CCInfoWindows.Converters;

/// <summary>
/// Converts a utilization value (0.0-1.0) to the threshold brush for that zone.
///
/// The colours come from <see cref="ChartColors"/> keyed by the app's own element theme, not from
/// a direct <c>Application.Current.Resources</c> lookup. That lookup resolved the four
/// Progress*Brush keys out of the ThemeDictionaries selected by
/// <c>Application.RequestedTheme</c> — the OS theme, which this app never sets — so with Windows in
/// Light and the app switched to Dark every progress bar kept the Light palette while every
/// {ThemeResource} around it followed the app. ChartColors mirrors Resources/AppTheme.xaml exactly
/// (pinned by ChartColorsTests.ProgressBrushes_MatchAppThemeXaml) and already drives the chart, so
/// the bars and the chart's glow indicator now agree on the same green.
///
/// Known remaining limitation: x:Bind OneWay does not re-evaluate on ActualThemeChanged, so a
/// theme toggle is picked up on the next utilization update rather than immediately.
/// </summary>
public class PercentageToColorConverter : IValueConverter
{
    private static readonly SolidColorBrush DefaultBrush = new(Colors.Gray);

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is not double utilization)
            return DefaultBrush;

        // Fresh brush per evaluation: a static cache would be shared mutable state for three
        // bindings that re-evaluate once per poll.
        return new SolidColorBrush(ChartColors.GetZoneColor(utilization, IsAppThemeDark()));
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotSupportedException();
    }

    /// <summary>
    /// Reads the theme off the root element the app actually themes (App.ApplyPersistedTheme and
    /// MainWindow's theme-change handler both set RequestedTheme there). Dark on failure because
    /// that is the app's default for any ColorMode other than "light".
    /// </summary>
    private static bool IsAppThemeDark() =>
        App.MainWindow?.Content is not FrameworkElement root || root.ActualTheme == ElementTheme.Dark;
}
