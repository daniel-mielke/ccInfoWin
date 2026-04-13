using CCInfoWindows.Helpers;
using CCInfoWindows.Models;
using Windows.UI;

namespace CCInfoWindows.Tests.Helpers;

public class ChartRendererTests
{
    // --- ToX tests ---

    [Fact]
    public void ToX_WindowStart_ReturnsZero()
    {
        var windowStart = DateTimeOffset.UtcNow;
        var result = ChartRenderer.ToX(windowStart, windowStart, plotWidth: 200f);
        Assert.Equal(0f, result, precision: 2);
    }

    [Fact]
    public void ToX_WindowEnd_ReturnsPlotWidth()
    {
        var windowStart = DateTimeOffset.UtcNow;
        var windowEnd = windowStart.AddSeconds(ChartRenderer.WindowDurationSeconds);
        var result = ChartRenderer.ToX(windowEnd, windowStart, plotWidth: 200f);
        Assert.Equal(200f, result, precision: 2);
    }

    [Fact]
    public void ToX_Midpoint_ReturnsHalfPlotWidth()
    {
        var windowStart = DateTimeOffset.UtcNow;
        var midpoint = windowStart.AddSeconds(ChartRenderer.WindowDurationSeconds / 2);
        var result = ChartRenderer.ToX(midpoint, windowStart, plotWidth: 200f);
        Assert.Equal(100f, result, precision: 2);
    }

    [Fact]
    public void ToX_BeforeWindowStart_ClampsToZero()
    {
        var windowStart = DateTimeOffset.UtcNow;
        var beforeStart = windowStart.AddSeconds(-60);
        var result = ChartRenderer.ToX(beforeStart, windowStart, plotWidth: 200f);
        Assert.Equal(0f, result, precision: 2);
    }

    [Fact]
    public void ToX_AfterWindowEnd_ClampsToPotWidth()
    {
        var windowStart = DateTimeOffset.UtcNow;
        var afterEnd = windowStart.AddSeconds(ChartRenderer.WindowDurationSeconds + 60);
        var result = ChartRenderer.ToX(afterEnd, windowStart, plotWidth: 200f);
        Assert.Equal(200f, result, precision: 2);
    }

    // --- ToY tests ---

    [Fact]
    public void ToY_ZeroUtilization_ReturnsTopMarginPlusPlotHeight()
    {
        var result = ChartRenderer.ToY(utilization: 0.0, plotHeight: 100f);
        Assert.Equal(ChartRenderer.TopMargin + 100f, result, precision: 2);
    }

    [Fact]
    public void ToY_FullUtilization_ReturnsTopMargin()
    {
        var result = ChartRenderer.ToY(utilization: 1.0, plotHeight: 100f);
        Assert.Equal(ChartRenderer.TopMargin, result, precision: 2);
    }

    [Fact]
    public void ToY_HalfUtilization_ReturnsTopMarginPlusHalfPlotHeight()
    {
        var result = ChartRenderer.ToY(utilization: 0.5, plotHeight: 100f);
        Assert.Equal(ChartRenderer.TopMargin + 50f, result, precision: 2);
    }

    [Fact]
    public void ToY_OverOneUtilization_ClampsToTopMargin()
    {
        var result = ChartRenderer.ToY(utilization: 1.5, plotHeight: 100f);
        Assert.Equal(ChartRenderer.TopMargin, result, precision: 2);
    }

    [Fact]
    public void ToY_NegativeUtilization_ClampsToTopMarginPlusPlotHeight()
    {
        var result = ChartRenderer.ToY(utilization: -0.1, plotHeight: 100f);
        Assert.Equal(ChartRenderer.TopMargin + 100f, result, precision: 2);
    }

    // --- GetZoneSegments tests ---

    [Fact]
    public void GetZoneSegments_SinglePoint_ReturnsOneSegment()
    {
        var points = new List<UsageHistoryPoint>
        {
            new() { Timestamp = DateTimeOffset.UtcNow, Utilization = 0.3 }
        };

        var segments = ChartRenderer.GetZoneSegments(points);

        Assert.Single(segments);
        Assert.Equal(0, segments[0].StartIndex);
        Assert.Equal(0, segments[0].EndIndex);
        Assert.Equal("ProgressGreenBrush", segments[0].BrushKey);
    }

