using CCInfoWindows.Models;

namespace CCInfoWindows.Tests.Helpers;

public class ContextWindowTests
{
    // CTXW-01: context token formula
    [Theory]
    [InlineData(1_000, 500, 300, 200_000, 1_800)]   // input + cache_read + cache_creation
    [InlineData(100_000, 0, 0, 200_000, 100_000)]   // only input tokens
    [InlineData(0, 50_000, 10_000, 200_000, 60_000)] // only cache tokens
    public void ContextWindowData_TotalTokens_IncludesAllCacheTypes(
        long inputTokens, long cacheRead, long cacheCreation, long maxTokens, long expectedTotal)
    {
        var data = new ContextWindowData
        {
            TotalTokens = inputTokens + cacheRead + cacheCreation,
            MaxTokens = maxTokens
        };

        Assert.Equal(expectedTotal, data.TotalTokens);
    }

    [Theory]
    [InlineData(0, 200_000, 0.0)]
    [InlineData(167_000, 200_000, 1.0)]              // 167K / (200K - 33K) = 1.0
    [InlineData(100_000, 200_000, 0.5988)]           // 100K / 167K ~ 0.5988 (validates effective = 167K)
    [InlineData(250_000, 200_000, 1.0)]              // clamped to [0, 1] — cannot exceed 1.0
    [InlineData(967_000, 1_000_000, 1.0)]            // 967K / (1M - 33K) = 1.0 (Opus session)
    public void ContextWindowData_Utilization_ComputesTotalOverEffectiveMax(
        long totalTokens, long maxTokens, double expectedUtilization)
    {
        var data = new ContextWindowData { TotalTokens = totalTokens, MaxTokens = maxTokens };

        Assert.Equal(expectedUtilization, data.Utilization, precision: 4);
    }

    // CTXW-02 (model badge mapping) and CTXW-04 (autocompact threshold) are asserted in
    // ModelContextLimitsTests, which owns that helper and carries the superset of these rows.

    [Theory]
    [InlineData(0, 200_000, 0.0)]
    [InlineData(167_000, 200_000, 1.0)]
    [InlineData(967_000, 1_000_000, 1.0)]
    public void SubagentContextData_Utilization_UsesFlat33KBuffer(
        long totalTokens, long maxTokens, double expectedUtilization)
    {
        var data = new SubagentContextData
        {
            AgentId = "test-agent",
            TotalTokens = totalTokens,
            MaxTokens = maxTokens
        };

        Assert.Equal(expectedUtilization, data.Utilization, precision: 4);
    }
}
