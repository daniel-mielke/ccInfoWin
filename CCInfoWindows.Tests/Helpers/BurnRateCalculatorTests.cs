using CCInfoWindows.Helpers;
using CCInfoWindows.Models;

namespace CCInfoWindows.Tests.Helpers;

public class BurnRateCalculatorTests
{
    private static UsageHistoryPoint MakePoint(DateTimeOffset ts, double utilNormalized)
        => new() { Timestamp = ts, Utilization = utilNormalized };

    [Fact]
    public void Predict_NullResetsAt_ReturnsNull()
    {
        var history = new List<UsageHistoryPoint>
        {
            MakePoint(DateTimeOffset.UtcNow.AddMinutes(-10), 0.30),
            MakePoint(DateTimeOffset.UtcNow.AddMinutes(-5), 0.40),
            MakePoint(DateTimeOffset.UtcNow.AddMinutes(-2), 0.50),
        };

        var result = BurnRateCalculator.Predict(history, 50.0, null);

        Assert.Null(result);
    }

    [Fact]
    public void Predict_PastResetsAt_ReturnsNull()
    {
        var pastResetsAt = DateTimeOffset.UtcNow.AddHours(-1);
        var history = new List<UsageHistoryPoint>
        {
            MakePoint(DateTimeOffset.UtcNow.AddMinutes(-10), 0.30),
            MakePoint(DateTimeOffset.UtcNow.AddMinutes(-5), 0.40),
            MakePoint(DateTimeOffset.UtcNow.AddMinutes(-2), 0.50),
        };

        var result = BurnRateCalculator.Predict(history, 50.0, pastResetsAt);

        Assert.Null(result);
    }

    [Fact]
    public void Predict_LowUtilization_ReturnsNull()
    {
        var resetsAt = DateTimeOffset.UtcNow.AddHours(3);
        var history = new List<UsageHistoryPoint>
        {
            MakePoint(DateTimeOffset.UtcNow.AddMinutes(-10), 0.05),
            MakePoint(DateTimeOffset.UtcNow.AddMinutes(-5), 0.10),
            MakePoint(DateTimeOffset.UtcNow.AddMinutes(-2), 0.13),
        };

        var result = BurnRateCalculator.Predict(history, 15.0, resetsAt);

        Assert.Null(result);
    }

    [Fact]
    public void Predict_TooFewPoints_ReturnsNull()
    {
        var resetsAt = DateTimeOffset.UtcNow.AddHours(3);
        var history = new List<UsageHistoryPoint>
        {
            MakePoint(DateTimeOffset.UtcNow.AddMinutes(-10), 0.30),
            MakePoint(DateTimeOffset.UtcNow.AddMinutes(-5), 0.40),
        };

        var result = BurnRateCalculator.Predict(history, 40.0, resetsAt);

        Assert.Null(result);
    }

    [Fact]
    public void Predict_FlatUsage_ReturnsNull()
    {
        var resetsAt = DateTimeOffset.UtcNow.AddHours(3);
        var history = new List<UsageHistoryPoint>
        {
            MakePoint(DateTimeOffset.UtcNow.AddMinutes(-10), 0.50),
            MakePoint(DateTimeOffset.UtcNow.AddMinutes(-7), 0.50),
            MakePoint(DateTimeOffset.UtcNow.AddMinutes(-4), 0.50),
            MakePoint(DateTimeOffset.UtcNow.AddMinutes(-1), 0.50),
        };

        var result = BurnRateCalculator.Predict(history, 50.0, resetsAt);

        Assert.Null(result);
    }

    [Fact]
    public void Predict_DecreasingUsage_ReturnsNull()
    {
        var resetsAt = DateTimeOffset.UtcNow.AddHours(3);
        var history = new List<UsageHistoryPoint>
        {
            MakePoint(DateTimeOffset.UtcNow.AddMinutes(-10), 0.80),
            MakePoint(DateTimeOffset.UtcNow.AddMinutes(-7), 0.75),
            MakePoint(DateTimeOffset.UtcNow.AddMinutes(-4), 0.68),
            MakePoint(DateTimeOffset.UtcNow.AddMinutes(-1), 0.60),
        };

        var result = BurnRateCalculator.Predict(history, 60.0, resetsAt);

        Assert.Null(result);
    }

    [Fact]
    public void Predict_ExhaustsAfterReset_ReturnsNull()
    {
        // Very slow burn: will exhaust after the 10-minute reset
        var resetsAt = DateTimeOffset.UtcNow.AddMinutes(10);
        var history = new List<UsageHistoryPoint>
        {
            MakePoint(DateTimeOffset.UtcNow.AddMinutes(-14), 0.50),
            MakePoint(DateTimeOffset.UtcNow.AddMinutes(-10), 0.501),
            MakePoint(DateTimeOffset.UtcNow.AddMinutes(-5), 0.502),
            MakePoint(DateTimeOffset.UtcNow.AddMinutes(-1), 0.503),
        };

        var result = BurnRateCalculator.Predict(history, 50.3, resetsAt);

        Assert.Null(result);
    }

    [Fact]
    public void Predict_FastBurn_ReturnsPrediction()
    {
        // 4 points going from 20% to 60% in 10 minutes = ~4%/min slope
        // At that rate, from 60%, need 40 more percent = ~10 min
        var resetsAt = DateTimeOffset.UtcNow.AddHours(3);
        var now = DateTimeOffset.UtcNow;
        var history = new List<UsageHistoryPoint>
        {
            MakePoint(now.AddMinutes(-10), 0.20),
            MakePoint(now.AddMinutes(-7), 0.33),
            MakePoint(now.AddMinutes(-4), 0.47),
            MakePoint(now.AddMinutes(-1), 0.60),
        };

        var result = BurnRateCalculator.Predict(history, 60.0, resetsAt);

        Assert.NotNull(result);
        Assert.InRange(result.MinutesUntilLimit, 5, 20);
        Assert.True(result.HitsLimitAt > now);
    }

    [Fact]
    public void Predict_MinutesUntilLimit_MinimumOne()
    {
        // Points that make projected exhaustion happen in < 60 seconds
        var resetsAt = DateTimeOffset.UtcNow.AddHours(3);
        var now = DateTimeOffset.UtcNow;
        // Very steep slope: 0% to 99% in 15 minutes → almost at limit, very few seconds remain
        var history = new List<UsageHistoryPoint>
        {
            MakePoint(now.AddMinutes(-14), 0.01),
            MakePoint(now.AddMinutes(-9), 0.34),
            MakePoint(now.AddMinutes(-4), 0.67),
            MakePoint(now.AddMinutes(-1), 0.99),
        };

        var result = BurnRateCalculator.Predict(history, 99.0, resetsAt);

        Assert.NotNull(result);
        Assert.Equal(1, result.MinutesUntilLimit);
    }

    [Fact]
    public void Predict_FullUtilization_ReturnsNull()
    {
        var resetsAt = DateTimeOffset.UtcNow.AddHours(3);
        var now = DateTimeOffset.UtcNow;
        var history = new List<UsageHistoryPoint>
        {
            MakePoint(now.AddMinutes(-10), 0.70),
            MakePoint(now.AddMinutes(-7), 0.80),
            MakePoint(now.AddMinutes(-4), 0.90),
            MakePoint(now.AddMinutes(-1), 1.00),
        };

        var result = BurnRateCalculator.Predict(history, 100.0, resetsAt);

        Assert.Null(result);
    }
}
