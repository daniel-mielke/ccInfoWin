using WinUI3Localizer;

namespace CCInfoWindows.Helpers;

/// <summary>
/// Formats burn rate time labels for display in banners and toast notifications.
/// Single source of truth for hours/minutes formatting (DRY).
/// </summary>
public static class BurnRateFormatter
{
    /// <summary>
    /// Formats a duration in minutes as a localized time label.
    /// Uses BurnRateFormat_* resource keys for localization.
    /// </summary>
    public static string FormatTimeLabel(int minutes)
    {
        var (h, m, format) = ParseTime(minutes);

        return format switch
        {
            TimeFormat.HoursOnly => string.Format(
                Localizer.Get().GetLocalizedString("BurnRateFormat_HoursOnly"), h),
            TimeFormat.HoursMinutes => string.Format(
                Localizer.Get().GetLocalizedString("BurnRateFormat_HoursMinutes"), h, m),
            _ => string.Format(
                Localizer.Get().GetLocalizedString("BurnRateFormat_MinutesOnly"), m),
        };
    }

    /// <summary>
    /// Parses minutes into hours, remaining minutes, and the appropriate time format.
    /// Internal for testability without WinUI localizer dependency.
    /// </summary>
    internal static (int hours, int remainingMinutes, TimeFormat format) ParseTime(int minutes)
    {
        if (minutes >= 60)
        {
            var h = minutes / 60;
            var m = minutes % 60;

            return m == 0
                ? (h, 0, TimeFormat.HoursOnly)
                : (h, m, TimeFormat.HoursMinutes);
        }

        return (0, minutes, TimeFormat.MinutesOnly);
    }
}
