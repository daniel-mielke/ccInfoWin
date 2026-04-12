using CCInfoWindows.Helpers;

namespace CCInfoWindows.Tests.Helpers;

public class ModelContextLimitsTests
{
    [Theory]
    [InlineData("claude-opus-4-6", 1_000_000)]
    [InlineData("claude-opus-4-1", 1_000_000)]
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

    [Fact]
    public void GetMaxContextTokens_SonnetWithExplicitMillionContext_ReturnsMillion()
    {
        var result = ModelContextLimits.GetMaxContextTokens("claude-sonnet-4-6", 1_000_000);

        Assert.Equal(1_000_000, result);
    }

    [Fact]
    public void GetMaxContextTokens_OpusIgnoresSonnetContextSize()
    {
        var result = ModelContextLimits.GetMaxContextTokens("claude-opus-4-6", 200_000);

        Assert.Equal(1_000_000, result);
    }

    [Theory]
    [InlineData("claude-opus-4-6", ModelContextLimits.ModelFamily.Opus)]
    [InlineData("claude-sonnet-4-6", ModelContextLimits.ModelFamily.Sonnet)]
    [InlineData("claude-haiku-4-5", ModelContextLimits.ModelFamily.Haiku)]
    [InlineData("unknown-model", ModelContextLimits.ModelFamily.Unknown)]
    [InlineData(null, ModelContextLimits.ModelFamily.Unknown)]
    [InlineData("", ModelContextLimits.ModelFamily.Unknown)]
    public void GetModelFamily_ReturnsCorrectFamily(string? modelName, ModelContextLimits.ModelFamily expected)
    {
        var result = ModelContextLimits.GetModelFamily(modelName);

        Assert.Equal(expected, result);
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
    [InlineData(180_000, 200_000, true)]    // exactly at 200K - 20K boundary
    [InlineData(180_001, 200_000, true)]    // above boundary
    [InlineData(200_000, 200_000, true)]    // at max
    [InlineData(179_999, 200_000, false)]   // just below boundary
    [InlineData(50_000, 200_000, false)]    // well below
    [InlineData(980_000, 1_000_000, true)]  // exactly at 1M - 20K boundary
    [InlineData(980_001, 1_000_000, true)]  // above 1M boundary
    [InlineData(979_999, 1_000_000, false)] // just below 1M boundary
    public void ShouldWarnAutocompact_UsesFlat20KBuffer(
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
