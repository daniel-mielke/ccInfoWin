using System.Globalization;

namespace CCInfoWindows.Helpers;

/// <summary>
/// Formats countdown timers and locale-patterned timestamps for the monitoring dashboard.
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
    private const string LogSource = nameof(CountdownFormatter);

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
            : FormatWithLocalePattern(resetsAt.Value, ResetDatePatternUid, CultureInfo.CurrentUICulture);

    /// <summary>
    /// Renders <paramref name="value"/> in local time using the pattern the active language stores
    /// under <paramref name="patternUid"/>. Shared by every label whose field order is a translated
    /// resource rather than a literal — the weekly reset date and the 5-hour next-window label.
    /// </summary>
    internal static string FormatWithLocalePattern(DateTimeOffset value, string patternUid, CultureInfo culture)
        => FormatWithPattern(value, LocalizedText.ResolveOrNull(patternUid, LogSource), patternUid, culture);

    /// <summary>
    /// Pattern-and-culture seam, internal so the formatting can be asserted without a WinUI3Localizer
    /// host (mirrors BurnRateFormatter.ParseTime). A missing or malformed pattern degrades to
    /// <see cref="CultureDefaultPattern"/> instead of throwing into the caller's UI update.
    /// <paramref name="patternUid"/> names the offending resw entry in the log — it is the only part
    /// of the message a maintainer can act on.
    /// </summary>
    internal static string FormatWithPattern(
        DateTimeOffset value,
        string? pattern,
        string patternUid,
        CultureInfo culture)
    {
        var localTime = value.ToLocalTime();

        if (!string.IsNullOrWhiteSpace(pattern))
        {
            try
            {
                return localTime.ToString(pattern, culture);
            }
            catch (FormatException ex)
            {
                AppLog.Write(
                    LogSource,
                    ex,
                    $"'{patternUid}' = \"{pattern}\" is not a valid custom date format string.");
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
}