    [Fact]
    public void GetZoneSegments_AllSameZone_ReturnsOneSegment()
    {
        var now = DateTimeOffset.UtcNow;
        var points = new List<UsageHistoryPoint>
        {
            new() { Timestamp = now, Utilization = 0.1 },
            new() { Timestamp = now.AddMinutes(1), Utilization = 0.2 },
            new() { Timestamp = now.AddMinutes(2), Utilization = 0.3 }
        };

        var segments = ChartRenderer.GetZoneSegments(points);

        Assert.Single(segments);
        Assert.Equal(0, segments[0].StartIndex);
        Assert.Equal(2, segments[0].EndIndex);
        Assert.Equal("ProgressGreenBrush", segments[0].BrushKey);
    }

    [Fact]
    public void GetZoneSegments_CrossingFiftyPercent_ReturnsTwoSegments()
    {
        var now = DateTimeOffset.UtcNow;
        var points = new List<UsageHistoryPoint>
        {
            new() { Timestamp = now, Utilization = 0.3 },
            new() { Timestamp = now.AddMinutes(1), Utilization = 0.6 }
        };

        var segments = ChartRenderer.GetZoneSegments(points);

        Assert.Equal(2, segments.Count);
        Assert.Equal("ProgressGreenBrush", segments[0].BrushKey);
        Assert.Equal(0, segments[0].StartIndex);
        Assert.Equal(0, segments[0].EndIndex);
        Assert.Equal("ProgressYellowBrush", segments[1].BrushKey);
        Assert.Equal(1, segments[1].StartIndex);
        Assert.Equal(1, segments[1].EndIndex);
    }

    [Fact]
    public void GetZoneSegments_MultipleZones_ReturnsCorrectSegments()
    {
        var now = DateTimeOffset.UtcNow;
        var points = new List<UsageHistoryPoint>
        {
            new() { Timestamp = now, Utilization = 0.1 },           // green
            new() { Timestamp = now.AddMinutes(1), Utilization = 0.3 }, // green
            new() { Timestamp = now.AddMinutes(2), Utilization = 0.6 }, // yellow
            new() { Timestamp = now.AddMinutes(3), Utilization = 0.8 }, // orange
            new() { Timestamp = now.AddMinutes(4), Utilization = 0.95 } // red
        };

        var segments = ChartRenderer.GetZoneSegments(points);

        Assert.Equal(4, segments.Count);
        Assert.Equal("ProgressGreenBrush", segments[0].BrushKey);
        Assert.Equal(0, segments[0].StartIndex);
        Assert.Equal(1, segments[0].EndIndex);
        Assert.Equal("ProgressYellowBrush", segments[1].BrushKey);
        Assert.Equal(2, segments[1].StartIndex);
        Assert.Equal(2, segments[1].EndIndex);
        Assert.Equal("ProgressOrangeBrush", segments[2].BrushKey);
        Assert.Equal(3, segments[2].StartIndex);
        Assert.Equal(3, segments[2].EndIndex);
        Assert.Equal("ProgressRedBrush", segments[3].BrushKey);
        Assert.Equal(4, segments[3].StartIndex);
        Assert.Equal(4, segments[3].EndIndex);
    }

    [Fact]
    public void GetZoneSegments_EmptyList_ReturnsEmptyList()
    {
        var segments = ChartRenderer.GetZoneSegments([]);
        Assert.Empty(segments);
    }

    // --- GetRightEdgeAbsoluteX tests ---

    [Fact]
    public void GetRightEdgeAbsoluteX_MidSegment_ReturnsNextPointX()
    {
        // endIndex < last point: right edge is next point's X (canvas-absolute)
        const float plotWidth = 200f;
        var windowStart = DateTimeOffset.UtcNow.AddHours(-2);
        var points = new List<UsageHistoryPoint>
        {
            new() { Timestamp = windowStart.AddHours(1), Utilization = 0.3 },
            new() { Timestamp = windowStart.AddHours(1.5), Utilization = 0.5 }
        };

        var result = ChartRenderer.GetRightEdgeAbsoluteX(points, endIndex: 0, windowStart, plotWidth);

        var expectedRelativeX = ChartRenderer.ToX(points[1].Timestamp, windowStart, plotWidth);
        var expectedAbsoluteX = ChartRenderer.LeftMargin + expectedRelativeX;
        Assert.Equal(expectedAbsoluteX, result, precision: 2);
    }

