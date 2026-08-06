using CCInfoWindows.Helpers;
using CCInfoWindows.Models;
using Windows.UI;

namespace CCInfoWindows.Tests.Helpers;

public class ChartRendererTests
{
    // --- ToX tests (inset-aware since the glow-clipping fix) ---

    [Fact]
    public void ToX_WindowStart_ReturnsGlowInset()
    {
        var windowStart = DateTimeOffset.UtcNow;
        var result = ChartRenderer.ToX(windowStart, windowStart, plotWidth: 200f);
        Assert.Equal(ChartRenderer.GlowInset, result, precision: 2);
    }

    [Fact]
    public void ToX_WindowEnd_ReturnsPlotWidthMinusGlowInset()
    {
        var windowStart = DateTimeOffset.UtcNow;
        var windowEnd = windowStart.AddSeconds(ChartRenderer.WindowDurationSeconds);
        var result = ChartRenderer.ToX(windowEnd, windowStart, plotWidth: 200f);
        Assert.Equal(200f - ChartRenderer.GlowInset, result, precision: 2);
    }

    [Fact]
    public void ToX_ZeroPlotWidth_DoesNotProduceNegativeSpan()
    {
        // ActualWidth is 0 during the first layout pass; a negative span would put NaN in the path.
        var windowStart = DateTimeOffset.UtcNow;
        var result = ChartRenderer.ToX(windowStart.AddHours(2), windowStart, plotWidth: 0f);

        Assert.True(result >= ChartRenderer.GlowInset);
        Assert.False(float.IsNaN(result));
    }

    [Fact]
    public void PlotSpanWidth_And_PlotBandHeight_ClampToOne()
    {
        Assert.Equal(1f, ChartRenderer.PlotSpanWidth(0f), precision: 4);
        Assert.Equal(1f, ChartRenderer.PlotBandHeight(0f), precision: 4);
    }

    [Fact]
    public void ToX_Midpoint_ReturnsHalfPlotWidth()
    {
        // Symmetry guard: the insets are equal on both sides, so the midpoint stays at
        // plotWidth/2 no matter what GlowInset is. Keep this one as-is on purpose.
        var windowStart = DateTimeOffset.UtcNow;
        var midpoint = windowStart.AddSeconds(ChartRenderer.WindowDurationSeconds / 2);
        var result = ChartRenderer.ToX(midpoint, windowStart, plotWidth: 200f);
        Assert.Equal(100f, result, precision: 2);
    }

    [Fact]
    public void ToX_BeforeWindowStart_ClampsToGlowInset()
    {
        var windowStart = DateTimeOffset.UtcNow;
        var beforeStart = windowStart.AddSeconds(-60);
        var result = ChartRenderer.ToX(beforeStart, windowStart, plotWidth: 200f);
        Assert.Equal(ChartRenderer.GlowInset, result, precision: 2);
    }

    [Fact]
    public void ToX_AfterWindowEnd_ClampsToPlotWidthMinusGlowInset()
    {
        var windowStart = DateTimeOffset.UtcNow;
        var afterEnd = windowStart.AddSeconds(ChartRenderer.WindowDurationSeconds + 60);
        var result = ChartRenderer.ToX(afterEnd, windowStart, plotWidth: 200f);
        Assert.Equal(200f - ChartRenderer.GlowInset, result, precision: 2);
    }

    // --- ToY tests (inset-aware) ---

    [Fact]
    public void ToY_ZeroUtilization_SitsGlowInsetAboveTheBottom()
    {
        var result = ChartRenderer.ToY(utilization: 0.0, plotHeight: 100f);
        Assert.Equal(ChartRenderer.TopMargin + 100f - ChartRenderer.GlowInset, result, precision: 2);
    }

    [Fact]
    public void ToY_FullUtilization_SitsGlowInsetBelowTheTop()
    {
        var result = ChartRenderer.ToY(utilization: 1.0, plotHeight: 100f);
        Assert.Equal(ChartRenderer.TopMargin + ChartRenderer.GlowInset, result, precision: 2);
    }

