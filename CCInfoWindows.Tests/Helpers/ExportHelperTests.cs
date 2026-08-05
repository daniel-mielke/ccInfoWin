using CCInfoWindows.Helpers;
using Microsoft.Graphics.Canvas;
using Windows.UI;

namespace CCInfoWindows.Tests.Helpers;

public class ExportHelperTests
{
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
        var liveCanvasHeight = LiveBorderHeight - (2f * LiveBorderPadding);

        Assert.Equal(liveCanvasHeight, ExportHelper.ExportConstants.ChartAreaHeight);
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

        var target = ExportHelper.RenderChartToPng(points, windowStart, "80%", "02:30", 0.8);

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

        var target = ExportHelper.RenderChartToPng(points, windowStart, "30%", "01:23", 0.30);

        Assert.NotNull(target);
        target.Dispose();
    }

    [Fact]
    [Trait("Category", "RequiresGPU")]
    public void RenderChartToPng_WithEmptyPoints_ReturnsNonNullTarget()
    {
        var points = new List<CCInfoWindows.Models.UsageHistoryPoint>();

        var target = ExportHelper.RenderChartToPng(points, null, "0%", "05:00", 0.0);

        Assert.NotNull(target);
        target.Dispose();
    }
}
