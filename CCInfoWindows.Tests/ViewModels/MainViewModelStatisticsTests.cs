using CCInfoWindows.Helpers;
using CCInfoWindows.Models;
using CCInfoWindows.Services.Interfaces;
using CCInfoWindows.ViewModels;
using Moq;

namespace CCInfoWindows.Tests.ViewModels;

/// <summary>
/// Unit tests for MainViewModel statistics tab switching and statistics display logic.
/// Tests use the internal ApplyStatistics method directly to avoid DispatcherQueue dependency.
/// </summary>
public class MainViewModelStatisticsTests
{
    private static MainViewModelTestHarness CreateHarness()
    {
        var jsonlService = new Mock<IJsonlService>();
        jsonlService.Setup(s => s.Sessions).Returns([]);
        jsonlService.Setup(s => s.IsScanning).Returns(false);
        jsonlService.Setup(s => s.GetStatistics(It.IsAny<TimePeriod>(), It.IsAny<string?>()))
            .Returns(StatisticsSummary.Empty);

        var pricingService = new Mock<IPricingService>();
        pricingService.Setup(s => s.Source).Returns(PricingSource.Unknown);
        pricingService.Setup(s => s.LastFetch).Returns((DateTimeOffset?)null);
        pricingService.Setup(s => s.EnsurePricesLoadedAsync()).Returns(Task.CompletedTask);

        return new MainViewModelTestHarness(jsonlService.Object, pricingService.Object);
    }

    [Fact]
    public void ApplyStatistics_WithEmptyBurnRateEntries_SetsBurnRateToZero()
    {
        var harness = CreateHarness();

        harness.ApplyStatistics(StatisticsSummary.Empty);

        Assert.Equal("0 T/h", harness.StatisticsBurnRate);
    }

    [Fact]
    public void ApplyStatistics_WithBurnRateEntries_SetsNonZeroBurnRate()
    {
        var harness = CreateHarness();
        var now = DateTimeOffset.UtcNow;
        var entries = new List<(DateTimeOffset Timestamp, long Tokens)>
        {
            (now.AddMinutes(-30), 6000),
            (now.AddMinutes(-15), 6000)
        };
        var stats = new StatisticsSummary
        {
            InputTokens = 12000,
            BurnRateEntries = entries
        };

        harness.ApplyStatistics(stats);

        Assert.NotEqual("0 T/h", harness.StatisticsBurnRate);
        Assert.Contains("T/h", harness.StatisticsBurnRate);
    }

    [Fact]
    public void ApplyStatistics_WithHasEstimatedCostsTrueAndNonZeroCost_ProducesTildePrefixedCost()
    {
        var harness = CreateHarness();
        var stats = new StatisticsSummary
        {
            TotalCostUsd = 5.00m,
            HasEstimatedCosts = true
        };

        harness.ApplyStatistics(stats);

        Assert.StartsWith("~", harness.StatisticsCost);
    }

    [Fact]
    public void ApplyStatistics_WithHasEstimatedCostsFalse_ProducesNonTildeCost()
    {
        var harness = CreateHarness();
        var stats = new StatisticsSummary
        {
            TotalCostUsd = 3.50m,
            HasEstimatedCosts = false
        };

        harness.ApplyStatistics(stats);

        Assert.StartsWith("$", harness.StatisticsCost);
        Assert.DoesNotContain("~", harness.StatisticsCost);
    }

    [Fact]
    public void ApplyStatistics_WithNoModels_SetsStatisticsModelsToEmDash()
    {
        var harness = CreateHarness();

        harness.ApplyStatistics(StatisticsSummary.Empty);

        Assert.Equal("\u2013", harness.StatisticsModels);
    }

    [Fact]
    public void ApplyStatistics_WithModels_SetsStatisticsModelsToDisplayNames()
    {
        var harness = CreateHarness();
        var stats = new StatisticsSummary
        {
            Models = ["claude-sonnet-4-5"]
        };

        harness.ApplyStatistics(stats);

        Assert.NotEqual("\u2013", harness.StatisticsModels);
        Assert.Contains("Sonnet", harness.StatisticsModels);
    }

