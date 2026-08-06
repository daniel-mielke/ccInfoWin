using System.Numerics;
using CCInfoWindows.Models;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Brushes;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.Graphics.Canvas.Text;
using Windows.Foundation;
using Windows.UI;

namespace CCInfoWindows.Helpers;

/// <summary>
/// Win2D chart drawing methods shared by MainView (live chart) and ExportHelper (PNG export).
/// All methods accept offsetX/offsetY to support rendering at arbitrary canvas positions.
/// </summary>
public static class ChartDrawing
{
    // Fill fade, applied vertically from the plot top down to the baseline. The hue gradient
    // runs horizontally at full alpha and this modulates it, so the area under the curve dies
    // away toward the baseline instead of sitting there as a flat slab.
    private const byte FillAlphaAtTop = 96;
    private const byte FillAlphaAtBaseline = 10;

    private const float GlowOuterRadius = 9f;
    private const float GlowBlurAmount = 3.0f;
    private const byte GlowOuterAlpha = 115;
    private const float GlowCoreRadius = 4.5f;
    private const float GlowWhiteCoreRadius = 2f;

    /// <summary>
    /// Slightly translucent so a hint of the zone hue bleeds through — a red indicator should
    /// still read as red-hot rather than as a white dot.
    /// </summary>
    private const byte GlowWhiteCoreAlpha = 235;

    /// <summary>Breathing room between the percentage label and the gridline it belongs to.</summary>
    public const float AxisLabelGutter = 4f;

    /// <summary>Width the percentage labels are laid out in. Below the widest label they wrap.</summary>
    public static float AxisLabelRectWidth => ChartRenderer.LeftMargin - AxisLabelGutter;

    private const float HourLabelHalfWidth = 20f;
    private const float HourLabelHeight = 14f;
    private const float HourLabelTopGap = 2f;

    private static readonly CanvasStrokeStyle DashStrokeStyle = new()
    {
        CustomDashStyle = [4f, 4f]
    };

    /// <summary>Percentage labels: right-aligned in the left gutter, centred on their gridline.</summary>
    private static readonly CanvasTextFormat AxisLabelFormat = new()
    {
        FontFamily = "Segoe UI Variable",
        FontSize = 10f,
        HorizontalAlignment = CanvasHorizontalAlignment.Right,
        VerticalAlignment = CanvasVerticalAlignment.Center
    };

    /// <summary>Hour ticks: centred on their tick position, which removes the old 5h edge hack.</summary>
    private static readonly CanvasTextFormat HourLabelFormat = new()
    {
        FontFamily = "Segoe UI Variable",
        FontSize = 10f,
        HorizontalAlignment = CanvasHorizontalAlignment.Center,
        VerticalAlignment = CanvasVerticalAlignment.Top
    };

    /// <summary>
    /// Fill and line path for one chart, built from a single tangent calculation so the two can
    /// never drift apart, plus the anchors the gradient brushes need.
    /// </summary>
    public sealed class ChartGeometry : IDisposable
    {
        public required CanvasGeometry Fill { get; init; }
        public required CanvasGeometry Line { get; init; }
        public required float SpanStartX { get; init; }

        /// <summary>Canvas-absolute right edge — the brush endpoint and the flat extension use it.</summary>
        public required float SpanEndX { get; init; }

        /// <summary>
        /// The same right edge in plot-relative ChartRenderer.ToX space, which is what
        /// BuildGradientStops normalises against. Carried on the geometry rather than recomputed so
        /// both consumers see one DateTimeOffset.UtcNow sample.
        /// </summary>
        public required float SpanEndPlotX { get; init; }

        public required float BaselineY { get; init; }
        public required float PlotTopY { get; init; }

        /// <summary>Points after min-spacing filtering — the gradient stops must match the curve.</summary>
        public required IReadOnlyList<UsageHistoryPoint> Points { get; init; }