    [Fact]
    public void ToY_HalfUtilization_ReturnsTopMarginPlusHalfPlotHeight()
    {
        // Same symmetry argument as ToX_Midpoint.
        var result = ChartRenderer.ToY(utilization: 0.5, plotHeight: 100f);
        Assert.Equal(ChartRenderer.TopMargin + 50f, result, precision: 2);
    }

    [Fact]
    public void ToY_OverOneUtilization_ClampsToInsetTop()
    {
        var result = ChartRenderer.ToY(utilization: 1.5, plotHeight: 100f);
        Assert.Equal(ChartRenderer.TopMargin + ChartRenderer.GlowInset, result, precision: 2);
    }

    [Fact]
    public void ToY_NegativeUtilization_ClampsToInsetBottom()
    {
        var result = ChartRenderer.ToY(utilization: -0.1, plotHeight: 100f);
        Assert.Equal(ChartRenderer.TopMargin + 100f - ChartRenderer.GlowInset, result, precision: 2);
    }

    // --- Glow has room at both vertical extremes ---

    [Fact]
    public void ToY_AtZeroPercent_LeavesGlowRadiusAboveThePlotBottom()
    {
        const float plotHeight = 118f;   // live canvas 144 minus top/bottom margins
        var y = ChartRenderer.ToY(0.0, plotHeight);
        var plotBottom = ChartRenderer.TopMargin + plotHeight;

        Assert.True(plotBottom - y >= ChartRenderer.GlowInset,
            $"glow at 0% would be clipped: y={y}, bottom={plotBottom}");
    }

    [Fact]
    public void ToY_AtHundredPercent_LeavesGlowRadiusBelowThePlotTop()
    {
        const float plotHeight = 118f;
        var y = ChartRenderer.ToY(1.0, plotHeight);

        Assert.True(y - ChartRenderer.TopMargin >= ChartRenderer.GlowInset,
            $"glow at 100% would be clipped: y={y}, top={ChartRenderer.TopMargin}");
    }

    // --- GetRightEdgeX tests (plot-relative, same space as ToX) ---

    [Fact]
    public void GetRightEdgeX_MidSegment_ReturnsNextPointX()
    {
        const float plotWidth = 200f;
        var windowStart = DateTimeOffset.UtcNow.AddHours(-2);
        var points = new List<UsageHistoryPoint>
        {
            new() { Timestamp = windowStart.AddHours(1), Utilization = 0.3 },
            new() { Timestamp = windowStart.AddHours(1.5), Utilization = 0.5 }
        };

        var result = ChartRenderer.GetRightEdgeX(points, endIndex: 0, windowStart, plotWidth);

        var expected = ChartRenderer.ToX(points[1].Timestamp, windowStart, plotWidth);
        Assert.Equal(expected, result, precision: 2);
    }

    [Fact]
    public void GetRightEdgeX_LastSegmentNowWithinWindow_ReturnsNowX()
    {
        // endIndex == last point, now is within the 5-hour window
        const float plotWidth = 200f;
        var windowStart = DateTimeOffset.UtcNow.AddHours(-2); // now is 2h into a 5h window
        var points = new List<UsageHistoryPoint>
        {
            new() { Timestamp = windowStart.AddHours(1), Utilization = 0.3 }
        };

        var result = ChartRenderer.GetRightEdgeX(points, endIndex: 0, windowStart, plotWidth);

        // ToX already clamps, so the extra Math.Min the old implementation applied was redundant.
        var nowX = ChartRenderer.ToX(DateTimeOffset.UtcNow, windowStart, plotWidth);
        Assert.Equal(nowX, result, precision: 1);
    }

    [Fact]
    public void GetRightEdgeX_LastSegmentNowBeyondWindow_ClampsToInsetRightEdge()
    {
        // endIndex == last point, now is past the 5-hour window end. ToX clamps to the inset
        // right edge; the old Math.Min(nowX, plotWidth) would have pushed the glow into the
        // clipped zone it was supposed to stay out of.
        const float plotWidth = 200f;
        var windowStart = DateTimeOffset.UtcNow.AddHours(-6); // window ended 1 hour ago
        var points = new List<UsageHistoryPoint>
        {
            new() { Timestamp = windowStart.AddHours(1), Utilization = 0.3 }
        };

        var result = ChartRenderer.GetRightEdgeX(points, endIndex: 0, windowStart, plotWidth);

        Assert.Equal(plotWidth - ChartRenderer.GlowInset, result, precision: 2);
    }

