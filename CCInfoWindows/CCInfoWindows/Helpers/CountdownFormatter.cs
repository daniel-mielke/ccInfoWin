using System.Globalization;

namespace CCInfoWindows.Helpers;

/// <summary>
/// Formats countdown timers and reset dates for display in the monitoring dashboard.
/// </summary>
public static class CountdownFormatter
{
    private static readonly CultureInfo GermanCulture = new("de-DE");

    /// <summary>
    /// Formats the remaining time until reset as "Xh Ymin" or "Ymin".
    /// Returns "--" if null or already past.
    /// </summary>
    public static string FormatCountdown(DateTimeOffset? resetsAt)
    {
        if (resetsAt is null)
            return "--";

        var remaining = resetsAt.Value - DateTimeOffset.UtcNow;

        if (remaining <= TimeSpan.Zero)
            return "--";

        var hours = (int)remaining.TotalHours;
        var minutes = remaining.Minutes;

        if (hours > 0)
            return $"{hours}h {minutes}min";

        if (minutes > 0)
            return $"{minutes}min";

        return "--";
    }

    /// <summary>
    /// Formats a reset date in German locale as "ddd dd.MM., HH:mm" (e.g., "Fr. 27.02., 10:00").
    /// Returns "--" if null.
    /// </summary>
    public static string FormatResetDate(DateTimeOffset? resetsAt)
    {
        if (resetsAt is null)
            return "--";

        var localTime = resetsAt.Value.ToLocalTime();
        return localTime.ToString("ddd dd.MM., HH:mm", GermanCulture);
    }
}
