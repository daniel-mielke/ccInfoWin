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
        Assert.Equal(240f, ExportHelper.ExportConstants.ExportHeight);
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
    public void RenderChartToPng_WithEmptyPoints_ReturnsNonNullTarget()
    {
        var points = new List<CCInfoWindows.Models.UsageHistoryPoint>();

        var target = ExportHelper.RenderChartToPng(points, null, "0%", "05:00", 0.0);

        Assert.NotNull(target);
        target.Dispose();
    }
}