        public void Dispose()
        {
            Fill.Dispose();
            Line.Dispose();
        }
    }

    public static void DrawAxesAndLabels(
        CanvasDrawingSession session,
        float plotWidth,
        float plotHeight,
        bool isDark,
        float offsetX = 0f,
        float offsetY = 0f)
    {
        var thresholdColor = ChartColors.GetColor("ThresholdBrush", isDark);
        var labelColor = ChartColors.GetColor("AxisLabelBrush", isDark);
        var lineStart = offsetX + ChartRenderer.LeftMargin;
        var lineEnd = offsetX + ChartRenderer.LeftMargin + plotWidth;

        var y0 = offsetY + ChartRenderer.ToY(0.0, plotHeight);
        var y50 = offsetY + ChartRenderer.ToY(0.5, plotHeight);
        var y100 = offsetY + ChartRenderer.ToY(1.0, plotHeight);
        session.DrawLine(lineStart, y0, lineEnd, y0, thresholdColor, 1f, DashStrokeStyle);
        session.DrawLine(lineStart, y50, lineEnd, y50, thresholdColor, 1f, DashStrokeStyle);
        session.DrawLine(lineStart, y100, lineEnd, y100, thresholdColor, 1f, DashStrokeStyle);

        DrawCenteredAxisLabel(session, "100%", offsetX, y100, labelColor);
        DrawCenteredAxisLabel(session, "50%", offsetX, y50, labelColor);
        DrawCenteredAxisLabel(session, "0%", offsetX, y0, labelColor);

        for (var hour = 0; hour <= 5; hour++)
        {
            // Same mapping ToX applies, so the ticks stay glued to the data when the inset changes.
            var x = offsetX + ChartRenderer.LeftMargin + ChartRenderer.GlowInset
                + ((hour / 5f) * ChartRenderer.PlotSpanWidth(plotWidth));

            var rect = new Rect(
                x - HourLabelHalfWidth, y0 + HourLabelTopGap,
                HourLabelHalfWidth * 2f, HourLabelHeight);
            session.DrawText($"{hour}h", rect, labelColor, HourLabelFormat);
        }
    }

    /// <summary>
    /// Builds the fill and line geometry for the whole point set. Returns null for empty input.
    /// Callers own the result and must dispose it.
    /// </summary>
    public static ChartGeometry? BuildChartGeometry(
        ICanvasResourceCreator resourceCreator,
        IReadOnlyList<UsageHistoryPoint> points,
        DateTimeOffset windowStart,
        float plotWidth,
        float plotHeight,
        float offsetX = 0f,
        float offsetY = 0f)
    {
        // Single guarantee of strictly increasing X — ComputeMonotoneTangents divides by dx.
        var filtered = ChartRenderer.FilterByMinSpacing(points, windowStart, plotWidth);
        if (filtered.Count == 0) return null;

        var count = filtered.Count;
        var xs = new double[count];
        var ys = new double[count];
        for (var i = 0; i < count; i++)
        {
            xs[i] = offsetX + ChartRenderer.LeftMargin
                + ChartRenderer.ToX(filtered[i].Timestamp, windowStart, plotWidth);
            ys[i] = offsetY + ChartRenderer.ToY(filtered[i].Utilization, plotHeight);
        }

        var baselineY = offsetY + ChartRenderer.ToY(0.0, plotHeight);
        var plotTopY = offsetY + ChartRenderer.ToY(1.0, plotHeight);
        var rightEdgePlotX = ChartRenderer.GetRightEdgeX(filtered, count - 1, windowStart, plotWidth);
        var rightEdgeX = offsetX + ChartRenderer.LeftMargin + rightEdgePlotX;
        var lastY = (float)ys[count - 1];

        // Screen space (y grows downward). Monotonicity survives the affine flip, so there is no
        // reason to compute in data space and convert afterwards.
        var tangents = ChartRenderer.ComputeMonotoneTangents(xs, ys);

        using var linePath = new CanvasPathBuilder(resourceCreator);
        linePath.BeginFigure((float)xs[0], (float)ys[0]);
        AppendCurve(linePath, xs, ys, tangents);
        linePath.AddLine(rightEdgeX, lastY);
        linePath.EndFigure(CanvasFigureLoop.Open);

        using var fillPath = new CanvasPathBuilder(resourceCreator);
        // No baseline run from the window origin to the first sample any more: with the
        // horizontal inset xs[0] IS the window origin plus inset, so the isolated mid-plot riser
        // that run was added to avoid cannot occur — and a baseline run plus riser is exactly the
        // staircase artefact this redesign removes.
        fillPath.BeginFigure((float)xs[0], baselineY);
        fillPath.AddLine((float)xs[0], (float)ys[0]);
        AppendCurve(fillPath, xs, ys, tangents);
        fillPath.AddLine(rightEdgeX, lastY);
        fillPath.AddLine(rightEdgeX, baselineY);
        fillPath.EndFigure(CanvasFigureLoop.Closed);

        return new ChartGeometry
        {
            Fill = CanvasGeometry.CreatePath(fillPath),
            Line = CanvasGeometry.CreatePath(linePath),
            SpanStartX = (float)xs[0],
            SpanEndX = rightEdgeX,
            SpanEndPlotX = rightEdgePlotX,
            BaselineY = baselineY,
            PlotTopY = plotTopY,
            Points = filtered
        };
    }

