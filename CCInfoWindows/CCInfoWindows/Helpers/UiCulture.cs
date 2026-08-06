using System.Globalization;

namespace CCInfoWindows.Helpers;

/// <summary>
/// Points the thread UI culture at the display language the localizer is showing.
///
/// WinUI3Localizer only swaps resw values. The day and month names rendered INTO a resw-supplied date
/// pattern come from CultureInfo.CurrentUICulture, so without this the German pattern
/// "ddd dd.MM., HH:mm" renders English weekday names on an English Windows — a half-translated label.
/// CountdownFormatter.FormatResetDate and MainViewModel's next-window label are the two affected
/// surfaces.
///
/// The language can change in exactly two places — App's startup path and the Settings dropdown — and
/// both had grown their own copy of this block. One copy is what keeps them from drifting apart.
///
/// CurrentCulture is deliberately NOT touched. Which language the UI speaks and how the user wants
/// numbers, currency and regional dates formatted are independent Windows settings; every numeric
/// formatter in this app (CostFormatter, TokenFormatter, the chart axis) is pinned to InvariantCulture
/// anyway, so following the display language here would change nothing except the user's own regional
/// choice. AppHostConventionTests pins that decision so it cannot be reversed by accident.
/// </summary>
internal static class UiCulture
{
    /// <summary>
    /// Aligns the UI culture with <paramref name="language"/>. A name this system cannot resolve is
    /// recorded and otherwise ignored: the localizer has already accepted the language at every call
    /// site, so keeping the previous culture beats failing the switch the user asked for.
    /// </summary>
    internal static void Apply(string language, string logSource)
        => Apply(language, logSource, AssignUiCulture);

    /// <summary>
    /// Assignment-injecting overload. <see cref="AssignUiCulture"/> writes two process-wide statics
    /// that every other test reads through CurrentUICulture, so the selection rule is asserted against
    /// a captured culture instead of against the globals (finding 33: no test may leave machine state
    /// behind, and xUnit runs test classes in parallel).
    /// </summary>
    internal static void Apply(string language, string logSource, Action<CultureInfo> assign)
    {
        var culture = Resolve(language, logSource);
        if (culture is null) return;

        assign(culture);
    }

    private static CultureInfo? Resolve(string language, string logSource)
    {
        // GetCultureInfo("") answers InvariantCulture instead of throwing, and invariant month names are
        // not a language: a blank code is a caller defect and must not reach the screen as one.
        if (string.IsNullOrWhiteSpace(language))
        {
            AppLog.Write(logSource, "no display language to align the UI culture with");
            return null;
        }

        try
        {
            return CultureInfo.GetCultureInfo(language);
        }
        catch (CultureNotFoundException ex)
        {
            AppLog.Write(logSource, ex, $"'{language}' is not a culture this system knows");
            return null;
        }
    }

    private static void AssignUiCulture(CultureInfo culture)
    {
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentUICulture = culture;
    }
}