    [Fact]
    public void GetRightEdgeAbsoluteX_LastSegmentNowWithinWindow_ReturnsNowX()
    {
        // endIndex == last point, now is within the 5-hour window
        const float plotWidth = 200f;
        var windowStart = DateTimeOffset.UtcNow.AddHours(-2); // now is 2h into a 5h window
        var points = new List<UsageHistoryPoint>
        {
            new() { Timestamp = windowStart.AddHours(1), Utilization = 0.3 }
        };

        var result = ChartRenderer.GetRightEdgeAbsoluteX(points, endIndex: 0, windowStart, plotWidth);

        // now is within the 5-hour window, so nowX < plotWidth -- result should be LeftMargin + nowX
        var nowX = ChartRenderer.ToX(DateTimeOffset.UtcNow, windowStart, plotWidth);
        var expectedAbsoluteX = ChartRenderer.LeftMargin + Math.Min(nowX, plotWidth);
        Assert.Equal(expectedAbsoluteX, result, precision: 1);
    }

    [Fact]
    public void GetRightEdgeAbsoluteX_LastSegmentNowBeyondWindow_ClampsToPlotWidth()
    {
        // endIndex == last point, now is past the 5-hour window end -- clamp to plotWidth
        const float plotWidth = 200f;
        var windowStart = DateTimeOffset.UtcNow.AddHours(-6); // window ended 1 hour ago
        var points = new List<UsageHistoryPoint>
        {
            new() { Timestamp = windowStart.AddHours(1), Utilization = 0.3 }
        };

        var result = ChartRenderer.GetRightEdgeAbsoluteX(points, endIndex: 0, windowStart, plotWidth);

        Assert.Equal(ChartRenderer.LeftMargin + plotWidth, result, precision: 2);
    }

    // --- GetContiguousSpans tests ---

    [Fact]
    public void GetContiguousSpans_EmptyPoints_ReturnsEmptyList()
    {
        var spans = ChartRenderer.GetContiguousSpans([]);
        Assert.Empty(spans);
    }

    [Fact]
    public void GetContiguousSpans_SinglePoint_ReturnsOneSpan_0_0()
    {
        var points = new List<UsageHistoryPoint>
        {
            new() { Timestamp = DateTimeOffset.UtcNow, Utilization = 0.3 }
        };

        var spans = ChartRenderer.GetContiguousSpans(points);

        Assert.Single(spans);
        Assert.Equal(0, spans[0].StartIndex);
        Assert.Equal(0, spans[0].EndIndex);
    }

    [Fact]
    public void GetContiguousSpans_FivePoints_ReturnsOneSpan_0_4()
    {
        var now = DateTimeOffset.UtcNow;
        var points = new List<UsageHistoryPoint>
        {
            new() { Timestamp = now, Utilization = 0.1 },
            new() { Timestamp = now.AddMinutes(1), Utilization = 0.3 },
            new() { Timestamp = now.AddMinutes(2), Utilization = 0.5 },
            new() { Timestamp = now.AddMinutes(3), Utilization = 0.7 },
            new() { Timestamp = now.AddMinutes(4), Utilization = 0.9 },
        };

        var spans = ChartRenderer.GetContiguousSpans(points);

        Assert.Single(spans);
        Assert.Equal(0, spans[0].StartIndex);
        Assert.Equal(4, spans[0].EndIndex);
    }

    [Fact]
    public void GetContiguousSpans_HundredPoints_ReturnsOneSpan_0_99()
    {
        var now = DateTimeOffset.UtcNow;
        var points = Enumerable.Range(0, 100)
            .Select(i => new UsageHistoryPoint
            {
                Timestamp = now.AddMinutes(i),
                Utilization = i / 100.0
            })
            .ToList();

        var spans = ChartRenderer.GetContiguousSpans(points);

        Assert.Single(spans);
        Assert.Equal(0, spans[0].StartIndex);
        Assert.Equal(99, spans[0].EndIndex);
    }

    // --- BuildGradientStops tests ---

    [Fact]
    public void BuildGradientStops_SinglePointSpan_ReturnsOneStop_Position0()
    {
        var windowStart = DateTimeOffset.UtcNow.AddHours(-2);
        var points = new List<UsageHistoryPoint>
        {
            new() { Timestamp = windowStart.AddHours(1), Utilization = 0.0 }
        };
        var colorLookup = ChartColors.BuildColorLookup(isDark: true);

        var stops = ChartRenderer.BuildGradientStops(points, 0, 0, windowStart, 200f, colorLookup);

        Assert.Single(stops);
        Assert.Equal(0.0f, stops[0].Position, precision: 4);
    }

