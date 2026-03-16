using CCInfoWindows.Helpers;

namespace CCInfoWindows.Tests.Helpers;

/// <summary>
/// Unit tests for BurnRateCalculator covering empty input, rolling window filtering,
/// and edge case guards.
/// </summary>
public class BurnRateCalculatorTests
{
    [Fact]
    public void ComputeBurnRate_EmptyEntries_ReturnsZero()
    {
        var entries = Enumerable.Empty<(DateTimeOffset, long)>();

        var result = BurnRateCalculator.ComputeBurnRate(entries);

        Assert.Equal(0, result);
    }

    [Fact]
    public void ComputeBurnRate_EntriesInWindow_ReturnsCorrectTokensPerHour()
    {
        // 3600 tokens in 60-minute window => 3600 tokens/hour
        var now = DateTimeOffset.UtcNow;
        var entries = new[]
        {
            (now.AddMinutes(-30), 1200L),
            (now.AddMinutes(-10), 2400L)
        };

        var result = BurnRateCalculator.ComputeBurnRate(entries, windowMinutes: 60);

        Assert.Equal(3600.0, result, precision: 0);
    }

    [Fact]
    public void ComputeBurnRate_EntriesOutsideWindow_AreExcluded()
    {
        var now = DateTimeOffset.UtcNow;
        var entries = new[]
        {
            (now.AddMinutes(-90), 9999L), // outside 60-min window
            (now.AddMinutes(-20), 600L)   // inside window
        };

        var result = BurnRateCalculator.ComputeBurnRate(entries, windowMinutes: 60);

        // Only the 600-token entry in window, 60-min window => 600/60*60 = 600 tokens/hour
        Assert.Equal(600.0, result, precision: 0);
    }

    [Fact]
    public void ComputeBurnRate_WindowMinutesZero_ReturnsZero()
    {
        var entries = new[]
        {
            (DateTimeOffset.UtcNow.AddMinutes(-5), 1000L)
        };

        var result = BurnRateCalculator.ComputeBurnRate(entries, windowMinutes: 0);

        Assert.Equal(0, result);
    }
}
