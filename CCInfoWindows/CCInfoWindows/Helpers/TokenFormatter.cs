using System.Globalization;

namespace CCInfoWindows.Helpers;

/// <summary>
/// Formats token counts as compact human-readable strings with K/M suffixes.
/// </summary>
public static class TokenFormatter
{
    private const long KiloThreshold = 1_000;
    private const long MegaThreshold = 1_000_000;
    private const string KiloSuffix = "K";
    private const string MegaSuffix = "M";

    /// <summary>
    /// Formats a token count as a compact string.
    /// Values below 1000 are returned as-is.
    /// Values >= 1000 use "1.2K" format.
    /// Values >= 1,000,000 use "1.2M" format.
    /// </summary>
    public static string FormatTokenCount(long tokens)
    {
        if (tokens >= MegaThreshold)
            return FormatWithSuffix(tokens, MegaThreshold, MegaSuffix);

        if (tokens >= KiloThreshold)
            return FormatWithSuffix(tokens, KiloThreshold, KiloSuffix);

        return tokens.ToString(CultureInfo.InvariantCulture);
    }

    private static string FormatWithSuffix(long tokens, long divisor, string suffix)
    {
        var value = (double)tokens / divisor;
        return $"{value.ToString("F1", CultureInfo.InvariantCulture)}{suffix}";
    }
}
