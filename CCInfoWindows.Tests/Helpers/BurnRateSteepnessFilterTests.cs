using CCInfoWindows.Helpers;
using CCInfoWindows.Models;

namespace CCInfoWindows.Tests.Helpers;

/// <summary>
/// v1.15.0 steepness filter. Complements the existing near-flat guard: that one stops
/// slope -> 0 (ETA -> infinity), this one stops slope -> infinity (false "exhausted in 2
/// minutes" alarm from a single bogus sample).
/// </summary>
public class BurnRateSteepnessFilterTests
{
    private static readonly DateTimeOffset Start = new(2026, 8, 5, 20, 0, 0, TimeSpan.Zero);

    private static UsageHistoryPoint Point(int seconds, double percent) =>
        new() { Timestamp = Start.AddSeconds(seconds), Utilization = percent / 100.0 };

    [Fact]
    public void FilterImplausibleJumps_EmptyInput_ReturnsEmpty()
        => Assert.Empty(BurnRateCalculator.FilterImplausibleJumps([]));

    [Fact]
    public void FilterImplausibleJumps_SinglePoint_IsAlwaysKept()
        => Assert.Single(BurnRateCalculator.FilterImplausibleJumps([Point(0, 50)]));

    [Fact]
    public void FilterImplausibleJumps_NormalUsage_KeepsEverything()
    {
        // Typical 30s polling: a couple of percentage points per poll.
        var points = new[] { Point(0, 40), Point(30, 42), Point(60, 45), Point(90, 47) };

        Assert.Equal(4, BurnRateCalculator.FilterImplausibleJumps(points).Count);
    }

    [Fact]
    public void FilterImplausibleJumps_ImplausibleUpwardJump_IsRejected()
    {
        // +50 points in one 30s poll == 1.67 points/s, way over the bound.
        var points = new[] { Point(0, 40), Point(30, 90), Point(60, 44) };

        var result = BurnRateCalculator.FilterImplausibleJumps(points);

        Assert.Equal(2, result.Count);
        Assert.Equal(0.40, result[0].Utilization, precision: 6);
        Assert.Equal(0.44, result[1].Utilization, precision: 6);
    }

    [Fact]
    public void FilterImplausibleJumps_ImplausibleDownwardJump_IsAlsoRejected()
    {
        var points = new[] { Point(0, 80), Point(30, 5), Point(60, 82) };

        var result = BurnRateCalculator.FilterImplausibleJumps(points);

        Assert.Equal(2, result.Count);
        Assert.Equal(0.82, result[^1].Utilization, precision: 6);
    }

    [Fact]
    public void FilterImplausibleJumps_ComparesAgainstTheLastAcceptedPoint_NotTheRejectedOne()
    {
        // If the outlier became the baseline, the following normal sample would look like an
        // equally large jump in the opposite direction and be dropped too.
        var points = new[] { Point(0, 40), Point(30, 95), Point(60, 43), Point(90, 45) };

        var result = BurnRateCalculator.FilterImplausibleJumps(points);

        Assert.Equal(3, result.Count);
        Assert.Equal([0.40, 0.43, 0.45], result.Select(p => Math.Round(p.Utilization, 6)));
    }

    [Fact]
    public void FilterImplausibleJumps_NonAdvancingTimestamp_IsRejected()
    {
        var points = new[] { Point(0, 40), Point(0, 60), Point(30, 42) };

        var result = BurnRateCalculator.FilterImplausibleJumps(points);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void FilterImplausibleJumps_ExactlyAtTheBound_IsKept()
    {
        // 15 points over 30s == exactly MaxPlausiblePointsPerSecond; the check is strictly greater.
        var points = new[] { Point(0, 40), Point(30, 55) };

        Assert.Equal(2, BurnRateCalculator.FilterImplausibleJumps(points).Count);
    }

    [Fact]
    public void Predict_SingleBogusSpike_HasNoInfluenceOnTheEta()
    {
        // End to end and the assertion that actually matters: the outlier must not change the
        // answer at all. Comparing against the same history minus the spike is stronger than
        // asserting a threshold, because it pins "ignored" rather than "somewhat dampened".
        var now = DateTimeOffset.UtcNow;
        var clean = new List<UsageHistoryPoint>
        {
            new() { Timestamp = now.AddMinutes(-4), Utilization = 0.40 },
            new() { Timestamp = now.AddMinutes(-3), Utilization = 0.405 },
            new() { Timestamp = now.AddMinutes(-1), Utilization = 0.41 },
            new() { Timestamp = now, Utilization = 0.415 },
        };
        var withSpike = new List<UsageHistoryPoint>(clean);
        withSpike.Insert(2, new UsageHistoryPoint { Timestamp = now.AddMinutes(-2), Utilization = 0.99 });

        var expected = BurnRateCalculator.Predict(clean, currentUtilization: 41.5, resetsAt: now.AddHours(3));
        var actual = BurnRateCalculator.Predict(withSpike, currentUtilization: 41.5, resetsAt: now.AddHours(3));

        Assert.NotNull(expected);   // guard: the clean series must actually produce a prediction
        Assert.NotNull(actual);
        Assert.Equal(expected!.MinutesUntilLimit, actual!.MinutesUntilLimit);
    }

    [Fact]
    public void Predict_SpikeWouldOtherwiseFireAFalseImminentAlarm()
    {
        // Shows what the filter prevents: fed the spike as legitimate data, the regression slope
        // is steep enough to claim exhaustion within minutes.
        var now = DateTimeOffset.UtcNow;
        var spikeAsRealTrend = new List<UsageHistoryPoint>
        {
            new() { Timestamp = now.AddMinutes(-4), Utilization = 0.40 },
            new() { Timestamp = now.AddMinutes(-3), Utilization = 0.60 },
            new() { Timestamp = now.AddMinutes(-2), Utilization = 0.80 },
            new() { Timestamp = now.AddMinutes(-1), Utilization = 0.95 },
        };

        // Each step is 20 points over 60s == 0.33 points/s, under the bound, so these survive
        // the filter and the alarm is genuine — the filter only removes single-poll jumps.
        var prediction = BurnRateCalculator.Predict(spikeAsRealTrend, currentUtilization: 95, resetsAt: now.AddHours(3));

        Assert.NotNull(prediction);
        Assert.True(prediction!.MinutesUntilLimit < 10);
    }
}
