using CCInfoWindows.Helpers;
using CCInfoWindows.Models;
using System.Text.Json;

namespace CCInfoWindows.Tests.Helpers;

/// <summary>
/// Unit tests for CostCalculator covering primary costUSD path, token fallback,
/// tiered pricing, and estimated-cost flagging.
/// </summary>
public class CostCalculatorTests
{
    private static JsonlEntry BuildEntry(
        decimal? costUsd = null,
        long inputTokens = 0,
        long outputTokens = 0,
        long cacheCreation = 0,
        long cacheRead = 0,
        string? model = "claude-sonnet-4-6")
    {
        return new JsonlEntry
        {
            Uuid = Guid.NewGuid().ToString(),
            RequestId = Guid.NewGuid().ToString(),
            Type = "assistant",
            CostUsd = costUsd,
            Message = new JsonlMessage
            {
                Model = model,
                Usage = new JsonlUsage
                {
                    InputTokens = inputTokens,
                    OutputTokens = outputTokens,
                    CacheCreationInputTokens = cacheCreation,
                    CacheReadInputTokens = cacheRead
                }
            }
        };
    }

    private static ModelPricing StandardPricing() => new()
    {
        InputCostPerToken = 0.000003,
        OutputCostPerToken = 0.000015,
        CacheCreationCost = 0.00000375,
        CacheReadCost = 0.0000003
    };

    private static ModelPricing TieredPricing() => new()
    {
        InputCostPerToken = 0.000003,
        OutputCostPerToken = 0.000015,
        CacheCreationCost = 0.00000375,
        CacheReadCost = 0.0000003,
        InputCostAbove200k = 0.000006,
        OutputCostAbove200k = 0.00003
    };

    [Fact]
    public void ComputeCost_EntryWithCostUsd_ReturnsCostUsdDirectly()
    {
        var entry = BuildEntry(costUsd: 5.0m, inputTokens: 100, outputTokens: 50);

        var (cost, isEstimated) = CostCalculator.ComputeCost(entry, StandardPricing());

        Assert.Equal(5.0m, cost);
        Assert.False(isEstimated);
    }

    [Fact]
    public void ComputeCost_EntryWithNullCostUsd_KnownModel_ReturnsComputedCost()
    {
        var entry = BuildEntry(costUsd: null, inputTokens: 1000, outputTokens: 500);
        var pricing = StandardPricing();

        var (cost, isEstimated) = CostCalculator.ComputeCost(entry, pricing);

        var expected = (decimal)(1000 * 0.000003 + 500 * 0.000015);
        Assert.Equal(expected, cost);
        Assert.False(isEstimated);
    }

    [Fact]
    public void ComputeCost_EntryWithNullCostUsd_UnknownModel_ReturnsZeroAndIsEstimated()
    {
        var entry = BuildEntry(costUsd: null, inputTokens: 1000, outputTokens: 500);

        var (cost, isEstimated) = CostCalculator.ComputeCost(entry, pricing: null);

        Assert.Equal(0m, cost);
        Assert.True(isEstimated);
    }

    [Fact]
    public void ComputeCost_EntryWithZeroCostUsd_KnownModel_FallsBackToTokenCalculation()
    {
        var entry = BuildEntry(costUsd: 0m, inputTokens: 1000, outputTokens: 500);
        var pricing = StandardPricing();

        var (cost, isEstimated) = CostCalculator.ComputeCost(entry, pricing);

        var expected = (decimal)(1000 * 0.000003 + 500 * 0.000015);
        Assert.Equal(expected, cost);
        Assert.False(isEstimated);
    }

    [Fact]
    public void ComputeCost_InputTokensAbove200k_UsesTieredPricing()
    {
        var entry = BuildEntry(costUsd: null, inputTokens: 300_000, outputTokens: 0);
        var pricing = TieredPricing();

        var (cost, isEstimated) = CostCalculator.ComputeCost(entry, pricing);

        // Above 200K: uses InputCostAbove200k = 0.000006
        var expected = (decimal)(300_000 * 0.000006);
        Assert.Equal(expected, cost);
        Assert.False(isEstimated);
    }

    [Fact]
    public void ComputeCost_InputTokensAtOrBelow200k_UsesStandardPricing()
    {
        var entry = BuildEntry(costUsd: null, inputTokens: 200_000, outputTokens: 0);
        var pricing = TieredPricing();

        var (cost, isEstimated) = CostCalculator.ComputeCost(entry, pricing);

        // At exactly 200K: uses standard InputCostPerToken = 0.000003
        var expected = (decimal)(200_000 * 0.000003);
        Assert.Equal(expected, cost);
        Assert.False(isEstimated);
    }

    [Fact]
    public void ComputeCost_CacheTokensIncludedInCost()
    {
        var entry = BuildEntry(
            costUsd: null,
            inputTokens: 0,
            outputTokens: 0,
            cacheCreation: 1000,
            cacheRead: 500);
        var pricing = StandardPricing();

        var (cost, isEstimated) = CostCalculator.ComputeCost(entry, pricing);

        var expected = (decimal)(1000 * 0.00000375 + 500 * 0.0000003);
        Assert.Equal(expected, cost);
        Assert.False(isEstimated);
    }

    [Fact]
    public void ComputeCost_NullUsage_ReturnsZeroNotEstimated()
    {
        var entry = new JsonlEntry
        {
            Uuid = "test",
            Type = "assistant",
            CostUsd = null,
            Message = new JsonlMessage { Model = "claude-sonnet-4-6", Usage = null }
        };

        var (cost, isEstimated) = CostCalculator.ComputeCost(entry, StandardPricing());

        Assert.Equal(0m, cost);
        Assert.False(isEstimated);
    }
}
