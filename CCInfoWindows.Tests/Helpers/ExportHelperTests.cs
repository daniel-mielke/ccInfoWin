using CCInfoWindows.Helpers;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Text;
using Windows.UI;

namespace CCInfoWindows.Tests.Helpers;

public class ExportHelperTests
{
    // xUnit cannot start a WinUI3Localizer host, so the render tests resolve their captions here.
    private static readonly Func<string, string> StubLocalizer = uid => $"[{uid}]";

    [Fact]
    public void ExportConstants_HasCorrectWidth()
    {
        Assert.Equal(328f, ExportHelper.ExportConstants.ExportWidth);
    }

    [Fact]
    public void ExportConstants_HasCorrectHeight()
    {
        Assert.Equal(244f, ExportHelper.ExportConstants.ExportHeight);
    }

    [Fact]
    public void ExportChartArea_MatchesTheLiveCanvasHeight()
    {
        // Live: Border Height 160 minus 2x8 padding == 144 canvas pixels. Keeping the export
        // chart area identical makes the export plot height match exactly rather than landing
        // within a few percent by accident.
        const float LiveBorderHeight = 160f;
        const float LiveBorderPadding = 8f;
        const float LiveCanvasHeight = LiveBorderHeight - (2f * LiveBorderPadding);

        Assert.Equal(LiveCanvasHeight, ExportHelper.ExportConstants.ChartAreaHeight);
    }

    [Fact]
    public void ExportPlotHeight_EqualsLivePlotHeight()
    {
        // Both sides subtract the same two margins, so equal canvas heights must give equal
        // plot heights — and therefore an identical curve shape in the PNG and on screen.
        const float LiveCanvasHeight = 144f;
        var livePlotHeight = LiveCanvasHeight - ChartRenderer.BottomMargin - ChartRenderer.TopMargin;
        var exportPlotHeight = ExportHelper.ExportConstants.ChartAreaHeight
            - ChartRenderer.BottomMargin - ChartRenderer.TopMargin;

        Assert.Equal(livePlotHeight, exportPlotHeight);
        Assert.True(ChartRenderer.PlotBandHeight(exportPlotHeight) > 0f);
    }

    [Fact]
    public void ExportConstants_HasCorrectDpi()
    {
        Assert.Equal(192f, ExportHelper.ExportConstants.ExportDpi);
    }

    [Fact]
    [Trait("Category", "RequiresGPU")]
    public void RenderChartToPng_WithPoints_ReturnsNonNullTarget()
    {
        var points = new List<CCInfoWindows.Models.UsageHistoryPoint>
        {
            new() { Timestamp = DateTimeOffset.UtcNow.AddHours(-2), Utilization = 0.3 },
            new() { Timestamp = DateTimeOffset.UtcNow.AddHours(-1), Utilization = 0.6 },
            new() { Timestamp = DateTimeOffset.UtcNow, Utilization = 0.8 }
        };
        var windowStart = DateTimeOffset.UtcNow.AddHours(-5);

        var target = ExportHelper.RenderChartToPng(points, windowStart, "80%", "02:30", 0.8, StubLocalizer);

        Assert.NotNull(target);
        Assert.Equal(ExportHelper.ExportConstants.ExportWidth, target.Size.Width, precision: 1);
        Assert.Equal(ExportHelper.ExportConstants.ExportHeight, target.Size.Height, precision: 1);
        target.Dispose();
    }

