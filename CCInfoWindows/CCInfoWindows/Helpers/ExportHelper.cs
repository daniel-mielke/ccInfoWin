using CCInfoWindows.Models;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.UI.Dispatching;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.UI;
using WinUI3Localizer;

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
        public const float ExportHeight = 244f;
        public const float ExportDpi = 192f;

        public static readonly Color BackgroundColor = Color.FromArgb(255, 30, 30, 30);
        public static readonly Color ChartAreaColor = Color.FromArgb(255, 44, 44, 46);
        public static readonly Color LabelColor = Color.FromArgb(255, 142, 142, 147);
        public static readonly Color WatermarkColor = Color.FromArgb(255, 99, 99, 102);
        public static readonly Color SectionLabelColor = Color.FromArgb(255, 96, 165, 250);
        public static readonly Color PrimaryTextColor = Color.FromArgb(255, 248, 248, 248);

        public const float HeaderTopMargin = 12f;
        public const float HeaderHorizontalPadding = 12f;
        public const float ChartTopMargin = 18f;
        // 144 == the live canvas height (Border 160 minus 2x8 padding), so the export plot height
        // matches the live one exactly instead of landing within 3.4% by accident.
        public const float ChartAreaHeight = 144f;
        public const float ChartAreaHorizontalPadding = 8f;
        public const float WatermarkBottomMargin = 6f;
        public const float WatermarkRightMargin = 8f;

        public const float PercentageFontSize = 32f;
        public const float ResetInLabelFontSize = 9f;
        public const float CountdownFontSize = 16f;
        public const float SectionLabelFontSize = 11f;
        public const float WatermarkFontSize = 11f;

        // Resource uids of the two localized captions, plus the text used when the resource
        // dictionary cannot answer. A uid is the resw name up to the first '.' -- that prefix is
        // what WinUI3Localizer keys its dictionary on, so "SectionHeaderFiveHour" resolves the
        // value of "SectionHeaderFiveHour.Text".
        public const string SectionLabelUid = "SectionHeaderFiveHour";
        public const string SectionLabelFallback = "5-HOUR WINDOW";
        public const string ResetInLabelUid = "ResetInLabel";
        public const string ResetInFallback = "RESET IN";

        // Product mark, deliberately not localized.
        public const string WatermarkText = "CCINFO";

        public const float ChartAreaCornerRadius = 8f;
        public const float ExportCornerRadius = 20f;
        public const float CountdownTopOffset = 2f;
        public const float SectionLabelGap = 15f;
    }

    /// <summary>
    /// Renders the chart to an offscreen CanvasRenderTarget at 192 DPI (2x).
    /// Caller is responsible for disposing the returned target.
    /// <paramref name="localize"/> overrides the resource lookup for the two captions; it exists so
    /// the render can be exercised without a WinUI3Localizer host, which xUnit cannot start.
    /// </summary>
    public static CanvasRenderTarget RenderChartToPng(
        IReadOnlyList<UsageHistoryPoint> points,
        DateTimeOffset? windowStart,
        string percentageText,
        string countdownText,
        double utilization,
        Func<string, string>? localize = null)
    {
        var device = CanvasDevice.GetSharedDevice();
        var renderTarget = new CanvasRenderTarget(
            device,
            ExportConstants.ExportWidth,
            ExportConstants.ExportHeight,
            ExportConstants.ExportDpi);

        using var session = renderTarget.CreateDrawingSession();

        DrawBackground(session);

        var chartAreaTop = DrawHeader(
            session, percentageText, countdownText, utilization, localize ?? ResolveFromLocalizer);
        DrawChartArea(session, device, points, windowStart, chartAreaTop);

        DrawWatermark(session);

        return renderTarget;
    }

    /// <summary>
    /// Resolves a caption for the language the UI is currently showing, falling back to
    /// <paramref name="fallback"/> when the dictionary has no usable answer. Internal for testing.
    /// </summary>
    internal static string Caption(Func<string, string> localize, string uid, string fallback)
    {
        var text = localize(uid);

        // A built localizer returns "" for an unknown uid and NullLocalizer echoes the uid back;
        // either would paint a resource key onto the exported PNG.
        return string.IsNullOrWhiteSpace(text) || text == uid ? fallback : text;
    }

    private static string ResolveFromLocalizer(string uid)
    {
        try
        {
            return Localizer.Get().GetLocalizedString(uid);
        }
        catch (Exception ex)
        {
            AppLog.Write(nameof(ExportHelper), ex, $"could not read caption '{uid}'.");
            return string.Empty;
        }
    }

    /// <summary>
    /// Renders the chart to PNG and saves it to a user-chosen file via FileSavePicker.
    /// </summary>
    public static async Task ExportChartAsPngAsync(
        Microsoft.UI.Windowing.AppWindow appWindow,
        IReadOnlyList<UsageHistoryPoint> points,
        DateTimeOffset? windowStart,
        string percentageText,
        string countdownText,
        double utilization,
        Func<string, string>? localize = null)
    {
        using var renderTarget = RenderChartToPng(
            points, windowStart, percentageText, countdownText, utilization, localize);

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
        string countdownText,
        double utilization,
        Func<string, string>? localize = null)
    {
        using var renderTarget = RenderChartToPng(
            points, windowStart, percentageText, countdownText, utilization, localize);

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
        session.Clear(Color.FromArgb(0, 0, 0, 0));
        var bounds = new Windows.Foundation.Rect(0, 0, ExportConstants.ExportWidth, ExportConstants.ExportHeight);
        session.FillRoundedRectangle(bounds, ExportConstants.ExportCornerRadius, ExportConstants.ExportCornerRadius, ExportConstants.BackgroundColor);
    }

    /// <summary>
    /// Draws the header block (percentage, reset-in, section label) and returns the Y position
    /// where the chart area should start.
    /// Layout (top to bottom):
    ///   Row 1: [Percentage%  left]  [reset-in caption  right]
    ///   Row 2:                      [countdown right]
    ///   Row 3: [5-hour-window caption left]
    ///   gap
    ///   Chart
    /// </summary>
    private static float DrawHeader(
        CanvasDrawingSession session,
        string percentageText,
        string countdownText,
        double utilization,
        Func<string, string> localize)
    {
        var percentageColor = ChartColors.GetZoneColor(utilization, isDark: true);
        var leftX = ExportConstants.HeaderHorizontalPadding;
        var rightX = ExportConstants.ExportWidth - ExportConstants.HeaderHorizontalPadding;
        var currentY = ExportConstants.HeaderTopMargin;

        // Row 1 left: large percentage
        using var percentFormat = new CanvasTextFormat
        {
            FontFamily = "Segoe UI Variable",
            FontSize = ExportConstants.PercentageFontSize,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            HorizontalAlignment = CanvasHorizontalAlignment.Left,
            VerticalAlignment = CanvasVerticalAlignment.Top
        };
        session.DrawText(percentageText, leftX, currentY, percentageColor, percentFormat);

        // Row 1 right: reset-in caption
        using var resetLabelFormat = new CanvasTextFormat
        {
            FontFamily = "Segoe UI Variable",
            FontSize = ExportConstants.ResetInLabelFontSize,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            HorizontalAlignment = CanvasHorizontalAlignment.Right,
            VerticalAlignment = CanvasVerticalAlignment.Top,
            WordWrapping = CanvasWordWrapping.NoWrap
        };
        var resetInCaption = Caption(
            localize, ExportConstants.ResetInLabelUid, ExportConstants.ResetInFallback);
        session.DrawText(resetInCaption, rightX, currentY, ExportConstants.LabelColor, resetLabelFormat);

        // Row 2 right: countdown value in white
        var countdownTop = currentY + ExportConstants.ResetInLabelFontSize + ExportConstants.CountdownTopOffset;
        using var countdownFormat = new CanvasTextFormat
        {
            FontFamily = "Segoe UI Variable",
            FontSize = ExportConstants.CountdownFontSize,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            HorizontalAlignment = CanvasHorizontalAlignment.Right,
            VerticalAlignment = CanvasVerticalAlignment.Top,
            WordWrapping = CanvasWordWrapping.NoWrap
        };
        session.DrawText(countdownText, rightX, countdownTop, ExportConstants.PrimaryTextColor, countdownFormat);

        // Row 3 left: section label in accent blue, below the percentage number
        var sectionLabelTop = currentY + ExportConstants.PercentageFontSize + ExportConstants.SectionLabelGap;
        using var sectionLabelFormat = new CanvasTextFormat
        {
            FontFamily = "Segoe UI Variable",
            FontSize = ExportConstants.SectionLabelFontSize,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            HorizontalAlignment = CanvasHorizontalAlignment.Left,
            VerticalAlignment = CanvasVerticalAlignment.Top,
            WordWrapping = CanvasWordWrapping.NoWrap
        };
        var sectionCaption = Caption(
            localize, ExportConstants.SectionLabelUid, ExportConstants.SectionLabelFallback);
        session.DrawText(sectionCaption, leftX, sectionLabelTop, ExportConstants.SectionLabelColor, sectionLabelFormat);

        var chartAreaTop = sectionLabelTop + ExportConstants.SectionLabelFontSize + ExportConstants.ChartTopMargin;
        return chartAreaTop;
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

        ChartDrawing.DrawAxesAndLabels(session, plotWidth, plotHeight, isDark: true, chartLeft, plotOffsetY);
        ChartDrawing.DrawChart(session, resourceCreator, points, windowStart.Value, plotWidth, plotHeight, isDark: true, chartLeft, plotOffsetY, lineWidth: 2.5f);
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
