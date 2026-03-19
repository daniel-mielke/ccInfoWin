using Windows.UI;
using WinUiColors = Microsoft.UI.Colors;

namespace CCInfoWindows.Helpers;

/// <summary>
/// Hard-coded color lookup table for chart zone and axis colors by theme.
/// Provides Win2D-compatible Windows.UI.Color values without requiring ThemeResource resolution.
/// </summary>
public static class ChartColors
{
    private static readonly Dictionary<(string BrushKey, bool IsDark), Color> ColorTable = new()
    {
        // Dark theme colors
        { ("ProgressGreenBrush", true),  Color.FromArgb(255, 0x30, 0xD1, 0x58) },
        { ("ProgressYellowBrush", true), Color.FromArgb(255, 0xFF, 0xD6, 0x0A) },
        { ("ProgressOrangeBrush", true), Color.FromArgb(255, 0xFF, 0x9F, 0x0A) },
        { ("ProgressRedBrush", true),    Color.FromArgb(255, 0xFF, 0x45, 0x3A) },
        { ("ThresholdBrush", true),      Color.FromArgb(255, 0x48, 0x48, 0x4A) },
        { ("AxisLabelBrush", true),      Color.FromArgb(255, 0x8E, 0x8E, 0x93) },

        // Light theme colors
        { ("ProgressGreenBrush", false),  Color.FromArgb(255, 0x34, 0xC7, 0x59) },
        { ("ProgressYellowBrush", false), Color.FromArgb(255, 0xFF, 0xCC, 0x00) },
        { ("ProgressOrangeBrush", false), Color.FromArgb(255, 0xFF, 0x95, 0x00) },
        { ("ProgressRedBrush", false),    Color.FromArgb(255, 0xFF, 0x3B, 0x30) },
        { ("ThresholdBrush", false),      Color.FromArgb(255, 0x48, 0x48, 0x4A) },
        { ("AxisLabelBrush", false),      Color.FromArgb(255, 0x6E, 0x6E, 0x73) },
    };

    /// <summary>
    /// Returns the theme-specific color for the given brush key.
    /// Falls back to Colors.Gray for unknown keys.
    /// </summary>
    public static Color GetColor(string brushKey, bool isDark)
    {
        return ColorTable.TryGetValue((brushKey, isDark), out var color) ? color : WinUiColors.Gray;
    }

    /// <summary>
    /// Returns the zone color for a given utilization value and theme.
    /// Combines ColorThresholds.GetThresholdKey with GetColor.
    /// </summary>
    public static Color GetZoneColor(double utilization, bool isDark)
    {
        var brushKey = ColorThresholds.GetThresholdKey(utilization);
        return GetColor(brushKey, isDark);
    }
}