    // --- BuildGradientStops tests ---
    //
    // spanEndX is always ChartRenderer.GetRightEdgeX for the span, exactly as ChartDrawing passes
    // it. Where a test wants the pre-fix "normalise to the last sample" framing it passes
    // ToX(last point) explicitly, which is what that value collapses to when polling is current.

    [Fact]
    public void BuildGradientStops_SinglePointSpan_ReturnsOneStop_Position0()
    {
        var windowStart = DateTimeOffset.UtcNow.AddHours(-2);
        var points = new List<UsageHistoryPoint>
        {
            new() { Timestamp = windowStart.AddHours(1), Utilization = 0.0 }
        };
        var colorLookup = ChartColors.BuildColorLookup(isDark: true);

        var stops = ChartRenderer.BuildGradientStops(
            points, 0, 0, windowStart, 200f, colorLookup,
            spanEndX: ChartRenderer.ToX(points[0].Timestamp, windowStart, 200f));

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

        var stops = ChartRenderer.BuildGradientStops(
            points, 0, 1, windowStart, 200f, colorLookup,
            spanEndX: ChartRenderer.ToX(points[1].Timestamp, windowStart, 200f));

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

        var stops = ChartRenderer.BuildGradientStops(
            points, 0, 2, windowStart, 200f, colorLookup,
            spanEndX: ChartRenderer.ToX(points[2].Timestamp, windowStart, 200f));

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

        var stops = ChartRenderer.BuildGradientStops(
            points, 0, 2, windowStart, 200f, colorLookup,
            spanEndX: ChartRenderer.ToX(points[2].Timestamp, windowStart, 200f));

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

        var stops = ChartRenderer.BuildGradientStops(
            points, 0, 0, windowStart, 200f, colorLookup,
            spanEndX: ChartRenderer.ToX(points[0].Timestamp, windowStart, 200f));

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

        var stops = ChartRenderer.BuildGradientStops(
            points, 0, points.Count - 1, windowStart, 200f, colorLookup,
            spanEndX: ChartRenderer.GetRightEdgeX(points, points.Count - 1, windowStart, 200f));

        foreach (var stop in stops)
        {
            Assert.InRange(stop.Position, 0.0f, 1.0f);
        }
    }

    [Fact]
    public void BuildGradientStops_RightEdgePastTheLastSample_KeepsTheLastStopBelowOne()
    {
        // Machine slept: the newest sample is at 2h but the right edge (and therefore the brush
        // endpoint) is at 4h. Forcing the last stop to 1.0 used to stretch the gradient 2.25x and
        // paint the 1h colour at 2.25h. Clamp edge behaviour holds the final colour across the
        // flat extension instead, which is what the curve actually shows there.
        var windowStart = DateTimeOffset.UtcNow.AddHours(-4);
        var points = new List<UsageHistoryPoint>
        {
            new() { Timestamp = windowStart.AddHours(1), Utilization = 0.2 },
            new() { Timestamp = windowStart.AddHours(2), Utilization = 0.6 },
        };
        var colorLookup = ChartColors.BuildColorLookup(isDark: true);

        var stops = ChartRenderer.BuildGradientStops(
            points, 0, 1, windowStart, 200f, colorLookup,
            spanEndX: ChartRenderer.ToX(windowStart.AddHours(4), windowStart, 200f));

        Assert.Equal(0.0f, stops[0].Position, precision: 4);
        Assert.Equal(1.0f / 3.0f, stops[^1].Position, precision: 4);
    }

