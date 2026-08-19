using WinUI3Localizer;

namespace CCInfoWindows.Helpers;

/// <summary>
/// The single place that reads a resw entry and judges whether the answer is usable.
///
/// Four call sites had grown their own copy of this rule (MainView's bootstrap banner, ExportHelper's
/// PNG captions, CountdownFormatter's reset-date pattern, MainViewModel's action banner) and they did
/// not agree: one accepted an echoed uid, so an unbuilt localizer would have painted
/// "DashboardStartupFailedMessage" onto the screen instead of a sentence.
///
/// The rule, applied uniformly: an answer is usable unless it is blank or equal to the uid that was
/// asked for. WinUI3Localizer's NullLocalizer — the instance in place before Build() completes —
/// echoes the uid back, and a built one answers an unknown uid with the empty string. Both mean "no
/// translation", and rendering either shows the user a resource key.
/// </summary>
internal static class LocalizedText
{
    /// <summary>Localized text, or <paramref name="fallback"/> when the dictionary cannot answer.</summary>
    internal static string Resolve(string uid, string fallback, string logSource)
        => ResolveOrNull(uid, logSource) ?? fallback;

    /// <summary>
    /// The same rule minus the echoed-uid clause, for callers whose tests read the echo to prove WHICH
    /// key a label reached for (SettingsViewModel's captions; see HeadlessLocalizerContractTests).
    /// The clause it drops is unreachable in the shipped app anyway — App awaits the localizer build
    /// before the first window exists — while the guarded lookup it keeps is the half that matters:
    /// these callers are property getters, so an escaping exception lands inside binding evaluation.
    /// </summary>
    internal static string ResolveKeepingEcho(string uid, string fallback, string logSource)
        => ResolveOrNull(LocalizerLookup, uid, logSource, rejectEcho: false) ?? fallback;

    /// <summary>
    /// Localized text, or null for callers that derive their own substitute rather than carrying a
    /// literal one — a date pattern rebuilt from the culture, say.
    /// </summary>
    internal static string? ResolveOrNull(string uid, string logSource)
        => ResolveOrNull(LocalizerLookup, uid, logSource);

    /// <summary>
    /// Lookup-injecting overload. xUnit cannot start a WinUI3Localizer host, and ExportHelper's
    /// callers already pass their own reader; both need the rule without the static dependency.
    /// </summary>
    internal static string Resolve(Func<string, string> lookup, string uid, string fallback, string logSource)
        => ResolveOrNull(lookup, uid, logSource) ?? fallback;

    internal static string? ResolveOrNull(
        Func<string, string> lookup, string uid, string logSource, bool rejectEcho = true)
    {
        try
        {
            var text = lookup(uid);

            if (string.IsNullOrWhiteSpace(text)) return null;

            return rejectEcho && string.Equals(text, uid, StringComparison.Ordinal) ? null : text;
        }
        catch (Exception ex)
        {
            // Localizer.Get() throws when the host never built, which is one of the very failures the
            // callers' fallback text exists to describe.
            AppLog.Write(logSource, ex, $"could not read '{uid}'.");
            return null;
        }
    }

    /// <summary>The app's real dictionary read. Every invocation happens inside the guard above.</summary>
    internal static string LocalizerLookup(string uid) => Localizer.Get().GetLocalizedString(uid);
}
