using System.Xml.Linq;

namespace CCInfoWindows.Tests.Localization;

/// <summary>
/// Phase 23 L10N-01 structural validation. Verifies that the six required resource
/// keys (4 new in Phase 23 + 2 pre-existing from Phase 20) exist in both locales
/// with non-empty, expected values.
///
/// Strategy: XDocument-based structural validation (per RESEARCH Pitfall 1 — xUnit
/// cannot initialize the WinUI3Localizer host, so we read the resw files directly).
///
/// Phase 27 extension policy:
///   - Plan 27-01 (L10N) appends LastFetchRelative.{JustNow,MinutesAgo,HoursAgo,DaysAgo,Never}
///   - Plan 27-02 (NEXTWIN) appends MainView.NextWindow.LabelDe / .LabelEn
///   - Plan 27-03 (PRICING) appends MainView.PricingErrorInfoBar.Title / .Message
///   - Plan 27-04 (ORGID) appends Settings.Account.RedetectButton + Dialog.OrgPicker.* + MainView.OrgMismatchInfoBar.*
/// </summary>
public class ResourceCoverageTests
{
    private const string EnUsRelativePath = "Strings/en-US/Resources.resw";
    private const string DeDeRelativePath = "Strings/de-DE/Resources.resw";

    private static readonly string[] RequiredKeys =
    [
        "NotSignedIn.Text",
        "NoData.Text",
        "Loading.Text",
        "InactiveSessionTooltip",
        "LoginReloadButton.[using:Microsoft.UI.Xaml.Controls]ToolTipService.ToolTip",
        "LoginReloadButton.[using:Microsoft.UI.Xaml.Automation]AutomationProperties.Name",
        // Phase 26 RENAME-01: session rename dialog + pencil button tooltip
        "Dialog.RenameSession.Title",
        "Dialog.RenameSession.SaveButton",
        "Dialog.RenameSession.CancelButton",
        "Dialog.RenameSession.ResetButton",
        "MainViewRenameButton.[using:Microsoft.UI.Xaml.Controls]ToolTipService.ToolTip",
        // Phase 26 RENAME-02: Settings Sessions tab content (Plan 03)
        "SettingsTabSessions",
        "Settings.Sessions.Header.Text",
        "Settings.Sessions.NoSessions.Text",
        "Settings.Sessions.OrphanLabel.Text",
        "Settings.Sessions.ClearButton.[using:Microsoft.UI.Xaml.Controls]ToolTipService.ToolTip",
        // Phase 27 L10N-01: localized last-fetch relative time on About tab
        "LastFetchRelative.JustNow",
        "LastFetchRelative.MinutesAgo",
        "LastFetchRelative.HoursAgo",
        "LastFetchRelative.DaysAgo",
        "LastFetchRelative.Never",
        // Phase 27 NEXTWIN-01..03: absolute next-window start label (D-NW-03 / CD-01)
        "MainView.NextWindow.LabelDe",
        "MainView.NextWindow.LabelEn",
    ];

    private static readonly Dictionary<string, string> ExpectedEnUs = new()
    {
        ["NotSignedIn.Text"] = "Not signed in",
        ["NoData.Text"] = "No data",
        ["Loading.Text"] = "Loading",
        ["InactiveSessionTooltip"] = "Inactive for > {0}min",
        ["LoginReloadButton.[using:Microsoft.UI.Xaml.Controls]ToolTipService.ToolTip"] = "Reload page",
        ["LoginReloadButton.[using:Microsoft.UI.Xaml.Automation]AutomationProperties.Name"] = "Reload login page",
        ["Dialog.RenameSession.Title"] = "Rename Session",
        ["Dialog.RenameSession.SaveButton"] = "Save",
        ["Dialog.RenameSession.CancelButton"] = "Cancel",
        ["Dialog.RenameSession.ResetButton"] = "Reset",
        ["MainViewRenameButton.[using:Microsoft.UI.Xaml.Controls]ToolTipService.ToolTip"] = "Rename session",
        // Phase 26 RENAME-02 Plan 03
        ["SettingsTabSessions"] = "Sessions",
        ["Settings.Sessions.Header.Text"] = "CUSTOM SESSION NAMES",
        ["Settings.Sessions.NoSessions.Text"] = "No sessions available.",
        ["Settings.Sessions.OrphanLabel.Text"] = "Session not found",
        ["Settings.Sessions.ClearButton.[using:Microsoft.UI.Xaml.Controls]ToolTipService.ToolTip"] = "Remove custom name",
        // Phase 27 L10N-01
        ["LastFetchRelative.JustNow"] = "just now",
        ["LastFetchRelative.MinutesAgo"] = "{0} minutes ago",
        ["LastFetchRelative.HoursAgo"] = "{0} hours ago",
        ["LastFetchRelative.DaysAgo"] = "{0} days ago",
        ["LastFetchRelative.Never"] = "Never",
        // Phase 27 NEXTWIN-01..03: format patterns (same values in both locales — format strings, not human-readable)
        ["MainView.NextWindow.LabelDe"] = "ddd d.M. HH:mm",
        ["MainView.NextWindow.LabelEn"] = "ddd HH:mm",
    };

