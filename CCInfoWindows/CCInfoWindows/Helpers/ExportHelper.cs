using CCInfoWindows.Models;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.UI.Dispatching;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.UI;

namespace CCInfoWindows.Helpers;

/// <summary>
/// Offscreen chart export: PNG rendering, file save via FileSavePicker, and clipboard copy.
/// Always renders in dark theme at 2x DPI (192 DPI = 656x480 physical pixels).
/// </summary>
public static class ExportHelper
{
    /// <summary>
    /// Layout and color constants for the export composition.
    /// </summary>
    public static class ExportConstants
    {
        public const float ExportWidth = 328f;
        public const float ExportHeight = 240f;
        public const float ExportDpi = 192f;

        public static readonly Color BackgroundColor = Color.FromArgb(255, 30, 30, 30);
        public static readonly Color ChartAreaColor = Color.FromArgb(255, 44, 44, 46);
        public static readonly Color LabelColor = Color.FromArgb(255, 142, 142, 147);
        public static readonly Color WatermarkColor = Color.FromArgb(255, 99, 99, 102);
        public static readonly Color PercentageColor = Color.FromArgb(255, 255, 255, 255);

        public const float SectionLabelTopMargin = 12f;
        public const float PercentageRowTopMargin = 8f;
        public const float ChartAreaTopMargin = 8f;
        public const float ChartAreaHeight = 120f;
        public const float ChartAreaHorizontalPadding = 8f;
        public const float WatermarkBottomMargin = 8f;
        public const float WatermarkRightMargin = 8f;

        public const string SectionLabel = "5-STUNDEN-FENSTER";
        public const string WatermarkText = "CCINFO";

        public const float SectionLabelFontSize = 11f;
        public const float PercentageFontSize = 28f;
        public const float CountdownFontSize = 13f;
        public const float WatermarkFontSize = 11f;

        public const float ChartAreaCornerRadius = 8f;
    }

    /// <summary>
    /// Renders the chart to an offscreen CanvasRenderTarget at 192 DPI (2x).
    /// Caller is responsible for disposing the returned target.
    /// </summary>
    public static CanvasRenderTarget RenderChartToPng(
        IReadOnlyList<UsageHistoryPoint> points,
        DateTimeOffset? windowStart,
        string percentageText,
        string countdownText)
    {
        var device = CanvasDevice.GetSharedDevice();
        var renderTarget = new CanvasRenderTarget(
            device,
            ExportConstants.ExportWidth,
            ExportConstants.ExportHeight,
            ExportConstants.ExportDpi);

        using var session = renderTarget.CreateDrawingSession();

        DrawBackground(session);
        DrawSectionLabel(session);

        var sectionLabelBottom = ExportConstants.SectionLabelTopMargin + ExportConstants.SectionLabelFontSize;
        var percentageRowTop = sectionLabelBottom + ExportConstants.PercentageRowTopMargin;
        DrawPercentageRow(session, percentageText, countdownText, percentageRowTop);

        var percentageRowBottom = percentageRowTop + ExportConstants.PercentageFontSize;
        var chartAreaTop = percentageRowBottom + ExportConstants.ChartAreaTopMargin;
        DrawChartArea(session, device, points, windowStart, chartAreaTop);

        DrawWatermark(session);

        return renderTarget;
    }

    /// <summary>
    /// Renders the chart to PNG and saves it to a user-chosen file via FileSavePicker.
    /// </summary>
    public static async Task ExportChartAsPngAsync(
        Microsoft.UI.Windowing.AppWindow appWindow,
        IReadOnlyList<UsageHistoryPoint> points,
        DateTimeOffset? windowStart,
        string percentageText,
        string countdownText)
    {
        using var renderTarget = RenderChartToPng(points, windowStart, percentageText, countdownText);

        var picker = new Microsoft.Windows.Storage.Pickers.FileSavePicker(appWindow.Id);
        picker.SuggestedFileName = $"ccinfo-{DateTimeOffset.Now:yyyy-MM-dd-HHmm}";
        picker.DefaultFileExtension = ".png";
        picker.FileTypeChoices.Add("PNG Image", [".png"]);

        var result = await picker.PickSaveFileAsync();
        if (result == null) return;

        var file = await StorageFile.GetFileFromPathAsync(result.Path);
        using var stream = await file.OpenAsync(FileAccessMode.ReadWrite);
        await renderTarget.SaveAsync(stream, CanvasBitmapFileFormat.Png);
    }