    [Fact]
    public void BuildGradientStops_TwoPointSpan_FirstPosition0_LastPosition1()
    {
        var windowStart = DateTimeOffset.UtcNow.AddHours(-4);
        var points = new List<UsageHistoryPoint>
        {
            new() { Timestamp = windowStart.AddHours(1), Utilization = 0.1 },
            new() { Timestamp = windowStart.AddHours(3), Utilization = 0.9 },
        };
        var colorLookup = ChartColors.BuildColorLookup(isDark: true);

        var stops = ChartRenderer.BuildGradientStops(points, 0, 1, windowStart, 200f, colorLookup);

        Assert.Equal(2, stops.Length);
        Assert.Equal(0.0f, stops[0].Position, precision: 4);
        Assert.Equal(1.0f, stops[^1].Position, precision: 4);
    }

    [Fact]
    public void BuildGradientStops_StopColors_MatchBuildColorLookup()
    {
        var windowStart = DateTimeOffset.UtcNow.AddHours(-4);
        var points = new List<UsageHistoryPoint>
        {
            new() { Timestamp = windowStart.AddHours(1), Utilization = 0.0 },
            new() { Timestamp = windowStart.AddHours(2), Utilization = 0.5 },
            new() { Timestamp = windowStart.AddHours(3), Utilization = 1.0 },
        };
        var colorLookup = ChartColors.BuildColorLookup(isDark: true);

        var stops = ChartRenderer.BuildGradientStops(points, 0, 2, windowStart, 200f, colorLookup);

        Assert.Equal(colorLookup[0], stops[0].Color);
        Assert.Equal(colorLookup[50], stops[1].Color);
        Assert.Equal(colorLookup[100], stops[2].Color);
    }

    [Fact]
    public void BuildGradientStops_PositionsNormalizedWithinSpan_NotFullChartWidth()
    {
        // Points at hours 1, 2, 3 within a window that starts at hour 0 (total 5 hours)
        // Span relative positions should be 0.0, 0.5, 1.0 (not absolute chart fractions)
        var windowStart = DateTimeOffset.UtcNow.AddHours(-4);
        var points = new List<UsageHistoryPoint>
        {
            new() { Timestamp = windowStart.AddHours(1), Utilization = 0.2 },
            new() { Timestamp = windowStart.AddHours(2), Utilization = 0.4 },
            new() { Timestamp = windowStart.AddHours(3), Utilization = 0.6 },
        };
        var colorLookup = ChartColors.BuildColorLookup(isDark: true);

        var stops = ChartRenderer.BuildGradientStops(points, 0, 2, windowStart, 200f, colorLookup);

        Assert.Equal(0.0f, stops[0].Position, precision: 3);
        Assert.Equal(0.5f, stops[1].Position, precision: 3);
        Assert.Equal(1.0f, stops[2].Position, precision: 3);
    }

    [Fact]
    public void BuildGradientStops_ReturnType_IsTupleArray_NotCanvasGradientStop()
    {
        var windowStart = DateTimeOffset.UtcNow.AddHours(-2);
        var points = new List<UsageHistoryPoint>
        {
            new() { Timestamp = windowStart.AddHours(1), Utilization = 0.5 }
        };
        var colorLookup = ChartColors.BuildColorLookup(isDark: true);

        var stops = ChartRenderer.BuildGradientStops(points, 0, 0, windowStart, 200f, colorLookup);

        Assert.IsType<(float Position, Color Color)[]>(stops);
    }

    [Fact]
    public void BuildGradientStops_AllPositions_ClampedBetween0And1()
    {
        var windowStart = DateTimeOffset.UtcNow.AddHours(-4);
        var points = Enumerable.Range(0, 10)
            .Select(i => new UsageHistoryPoint
            {
                Timestamp = windowStart.AddHours(i * 0.3),
                Utilization = i / 10.0
            })
            .ToList();
        var colorLookup = ChartColors.BuildColorLookup(isDark: true);

        var stops = ChartRenderer.BuildGradientStops(points, 0, points.Count - 1, windowStart, 200f, colorLookup);

        foreach (var stop in stops)
        {
            Assert.InRange(stop.Position, 0.0f, 1.0f);
        }
    }
}