    private static readonly Dictionary<string, string> ExpectedDeDe = new()
    {
        ["NotSignedIn.Text"] = "Nicht angemeldet",
        ["NoData.Text"] = "Keine Daten",
        ["Loading.Text"] = "Wird geladen",
        ["InactiveSessionTooltip"] = "Inaktiv seit > {0}min",
        ["LoginReloadButton.[using:Microsoft.UI.Xaml.Controls]ToolTipService.ToolTip"] = "Seite neu laden",
        ["LoginReloadButton.[using:Microsoft.UI.Xaml.Automation]AutomationProperties.Name"] = "Login-Seite neu laden",
        ["Dialog.RenameSession.Title"] = "Sitzung umbenennen",
        ["Dialog.RenameSession.SaveButton"] = "Speichern",
        ["Dialog.RenameSession.CancelButton"] = "Abbrechen",
        ["Dialog.RenameSession.ResetButton"] = "Zurücksetzen",
        ["MainViewRenameButton.[using:Microsoft.UI.Xaml.Controls]ToolTipService.ToolTip"] = "Sitzung umbenennen",
        // Phase 26 RENAME-02 Plan 03
        ["SettingsTabSessions"] = "Sitzungen",
        ["Settings.Sessions.Header.Text"] = "EIGENE SITZUNGSNAMEN",
        ["Settings.Sessions.NoSessions.Text"] = "Keine Sitzungen verfügbar.",
        ["Settings.Sessions.OrphanLabel.Text"] = "Sitzung nicht gefunden",
        ["Settings.Sessions.ClearButton.[using:Microsoft.UI.Xaml.Controls]ToolTipService.ToolTip"] = "Eigenen Namen entfernen",
        // Phase 27 L10N-01
        ["LastFetchRelative.JustNow"] = "gerade eben",
        ["LastFetchRelative.MinutesAgo"] = "vor {0} Minuten",
        ["LastFetchRelative.HoursAgo"] = "vor {0} Stunden",
        ["LastFetchRelative.DaysAgo"] = "vor {0} Tagen",
        ["LastFetchRelative.Never"] = "Nie",
        // Phase 27 NEXTWIN-01..03: format patterns (same values in both locales — format strings, not human-readable)
        ["MainView.NextWindow.LabelDe"] = "ddd d.M. HH:mm",
        ["MainView.NextWindow.LabelEn"] = "ddd HH:mm",
    };

    [Fact]
    public void EnUs_AllSixL10N01Keys_ExistWithExpectedValues()
    {
        var keyToValue = LoadResw(EnUsRelativePath);

        foreach (var key in RequiredKeys)
        {
            Assert.True(keyToValue.ContainsKey(key), $"en-US Resources.resw is missing key '{key}'.");
            Assert.False(string.IsNullOrWhiteSpace(keyToValue[key]), $"en-US key '{key}' has an empty value.");
            Assert.Equal(ExpectedEnUs[key], keyToValue[key]);
        }
    }

    [Fact]
    public void DeDe_AllSixL10N01Keys_ExistWithExpectedValues()
    {
        var keyToValue = LoadResw(DeDeRelativePath);

        foreach (var key in RequiredKeys)
        {
            Assert.True(keyToValue.ContainsKey(key), $"de-DE Resources.resw is missing key '{key}'.");
            Assert.False(string.IsNullOrWhiteSpace(keyToValue[key]), $"de-DE key '{key}' has an empty value.");
            Assert.Equal(ExpectedDeDe[key], keyToValue[key]);
        }
    }

    [Fact]
    public void InactiveSessionTooltip_ContainsSinglePositionalPlaceholderAndNoNewline()
    {
        // D-05: single {0} placeholder — Phase 22's string.Format substitutes the threshold integer.
        // D-07: no \n in the resw value — Phase 22 owns the multi-line composition (path + "\n" + threshold).
        foreach (var (locale, expected) in new[] { ("en-US", ExpectedEnUs), ("de-DE", ExpectedDeDe) })
        {
            var template = expected["InactiveSessionTooltip"];

            Assert.Contains("{0}", template);
            Assert.DoesNotContain("{1}", template);
            Assert.DoesNotContain("\n", template);
        }
    }

    [Fact]
    public void Resw_ContainsNoDuplicateKeyEntries()
    {
        // D-02 guard: re-authoring LoginReloadButton.* would produce duplicate <data> entries
        // and silent runtime resource lookup failures.
        AssertNoDuplicates(EnUsRelativePath, "en-US");
        AssertNoDuplicates(DeDeRelativePath, "de-DE");
    }

    private static Dictionary<string, string> LoadResw(string relativePath)
    {
        var fullPath = Path.Combine(AppContext.BaseDirectory, relativePath);
        Assert.True(File.Exists(fullPath), $"Resw file not found at: {fullPath}");

        var doc = XDocument.Load(fullPath);
        var dataElements = doc.Root?.Elements("data") ?? Enumerable.Empty<XElement>();

        var result = new Dictionary<string, string>();
        foreach (var data in dataElements)
        {
            var name = data.Attribute("name")?.Value;
            var value = data.Element("value")?.Value;
            if (name != null && value != null)
            {
                result[name] = value;
            }
        }

        return result;
    }

    private static void AssertNoDuplicates(string relativePath, string locale)
    {
        var fullPath = Path.Combine(AppContext.BaseDirectory, relativePath);
        var doc = XDocument.Load(fullPath);
        var names = doc.Root?.Elements("data")
            .Select(d => d.Attribute("name")?.Value)
            .Where(n => n != null)
            .ToList() ?? new List<string?>();

        var duplicates = names.GroupBy(n => n).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        Assert.Empty(duplicates);
    }
}