    [Fact]
    public void BuildGradientStops_EveryStop_RendersAtItsOwnSampleX()
    {
        // The invariant behind finding 38: a stop at position p renders at
        // spanStartX + p * (spanEndX - spanStartX). That has to be the sample's own X, or the
        // colouring and the geometry disagree.
        const float plotWidth = 200f;
        var windowStart = DateTimeOffset.UtcNow.AddHours(-4);
        var points = Enumerable.Range(0, 6)
            .Select(i => new UsageHistoryPoint
            {
                Timestamp = windowStart.AddMinutes(i * 20),
                Utilization = i / 6.0
            })
            .ToList();
        var colorLookup = ChartColors.BuildColorLookup(isDark: true);
        var spanStartX = ChartRenderer.ToX(points[0].Timestamp, windowStart, plotWidth);
        var spanEndX = ChartRenderer.ToX(windowStart.AddHours(4), windowStart, plotWidth);

        var stops = ChartRenderer.BuildGradientStops(
            points, 0, points.Count - 1, windowStart, plotWidth, colorLookup, spanEndX);

        for (var i = 0; i < points.Count; i++)
        {
            var renderedX = spanStartX + (stops[i].Position * (spanEndX - spanStartX));
            var sampleX = ChartRenderer.ToX(points[i].Timestamp, windowStart, plotWidth);
            Assert.Equal(sampleX, renderedX, precision: 3);
        }
    }

    // --- FilterByMinSpacing ---

    [Fact]
    public void FilterByMinSpacing_EmptyInput_ReturnsEmpty()
        => Assert.Empty(ChartRenderer.FilterByMinSpacing([], DateTimeOffset.UtcNow, 200f));

    [Fact]
    public void FilterByMinSpacing_SinglePoint_KeepsIt()
    {
        var windowStart = DateTimeOffset.UtcNow.AddHours(-1);
        var points = new List<UsageHistoryPoint>
        {
            new() { Timestamp = windowStart.AddMinutes(10), Utilization = 0.4 }
        };

        var result = ChartRenderer.FilterByMinSpacing(points, windowStart, 200f);

        Assert.Single(result);
        Assert.Equal(0.4, result[0].Utilization);
    }

    [Fact]
    public void FilterByMinSpacing_DuplicateTimestamps_CollapseToOne()
    {
        var windowStart = DateTimeOffset.UtcNow.AddHours(-2);
        var t = windowStart.AddHours(1);
        var points = new List<UsageHistoryPoint>
        {
            new() { Timestamp = t, Utilization = 0.2 },
            new() { Timestamp = t, Utilization = 0.3 },
            new() { Timestamp = t, Utilization = 0.4 },
        };

        var result = ChartRenderer.FilterByMinSpacing(points, windowStart, 200f);

        Assert.Single(result);
        Assert.Equal(0.4, result[0].Utilization);   // newest wins
    }

    [Fact]
    public void FilterByMinSpacing_PointsPastWindowEnd_CollapseToOne()
    {
        // ToX clamps everything past the 5-hour mark to the same X. Left alone that is dx == 0
        // in the tangent calculation, i.e. NaN into the Win2D path.
        var windowStart = DateTimeOffset.UtcNow.AddHours(-8);
        var points = new List<UsageHistoryPoint>
        {
            new() { Timestamp = windowStart.AddHours(1), Utilization = 0.2 },
            new() { Timestamp = windowStart.AddHours(6), Utilization = 0.5 },
            new() { Timestamp = windowStart.AddHours(7), Utilization = 0.6 },
            new() { Timestamp = windowStart.AddHours(8), Utilization = 0.7 },
        };

        var result = ChartRenderer.FilterByMinSpacing(points, windowStart, 200f);

        Assert.Equal(2, result.Count);
        Assert.Equal(0.7, result[^1].Utilization);
    }

    [Fact]
    public void FilterByMinSpacing_AlwaysKeepsTheNewestSample()
    {
        // The newest value positions the glow indicator, so it has to survive downsampling.
        var windowStart = DateTimeOffset.UtcNow.AddHours(-5);
        var points = Enumerable.Range(0, 600)
            .Select(i => new UsageHistoryPoint
            {
                Timestamp = windowStart.AddSeconds(i * 30),
                Utilization = i / 600.0
            })
            .ToList();

        var result = ChartRenderer.FilterByMinSpacing(points, windowStart, 200f);

        Assert.Equal(points[^1].Utilization, result[^1].Utilization);
    }

