using System.Globalization;
using WinUI3Localizer;

namespace CCInfoWindows.Helpers;

/// <summary>
/// Formats countdown timers and reset dates for display in the monitoring dashboard.
/// </summary>
public static class CountdownFormatter
{
    /// <summary>
    /// Single-segment resw key carrying the reset-date pattern of the active language.
    /// WinUI3Localizer 2.3.0 keys its dictionary on the text before the FIRST '.', so a dotted
    /// uid resolves to nothing.
    /// </summary>
    public const string ResetDatePatternUid = "WeeklyResetDatePattern";

    private const string NoValue = "--";
    private const int HoursPerDay = 24;

    /// <summary>
    /// Formats the remaining time until reset as "Xd Yh", "Xh Ymin", or "Ymin".
    /// Returns "--" if null or already past.
    /// </summary>
    public static string FormatCountdown(DateTimeOffset? resetsAt)
    {
        if (resetsAt is null)
            return NoValue;

        var remaining = resetsAt.Value - DateTimeOffset.UtcNow;

        if (remaining <= TimeSpan.Zero)
            return NoValue;

        if (remaining.TotalHours >= HoursPerDay)
        {
            var days = (int)remaining.TotalDays;
            var hrs = remaining.Hours;
            return $"{days}d {hrs}h";
        }

        var hours = (int)remaining.TotalHours;
        var minutes = remaining.Minutes;

        if (hours > 0)
            return $"{hours}h {minutes}min";

        if (minutes > 0)
            return $"{minutes}min";

        return NoValue;
    }

    /// <summary>
    /// Formats a reset date for the active UI language: the field order comes from that language's
    /// <see cref="ResetDatePatternUid"/> entry and the day/month names from
    /// <see cref="CultureInfo.CurrentUICulture"/>. Returns "--" if null.
    /// </summary>
    public static string FormatResetDate(DateTimeOffset? resetsAt) =>
        resetsAt is null
            ? NoValue
            : FormatResetDate(resetsAt.Value, ResolvePattern(), CultureInfo.CurrentUICulture);

    /// <summary>
    /// Pattern-and-culture overload, internal so the formatting can be asserted without a
    /// WinUI3Localizer host (mirrors BurnRateFormatter.ParseTime). A missing or malformed pattern
    /// degrades to <see cref="CultureDefaultPattern"/> instead of throwing into the caller's UI update.
    /// </summary>
    internal static string FormatResetDate(DateTimeOffset resetsAt, string? pattern, CultureInfo culture)
    {
        var localTime = resetsAt.ToLocalTime();

        if (!string.IsNullOrWhiteSpace(pattern))
        {
            try
            {
                return localTime.ToString(pattern, culture);
            }
            catch (FormatException ex)
            {
                AppLog.Write(
                    nameof(CountdownFormatter),
                    ex,
                    $"'{ResetDatePatternUid}' = \"{pattern}\" is not a valid custom date format string.");
            }
        }

        return localTime.ToString(CultureDefaultPattern(culture), culture);
    }

    /// <summary>
    /// Assembles a pattern from the culture itself, so the layout can never contradict the names
    /// rendered into it.
    /// </summary>
    internal static string CultureDefaultPattern(CultureInfo culture) =>
        $"ddd, {culture.DateTimeFormat.MonthDayPattern}, {culture.DateTimeFormat.ShortTimePattern}";

    /// <summary>
    /// Reads the pattern of the active language, or null when the answer cannot be trusted: an
    /// unbuilt localizer (NullLocalizer) echoes the uid back and a built one returns an empty
    /// string for an unknown uid, and "WeeklyResetDatePattern" fed to ToString renders as a date.
    /// </summary>
    private static string? ResolvePattern()
    {
        try
        {
            var pattern = Localizer.Get().GetLocalizedString(ResetDatePatternUid);
            return string.IsNullOrWhiteSpace(pattern) || pattern == ResetDatePatternUid
                ? null
                : pattern;
        }
        catch (Exception ex)
        {
            AppLog.Write(nameof(CountdownFormatter), ex, $"could not read '{ResetDatePatternUid}'.");
            return null;
        }
    }
}