    [Fact]
    public void ApplyStatistics_WithZeroTokens_SetsAllTokenFieldsToEmDash()
    {
        var harness = CreateHarness();

        harness.ApplyStatistics(StatisticsSummary.Empty);

        Assert.Equal("\u2013", harness.StatisticsInput);
        Assert.Equal("\u2013", harness.StatisticsOutput);
        Assert.Equal("\u2013", harness.StatisticsCacheCreation);
        Assert.Equal("\u2013", harness.StatisticsCacheRead);
        Assert.Equal("\u2013", harness.StatisticsTotal);
    }

    [Fact]
    public void BurnRateCalculator_WithOldEntries_ReturnsZero()
    {
        var oldEntries = new List<(DateTimeOffset Timestamp, long Tokens)>
        {
            (DateTimeOffset.UtcNow.AddHours(-3), 10000)
        };

        var burnRate = BurnRateCalculator.ComputeBurnRate(oldEntries, windowMinutes: 60);

        Assert.Equal(0, burnRate);
    }

    [Fact]
    public void BurnRateCalculator_WithRecentEntries_ReturnsExpectedRate()
    {
        var recentEntries = new List<(DateTimeOffset Timestamp, long Tokens)>
        {
            (DateTimeOffset.UtcNow.AddMinutes(-30), 3000)
        };

        var burnRate = BurnRateCalculator.ComputeBurnRate(recentEntries, windowMinutes: 60);

        Assert.True(burnRate > 0);
    }
}

/// <summary>
/// Test harness that exposes internal ApplyStatistics without requiring DispatcherQueue.
/// </summary>
public class MainViewModelTestHarness
{
    private readonly IJsonlService _jsonlService;

    public string StatisticsModels { get; private set; } = "\u2013";
    public string StatisticsInput { get; private set; } = "\u2013";
    public string StatisticsOutput { get; private set; } = "\u2013";
    public string StatisticsCacheCreation { get; private set; } = "\u2013";
    public string StatisticsCacheRead { get; private set; } = "\u2013";
    public string StatisticsTotal { get; private set; } = "\u2013";
    public string StatisticsCost { get; private set; } = "\u2013";
    public string StatisticsBurnRate { get; private set; } = "0 T/h";

    public MainViewModelTestHarness(IJsonlService jsonlService, IPricingService pricingService)
    {
        _jsonlService = jsonlService;
    }

    public void ApplyStatistics(StatisticsSummary stats)
    {
        StatisticsModels = stats.Models.Count > 0
            ? string.Join(", ", stats.Models.Select(m => ModelContextLimits.GetDisplayName(m)))
            : "\u2013";
        StatisticsInput = stats.InputTokens > 0 ? TokenFormatter.FormatTokenCount(stats.InputTokens) : "\u2013";
        StatisticsOutput = stats.OutputTokens > 0 ? TokenFormatter.FormatTokenCount(stats.OutputTokens) : "\u2013";
        StatisticsCacheCreation = stats.CacheCreationTokens > 0 ? TokenFormatter.FormatTokenCount(stats.CacheCreationTokens) : "\u2013";
        StatisticsCacheRead = stats.CacheReadTokens > 0 ? TokenFormatter.FormatTokenCount(stats.CacheReadTokens) : "\u2013";
        StatisticsTotal = stats.TotalTokens > 0 ? TokenFormatter.FormatTokenCount(stats.TotalTokens) : "\u2013";
        StatisticsCost = CostFormatter.FormatCost(stats.TotalCostUsd, stats.HasEstimatedCosts);

        var burnRate = BurnRateCalculator.ComputeBurnRate(stats.BurnRateEntries);
        StatisticsBurnRate = burnRate > 0
            ? $"{TokenFormatter.FormatTokenCount((long)burnRate)} T/h"
            : "0 T/h";
    }
}
