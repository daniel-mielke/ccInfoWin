using CCInfoWindows.Models;
using Microsoft.Graphics.Canvas;
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
    private const string PngExtension = ".png";
    private const string LogSource = nameof(ExportHelper);

    /// <summary>Picker filter label. Deliberately not localized — it names a file format.</summary>
    private const string PngFileTypeLabel = "PNG Image";

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
            session, percentageText, countdownText, utilization, localize ?? LocalizedText.LocalizerLookup);
        DrawChartArea(session, device, points, windowStart, chartAreaTop);

        DrawWatermark(session);

        return renderTarget;
    }

    /// <summary>
    /// Renders the chart to PNG and saves it to a user-chosen file via FileSavePicker.
    /// </summary>
    /// <returns>
    /// False only when something failed and the caller should say so. A cancelled picker returns
    /// true: nothing went wrong, so no banner is owed. Failures are logged here, not rethrown —
    /// escaping ones reached App.OnUnhandledException, which marks them Handled, so the Export
    /// button silently did nothing and users re-clicked it indefinitely (finding 24).
    /// </returns>
    public static async Task<bool> ExportChartAsPngAsync(
        Microsoft.UI.Windowing.AppWindow appWindow,
        IReadOnlyList<UsageHistoryPoint> points,
        DateTimeOffset? windowStart,
        string percentageText,
        string countdownText,
        double utilization,
        Func<string, string>? localize = null)
    {
        try
        {
            using var renderTarget = RenderChartToPng(
                points, windowStart, percentageText, countdownText, utilization, localize);

            var picker = new Microsoft.Windows.Storage.Pickers.FileSavePicker(appWindow.Id);
            picker.SuggestedFileName = $"ccinfo-{DateTimeOffset.Now:yyyy-MM-dd-HHmm}";
            picker.DefaultFileExtension = PngExtension;
            picker.FileTypeChoices.Add(PngFileTypeLabel, [PngExtension]);

            var result = await picker.PickSaveFileAsync();
            if (result == null) return true;

            var file = await StorageFile.GetFileFromPathAsync(result.Path);
            using var stream = await file.OpenAsync(FileAccessMode.ReadWrite);

            // Overwriting an existing, larger PNG otherwise leaves its tail past the new IEND chunk.
            stream.Size = 0;
            await renderTarget.SaveAsync(stream, CanvasBitmapFileFormat.Png);
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Write(LogSource, ex, "saving the chart as PNG failed");
            return false;
        }
    }

    /// <summary>
    /// Renders the chart to PNG and places it on the system clipboard as a bitmap.
    /// </summary>
    /// <returns>False when the clipboard was not written; the reason is in app.log.</returns>
    public static async Task<bool> CopyChartToClipboardAsync(
        DispatcherQueue dispatcherQueue,
        IReadOnlyList<UsageHistoryPoint> points,
        DateTimeOffset? windowStart,
        string percentageText,
        string countdownText,
        double utilization,
        Func<string, string>? localize = null)
    {
        try
        {
            using var renderTarget = RenderChartToPng(
                points, windowStart, percentageText, countdownText, utilization, localize);

            // Disposable here because Flush() below materializes the bitmap into the clipboard, so
            // nothing keeps pulling from this stream after this method returns.
            using var stream = new InMemoryRandomAccessStream();
            await renderTarget.SaveAsync(stream, CanvasBitmapFileFormat.Png);
            stream.Seek(0);

            var dataPackage = new DataPackage();
            dataPackage.SetBitmap(RandomAccessStreamReference.CreateFromStream(stream));

            return await PlaceOnClipboardAsync(dispatcherQueue, dataPackage);
        }
        catch (Exception ex)
        {
            AppLog.Write(LogSource, ex, "rendering the chart for the clipboard failed");
            return false;
        }
    }

    /// <summary>
    /// Writes the package on the UI thread and awaits the result, because Clipboard is thread-affine
    /// and the previous fire-and-forget TryEnqueue completed the caller's await before the clipboard
    /// had been touched — making CLIPBRD_E_CANT_OPEN unobservable.
    /// </summary>
    private static async Task<bool> PlaceOnClipboardAsync(DispatcherQueue dispatcherQueue, DataPackage dataPackage)
    {
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        if (!dispatcherQueue.TryEnqueue(() => completion.TrySetResult(TrySetClipboardContent(dataPackage))))
        {
            AppLog.Write(LogSource, "the UI thread queue refused the clipboard write");
            return false;
        }

        return await completion.Task;
    }

    /// <summary>
    /// SetBitmap uses delayed rendering: the bytes are pulled only when a target pastes, so without
    /// Flush the entry is a promise this process has to stay alive to keep. Flush hands the data to
    /// the OS, which is what makes a copied chart survive closing the app.
    /// </summary>
    private static bool TrySetClipboardContent(DataPackage dataPackage)
    {
        try
        {
            Clipboard.SetContent(dataPackage);
            Clipboard.Flush();
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Write(LogSource, ex, "writing the chart to the clipboard failed");
            return false;
        }
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
            FontFamily = ChartDrawing.ChartFontFamily,
            FontSize = ExportConstants.PercentageFontSize,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            HorizontalAlignment = CanvasHorizontalAlignment.Left,
            VerticalAlignment = CanvasVerticalAlignment.Top
        };
        session.DrawText(percentageText, leftX, currentY, percentageColor, percentFormat);

        // Row 1 right: reset-in caption
        using var resetLabelFormat = CreateCaptionFormat(
            ExportConstants.ResetInLabelFontSize, CanvasHorizontalAlignment.Right);
        var resetInCaption = LocalizedText.Resolve(
            localize, ExportConstants.ResetInLabelUid, ExportConstants.ResetInFallback, LogSource);
        session.DrawText(resetInCaption, rightX, currentY, ExportConstants.LabelColor, resetLabelFormat);

        // Row 2 right: countdown value in white
        var countdownTop = currentY + ExportConstants.ResetInLabelFontSize + ExportConstants.CountdownTopOffset;
        using var countdownFormat = CreateCaptionFormat(
            ExportConstants.CountdownFontSize, CanvasHorizontalAlignment.Right);
        session.DrawText(countdownText, rightX, countdownTop, ExportConstants.PrimaryTextColor, countdownFormat);

        // Row 3 left: section label in accent blue, below the percentage number
        var sectionLabelTop = currentY + ExportConstants.PercentageFontSize + ExportConstants.SectionLabelGap;
        using var sectionLabelFormat = CreateCaptionFormat(
            ExportConstants.SectionLabelFontSize, CanvasHorizontalAlignment.Left);
        var sectionCaption = LocalizedText.Resolve(
            localize, ExportConstants.SectionLabelUid, ExportConstants.SectionLabelFallback, LogSource);
        session.DrawText(sectionCaption, leftX, sectionLabelTop, ExportConstants.SectionLabelColor, sectionLabelFormat);

        var chartAreaTop = sectionLabelTop + ExportConstants.SectionLabelFontSize + ExportConstants.ChartTopMargin;
        return chartAreaTop;
    }

    /// <summary>
    /// The three header captions — reset-in, countdown, section label — differ only in size and
    /// horizontal alignment. What makes them one family of captions (the chart font, SemiBold, top
    /// alignment, no wrapping) is written once here, so a caption-wide change is one edit.
    ///
    /// The percentage and the watermark stay hand-built on purpose: their weight, vertical alignment
    /// and wrapping all differ, and folding them in would need three more parameters than it saves.
    /// Callers own the returned format and must dispose it.
    /// </summary>
    private static CanvasTextFormat CreateCaptionFormat(
        float fontSize,
        CanvasHorizontalAlignment horizontalAlignment) =>
        new()
        {
            FontFamily = ChartDrawing.ChartFontFamily,
            FontSize = fontSize,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            HorizontalAlignment = horizontalAlignment,
            VerticalAlignment = CanvasVerticalAlignment.Top,
            WordWrapping = CanvasWordWrapping.NoWrap
        };

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
            FontFamily = ChartDrawing.ChartFontFamily,
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