    /// <summary>
    /// Renders the chart to PNG and places it on the system clipboard as a bitmap.
    /// </summary>
    public static async Task CopyChartToClipboardAsync(
        DispatcherQueue dispatcherQueue,
        IReadOnlyList<UsageHistoryPoint> points,
        DateTimeOffset? windowStart,
        string percentageText,
        string countdownText)
    {
        using var renderTarget = RenderChartToPng(points, windowStart, percentageText, countdownText);

        var stream = new InMemoryRandomAccessStream();
        await renderTarget.SaveAsync(stream, CanvasBitmapFileFormat.Png);
        stream.Seek(0);

        var streamRef = RandomAccessStreamReference.CreateFromStream(stream);
        var dataPackage = new DataPackage();
        dataPackage.SetBitmap(streamRef);

        dispatcherQueue.TryEnqueue(() => Clipboard.SetContent(dataPackage));
    }

    private static void DrawBackground(CanvasDrawingSession session)
    {
        session.Clear(ExportConstants.BackgroundColor);
    }

    private static void DrawSectionLabel(CanvasDrawingSession session)
    {
        using var format = new CanvasTextFormat
        {
            FontFamily = "Segoe UI Variable",
            FontSize = ExportConstants.SectionLabelFontSize,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            HorizontalAlignment = CanvasHorizontalAlignment.Center,
            VerticalAlignment = CanvasVerticalAlignment.Top
        };

        session.DrawText(
            ExportConstants.SectionLabel,
            ExportConstants.ExportWidth / 2f,
            ExportConstants.SectionLabelTopMargin,
            ExportConstants.LabelColor,
            format);
    }

    private static void DrawPercentageRow(
        CanvasDrawingSession session,
        string percentageText,
        string countdownText,
        float topY)
    {
        using var percentFormat = new CanvasTextFormat
        {
            FontFamily = "Segoe UI Variable",
            FontSize = ExportConstants.PercentageFontSize,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            HorizontalAlignment = CanvasHorizontalAlignment.Left,
            VerticalAlignment = CanvasVerticalAlignment.Top
        };

        using var countdownFormat = new CanvasTextFormat
        {
            FontFamily = "Segoe UI Variable",
            FontSize = ExportConstants.CountdownFontSize,
            HorizontalAlignment = CanvasHorizontalAlignment.Right,
            VerticalAlignment = CanvasVerticalAlignment.Bottom
        };

        const float horizontalPadding = 12f;
        session.DrawText(percentageText, horizontalPadding, topY, ExportConstants.PercentageColor, percentFormat);

        var countdownBottom = topY + ExportConstants.PercentageFontSize;
        session.DrawText(
            countdownText,
            ExportConstants.ExportWidth - horizontalPadding,
            countdownBottom,
            ExportConstants.LabelColor,
            countdownFormat);
    }

