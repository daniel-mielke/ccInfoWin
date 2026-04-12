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
    /// Returns the maximum context token count for the given model name.
    /// Opus models return 1M. Sonnet models return sonnetContextSize (default 200K). All others return 200K.
    /// </summary>
    public static long GetMaxContextTokens(string? modelName, long sonnetContextSize = DefaultContextLimit)
    {
        return GetModelFamily(modelName) switch
        {
            ModelFamily.Opus => ExtendedContextLimit,
            ModelFamily.Sonnet => sonnetContextSize,
            _ => DefaultContextLimit
        };
    }

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
