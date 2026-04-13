using CCInfoWindows.Models;
using Windows.UI;

namespace CCInfoWindows.Helpers;

/// <summary>
/// Pure coordinate math for the area chart. No Win2D dependency -- only numeric calculations.
/// </summary>
public static class ChartRenderer
{
    public const float LeftMargin = 22f;
    public const float TopMargin = 10f;
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
    /// 0.0 maps to plotHeight (bottom), 1.0 maps to TopMargin (top). Values are clamped.
    /// </summary>
    public static float ToY(double utilization, float plotHeight)
    {
        return TopMargin + (float)((1.0 - Math.Clamp(utilization, 0.0, 1.0)) * plotHeight);
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
    [Obsolete("Use GetContiguousSpans — zone-based segmentation replaced by continuous gradient in Phase 17")]
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

    /// <summary>
    /// Returns all data points as a single contiguous span.
    /// Since UsageHistoryPoint has no IsGap field, all points are always contiguous.
    /// Returns an empty list for empty input. Signature ready for future gap support.
    /// </summary>
    public static List<(int StartIndex, int EndIndex)> GetContiguousSpans(
        IReadOnlyList<UsageHistoryPoint> points)
    {
        if (points.Count == 0) return [];
        return [(0, points.Count - 1)];
    }

    /// <summary>
    /// Builds gradient stop tuples for a span of data points.
    /// Positions are normalized to [0, 1] within the span (not the full chart width).
    /// Colors are looked up from the pre-built colorLookup array by utilization index.
    /// Return type is plain C# tuples — no Win2D dependency. Conversion to CanvasGradientStop
    /// happens in ChartDrawing (Phase 17, Plan 02).
    /// </summary>
    public static (float Position, Color Color)[] BuildGradientStops(
        IReadOnlyList<UsageHistoryPoint> points,
        int startIndex,
        int endIndex,
        DateTimeOffset windowStart,
        float plotWidth,
        Color[] colorLookup)
    {
        var spanStartX = ToX(points[startIndex].Timestamp, windowStart, plotWidth);
        var spanEndX = ToX(points[endIndex].Timestamp, windowStart, plotWidth);
        var spanWidth = spanEndX - spanStartX;

        if (spanWidth <= 0f) spanWidth = 1f;

        var stops = new List<(float Position, Color Color)>();

        for (var i = startIndex; i <= endIndex; i++)
        {
            var x = ToX(points[i].Timestamp, windowStart, plotWidth);
            var position = Math.Clamp((x - spanStartX) / spanWidth, 0f, 1f);
            var colorIndex = (int)Math.Clamp(points[i].Utilization * 100.0, 0, 100);
            stops.Add((position, colorLookup[colorIndex]));
        }

        if (stops.Count == 1)
        {
            stops[0] = (0.0f, stops[0].Color);
        }
        else if (stops.Count > 1)
        {
            stops[0] = (0.0f, stops[0].Color);
            stops[^1] = (1.0f, stops[^1].Color);
        }

        return [.. stops];
    }
}
