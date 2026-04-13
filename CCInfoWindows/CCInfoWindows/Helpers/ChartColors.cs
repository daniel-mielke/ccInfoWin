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

    /// <summary>
    /// Builds a 101-element color array for the gradient lookup table.
    /// Index i represents utilization i% (0 to 100), interpolated across 4 gradient stops.
    /// Values beyond 90% are clamped to the red stop color. Alpha is always 255.
    /// </summary>
    public static Color[] BuildColorLookup(bool isDark)
    {
        var stops = new (double Position, Color Color)[]
        {
            (0.00, GetColor("ProgressGreenBrush", isDark)),
            (0.50, GetColor("ProgressYellowBrush", isDark)),
            (0.75, GetColor("ProgressOrangeBrush", isDark)),
            (0.90, GetColor("ProgressRedBrush", isDark)),
        };

        var lookup = new Color[101];
        for (var i = 0; i <= 100; i++)
        {
            var t = i / 100.0;
            lookup[i] = InterpolateColor(stops, t);
        }

        return lookup;
    }

    private static Color InterpolateColor((double Position, Color Color)[] stops, double t)
    {
        var clamped = Math.Clamp(t, 0.0, 1.0);

        for (var i = 0; i < stops.Length - 1; i++)
        {
            var current = stops[i];
            var next = stops[i + 1];

            if (clamped <= next.Position)
            {
                var segmentLength = next.Position - current.Position;
                var segmentT = segmentLength > 0.0
                    ? (clamped - current.Position) / segmentLength
                    : 0.0;
                return LerpColor(current.Color, next.Color, segmentT);
            }
        }

        return stops[^1].Color;
    }

    private static Color LerpColor(Color a, Color b, double t)
    {
        return Color.FromArgb(
            255,
            (byte)(a.R + (b.R - a.R) * t),
            (byte)(a.G + (b.G - a.G) * t),
            (byte)(a.B + (b.B - a.B) * t));
    }
}
