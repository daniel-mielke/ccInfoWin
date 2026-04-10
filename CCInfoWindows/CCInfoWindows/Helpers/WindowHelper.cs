using CCInfoWindows.Models;
using Microsoft.UI.Windowing;
using Windows.Graphics;

namespace CCInfoWindows.Helpers;

/// <summary>
/// Utility methods for window position validation and sizing.
/// </summary>
public static class WindowHelper
{
    private const int DefaultWidthDips = 360;
    private const int DefaultHeightDips = 980;

    /// <summary>
    /// Validates that a saved window position is visible on at least one connected display.
    /// Returns false if the window would be entirely off-screen (e.g., after monitor disconnect).
    /// </summary>
    public static bool IsPositionOnScreen(WindowState state)
    {
        try
        {
            var point = new PointInt32(state.X + state.Width / 2, state.Y + state.Height / 2);
            var displayArea = DisplayArea.GetFromPoint(point, DisplayAreaFallback.None);
            return displayArea != null;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Returns the default window size (360x980 DIPs) scaled to physical pixels for the given DPI.
    /// AppWindow.Resize() requires physical pixels, not DIPs.
    /// </summary>
    public static SizeInt32 GetDefaultWindowSize(double dpiScale = 1.0)
    {
        return new SizeInt32(
            (int)(DefaultWidthDips * dpiScale),
            (int)(DefaultHeightDips * dpiScale));
    }
}
