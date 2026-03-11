namespace CCInfoWindows.Helpers;

/// <summary>
/// Provides context token limits and display names for known Claude models.
/// </summary>
public static class ModelContextLimits
{
    public const long DefaultContextLimit = 200_000;

    private const double LargeModelAutocompactThreshold = 0.90;
    private const double SmallModelAutocompactThreshold = 0.95;
    private const long LargeModelThresholdTokens = 100_000;

    private static readonly Dictionary<string, long> ContextLimits = new(StringComparer.OrdinalIgnoreCase)
    {
        ["claude-opus-4-6"] = 200_000,
        ["claude-sonnet-4-6"] = 200_000,
        ["claude-haiku-4-5"] = 200_000,
        ["claude-haiku-4-5-20251001"] = 200_000,
        ["claude-sonnet-4-5"] = 200_000,
        ["claude-sonnet-4-5-20250929"] = 200_000,
        ["claude-opus-4-5"] = 200_000,
        ["claude-opus-4-1"] = 200_000,
        ["claude-sonnet-4-0"] = 200_000,
        ["claude-opus-4-0"] = 200_000,
        ["claude-sonnet-4-20250514"] = 200_000,
        ["claude-opus-4-20250514"] = 200_000,
    };

    /// <summary>Returns the maximum context token count for the given model name, or the default 200K.</summary>
    public static long GetMaxContextTokens(string? modelName)
    {
        if (string.IsNullOrEmpty(modelName))
            return DefaultContextLimit;

        return ContextLimits.TryGetValue(modelName, out var limit) ? limit : DefaultContextLimit;
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
    /// Returns true when the token count is at or above the autocompact warning threshold
    /// for the given max token limit.
    /// </summary>
    public static bool ShouldWarnAutocompact(long totalTokens, long maxTokens)
    {
        if (maxTokens <= 0)
            return false;

        var utilization = (double)totalTokens / maxTokens;
        var threshold = maxTokens >= LargeModelThresholdTokens
            ? LargeModelAutocompactThreshold
            : SmallModelAutocompactThreshold;

        return utilization >= threshold;
    }

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
