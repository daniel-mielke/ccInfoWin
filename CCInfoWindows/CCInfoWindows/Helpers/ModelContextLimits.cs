using CCInfoWindows.Models;

namespace CCInfoWindows.Helpers;

/// <summary>
/// Provides context token limits and display names for known Claude models.
/// </summary>
public static class ModelContextLimits
{
    public enum ModelFamily
    {
        Unknown,
        Opus,
        Sonnet,
        Haiku
    }

    public const long DefaultContextLimit = 200_000;
    public const long ExtendedContextLimit = 1_000_000;
    public const long StandardAutocompactBuffer = 33_000;
    public const long AutocompactWarningBuffer = 20_000;

    /// <summary>
    /// Determines the model family from the given model name using substring matching.
    /// </summary>
    public static ModelFamily GetModelFamily(string? modelName)
    {
        if (string.IsNullOrEmpty(modelName))
            return ModelFamily.Unknown;

        var lower = modelName.ToLowerInvariant();

        if (lower.Contains("opus"))
            return ModelFamily.Opus;
        if (lower.Contains("sonnet"))
            return ModelFamily.Sonnet;
        if (lower.Contains("haiku"))
            return ModelFamily.Haiku;

        return ModelFamily.Unknown;
    }

    /// <summary>
    /// Returns the maximum context token count for the given model, resolved from live data
    /// rather than a hardcoded family map. Resolution order:
    ///
    ///   1. Session evidence — a transcript above 200K tokens proves a large window,
    ///      whatever the pricing data claims. Applied first and unconditionally.
    ///   2. Pricing data — <c>max_input_tokens</c> from LiteLLM, with the above-200k
    ///      price tier acting as a veto (see <see cref="HasAbove200kPricingTier"/>).
    ///   3. Fallback — 200K.
    ///
    /// <paramref name="pricingLookup"/> is a <c>Func</c> rather than an IPricingService so
    /// Helpers stays free of a Services.Interfaces dependency. Production passes
    /// <c>IPricingService.GetPrice</c> as a method group.
    /// </summary>
    public static long GetMaxContextTokens(
        string? modelName,
        Func<string, ModelPricing?>? pricingLookup = null,
        long observedTokens = 0)
    {
        // 1. Never assume a window smaller than the session has already demonstrated.
        //    Strictly greater: exactly 200_000 is consistent with a 200K window.
        if (observedTokens > DefaultContextLimit)
            return ExtendedContextLimit;

        // 2.
        if (!string.IsNullOrEmpty(modelName) && pricingLookup is not null)
        {
            var pricing = pricingLookup(modelName);
            var maxInput = pricing?.MaxInputTokens;

            if (maxInput is > 0 and <= DefaultContextLimit)
                return maxInput.Value;

            if (maxInput > DefaultContextLimit)
                return HasAbove200kPricingTier(pricing!) ? DefaultContextLimit : maxInput.Value;
        }

        // 3.
        return DefaultContextLimit;
    }

    /// <summary>
    /// True when the model prices tokens beyond 200K separately.
    ///
    /// That surcharge is the marker of an *opt-in* extended context: the large window
    /// exists but has to be requested with a beta header (Sonnet 4's 1M is the canonical
    /// case). A normal Claude Code session does not send that header, so the window it
    /// actually gets is 200K — reporting 1M there would hide a real limit.
    ///
    /// Models whose large window is native (Sonnet 5, Sonnet 4.6, Opus 4.6/4.7/4.8,
    /// Opus 5, Fable 5) carry no above-200k tier and keep their full max_input_tokens.
    /// </summary>
    public static bool HasAbove200kPricingTier(ModelPricing pricing) =>
        pricing.InputCostAbove200k is > 0
        || pricing.OutputCostAbove200k is > 0
        || pricing.CacheCreationCostAbove200k is > 0
        || pricing.CacheReadCostAbove200k is > 0;

    /// <summary>
    /// Returns the effective max tokens after subtracting the flat 33K autocompact buffer.
    /// </summary>
    public static long GetEffectiveMaxTokens(long maxTokens)
        => Math.Max(1, maxTokens - StandardAutocompactBuffer);

    /// <summary>
    /// Returns true when the remaining tokens fall below the flat 20K autocompact warning threshold.
    /// </summary>
    public static bool ShouldWarnAutocompact(long totalTokens, long maxTokens)
    {
        if (maxTokens <= 0)
            return false;
        return totalTokens >= maxTokens - AutocompactWarningBuffer;
    }

    /// <summary>
    /// Returns a friendly display name for the given model name.
    /// Strips date suffixes and formats as "Family Major.Minor".
    /// </summary>
    public static string GetDisplayName(string? modelName)
    {
        if (string.IsNullOrEmpty(modelName))
            return "Unbekannt";

        var normalized = StripDateSuffix(modelName);
        return ParseDisplayName(normalized) ?? modelName;
    }

    /// <summary>
    /// Returns the badge background hex color for the given model name.
    /// Opus = purple (#BF5AF2), Sonnet = orange (#FF9F0A), Haiku = blue (#0A84FF).
    /// Falls back to gray (#636366) for unknown models.
    /// </summary>
    public static string GetBadgeColorHex(string? modelName)
    {
        if (string.IsNullOrEmpty(modelName))
            return FallbackBadgeColor;

        var lower = modelName.ToLowerInvariant();

        if (lower.Contains("opus"))
            return OpusBadgeColor;
        if (lower.Contains("sonnet"))
            return SonnetBadgeColor;
        if (lower.Contains("haiku"))
            return HaikuBadgeColor;

        return FallbackBadgeColor;
    }

    private const string OpusBadgeColor = "#BF5AF2";
    private const string SonnetBadgeColor = "#FF9F0A";
    private const string HaikuBadgeColor = "#0A84FF";
    private const string FallbackBadgeColor = "#636366";

    private static string StripDateSuffix(string modelName)
    {
        // Strip date suffixes like "-20251001"
        var parts = modelName.Split('-');
        if (parts.Length > 0 && parts[^1].Length == 8 && long.TryParse(parts[^1], out _))
            return string.Join('-', parts[..^1]);

        return modelName;
    }

    private static string? ParseDisplayName(string modelName)
    {
        // Expected pattern: claude-{family}-{major}-{minor} or claude-{family}-{major}
        var parts = modelName.Split('-');
        if (parts.Length < 3 || !string.Equals(parts[0], "claude", StringComparison.OrdinalIgnoreCase))
            return null;

        var family = CapitalizeFirst(parts[1]);

        if (parts.Length >= 4 && IsVersionNumber(parts[2]) && IsVersionNumber(parts[3]))
            return $"{family} {parts[2]}.{parts[3]}";

        if (parts.Length == 3 && IsVersionNumber(parts[2]))
            return $"{family} {parts[2]}";

        // Handle: claude-haiku-4-5-... where family is single word
        // Already handled by StripDateSuffix above
        if (parts.Length >= 4 && IsVersionNumber(parts[^2]) && IsVersionNumber(parts[^1]))
        {
            var familyParts = parts[1..^2];
            var familyName = CapitalizeFirst(string.Join("-", familyParts));
            return $"{familyName} {parts[^2]}.{parts[^1]}";
        }

        return null;
    }

    private static bool IsVersionNumber(string part) =>
        int.TryParse(part, out _);

    private static string CapitalizeFirst(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        return char.ToUpperInvariant(text[0]) + text[1..];
    }
}
