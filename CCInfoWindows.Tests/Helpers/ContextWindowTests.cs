using CCInfoWindows.Helpers;
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
    [InlineData(200_000, 200_000, 1.0)]
    public void ContextWindowData_Utilization_ComputesTotalOverEffectiveMax(
        long totalTokens, long maxTokens, double expectedUtilization)
    {
        var data = new ContextWindowData { TotalTokens = totalTokens, MaxTokens = maxTokens };

        Assert.Equal(expectedUtilization, data.Utilization, precision: 5);
    }

    [Fact]
    public void ContextWindowData_Utilization_ClampsAtOne()
    {
        // Utilization is clamped to [0, 1] — cannot exceed 1.0
        var data = new ContextWindowData { TotalTokens = 250_000, MaxTokens = 200_000 };

        Assert.Equal(1.0, data.Utilization);
    }

    [Fact]
    public void ContextWindowData_Utilization_ZeroTokens_ReturnsZero()
    {
        var data = new ContextWindowData { TotalTokens = 0, MaxTokens = 200_000 };

        Assert.Equal(0.0, data.Utilization);
    }

    // CTXW-02: model badge mapping
    [Theory]
    [InlineData("claude-opus-4-6", "Opus 4.6")]
    [InlineData("claude-sonnet-4-6", "Sonnet 4.6")]
    [InlineData("claude-haiku-4-5", "Haiku 4.5")]
    [InlineData("claude-haiku-4-5-20251001", "Haiku 4.5")]
    [InlineData(null, "Unbekannt")]
    public void ModelContextLimits_GetDisplayName_ReturnsFriendlyBadgeNames(
        string? modelName, string expected)
    {
        var result = ModelContextLimits.GetDisplayName(modelName);

        Assert.Equal(expected, result);
    }

    // CTXW-04: autocompact threshold
    [Theory]
    [InlineData(180_000, 200_000, true)]   // exactly 90% -> warn
    [InlineData(190_000, 200_000, true)]   // above 90% -> warn
    [InlineData(179_999, 200_000, false)]  // just below 90% -> no warn
    [InlineData(50_000, 200_000, false)]   // well below -> no warn
    public void ModelContextLimits_ShouldWarnAutocompact_LargeModel_NinetyPercentThreshold(
        long totalTokens, long maxTokens, bool expected)
    {
        var result = ModelContextLimits.ShouldWarnAutocompact(totalTokens, maxTokens);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(95_000, 100_000, true)]   // exactly 95% for small model -> warn
    [InlineData(94_999, 100_000, false)]  // just below 95% -> no warn
    public void ModelContextLimits_ShouldWarnAutocompact_SmallModel_NinetyFivePercentThreshold(
        long totalTokens, long maxTokens, bool expected)
    {
        var result = ModelContextLimits.ShouldWarnAutocompact(totalTokens, maxTokens);

        Assert.Equal(expected, result);
    }
}