    private static void DrawChartArea(
        CanvasDrawingSession session,
        ICanvasResourceCreator resourceCreator,
        IReadOnlyList<UsageHistoryPoint> points,
        DateTimeOffset? windowStart,
        float chartAreaTop)
    {
        var chartLeft = ExportConstants.ChartAreaHorizontalPadding;
        var chartWidth = ExportConstants.ExportWidth - (ExportConstants.ChartAreaHorizontalPadding * 2f);

        var chartRect = new Windows.Foundation.Rect(
            chartLeft,
            chartAreaTop,
            chartWidth,
            ExportConstants.ChartAreaHeight);

        session.FillRoundedRectangle(chartRect, ExportConstants.ChartAreaCornerRadius, ExportConstants.ChartAreaCornerRadius, ExportConstants.ChartAreaColor);

        if (points.Count == 0 || windowStart == null) return;

        var innerLeft = chartLeft + ChartRenderer.LeftMargin;
        var plotWidth = chartWidth - ChartRenderer.LeftMargin;
        var plotHeight = ExportConstants.ChartAreaHeight - ChartRenderer.BottomMargin - ChartRenderer.TopMargin;
        var plotOffsetY = chartAreaTop;

        DrawExportAxesAndLabels(session, plotWidth, plotHeight, plotOffsetY, chartLeft);
        DrawExportChartFills(session, resourceCreator, points, windowStart.Value, plotWidth, plotHeight, plotOffsetY, chartLeft);
        DrawExportChartTopLine(session, resourceCreator, points, windowStart.Value, plotWidth, plotHeight, plotOffsetY, chartLeft);
        DrawExportGlowIndicator(session, resourceCreator, points, windowStart.Value, plotWidth, plotHeight, plotOffsetY, chartLeft);
    }

    private static void DrawExportAxesAndLabels(
        CanvasDrawingSession session,
        float plotWidth,
        float plotHeight,
        float offsetY,
        float offsetX)
    {
        using var dashStroke = new Microsoft.Graphics.Canvas.Geometry.CanvasStrokeStyle
        {
            CustomDashStyle = [4f, 4f]
        };
        using var labelFormat = new CanvasTextFormat
        {
            FontFamily = "Segoe UI Variable",
            FontSize = 10f
        };

        var thresholdColor = ChartColors.GetColor("ThresholdBrush", isDark: true);
        var labelColor = ChartColors.GetColor("AxisLabelBrush", isDark: true);
        var lineStart = offsetX + ChartRenderer.LeftMargin;
        var lineEnd = offsetX + ChartRenderer.LeftMargin + plotWidth;

        var y0 = offsetY + ChartRenderer.ToY(0.0, plotHeight);
        var y50 = offsetY + ChartRenderer.ToY(0.5, plotHeight);
        var y100 = offsetY + ChartRenderer.ToY(1.0, plotHeight);

        session.DrawLine(lineStart, y0, lineEnd, y0, thresholdColor, 1f, dashStroke);
        session.DrawLine(lineStart, y50, lineEnd, y50, thresholdColor, 1f, dashStroke);
        session.DrawLine(lineStart, y100, lineEnd, y100, thresholdColor, 1f, dashStroke);

        session.DrawText("100%", offsetX, y100 - 6f, labelColor, labelFormat);
        session.DrawText("50%", offsetX, y50 - 6f, labelColor, labelFormat);
        session.DrawText("0%", offsetX, y0 - 6f, labelColor, labelFormat);

        for (var hour = 0; hour <= 5; hour++)
        {
            var xRatio = hour / 5f;
            var x = offsetX + ChartRenderer.LeftMargin + (xRatio * plotWidth);
            if (hour == 5) x -= 14f;
            session.DrawText($"{hour}h", x, y0 + 2f, labelColor, labelFormat);
        }
    }

