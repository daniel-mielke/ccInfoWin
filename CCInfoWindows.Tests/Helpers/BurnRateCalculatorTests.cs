using CCInfoWindows.Helpers;
using CCInfoWindows.Models;

namespace CCInfoWindows.Tests.Helpers;

public class BurnRateCalculatorTests
{
    private static UsageHistoryPoint MakePoint(DateTimeOffset ts, double utilNormalized)
        => new() { Timestamp = ts, Utilization = utilNormalized };

    /// <summary>
    /// Builds a history from minute offsets against a single captured instant. Reading UtcNow once per
    /// element — what these cases used to do — puts the samples a few microseconds off the intended
    /// spacing, which is harmless at three-minute gaps and flaky at tight ones.
    /// </summary>
    private static List<UsageHistoryPoint> History(
        DateTimeOffset now, params (int MinutesAgo, double Utilization)[] samples) =>
        [.. samples.Select(s => MakePoint(now.AddMinutes(-s.MinutesAgo), s.Utilization))];

    /// <summary>
    /// Every input shape Predict has to reject. The first column names the reason so a failure says
    /// which one broke; the reset offset is minutes from the same captured "now", null for no reset.
    /// </summary>
    public static TheoryData<string, int?, double, (int MinutesAgo, double Utilization)[]> RejectedInputs() => new()
    {
        { "no resetsAt", null, 50.0, [(10, 0.30), (5, 0.40), (2, 0.50)] },
        { "resetsAt already past", -60, 50.0, [(10, 0.30), (5, 0.40), (2, 0.50)] },
        { "utilization below the floor", 180, 15.0, [(10, 0.05), (5, 0.10), (2, 0.13)] },
        { "fewer samples than the fit needs", 180, 40.0, [(10, 0.30), (5, 0.40)] },
        { "flat usage", 180, 50.0, [(10, 0.50), (7, 0.50), (4, 0.50), (1, 0.50)] },
        { "decreasing usage", 180, 60.0, [(10, 0.80), (7, 0.75), (4, 0.68), (1, 0.60)] },
        // Very slow burn: exhaustion lands after the 10-minute reset, so there is nothing to warn about.
        { "exhaustion falls past the reset", 10, 50.3, [(14, 0.50), (10, 0.501), (5, 0.502), (1, 0.503)] },
        { "already at full utilization", 180, 100.0, [(10, 0.70), (7, 0.80), (4, 0.90), (1, 1.00)] },
    };

    [Theory]
    [MemberData(nameof(RejectedInputs))]
    public void Predict_RejectsUnusableHistory(
        string reason, int? resetsInMinutes, double currentUtilization,
        (int MinutesAgo, double Utilization)[] samples)
    {
        var now = DateTimeOffset.UtcNow;
        var resetsAt = resetsInMinutes is null
            ? (DateTimeOffset?)null
            : now.AddMinutes(resetsInMinutes.Value);

        var result = BurnRateCalculator.Predict(History(now, samples), currentUtilization, resetsAt);

        Assert.True(result is null, $"Predict should return null for: {reason}");
    }

    [Fact]
    public void Predict_FastBurn_ReturnsPrediction()
    {
        // 4 points going from 20% to 60% in 10 minutes = ~4%/min slope
        // At that rate, from 60%, need 40 more percent = ~10 min
        var now = DateTimeOffset.UtcNow;
        var history = History(now, (10, 0.20), (7, 0.33), (4, 0.47), (1, 0.60));

        var result = BurnRateCalculator.Predict(history, 60.0, now.AddHours(3));

        Assert.NotNull(result);
        Assert.InRange(result.MinutesUntilLimit, 5, 20);
        Assert.True(result.HitsLimitAt > now);
    }

    [Fact]
    public void Predict_MinutesUntilLimit_MinimumOne()
    {
        // Points that make projected exhaustion happen in < 60 seconds.
        // Very steep slope: 0% to 99% in 15 minutes → almost at limit, very few seconds remain
        var now = DateTimeOffset.UtcNow;
        var history = History(now, (14, 0.01), (9, 0.34), (4, 0.67), (1, 0.99));

        var result = BurnRateCalculator.Predict(history, 99.0, now.AddHours(3));

        Assert.NotNull(result);
        Assert.Equal(1, result.MinutesUntilLimit);
    }

    [Fact]
    public void TryClaimFirstReport_IsTrueOnceAndFalseAfterwards()
    {
        // Finding 34 replaced two Debug.WriteLine calls (erased from Release) with AppLog entries.
        // Predict re-filters its whole lookback on every poll, so an unbounded entry per rejection
        // would flood a 1 MiB app.log and evict the failures worth keeping. This latch is what keeps
        // it to one entry per condition per process.
        var latch = 0;

        Assert.True(BurnRateCalculator.TryClaimFirstReport(ref latch));
        Assert.False(BurnRateCalculator.TryClaimFirstReport(ref latch));
        Assert.False(BurnRateCalculator.TryClaimFirstReport(ref latch));
    }

    [Fact]
    public void TryClaimFirstReport_ClaimsExactlyOnce_UnderConcurrentCallers()
    {
        // The filter runs on a poll callback, so two overlapping polls can reach the same latch.
        var latch = 0;
        var claims = 0;

        Parallel.For(0, 64, _ =>
        {
            if (BurnRateCalculator.TryClaimFirstReport(ref latch))
            {
                Interlocked.Increment(ref claims);
            }
        });

        Assert.Equal(1, claims);
    }

    [Fact]
    public void TryClaimFirstReport_TracksEachConditionSeparately()
    {
        // Two independent latches, so a rejected timestamp does not consume the steepness bound's
        // one and only entry.
        var first = 0;
        var second = 0;

        Assert.True(BurnRateCalculator.TryClaimFirstReport(ref first));
        Assert.True(BurnRateCalculator.TryClaimFirstReport(ref second));
    }
}