    [Fact]
    public void FilterByMinSpacing_SixHundredPointsOnTwoHundredPixels_DownsamplesToPixelWidth()
    {
        var windowStart = DateTimeOffset.UtcNow.AddHours(-5);
        var points = Enumerable.Range(0, 600)
            .Select(i => new UsageHistoryPoint
            {
                Timestamp = windowStart.AddSeconds(i * 30),
                Utilization = 0.5
            })
            .ToList();

        var result = ChartRenderer.FilterByMinSpacing(points, windowStart, 200f);

        Assert.True(result.Count <= 200, $"expected <= 200 points after downsampling, got {result.Count}");
        Assert.True(result.Count > 100, $"downsampling threw away too much: {result.Count}");
    }

    [Fact]
    public void FilterByMinSpacing_ResultHasStrictlyIncreasingX()
    {
        const float plotWidth = 200f;
        var windowStart = DateTimeOffset.UtcNow.AddHours(-8);
        var points = Enumerable.Range(0, 900)
            .Select(i => new UsageHistoryPoint
            {
                Timestamp = windowStart.AddSeconds(i * 30),   // runs well past the window end
                Utilization = (i % 100) / 100.0
            })
            .ToList();

        var result = ChartRenderer.FilterByMinSpacing(points, windowStart, plotWidth);

        for (var i = 1; i < result.Count; i++)
        {
            var prev = ChartRenderer.ToX(result[i - 1].Timestamp, windowStart, plotWidth);
            var cur = ChartRenderer.ToX(result[i].Timestamp, windowStart, plotWidth);
            Assert.True(cur > prev, $"X not strictly increasing at index {i}: {prev} -> {cur}");
        }
    }

    // --- ComputeMonotoneTangents ---

    [Fact]
    public void ComputeMonotoneTangents_Empty_ReturnsEmpty()
        => Assert.Empty(ChartRenderer.ComputeMonotoneTangents([], []));

    [Fact]
    public void ComputeMonotoneTangents_SinglePoint_ReturnsZero()
        => Assert.Equal([0.0], ChartRenderer.ComputeMonotoneTangents([1.0], [2.0]));

    [Fact]
    public void ComputeMonotoneTangents_TwoPoints_BothEqualTheSecant()
    {
        // n == 2 degenerates into a straight line on its own — no special case needed.
        var m = ChartRenderer.ComputeMonotoneTangents([0.0, 10.0], [0.0, 5.0]);

        Assert.Equal(2, m.Length);
        Assert.Equal(0.5, m[0], precision: 10);
        Assert.Equal(0.5, m[1], precision: 10);
    }

    [Fact]
    public void ComputeMonotoneTangents_TwoPoints_ProduceCollinearControlPoints()
    {
        var m = ChartRenderer.ComputeMonotoneTangents([0.0, 9.0], [0.0, 3.0]);
        var (c1, c2) = ChartRenderer.ToBezierControlPoints(0.0, 0.0, m[0], 9.0, 3.0, m[1]);

        // Both control points must sit on the straight line y = x/3.
        Assert.Equal(c1.X / 3.0, c1.Y, precision: 10);
        Assert.Equal(c2.X / 3.0, c2.Y, precision: 10);
    }

    [Fact]
    public void ComputeMonotoneTangents_Plateau_ProducesFlatTangents()
    {
        var m = ChartRenderer.ComputeMonotoneTangents([0.0, 1.0, 2.0], [5.0, 5.0, 5.0]);

        Assert.All(m, value => Assert.Equal(0.0, value, precision: 10));
    }

    [Fact]
    public void ComputeMonotoneTangents_LocalMaximum_FlattensTheTangentThere()
    {
        var m = ChartRenderer.ComputeMonotoneTangents([0.0, 1.0, 2.0], [0.0, 10.0, 0.0]);

        Assert.Equal(0.0, m[1], precision: 10);
    }

    [Fact]
    public void ComputeMonotoneTangents_LocalMinimum_FlattensTheTangentThere()
    {
        var m = ChartRenderer.ComputeMonotoneTangents([0.0, 1.0, 2.0], [10.0, 0.0, 10.0]);

        Assert.Equal(0.0, m[1], precision: 10);
    }

