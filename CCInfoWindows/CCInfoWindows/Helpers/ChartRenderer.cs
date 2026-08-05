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
    /// Padding reserved inside the plot area on all four sides so the glow indicator is never
    /// clipped at an edge.
    ///
    /// The mechanically "correct" value is outer radius + 3σ = 9 + 9 = 18px, but that spends 31%
    /// of a 118px plot height on pixels nobody can see: at 3σ the Gaussian weight is about 1% of
    /// the maximum, which against 115/255 source alpha lands below 1/255. The perceptually
    /// clamping radius is roughly 1.5σ, i.e. 9 + 4.5 = 13.5. 11px = outer radius 9 plus 2px
    /// protects the disc and its visible blur shoulder at 19% cost.
    ///
    /// Do not "fix" this to 18 by re-deriving the formula -- the formula is not the constraint.
    /// </summary>
    public const float GlowInset = 11f;

    /// <summary>
    /// Horizontal extent available to data points after reserving the glow inset on both sides.
    /// Clamped to >= 1 because ActualWidth is 0 during the first layout pass, which would
    /// otherwise produce a negative span and NaN coordinates.
    /// </summary>
    public static float PlotSpanWidth(float plotWidth) => Math.Max(1f, plotWidth - (2f * GlowInset));

    /// <summary>
    /// Vertical extent available to data points after reserving the glow inset on both sides.
    /// </summary>
    public static float PlotBandHeight(float plotHeight) => Math.Max(1f, plotHeight - (2f * GlowInset));

    /// <summary>
    /// Maps a timestamp to an X pixel coordinate within the 5-hour plot area.
    /// Clamped to [GlowInset, GlowInset + PlotSpanWidth].
    /// </summary>
    public static float ToX(DateTimeOffset timestamp, DateTimeOffset windowStart, float plotWidth)
    {
        var elapsed = (timestamp - windowStart).TotalSeconds;
        var ratio = elapsed / WindowDurationSeconds;
        return GlowInset + (float)(Math.Clamp(ratio, 0.0, 1.0) * PlotSpanWidth(plotWidth));
    }

    /// <summary>
    /// Maps a utilization value (0.0-1.0) to a Y pixel coordinate.
    /// 0.0 maps to the bottom of the inset band, 1.0 to its top. Values are clamped.
    /// </summary>
    public static float ToY(double utilization, float plotHeight)
    {
        return TopMargin + GlowInset
            + (float)((1.0 - Math.Clamp(utilization, 0.0, 1.0)) * PlotBandHeight(plotHeight));
    }

    /// <summary>
    /// Returns the canvas-absolute X coordinate of the right edge for a span.
    /// For mid-span ends, the right edge is the next point's X position.
    /// For the last span, the right edge is the current time.
    /// The returned value already includes LeftMargin -- use directly in path calls.
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

        return LeftMargin + ToX(DateTimeOffset.UtcNow, windowStart, plotWidth);
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
    /// Drops points that would land closer than <paramref name="minSpacing"/> pixels to the
    /// previously kept point, guaranteeing strictly increasing X in the result.
    ///
    /// Two reasons this has to run BEFORE the tangent calculation:
    ///   - Correctness: ToX clamps to the window edge, so two samples past the 5-hour mark map
    ///     to the same X. dx == 0 was harmless for a step curve (a zero-length segment) but is a
    ///     division by zero in ComputeMonotoneTangents, i.e. NaN straight into the Win2D path.
    ///   - Fidelity: smoothing first and discarding points afterwards destroys exactly the
    ///     monotonicity guarantee Fritsch-Carlson was chosen for.
    ///
    /// It also does the downsampling. A 5-hour window polled every 30s holds ~600 points on a
    /// ~280px plot -- more than two per pixel, and every one of them also becomes a gradient stop.
    /// </summary>
    public static List<UsageHistoryPoint> FilterByMinSpacing(
        IReadOnlyList<UsageHistoryPoint> points,
        DateTimeOffset windowStart,
        float plotWidth,
        float minSpacing = 1f)
    {
        var result = new List<UsageHistoryPoint>();
        if (points.Count == 0) return result;

        result.Add(points[0]);
        var lastKeptIndex = 0;
        var lastX = ToX(points[0].Timestamp, windowStart, plotWidth);

        for (var i = 1; i < points.Count; i++)
        {
            var x = ToX(points[i].Timestamp, windowStart, plotWidth);
            if (x - lastX < minSpacing) continue;

            result.Add(points[i]);
            lastKeptIndex = i;
            lastX = x;
        }

        // The newest sample positions the glow indicator and the right edge, so it must always
        // survive. When it collapses into the previous pixel bucket it replaces the kept point
        // instead of being appended -- appending would reintroduce dx == 0.
        if (lastKeptIndex != points.Count - 1)
        {
            result[^1] = points[^1];
        }

        return result;
    }

    /// <summary>
    /// Fritsch-Carlson monotone tangents for cubic Hermite interpolation.
    ///
    /// Catmull-Rom and natural splines overshoot: between 95% and 60% the curve bulges past 100%
    /// and gets clipped at the top gridline. Fritsch-Carlson limits the tangents so the
    /// interpolant stays monotone wherever the data is monotone, which keeps it inside the
    /// enclosing sample values without any after-the-fact clamping.
    ///
    /// Precondition: <paramref name="xs"/> is strictly increasing. Route input through
    /// <see cref="FilterByMinSpacing"/>, which is the single guarantee of that.
    /// </summary>
    public static double[] ComputeMonotoneTangents(IReadOnlyList<double> xs, IReadOnlyList<double> ys)
    {
        var n = xs.Count;
        if (n == 0) return [];
        if (n == 1) return [0.0];

        var secants = new double[n - 1];
        for (var i = 0; i < n - 1; i++)
        {
            secants[i] = (ys[i + 1] - ys[i]) / (xs[i + 1] - xs[i]);
        }

        var m = new double[n];
        m[0] = secants[0];
        m[n - 1] = secants[n - 2];
        for (var i = 1; i < n - 1; i++)
        {
            m[i] = (secants[i - 1] + secants[i]) / 2.0;
        }

        for (var i = 0; i < n - 1; i++)
        {
            if (secants[i] == 0.0)
            {
                // Plateau: both ends flat, otherwise the curve would bow off a horizontal run.
                m[i] = 0.0;
                m[i + 1] = 0.0;
                continue;
            }

            var a = m[i] / secants[i];
            var b = m[i + 1] / secants[i];

            // A tangent pointing against the secant marks a local extremum -- flatten it.
            if (a < 0.0) { m[i] = 0.0; a = 0.0; }
            if (b < 0.0) { m[i + 1] = 0.0; b = 0.0; }

            // Fritsch-Carlson circle condition: keep (a, b) inside the radius-3 circle.
            var sumOfSquares = (a * a) + (b * b);
            if (sumOfSquares > 9.0)
            {
                var scale = 3.0 / Math.Sqrt(sumOfSquares);
                m[i] = scale * a * secants[i];
                m[i + 1] = scale * b * secants[i];
            }
        }

        return m;
    }

    /// <summary>
    /// Converts one Hermite segment into the two cubic Bezier control points Win2D needs.
    ///
    /// The dx/3 factor is load-bearing: dx/2 still looks smooth but no longer honours the
    /// tangents, which brings the overshoot back. Pinned by a test.
    /// </summary>
    public static ((double X, double Y) C1, (double X, double Y) C2) ToBezierControlPoints(
        double x0, double y0, double m0,
        double x1, double y1, double m1)
    {
        var third = (x1 - x0) / 3.0;
        return (
            (x0 + third, y0 + (m0 * third)),
            (x1 - third, y1 - (m1 * third)));
    }

    /// <summary>
    /// Builds gradient stop tuples for a span of data points.
    /// Positions are normalized to [0, 1] within the span (not the full chart width), so the
    /// glow insets cancel out and do not shift the colors.
    /// Colors are looked up from the pre-built colorLookup array by utilization index.
    /// Return type is plain C# tuples — no Win2D dependency. Conversion to CanvasGradientStop
    /// happens in ChartDrawing.
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
