using CCInfoWindows.Helpers;

namespace CCInfoWindows.Tests.Helpers;

public class ModelContextLimitsTests
{
    [Theory]
    [InlineData("claude-opus-4-6", 200_000)]
    [InlineData("claude-sonnet-4-6", 200_000)]
    [InlineData("claude-haiku-4-5", 200_000)]
    [InlineData("claude-haiku-4-5-20251001", 200_000)]
    [InlineData("claude-sonnet-4-5-20250929", 200_000)]
    public void GetMaxContextTokens_KnownModel_ReturnsCorrectLimit(string modelName, long expected)
    {
        var result = ModelContextLimits.GetMaxContextTokens(modelName);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("unknown-model")]
    [InlineData("gpt-4")]
    [InlineData("some-random-model")]
    public void GetMaxContextTokens_UnknownModel_ReturnsDefault(string modelName)
    {
        var result = ModelContextLimits.GetMaxContextTokens(modelName);

        Assert.Equal(ModelContextLimits.DefaultContextLimit, result);
    }

    [Fact]
    public void GetMaxContextTokens_NullModel_ReturnsDefault()
    {
        var result = ModelContextLimits.GetMaxContextTokens(null);

        Assert.Equal(ModelContextLimits.DefaultContextLimit, result);
    }

    [Theory]
    [InlineData("claude-opus-4-6", "Opus 4.6")]
    [InlineData("claude-sonnet-4-6", "Sonnet 4.6")]
    [InlineData("claude-haiku-4-5", "Haiku 4.5")]
    [InlineData("claude-haiku-4-5-20251001", "Haiku 4.5")]
    [InlineData("claude-sonnet-4-5-20250929", "Sonnet 4.5")]
    [InlineData("claude-opus-4-1", "Opus 4.1")]
    public void GetDisplayName_KnownModel_ReturnsFormattedName(string modelName, string expected)
    {
        var result = ModelContextLimits.GetDisplayName(modelName);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void GetDisplayName_NullModel_ReturnsUnbekannt()
    {
        var result = ModelContextLimits.GetDisplayName(null);

        Assert.Equal("Unbekannt", result);
    }

    [Fact]
    public void GetDisplayName_EmptyModel_ReturnsUnbekannt()
    {
        var result = ModelContextLimits.GetDisplayName("");

        Assert.Equal("Unbekannt", result);
    }

    [Theory]
    [InlineData(190_000, 200_000, true)]   // 95% >= 90% threshold
    [InlineData(180_001, 200_000, true)]   // 90.0005% >= 90%
    [InlineData(180_000, 200_000, true)]   // exactly 90%
    [InlineData(179_999, 200_000, false)]  // just below 90%
    public void ShouldWarnAutocompact_LargeModel_UsesNinetyPercentThreshold(
        long totalTokens, long maxTokens, bool expected)
    {
        var result = ModelContextLimits.ShouldWarnAutocompact(totalTokens, maxTokens);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void ShouldWarnAutocompact_ZeroMaxTokens_ReturnsFalse()
    {
        var result = ModelContextLimits.ShouldWarnAutocompact(100, 0);

        Assert.False(result);
    }
}
