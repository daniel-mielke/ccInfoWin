namespace CCInfoWindows.Helpers;

/// <summary>
/// Calculates token consumption rate (tokens per hour) from a rolling time window.
/// </summary>
public static class BurnRateCalculator
{
    /// <summary>
    /// Returns the token burn rate in tokens per hour for the given rolling window.
    /// Returns 0 if the window is zero or negative, or if no entries fall within the window.
    /// </summary>
    /// <param name="entries">Per-entry tuples of (timestamp, total tokens for that entry).</param>
    /// <param name="windowMinutes">Size of the rolling window in minutes. Default 60.</param>
    public static double ComputeBurnRate(
        IEnumerable<(DateTimeOffset Timestamp, long Tokens)> entries,
        int windowMinutes = 60)
    {
        if (windowMinutes <= 0)
            return 0;

        var cutoff = DateTimeOffset.UtcNow.AddMinutes(-windowMinutes);
        var recent = entries.Where(e => e.Timestamp >= cutoff).ToList();

        if (recent.Count == 0)
            return 0;

        var totalTokens = recent.Sum(e => e.Tokens);
        return totalTokens / (double)windowMinutes * 60;
    }
}