    /// <summary>
    /// Draws fill, top line and glow indicator from a single geometry build, so the filled area
    /// and the stroked curve cannot drift apart and the tangents are computed once per frame.
    /// No-op for an empty point set.
    /// </summary>
    public static void DrawChart(
        CanvasDrawingSession session,
        ICanvasResourceCreator resourceCreator,
        IReadOnlyList<UsageHistoryPoint> points,
        DateTimeOffset windowStart,
        float plotWidth,
        float plotHeight,
        bool isDark,
        float offsetX = 0f,
        float offsetY = 0f,
        float lineWidth = 2.0f)
    {
        using var geometry = BuildChartGeometry(
            resourceCreator, points, windowStart, plotWidth, plotHeight, offsetX, offsetY);
        if (geometry is null) return;

        // One hue gradient serves both: the fill modulates it with a vertical opacity brush,
        // the line strokes with it directly.
        using var hueBrush = BuildHueBrush(resourceCreator, geometry, windowStart, plotWidth, isDark);

        // White-with-alpha rather than black-with-alpha: D2D reads only the alpha channel here,
        // but white stays correct if these stops ever end up in an effect where RGB matters.
        var fadeStops = new[]
        {
            new CanvasGradientStop { Position = 0f, Color = Color.FromArgb(FillAlphaAtTop, 255, 255, 255) },
            new CanvasGradientStop { Position = 1f, Color = Color.FromArgb(FillAlphaAtBaseline, 255, 255, 255) }
        };

        using (var fadeBrush = new CanvasLinearGradientBrush(
            resourceCreator, fadeStops, CanvasEdgeBehavior.Clamp, CanvasAlphaMode.Premultiplied)
        {
            StartPoint = new Vector2(0f, geometry.PlotTopY),
            EndPoint = new Vector2(0f, geometry.BaselineY)
        })
        {
            // Two gradients in one draw call: hue horizontally, alpha vertically. No intermediate
            // surfaces, no effect graph, DPI-independent because both are evaluated in geometry
            // coordinates. Runner-up if this ever breaks: session.CreateLayer(fadeBrush) around a
            // plain two-argument FillGeometry.
            session.FillGeometry(geometry.Fill, hueBrush, fadeBrush);
        }

        session.DrawGeometry(geometry.Line, hueBrush, lineWidth);

        DrawGlowIndicator(session, resourceCreator, geometry, plotHeight, isDark, offsetY);
    }

