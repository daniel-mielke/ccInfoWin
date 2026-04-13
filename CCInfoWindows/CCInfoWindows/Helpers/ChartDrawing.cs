using System.Numerics;
using CCInfoWindows.Models;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Brushes;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.Graphics.Canvas.Text;
using Windows.UI;

namespace CCInfoWindows.Helpers;

/// <summary>
/// Win2D chart drawing methods shared by MainView (live chart) and ExportHelper (PNG export).
/// All methods accept offsetX/offsetY to support rendering at arbitrary canvas positions.
/// </summary>
public static class ChartDrawing
{
    private const byte FillAlpha = 64;

    private static readonly CanvasStrokeStyle DashStrokeStyle = new()
    {
        CustomDashStyle = [4f, 4f]
    };

    private static readonly CanvasTextFormat AxisLabelFormat = new()
    {
        FontFamily = "Segoe UI Variable",
        FontSize = 10f
    };

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

        session.DrawText("100%", offsetX, y100 - 6f, labelColor, AxisLabelFormat);
        session.DrawText("50%", offsetX, y50 - 6f, labelColor, AxisLabelFormat);
        session.DrawText("0%", offsetX, y0 - 6f, labelColor, AxisLabelFormat);

        for (var hour = 0; hour <= 5; hour++)
        {
            var xRatio = hour / 5f;
            var x = offsetX + ChartRenderer.LeftMargin + (xRatio * plotWidth);
            if (hour == 5) x -= 14f;
            session.DrawText($"{hour}h", x, y0 + 2f, labelColor, AxisLabelFormat);
        }
    }

    public static void DrawChartFills(
        CanvasDrawingSession session,
        ICanvasResourceCreator resourceCreator,
        IReadOnlyList<UsageHistoryPoint> points,
        DateTimeOffset windowStart,
        float plotWidth,
        float plotHeight,
        bool isDark,
        float offsetX = 0f,
        float offsetY = 0f)
    {
        var baselineY = offsetY + ChartRenderer.ToY(0.0, plotHeight);
        var colorLookup = ChartColors.BuildColorLookup(isDark);
        var spans = ChartRenderer.GetContiguousSpans(points);

        foreach (var (startIndex, endIndex) in spans)
        {
            var rawStops = ChartRenderer.BuildGradientStops(
                points, startIndex, endIndex, windowStart, plotWidth, colorLookup);

            var fillStops = ConvertToFillStops(rawStops);

            var spanStartAbsoluteX = offsetX + ChartRenderer.LeftMargin
                + ChartRenderer.ToX(points[startIndex].Timestamp, windowStart, plotWidth);
            var spanEndAbsoluteX = offsetX
                + ChartRenderer.GetRightEdgeAbsoluteX(points, endIndex, windowStart, plotWidth);

            using var fillBrush = new CanvasLinearGradientBrush(
                resourceCreator, fillStops,
                CanvasEdgeBehavior.Clamp, CanvasAlphaMode.Premultiplied);
            fillBrush.StartPoint = new Vector2(spanStartAbsoluteX, 0f);
            fillBrush.EndPoint = new Vector2(spanEndAbsoluteX, 0f);

            var rightEdgeX = offsetX
                + ChartRenderer.GetRightEdgeAbsoluteX(points, endIndex, windowStart, plotWidth);

            using var pathBuilder = new CanvasPathBuilder(resourceCreator);
            var firstX = offsetX + ChartRenderer.LeftMargin
                + ChartRenderer.ToX(points[startIndex].Timestamp, windowStart, plotWidth);
            pathBuilder.BeginFigure(firstX, baselineY);

            for (var i = startIndex; i <= endIndex; i++)
            {
                var x = offsetX + ChartRenderer.LeftMargin
                    + ChartRenderer.ToX(points[i].Timestamp, windowStart, plotWidth);
                var y = offsetY + ChartRenderer.ToY(points[i].Utilization, plotHeight);

                if (i == startIndex)
                {
                    pathBuilder.AddLine(x, y);
                }
                else
                {
                    var prevY = offsetY + ChartRenderer.ToY(points[i - 1].Utilization, plotHeight);
                    pathBuilder.AddLine(x, prevY);
                    pathBuilder.AddLine(x, y);
                }
            }

            var lastY = offsetY + ChartRenderer.ToY(points[endIndex].Utilization, plotHeight);
            pathBuilder.AddLine(rightEdgeX, lastY);
            pathBuilder.AddLine(rightEdgeX, baselineY);
            pathBuilder.EndFigure(CanvasFigureLoop.Closed);

            using var geometry = CanvasGeometry.CreatePath(pathBuilder);
            session.FillGeometry(geometry, fillBrush);
        }
    }

    public static void DrawChartTopLine(
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
        var colorLookup = ChartColors.BuildColorLookup(isDark);
        var spans = ChartRenderer.GetContiguousSpans(points);

        foreach (var (startIndex, endIndex) in spans)
        {
            var rawStops = ChartRenderer.BuildGradientStops(
                points, startIndex, endIndex, windowStart, plotWidth, colorLookup);

            var lineStops = ConvertToLineStops(rawStops);

            var spanStartAbsoluteX = offsetX + ChartRenderer.LeftMargin
                + ChartRenderer.ToX(points[startIndex].Timestamp, windowStart, plotWidth);
            var spanEndAbsoluteX = offsetX
                + ChartRenderer.GetRightEdgeAbsoluteX(points, endIndex, windowStart, plotWidth);

            using var lineBrush = new CanvasLinearGradientBrush(
                resourceCreator, lineStops,
                CanvasEdgeBehavior.Clamp, CanvasAlphaMode.Premultiplied);
            lineBrush.StartPoint = new Vector2(spanStartAbsoluteX, 0f);
            lineBrush.EndPoint = new Vector2(spanEndAbsoluteX, 0f);

            var rightEdgeX = offsetX
                + ChartRenderer.GetRightEdgeAbsoluteX(points, endIndex, windowStart, plotWidth);

            using var pathBuilder = new CanvasPathBuilder(resourceCreator);
            var firstX = offsetX + ChartRenderer.LeftMargin
                + ChartRenderer.ToX(points[startIndex].Timestamp, windowStart, plotWidth);
            var firstY = offsetY + ChartRenderer.ToY(points[startIndex].Utilization, plotHeight);
            pathBuilder.BeginFigure(firstX, firstY);

            for (var i = startIndex + 1; i <= endIndex; i++)
            {
                var x = offsetX + ChartRenderer.LeftMargin
                    + ChartRenderer.ToX(points[i].Timestamp, windowStart, plotWidth);
                var y = offsetY + ChartRenderer.ToY(points[i].Utilization, plotHeight);
                var prevY = offsetY + ChartRenderer.ToY(points[i - 1].Utilization, plotHeight);
                pathBuilder.AddLine(x, prevY);
                pathBuilder.AddLine(x, y);
            }

            var lastY = offsetY + ChartRenderer.ToY(points[endIndex].Utilization, plotHeight);
            pathBuilder.AddLine(rightEdgeX, lastY);
            pathBuilder.EndFigure(CanvasFigureLoop.Open);

            using var geometry = CanvasGeometry.CreatePath(pathBuilder);
            session.DrawGeometry(geometry, lineBrush, lineWidth);
        }
    }

    public static void DrawGlowIndicator(
        CanvasDrawingSession session,
        ICanvasResourceCreator resourceCreator,
        IReadOnlyList<UsageHistoryPoint> points,
        DateTimeOffset windowStart,
        float plotWidth,
        float plotHeight,
        bool isDark,
        float offsetX = 0f,
        float offsetY = 0f)
    {
        var lastPoint = points[^1];
        var x = offsetX + ChartRenderer.GetRightEdgeAbsoluteX(points, points.Count - 1, windowStart, plotWidth);
        var y = offsetY + ChartRenderer.ToY(lastPoint.Utilization, plotHeight);
        var zoneColor = ChartColors.GetZoneColor(lastPoint.Utilization, isDark);
        var glowColor = Color.FromArgb(115, zoneColor.R, zoneColor.G, zoneColor.B);

        using var commandList = new CanvasCommandList(resourceCreator);
        using (var clSession = commandList.CreateDrawingSession())
        {
            clSession.FillCircle(x, y, 8f, glowColor);
        }

        using var blurEffect = new GaussianBlurEffect
        {
            Source = commandList,
            BlurAmount = 3.0f
        };
        session.DrawImage(blurEffect);

        session.FillCircle(x, y, 4f, zoneColor);
    }

    private static CanvasGradientStop[] ConvertToFillStops(
        (float Position, Color Color)[] rawStops)
    {
        var result = new CanvasGradientStop[rawStops.Length];
        for (var i = 0; i < rawStops.Length; i++)
        {
            var c = rawStops[i].Color;
            result[i] = new CanvasGradientStop
            {
                Position = rawStops[i].Position,
                Color = Color.FromArgb(FillAlpha, c.R, c.G, c.B)
            };
        }
        return result;
    }

    private static CanvasGradientStop[] ConvertToLineStops(
        (float Position, Color Color)[] rawStops)
    {
        var result = new CanvasGradientStop[rawStops.Length];
        for (var i = 0; i < rawStops.Length; i++)
        {
            result[i] = new CanvasGradientStop
            {
                Position = rawStops[i].Position,
                Color = rawStops[i].Color
            };
        }
        return result;
    }
}
