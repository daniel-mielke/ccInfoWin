using CCInfoWindows.Models;

namespace CCInfoWindows.Helpers;

/// <summary>
/// Computes USD cost for a single JSONL entry using the costUSD field when present,
/// falling back to token-count times model pricing.
/// </summary>
public static class CostCalculator
{
    private const long TierBreakpointTokens = 200_000;

    /// <summary>
    /// Returns the USD cost for a JSONL entry and whether the cost is estimated.
    /// Primary: uses entry.CostUsd when it is present and greater than zero.
    /// Fallback: multiplies token counts by per-token prices from the provided pricing.
    /// When pricing is null, returns (0, true) to signal an estimated cost.
    /// </summary>
    public static (decimal cost, bool isEstimated) ComputeCost(JsonlEntry entry, ModelPricing? pricing)
    {
        if (entry.CostUsd is > 0m)
            return (entry.CostUsd.Value, false);

        if (pricing is null)
            return (0m, true);

        var usage = entry.Message?.Usage;
        if (usage is null)
            return (0m, false);

        var inputTokens = usage.InputTokens ?? 0;
        var outputTokens = usage.OutputTokens ?? 0;
        var cacheCreation = usage.CacheCreationInputTokens ?? 0;
        var cacheRead = usage.CacheReadInputTokens ?? 0;

        var inputPrice = SelectInputPrice(inputTokens, pricing);
        var outputPrice = SelectOutputPrice(outputTokens, pricing);
        var cacheCreationPrice = SelectCacheCreationPrice(cacheCreation, pricing);
        var cacheReadPrice = SelectCacheReadPrice(cacheRead, pricing);

        var cost = (inputTokens * inputPrice)
                 + (outputTokens * outputPrice)
                 + (cacheCreation * cacheCreationPrice)
                 + (cacheRead * cacheReadPrice);

        return ((decimal)cost, false);
    }

    private static double SelectInputPrice(long inputTokens, ModelPricing pricing) =>
        inputTokens > TierBreakpointTokens && pricing.InputCostAbove200k.HasValue
            ? pricing.InputCostAbove200k.Value
            : pricing.InputCostPerToken;

    private static double SelectOutputPrice(long outputTokens, ModelPricing pricing) =>
        outputTokens > TierBreakpointTokens && pricing.OutputCostAbove200k.HasValue
            ? pricing.OutputCostAbove200k.Value
            : pricing.OutputCostPerToken;

    private static double SelectCacheCreationPrice(long cacheCreation, ModelPricing pricing) =>
        cacheCreation > TierBreakpointTokens && pricing.CacheCreationCostAbove200k.HasValue
            ? pricing.CacheCreationCostAbove200k.Value
            : pricing.CacheCreationCost ?? 0.0;

    private static double SelectCacheReadPrice(long cacheRead, ModelPricing pricing) =>
        cacheRead > TierBreakpointTokens && pricing.CacheReadCostAbove200k.HasValue
            ? pricing.CacheReadCostAbove200k.Value
            : pricing.CacheReadCost ?? 0.0;
}