    [Fact]
    public void ComputeMonotoneTangents_SatisfyTheFritschCarlsonCircleCondition()
    {
        var xs = new[] { 0.0, 1.0, 2.0, 3.0, 4.0, 5.0 };
        var ys = new[] { 0.0, 0.01, 0.9, 0.92, 0.93, 1.0 };

        var m = ChartRenderer.ComputeMonotoneTangents(xs, ys);

        for (var i = 0; i < xs.Length - 1; i++)
        {
            var secant = (ys[i + 1] - ys[i]) / (xs[i + 1] - xs[i]);
            if (secant == 0.0) continue;

            var a = m[i] / secant;
            var b = m[i + 1] / secant;
            Assert.True((a * a) + (b * b) <= 9.0 + 1e-9,
                $"circle condition violated at {i}: a^2+b^2 = {(a * a) + (b * b)}");
        }
    }

    [Fact]
    public void ComputeMonotoneTangents_TangentsShareTheSignOfTheirNeighbouringSecants()
    {
        var xs = new[] { 0.0, 1.0, 2.0, 3.0 };
        var ys = new[] { 0.0, 3.0, 3.5, 10.0 };

        var m = ChartRenderer.ComputeMonotoneTangents(xs, ys);

        Assert.All(m, value => Assert.True(value >= 0.0, $"monotone-increasing data got tangent {value}"));
    }

    [Fact]
    public void MonotoneCurve_DoesNotOvershoot_OnDenseSampling()
    {
        // The reason Fritsch-Carlson was chosen over Catmull-Rom: a 95% -> 60% drop must not
        // bulge past 100% and clip at the top gridline. Evaluate the actual Bezier densely and
        // assert it stays inside the enclosing sample values on every segment.
        var xs = new[] { 0.0, 1.0, 2.0, 3.0, 4.0 };
        var ys = new[] { 0.10, 0.95, 0.60, 0.62, 0.20 };

        var m = ChartRenderer.ComputeMonotoneTangents(xs, ys);

        for (var i = 1; i < xs.Length; i++)
        {
            var (c1, c2) = ChartRenderer.ToBezierControlPoints(
                xs[i - 1], ys[i - 1], m[i - 1], xs[i], ys[i], m[i]);

            var lo = Math.Min(ys[i - 1], ys[i]);
            var hi = Math.Max(ys[i - 1], ys[i]);

            for (var step = 0; step <= 100; step++)
            {
                var t = step / 100.0;
                var y = CubicBezier(ys[i - 1], c1.Y, c2.Y, ys[i], t);
                Assert.InRange(y, lo - 1e-9, hi + 1e-9);
            }
        }
    }

    // --- ToBezierControlPoints ---

    [Fact]
    public void ToBezierControlPoints_PlacesControlPointsAtOneThirdOfDx()
    {
        // dx/3 is load-bearing: dx/2 looks smooth but stops honouring the tangents, which brings
        // the overshoot back. Pinned deliberately.
        var (c1, c2) = ChartRenderer.ToBezierControlPoints(0.0, 0.0, 1.0, 9.0, 9.0, 1.0);

        Assert.Equal(3.0, c1.X, precision: 10);
        Assert.Equal(6.0, c2.X, precision: 10);
    }

    [Fact]
    public void ToBezierControlPoints_AppliesTangentSlopeOverTheSameThird()
    {
        var (c1, c2) = ChartRenderer.ToBezierControlPoints(0.0, 0.0, 2.0, 3.0, 12.0, 4.0);

        Assert.Equal(2.0, c1.Y, precision: 10);   // 0 + 2 * (3/3)
        Assert.Equal(8.0, c2.Y, precision: 10);   // 12 - 4 * (3/3)
    }

    [Fact]
    public void ToBezierControlPoints_ZeroTangents_KeepControlPointsLevelWithTheirEnds()
    {
        var (c1, c2) = ChartRenderer.ToBezierControlPoints(0.0, 5.0, 0.0, 6.0, 5.0, 0.0);

        Assert.Equal(5.0, c1.Y, precision: 10);
        Assert.Equal(5.0, c2.Y, precision: 10);
    }

    private static double CubicBezier(double p0, double p1, double p2, double p3, double t)
    {
        var u = 1.0 - t;
        return (u * u * u * p0)
             + (3.0 * u * u * t * p1)
             + (3.0 * u * t * t * p2)
             + (t * t * t * p3);
    }
}
