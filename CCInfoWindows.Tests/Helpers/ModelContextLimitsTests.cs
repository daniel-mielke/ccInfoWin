using CCInfoWindows.Helpers;
using CCInfoWindows.Models;

namespace CCInfoWindows.Tests.Helpers;

public class ModelContextLimitsTests
{
    private static ModelPricing Pricing(long? maxInput, double? inputAbove200k = null, double? outputAbove200k = null,
        double? cacheCreationAbove200k = null, double? cacheReadAbove200k = null) =>
        new()
        {
            MaxInputTokens = maxInput,
            InputCostAbove200k = inputAbove200k,
            OutputCostAbove200k = outputAbove200k,
            CacheCreationCostAbove200k = cacheCreationAbove200k,
            CacheReadCostAbove200k = cacheReadAbove200k,
        };

    private static Func<string, ModelPricing?> Lookup(ModelPricing? pricing) => _ => pricing;

    // -------------------------------------------------------------------------
    // Resolution step 2: pricing data
    // -------------------------------------------------------------------------

    [Fact]
    public void GetMaxContextTokens_LargeWindowWithoutAbove200kTier_ReturnsLargeWindow()
    {
        // Sonnet 5 / Sonnet 4.6 / Opus 4.6-4.8 / Opus 5 / Fable 5: native 1M, no surcharge.
        var result = ModelContextLimits.GetMaxContextTokens(
            "claude-sonnet-5", Lookup(Pricing(1_000_000)));

        Assert.Equal(1_000_000, result);
    }

    [Fact]
    public void GetMaxContextTokens_LargeWindowWithAbove200kTier_FallsBackToDefault()
    {
        // The Sonnet-4 case: LiteLLM lists 1M, but reaching it needs a beta header that a
        // normal Claude Code session never sends. The surcharge tier is the marker.
        var result = ModelContextLimits.GetMaxContextTokens(
            "claude-sonnet-4-20250514", Lookup(Pricing(1_000_000, inputAbove200k: 6e-06, outputAbove200k: 2.25e-05)));

        Assert.Equal(ModelContextLimits.DefaultContextLimit, result);
    }

    [Fact]
    public void GetMaxContextTokens_SmallWindow_TakesPricingValue()
    {
        // The Opus-4.5 fix: previously every Opus was hardcoded to 1M, so the autocompact
        // warning could never fire. 200K is the real window.
        var result = ModelContextLimits.GetMaxContextTokens(
            "claude-opus-4-5", Lookup(Pricing(200_000)));

        Assert.Equal(200_000, result);
    }

    [Fact]
    public void GetMaxContextTokens_NoPricingLookup_ReturnsDefault()
    {
        Assert.Equal(ModelContextLimits.DefaultContextLimit,
            ModelContextLimits.GetMaxContextTokens("claude-opus-4-5"));
    }

    [Fact]
    public void GetMaxContextTokens_PricingWithoutMaxInputTokens_ReturnsDefault()
    {
        Assert.Equal(ModelContextLimits.DefaultContextLimit,
            ModelContextLimits.GetMaxContextTokens("claude-x", Lookup(Pricing(null))));
    }

    [Theory]
    [InlineData("unknown-model")]
    [InlineData("gpt-4")]
    [InlineData("some-random-model")]
    public void GetMaxContextTokens_UnknownModel_ReturnsDefault(string modelName)
    {
        var result = ModelContextLimits.GetMaxContextTokens(modelName, Lookup(null));

        Assert.Equal(ModelContextLimits.DefaultContextLimit, result);
    }

    [Fact]
    public void GetMaxContextTokens_NullModel_ReturnsDefault()
    {
        var result = ModelContextLimits.GetMaxContextTokens(null, Lookup(Pricing(1_000_000)));

        Assert.Equal(ModelContextLimits.DefaultContextLimit, result);
    }

    // -------------------------------------------------------------------------
    // Resolution step 1: session evidence is a floor, applied before pricing
    // -------------------------------------------------------------------------

    [Fact]
    public void GetMaxContextTokens_ObservedTokensAboveDefault_OverridesSmallerPricingWindow()
    {
        var result = ModelContextLimits.GetMaxContextTokens(
            "claude-opus-4-5", Lookup(Pricing(200_000)), observedTokens: 250_000);

        Assert.Equal(1_000_000, result);
    }

    [Fact]
    public void GetMaxContextTokens_ObservedTokensExactlyAtDefault_DoesNotPromote()
    {
        // Strictly greater — 200_000 observed tokens is consistent with a 200K window.
        var result = ModelContextLimits.GetMaxContextTokens(
            "claude-opus-4-5", Lookup(Pricing(200_000)), observedTokens: 200_000);

        Assert.Equal(200_000, result);
    }

    [Fact]
    public void GetMaxContextTokens_ObservedTokensAboveDefault_BeatsAbove200kVeto()
    {
        // A 400K transcript proves the window regardless of what the price table implies.
        var result = ModelContextLimits.GetMaxContextTokens(
            "claude-sonnet-4-20250514",
            Lookup(Pricing(1_000_000, inputAbove200k: 6e-06)),
            observedTokens: 400_000);

        Assert.Equal(1_000_000, result);
    }

    // -------------------------------------------------------------------------
    // HasAbove200kPricingTier — one test per field
    // -------------------------------------------------------------------------

    [Fact]
    public void HasAbove200kPricingTier_NoFields_ReturnsFalse()
        => Assert.False(ModelContextLimits.HasAbove200kPricingTier(Pricing(1_000_000)));

    [Fact]
    public void HasAbove200kPricingTier_InputOnly_ReturnsTrue()
        => Assert.True(ModelContextLimits.HasAbove200kPricingTier(Pricing(1_000_000, inputAbove200k: 1e-06)));

    [Fact]
    public void HasAbove200kPricingTier_OutputOnly_ReturnsTrue()
        => Assert.True(ModelContextLimits.HasAbove200kPricingTier(Pricing(1_000_000, outputAbove200k: 1e-06)));

    [Fact]
    public void HasAbove200kPricingTier_CacheCreationOnly_ReturnsTrue()
        => Assert.True(ModelContextLimits.HasAbove200kPricingTier(Pricing(1_000_000, cacheCreationAbove200k: 1e-06)));

    [Fact]
    public void HasAbove200kPricingTier_CacheReadOnly_ReturnsTrue()
        => Assert.True(ModelContextLimits.HasAbove200kPricingTier(Pricing(1_000_000, cacheReadAbove200k: 1e-06)));

    [Fact]
    public void HasAbove200kPricingTier_ZeroValuedFields_ReturnsFalse()
        => Assert.False(ModelContextLimits.HasAbove200kPricingTier(Pricing(1_000_000, inputAbove200k: 0, outputAbove200k: 0)));

    // -------------------------------------------------------------------------
    // Regression: the bug the old Opus => 1M mapping hid
    // -------------------------------------------------------------------------

    [Fact]
    public void ShouldWarnAutocompact_FiresForOpus45SessionNear200K()
    {
        // Before this phase Opus resolved to 1M unconditionally, so a 190K-token Opus 4.5
        // session showed 19% usage and the autocompact warning never fired.
        var maxTokens = ModelContextLimits.GetMaxContextTokens(
            "claude-opus-4-5", Lookup(Pricing(200_000)), observedTokens: 190_000);

        Assert.Equal(200_000, maxTokens);
        Assert.True(ModelContextLimits.ShouldWarnAutocompact(190_000, maxTokens));
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