    /// <summary>
    /// Three layers: a blurred halo, the solid zone disc, and a near-white core. The core is
    /// alpha 235 rather than opaque so a hint of the zone hue bleeds through and a red indicator
    /// still reads as red-hot.
    /// </summary>
    private static void DrawGlowIndicator(
        CanvasDrawingSession session,
        ICanvasResourceCreator resourceCreator,
        ChartGeometry geometry,
        float plotHeight,
        bool isDark,
        float offsetY)
    {
        var lastPoint = geometry.Points[^1];
        var x = geometry.SpanEndX;
        var y = offsetY + ChartRenderer.ToY(lastPoint.Utilization, plotHeight);
        var zoneColor = ChartColors.GetZoneColor(lastPoint.Utilization, isDark);
        var glowColor = Color.FromArgb(GlowOuterAlpha, zoneColor.R, zoneColor.G, zoneColor.B);

        using var commandList = new CanvasCommandList(resourceCreator);
        using (var clSession = commandList.CreateDrawingSession())
        {
            clSession.FillCircle(x, y, GlowOuterRadius, glowColor);
        }

        using var blurEffect = new GaussianBlurEffect
        {
            Source = commandList,
            BlurAmount = GlowBlurAmount
        };
        session.DrawImage(blurEffect);

        session.FillCircle(x, y, GlowCoreRadius, zoneColor);
        session.FillCircle(x, y, GlowWhiteCoreRadius,
            Color.FromArgb(GlowWhiteCoreAlpha, 255, 255, 255));
    }

    /// <summary>
    /// Horizontal hue gradient across the span, at full alpha. The fill modulates it with a
    /// separate vertical opacity brush; the line uses it directly.
    ///
    /// The stops are normalised against the geometry's own right edge, so a sample's colour lands
    /// on that sample's X even when the last poll is old and the flat extension is long.
    /// </summary>
    private static CanvasLinearGradientBrush BuildHueBrush(
        ICanvasResourceCreator resourceCreator,
        ChartGeometry geometry,
        DateTimeOffset windowStart,
        float plotWidth,
        bool isDark)
    {
        var colorLookup = ChartColors.BuildColorLookup(isDark);
        var rawStops = ChartRenderer.BuildGradientStops(
            geometry.Points, 0, geometry.Points.Count - 1, windowStart, plotWidth, colorLookup,
            geometry.SpanEndPlotX);

        var stops = new CanvasGradientStop[rawStops.Length];
        for (var i = 0; i < rawStops.Length; i++)
        {
            stops[i] = new CanvasGradientStop
            {
                Position = rawStops[i].Position,
                Color = rawStops[i].Color
            };
        }

        return new CanvasLinearGradientBrush(
            resourceCreator, stops, CanvasEdgeBehavior.Clamp, CanvasAlphaMode.Premultiplied)
        {
            StartPoint = new Vector2(geometry.SpanStartX, 0f),
            EndPoint = new Vector2(geometry.SpanEndX, 0f)
        };
    }

    private static void AppendCurve(
        CanvasPathBuilder path, double[] xs, double[] ys, double[] tangents)
    {
        for (var i = 1; i < xs.Length; i++)
        {
            var (c1, c2) = ChartRenderer.ToBezierControlPoints(
                xs[i - 1], ys[i - 1], tangents[i - 1],
                xs[i], ys[i], tangents[i]);

            path.AddCubicBezier(
                new Vector2((float)c1.X, (float)c1.Y),
                new Vector2((float)c2.X, (float)c2.Y),
                new Vector2((float)xs[i], (float)ys[i]));
        }
    }

    private static void DrawCenteredAxisLabel(
        CanvasDrawingSession session, string text, float offsetX, float lineY, Color color)
    {
        var rect = new Rect(
            offsetX, lineY - (HourLabelHeight / 2f),
            AxisLabelRectWidth, HourLabelHeight);
        session.DrawText(text, rect, color, AxisLabelFormat);
    }
}