    private static void DrawExportChartFills(
        CanvasDrawingSession session,
        ICanvasResourceCreator resourceCreator,
        IReadOnlyList<UsageHistoryPoint> points,
        DateTimeOffset windowStart,
        float plotWidth,
        float plotHeight,
        float offsetY,
        float offsetX)
    {
        var baselineY = offsetY + ChartRenderer.ToY(0.0, plotHeight);
        var segments = ChartRenderer.GetZoneSegments(points);

        foreach (var (startIndex, endIndex, brushKey) in segments)
        {
            var zoneColor = ChartColors.GetColor(brushKey, isDark: true);
            var fillColor = Color.FromArgb(102, zoneColor.R, zoneColor.G, zoneColor.B);
            var rightEdgeX = offsetX + ChartRenderer.GetRightEdgeAbsoluteX(points, endIndex, windowStart, plotWidth);

            using var pathBuilder = new CanvasPathBuilder(resourceCreator);
            var firstX = offsetX + ChartRenderer.LeftMargin + ChartRenderer.ToX(points[startIndex].Timestamp, windowStart, plotWidth);
            pathBuilder.BeginFigure(firstX, baselineY);

            for (var i = startIndex; i <= endIndex; i++)
            {
                var x = offsetX + ChartRenderer.LeftMargin + ChartRenderer.ToX(points[i].Timestamp, windowStart, plotWidth);
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
            session.FillGeometry(geometry, fillColor);
        }
    }

    private static void DrawExportChartTopLine(
        CanvasDrawingSession session,
        ICanvasResourceCreator resourceCreator,
        IReadOnlyList<UsageHistoryPoint> points,
        DateTimeOffset windowStart,
        float plotWidth,
        float plotHeight,
        float offsetY,
        float offsetX)
    {
        var segments = ChartRenderer.GetZoneSegments(points);

        foreach (var (startIndex, endIndex, brushKey) in segments)
        {
            var zoneColor = ChartColors.GetColor(brushKey, isDark: true);
            var rightEdgeX = offsetX + ChartRenderer.GetRightEdgeAbsoluteX(points, endIndex, windowStart, plotWidth);

            using var pathBuilder = new CanvasPathBuilder(resourceCreator);
            var firstX = offsetX + ChartRenderer.LeftMargin + ChartRenderer.ToX(points[startIndex].Timestamp, windowStart, plotWidth);
            var firstY = offsetY + ChartRenderer.ToY(points[startIndex].Utilization, plotHeight);
            pathBuilder.BeginFigure(firstX, firstY);

            for (var i = startIndex + 1; i <= endIndex; i++)
            {
                var x = offsetX + ChartRenderer.LeftMargin + ChartRenderer.ToX(points[i].Timestamp, windowStart, plotWidth);
                var y = offsetY + ChartRenderer.ToY(points[i].Utilization, plotHeight);
                var prevY = offsetY + ChartRenderer.ToY(points[i - 1].Utilization, plotHeight);
                pathBuilder.AddLine(x, prevY);
                pathBuilder.AddLine(x, y);
            }

            var lastY = offsetY + ChartRenderer.ToY(points[endIndex].Utilization, plotHeight);
            pathBuilder.AddLine(rightEdgeX, lastY);
            pathBuilder.EndFigure(CanvasFigureLoop.Open);

            using var geometry = CanvasGeometry.CreatePath(pathBuilder);
            session.DrawGeometry(geometry, zoneColor, 2f);
        }
    }

    private static void DrawExportGlowIndicator(
        CanvasDrawingSession session,
        ICanvasResourceCreator resourceCreator,
        IReadOnlyList<UsageHistoryPoint> points,
        DateTimeOffset windowStart,
        float plotWidth,
        float plotHeight,
        float offsetY,
        float offsetX)
    {
        var lastPoint = points[^1];
        var x = offsetX + ChartRenderer.GetRightEdgeAbsoluteX(points, points.Count - 1, windowStart, plotWidth);
        var y = offsetY + ChartRenderer.ToY(lastPoint.Utilization, plotHeight);
        var zoneColor = ChartColors.GetZoneColor(lastPoint.Utilization, isDark: true);
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

    private static void DrawWatermark(CanvasDrawingSession session)
    {
        using var format = new CanvasTextFormat
        {
            FontFamily = "Segoe UI Variable",
            FontSize = ExportConstants.WatermarkFontSize,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            HorizontalAlignment = CanvasHorizontalAlignment.Right,
            VerticalAlignment = CanvasVerticalAlignment.Bottom
        };

        session.DrawText(
            ExportConstants.WatermarkText,
            ExportConstants.ExportWidth - ExportConstants.WatermarkRightMargin,
            ExportConstants.ExportHeight - ExportConstants.WatermarkBottomMargin,
            ExportConstants.WatermarkColor,
            format);
    }
}
