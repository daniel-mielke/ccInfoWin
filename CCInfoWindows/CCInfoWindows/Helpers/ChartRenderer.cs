using CCInfoWindows.Models;

namespace CCInfoWindows.Helpers;

/// <summary>
/// Pure coordinate math for the area chart. No Win2D dependency -- only numeric calculations.
/// </summary>
public static class ChartRenderer
{
    public const float LeftMargin = 22f;
    public const float BottomMargin = 16f;
    public const double WindowDurationSeconds = 5 * 60 * 60;

    /// <summary>
    /// Maps a timestamp to an X pixel coordinate within the 5-hour plot area.
    /// Clamped to [0, plotWidth].
    /// </summary>
    public static float ToX(DateTimeOffset timestamp, DateTimeOffset windowStart, float plotWidth)
    {
        var elapsed = (timestamp - windowStart).TotalSeconds;
        var ratio = elapsed / WindowDurationSeconds;
        return (float)(Math.Clamp(ratio, 0.0, 1.0) * plotWidth);
    }

    /// <summary>
    /// Maps a utilization value (0.0-1.0) to a Y pixel coordinate.
    /// 0.0 maps to plotHeight (bottom), 1.0 maps to 0 (top). Values are clamped.
    /// </summary>
    public static float ToY(double utilization, float plotHeight)
    {
        return (float)((1.0 - Math.Clamp(utilization, 0.0, 1.0)) * plotHeight);
    }

    /// <summary>
    /// Returns the canvas-absolute X coordinate of the right edge for a zone segment.
    /// For mid-segment ends, the right edge is the next point's X position.
    /// For the last segment, the right edge is the current time clamped to plot bounds.
    /// The returned value already includes LeftMargin -- use directly in AddLine calls.
    /// </summary>
    public static float GetRightEdgeAbsoluteX(
        IReadOnlyList<UsageHistoryPoint> points,
        int endIndex,
        DateTimeOffset windowStart,
        float plotWidth)
    {
        if (endIndex < points.Count - 1)
        {
            return LeftMargin + ToX(points[endIndex + 1].Timestamp, windowStart, plotWidth);
        }
        var nowX = ToX(DateTimeOffset.UtcNow, windowStart, plotWidth);
        return LeftMargin + Math.Min(nowX, plotWidth);
    }

    /// <summary>
    /// Groups consecutive data points by color zone (from ColorThresholds).
    /// Returns a list of (StartIndex, EndIndex, BrushKey) tuples.
    /// </summary>
    public static List<(int StartIndex, int EndIndex, string BrushKey)> GetZoneSegments(
        IReadOnlyList<UsageHistoryPoint> points)
    {
        var segments = new List<(int StartIndex, int EndIndex, string BrushKey)>();
        if (points.Count == 0) return segments;

        var segmentStart = 0;
        var currentZone = ColorThresholds.GetThresholdKey(points[0].Utilization);

        for (var i = 1; i < points.Count; i++)
        {
            var zone = ColorThresholds.GetThresholdKey(points[i].Utilization);
            if (zone != currentZone)
            {
                segments.Add((segmentStart, i - 1, currentZone));
                segmentStart = i;
                currentZone = zone;
            }
        }

        segments.Add((segmentStart, points.Count - 1, currentZone));
        return segments;
    }
}
