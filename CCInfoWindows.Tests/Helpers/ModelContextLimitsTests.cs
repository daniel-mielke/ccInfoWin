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

    // The badge names the context panel shows. ContextWindowTests used to carry a second copy of
    // this table; this one is the superset and the only home.
    [Theory]
    [InlineData("claude-opus-4-6", "Opus 4.6")]
    [InlineData("claude-sonnet-4-6", "Sonnet 4.6")]
    [InlineData("claude-haiku-4-5", "Haiku 4.5")]
    [InlineData("claude-haiku-4-5-20251001", "Haiku 4.5")]
    [InlineData("claude-sonnet-4-5-20250929", "Sonnet 4.5")]
    [InlineData("claude-opus-4-1", "Opus 4.1")]
    [InlineData(null, "Unbekannt")]
    [InlineData("", "Unbekannt")]
    public void GetDisplayName_ReturnsTheFriendlyBadgeName(string? modelName, string expected)
    {
        var result = ModelContextLimits.GetDisplayName(modelName);

        Assert.Equal(expected, result);
    }

    // -------------------------------------------------------------------------
    // Autocompact warning — 20K before the EFFECTIVE max (see CTX-04 amendment)
    // -------------------------------------------------------------------------

    [Theory]
    // 200K model: effective max 167K, so the warning threshold is 147K.
    [InlineData(147_000, 200_000, true)]    // exactly at the boundary
    [InlineData(147_001, 200_000, true)]    // above the boundary
    [InlineData(160_000, 200_000, true)]    // above the boundary, bar not yet saturated
    [InlineData(167_000, 200_000, true)]    // at the effective max, where the bar reads 100%
    [InlineData(190_000, 200_000, true)]    // bar long saturated
    [InlineData(200_000, 200_000, true)]    // at the raw max
    [InlineData(146_999, 200_000, false)]   // just below the boundary
    [InlineData(50_000, 200_000, false)]    // well below
    // 1M model: effective max 967K, so the warning threshold is 947K.
    [InlineData(947_000, 1_000_000, true)]
    [InlineData(947_001, 1_000_000, true)]
    [InlineData(946_999, 1_000_000, false)]
    public void ShouldWarnAutocompact_Warns20KBeforeTheEffectiveMax(
        long totalTokens, long maxTokens, bool expected)
    {
        var result = ModelContextLimits.ShouldWarnAutocompact(totalTokens, maxTokens);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(200_000)]
    [InlineData(1_000_000)]
    public void ShouldWarnAutocompact_WarnsBeforeTheBarSaturates(long maxTokens)
    {
        // The regression this replaces: the buffer was subtracted from the RAW max, so for a 200K
        // model the warning threshold sat at 180K while the bar already read 100% at 167K — the
        // pre-announcement arrived 13,000 tokens after the event. One token below the effective
        // max the warning must already be on, and the bar must not be saturated yet.
        var effectiveMax = ModelContextLimits.GetEffectiveMaxTokens(maxTokens);
        var justBeforeSaturation = effectiveMax - 1;
        var data = new ContextWindowData { TotalTokens = justBeforeSaturation, MaxTokens = maxTokens };

        Assert.True(ModelContextLimits.ShouldWarnAutocompact(justBeforeSaturation, maxTokens));
        Assert.True(data.Utilization < 1.0);
    }

    [Fact]
    public void ShouldWarnAutocompact_ZeroMaxTokens_ReturnsFalse()
    {
        var result = ModelContextLimits.ShouldWarnAutocompact(100, 0);

        Assert.False(result);
    }

    [Fact]
    public void ShouldWarnAutocompact_EmptyContextInADegenerateWindow_DoesNotWarn()
    {
        // maxTokens below the 33K reserve clamps the effective max to 1, which would make
        // "effective - 20K" negative and warn about a context holding nothing at all.
        Assert.False(ModelContextLimits.ShouldWarnAutocompact(0, 1_000));
        Assert.True(ModelContextLimits.ShouldWarnAutocompact(1, 1_000));
    }

    // -------------------------------------------------------------------------
    // Family classification drives the badge colour — one ladder, not two
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("claude-opus-4-6", "#BF5AF2")]
    [InlineData("claude-sonnet-4-6", "#FF9F0A")]
    [InlineData("claude-haiku-4-5", "#0A84FF")]
    [InlineData("unknown-model", "#636366")]
    [InlineData(null, "#636366")]
    [InlineData("", "#636366")]
    public void GetBadgeColorHex_FollowsTheFamilyClassification(string? modelName, string expectedHex)
    {
        Assert.Equal(expectedHex, ModelContextLimits.GetBadgeColorHex(modelName));
    }

    [Theory]
    [InlineData("claude-sonnet-4-5-20250929", "claude-sonnet-4-5")]
    [InlineData("claude-haiku-4-5-20251001", "claude-haiku-4-5")]
    [InlineData("claude-sonnet-4-5", "claude-sonnet-4-5")]        // nothing to strip
    [InlineData("claude-opus-4-5-2025092", "claude-opus-4-5-2025092")]   // 7 digits, not a date
    [InlineData("claude-opus-4-5-2025092x", "claude-opus-4-5-2025092x")] // 8 chars, not numeric
    [InlineData("", "")]
    public void StripDateSuffix_RemovesOnlyAnEightDigitTrailingSegment(string modelName, string expected)
    {
        Assert.Equal(expected, ModelContextLimits.StripDateSuffix(modelName));
    }
}