    [Fact]
    [Trait("Category", "RequiresGPU")]
    public void RenderChartToPng_WithAdversarialPoints_RendersWithoutThrowing()
    {
        // Everything that can make the tangent calculation divide by zero, in one set:
        // duplicate timestamps, samples past the 5-hour window (ToX clamps them onto the same
        // X), and enough points to exercise the downsampling path. This runs the whole
        // pipeline on a real Direct2D device -- a NaN control point would surface here.
        var now = DateTimeOffset.UtcNow;
        var windowStart = now.AddHours(-8);
        var duplicate = windowStart.AddHours(1);

        var points = new List<CCInfoWindows.Models.UsageHistoryPoint>
        {
            new() { Timestamp = duplicate, Utilization = 0.0 },
            new() { Timestamp = duplicate, Utilization = 0.95 },
            new() { Timestamp = duplicate, Utilization = 0.30 },
        };
        points.AddRange(Enumerable.Range(0, 600).Select(i => new CCInfoWindows.Models.UsageHistoryPoint
        {
            Timestamp = windowStart.AddSeconds(3600 + (i * 30)),   // runs past the window end
            Utilization = (i % 101) / 100.0                        // sweeps 0%..100% repeatedly
        }));

        var target = ExportHelper.RenderChartToPng(points, windowStart, "30%", "01:23", 0.30, StubLocalizer);

        Assert.NotNull(target);
        target.Dispose();
    }

    [Theory]
    [Trait("Category", "RequiresGPU")]
    [InlineData("100%")]
    [InlineData("50%")]
    [InlineData("0%")]
    public void AxisLabelFitsInGutter(string label)
    {
        // The v1.6 redesign lays the percentage labels out in a rect, and a rect wraps. At
        // LeftMargin 22 the rect was 18px while "100%" measures 24.36px, so it broke into
        // "100" / "%" straddling its own gridline -- visible only on screen, no test failed.
        using var format = new CanvasTextFormat
        {
            FontFamily = "Segoe UI Variable",
            FontSize = 10f
        };
        using var layout = new CanvasTextLayout(
            CanvasDevice.GetSharedDevice(), label, format, 1000f, 100f);

        Assert.True(
            layout.LayoutBounds.Width <= ChartDrawing.AxisLabelRectWidth,
            $"'{label}' needs {layout.LayoutBounds.Width:0.00}px but the gutter is "
            + $"{ChartDrawing.AxisLabelRectWidth}px -- it will wrap onto two lines.");
    }

    [Fact]
    [Trait("Category", "RequiresGPU")]
    public void RenderChartToPng_WithEmptyPoints_ReturnsNonNullTarget()
    {
        var points = new List<CCInfoWindows.Models.UsageHistoryPoint>();

        var target = ExportHelper.RenderChartToPng(points, null, "0%", "05:00", 0.0, StubLocalizer);

        Assert.NotNull(target);
        target.Dispose();
    }

    [Fact]
    public void ExportCaptions_ResolveThroughTheSharedFallbackRule()
    {
        // ExportHelper used to carry its own copy of "blank or echoed uid means no translation"
        // (Caption), one of the four that finding 30 collapsed. The rule itself is asserted in
        // LocalizedTextTests; what matters here is that the export's own uid/fallback pairs still
        // survive a dictionary that cannot answer.
        Assert.Equal(
            ExportHelper.ExportConstants.SectionLabelFallback,
            LocalizedText.Resolve(
                uid => uid,
                ExportHelper.ExportConstants.SectionLabelUid,
                ExportHelper.ExportConstants.SectionLabelFallback,
                nameof(ExportHelper)));

        Assert.Equal(
            ExportHelper.ExportConstants.ResetInFallback,
            LocalizedText.Resolve(
                _ => string.Empty,
                ExportHelper.ExportConstants.ResetInLabelUid,
                ExportHelper.ExportConstants.ResetInFallback,
                nameof(ExportHelper)));
    }

    [Fact]
    public void ExportCaptions_CarryNoGermanLiteral()
    {
        // Finding 21: the section caption was the const "5-STUNDEN-FENSTER", so every PNG an English
        // user exported was captioned in German. The fallback is now the English text and the German
        // one lives in the de-DE dictionary under SectionLabelUid.
        Assert.Equal("5-HOUR WINDOW", ExportHelper.ExportConstants.SectionLabelFallback);
        Assert.Equal("SectionHeaderFiveHour", ExportHelper.ExportConstants.SectionLabelUid);
        Assert.Equal("ResetInLabel", ExportHelper.ExportConstants.ResetInLabelUid);
    }
}
