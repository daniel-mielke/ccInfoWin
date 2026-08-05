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
///   - Plan 27-01 (L10N) appends LastFetch{JustNow,MinutesAgo,HoursAgo,DaysAgo,Never}
///   - Plan 27-02 (NEXTWIN) appends NextWindowLabelDe / NextWindowLabelEn
///   - Plan 27-03 (PRICING) appends PricingErrorInfoBar.Title / .Message
///   - Plan 27-04 (ORGID) appends SettingsAccountRedetectButton.Text + Dialog.OrgPicker.*
///
/// IMPORTANT: WinUI3Localizer 2.3.0 only resolves Foo.Property keys (Length==2 split on '.').
/// Three-segment keys like "MainView.Foo.Title" are silently dropped — controls render
/// with null Title/Message. See LocalizerKeySegmentLimitTest below for enforcement.
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
        "RenameSessionDialogTitle",
        "RenameSessionDialogSaveButton",
        "RenameSessionDialogCancelButton",
        "RenameSessionDialogResetButton",
        "MainViewRenameButton.[using:Microsoft.UI.Xaml.Controls]ToolTipService.ToolTip",
        // Phase 26 RENAME-02: Settings Sessions tab content (Plan 03)
        "SettingsTabSessions",
        "SettingsSessionsHeader.Text",
        "SettingsSessionsNoSessions.Text",
        "SettingsSessionsOrphanLabel.Text",
        "Settings.Sessions.ClearButton.[using:Microsoft.UI.Xaml.Controls]ToolTipService.ToolTip",
        // Phase 27 L10N-01: localized last-fetch relative time on About tab
        "LastFetchJustNow",
        "LastFetchMinutesAgo",
        "LastFetchHoursAgo",
        "LastFetchDaysAgo",
        "LastFetchNever",
        // Phase 27 NEXTWIN-01..03: absolute next-window start label (D-NW-03 / CD-01)
        "NextWindowLabelDe",
        "NextWindowLabelEn",
        // Phase 27 PRICING-01..03: pricing-service silent-failure surfacing
        "PricingErrorInfoBar.Title",
        "PricingErrorInfoBar.Message",
        // Phase 27 ORGID-01..05 (D-OG-06): org-id picker localization
        "SettingsAccountRedetectButton.Text",
        "OrgPickerDialogTitle",
        "OrgPickerDialogSwitchButton",
        "OrgPickerDialogCancelButton",
        "OrgPickerDialogNoOrgs",
    ];

    private static readonly Dictionary<string, string> ExpectedEnUs = new()
    {
        ["NotSignedIn.Text"] = "Not signed in",
        ["NoData.Text"] = "No data",
        ["Loading.Text"] = "Loading",
        ["InactiveSessionTooltip"] = "Inactive for > {0}min",
        ["LoginReloadButton.[using:Microsoft.UI.Xaml.Controls]ToolTipService.ToolTip"] = "Reload page",
        ["LoginReloadButton.[using:Microsoft.UI.Xaml.Automation]AutomationProperties.Name"] = "Reload login page",
        ["RenameSessionDialogTitle"] = "Rename Session",
        ["RenameSessionDialogSaveButton"] = "Save",
        ["RenameSessionDialogCancelButton"] = "Cancel",
        ["RenameSessionDialogResetButton"] = "Reset",
        ["MainViewRenameButton.[using:Microsoft.UI.Xaml.Controls]ToolTipService.ToolTip"] = "Rename session",
        // Phase 26 RENAME-02 Plan 03
        ["SettingsTabSessions"] = "Sessions",
        ["SettingsSessionsHeader.Text"] = "CUSTOM SESSION NAMES",
        ["SettingsSessionsNoSessions.Text"] = "No sessions available.",
        ["SettingsSessionsOrphanLabel.Text"] = "Session not found",
        ["Settings.Sessions.ClearButton.[using:Microsoft.UI.Xaml.Controls]ToolTipService.ToolTip"] = "Remove custom name",
        // Phase 27 L10N-01
        ["LastFetchJustNow"] = "just now",
        ["LastFetchMinutesAgo"] = "{0} minutes ago",
        ["LastFetchHoursAgo"] = "{0} hours ago",
        ["LastFetchDaysAgo"] = "{0} days ago",
        ["LastFetchNever"] = "Never",
        // Phase 27 NEXTWIN-01..03: format patterns (same values in both locales — format strings, not human-readable)
        ["NextWindowLabelDe"] = "ddd d.M. HH:mm",
        ["NextWindowLabelEn"] = "ddd HH:mm",
        // Phase 27 PRICING-01..03: pricing-service silent-failure surfacing
        ["PricingErrorInfoBar.Title"] = "Pricing data unavailable",
        ["PricingErrorInfoBar.Message"] = "Cost figures may be inaccurate.",
        // Phase 27 ORGID-01..05 (D-OG-06): org-id picker localization
        ["SettingsAccountRedetectButton.Text"] = "Re-detect organization",
        ["OrgPickerDialogTitle"] = "Select organization",
        ["OrgPickerDialogSwitchButton"] = "Switch",
        ["OrgPickerDialogCancelButton"] = "Cancel",
        ["OrgPickerDialogNoOrgs"] = "Could not load organizations. The connection to claude.ai is broken — restart the app or sign in again.",
    };

    private static readonly Dictionary<string, string> ExpectedDeDe = new()
    {
        ["NotSignedIn.Text"] = "Nicht angemeldet",
        ["NoData.Text"] = "Keine Daten",
        ["Loading.Text"] = "Wird geladen",
        ["InactiveSessionTooltip"] = "Inaktiv seit > {0}min",
        ["LoginReloadButton.[using:Microsoft.UI.Xaml.Controls]ToolTipService.ToolTip"] = "Seite neu laden",
        ["LoginReloadButton.[using:Microsoft.UI.Xaml.Automation]AutomationProperties.Name"] = "Login-Seite neu laden",
        ["RenameSessionDialogTitle"] = "Sitzung umbenennen",
        ["RenameSessionDialogSaveButton"] = "Speichern",
        ["RenameSessionDialogCancelButton"] = "Abbrechen",
        ["RenameSessionDialogResetButton"] = "Zurücksetzen",
        ["MainViewRenameButton.[using:Microsoft.UI.Xaml.Controls]ToolTipService.ToolTip"] = "Sitzung umbenennen",
        // Phase 26 RENAME-02 Plan 03
        ["SettingsTabSessions"] = "Sitzungen",
        ["SettingsSessionsHeader.Text"] = "EIGENE SITZUNGSNAMEN",
        ["SettingsSessionsNoSessions.Text"] = "Keine Sitzungen verfügbar.",
        ["SettingsSessionsOrphanLabel.Text"] = "Sitzung nicht gefunden",
        ["Settings.Sessions.ClearButton.[using:Microsoft.UI.Xaml.Controls]ToolTipService.ToolTip"] = "Eigenen Namen entfernen",
        // Phase 27 L10N-01
        ["LastFetchJustNow"] = "gerade eben",
        ["LastFetchMinutesAgo"] = "vor {0} Minuten",
        ["LastFetchHoursAgo"] = "vor {0} Stunden",
        ["LastFetchDaysAgo"] = "vor {0} Tagen",
        ["LastFetchNever"] = "Nie",
        // Phase 27 NEXTWIN-01..03: format patterns (same values in both locales — format strings, not human-readable)
        ["NextWindowLabelDe"] = "ddd d.M. HH:mm",
        ["NextWindowLabelEn"] = "ddd HH:mm",
        // Phase 27 PRICING-01..03: pricing-service silent-failure surfacing
        ["PricingErrorInfoBar.Title"] = "Preisdaten nicht verfügbar",
        ["PricingErrorInfoBar.Message"] = "Kostendaten können ungenau sein.",
        // Phase 27 ORGID-01..05 (D-OG-06): org-id picker localization
        ["SettingsAccountRedetectButton.Text"] = "Organisation neu erkennen",
        ["OrgPickerDialogTitle"] = "Organisation auswählen",
        ["OrgPickerDialogSwitchButton"] = "Wechseln",
        ["OrgPickerDialogCancelButton"] = "Abbrechen",
        ["OrgPickerDialogNoOrgs"] = "Organisationen konnten nicht geladen werden. Die Verbindung zu claude.ai ist unterbrochen — starte die App neu oder melde dich erneut an.",
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

    [Fact]
    public void XamlUidValues_AreSingleSegment_ForLocalizerLookup()
    {
        // WinUI3Localizer 2.3.0 only resolves XAML l:Uids.Uid values that split into exactly
        // 2 segments on '.' (Library.cs:307: `if (uidSource.Split('.') is { Length: 2 } splitResult)`).
        // Single-segment Uids like "FooInfoBar" succeed because the library then derives the
        // property name from the resw key suffix (FooInfoBar.Title -> sets TitleProperty).
        // Two-segment Uids like "Foo.Bar" make the library mis-parse "Foo" as the Uid and
        // search for "BarProperty" on the target type — silently fails for InfoBar.Title etc.
        //
        // Exception: attached-property syntax "Foo.[using:NS]Class.Property" is library-handled.
        //
        // Regression guard for Phase 25 / 27: Toast.SessionVisibilityMigration and
        // MainView.PricingErrorInfoBar rendered with empty Title/Message because their
        // UIDs had 2 segments before the suffix.
        var xamlSourceDir = FindXamlSourceDir();
        // Filter out MSBuild-generated copies in obj/ and bin/ — they are stale snapshots.
        var xamlFiles = Directory
            .EnumerateFiles(xamlSourceDir, "*.xaml", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                     && !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));

        var uidPattern = new System.Text.RegularExpressions.Regex(
            @"l:Uids\.Uid\s*=\s*""([^""]+)""",
            System.Text.RegularExpressions.RegexOptions.Compiled);

        var violations = new List<string>();
        foreach (var xamlPath in xamlFiles)
        {
            var content = File.ReadAllText(xamlPath);
            foreach (System.Text.RegularExpressions.Match m in uidPattern.Matches(content))
            {
                var uid = m.Groups[1].Value;
                // Skip attached-property syntax — library handles "[using:...]" specially.
                if (uid.Contains('[')) continue;
                // Library splits on '.' and only accepts Length==2 — meaning the Uid value
                // itself should NOT contain a '.' (the trailing .Property comes from the resw key).
                if (uid.Contains('.'))
                {
                    violations.Add($"{Path.GetFileName(xamlPath)}: l:Uids.Uid=\"{uid}\" has '.' — library will mis-parse.");
                }
            }
        }

        Assert.Empty(violations);
    }

    [Fact]
    public void GetLocalizedStringCalls_UseSingleSegmentUids_ForLocalizerLookup()
    {
        // WinUI3Localizer 2.3.0 stores items keyed by the prefix BEFORE the first '.' in the resw
        // key name (LocalizerBuilder.cs:206: name.IndexOf('.') splits Uid vs DependencyPropertyName).
        // GetLocalizedString(uid) then looks up the full uid string against that prefix-keyed
        // dictionary. Multi-segment uids like "Dialog.OrgPicker.Title" never match the internal
        // dictionary key (which is just "Dialog") and silently return empty string.
        //
        // Regression guard for the entire OrgPicker / RenameSession / NextWindow / LastFetchRelative
        // family of v1.5 bugs: all rendered with empty Title/Buttons/format strings because their
        // GetLocalizedString uids had 2+ segments.
        //
        // Exception: PropertyName-suffixed uids (Foo.Bar where Bar is a DependencyProperty name)
        // are technically allowed but unusual for direct API calls — we treat any '.' as suspicious.
        var sourceDir = FindCSharpSourceDir();
        var csFiles = Directory
            .EnumerateFiles(sourceDir, "*.cs", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                     && !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));

        var callPattern = new System.Text.RegularExpressions.Regex(
            @"GetLocalizedString\s*\(\s*""([^""]+)""\s*\)",
            System.Text.RegularExpressions.RegexOptions.Compiled);

        var violations = new List<string>();
        foreach (var csPath in csFiles)
        {
            var content = File.ReadAllText(csPath);
            foreach (System.Text.RegularExpressions.Match m in callPattern.Matches(content))
            {
                var uid = m.Groups[1].Value;
                if (uid.Contains('.'))
                {
                    violations.Add($"{Path.GetFileName(csPath)}: GetLocalizedString(\"{uid}\") has '.' — library returns empty.");
                }
            }
        }

        Assert.Empty(violations);
    }

    private static string FindCSharpSourceDir()
    {
        // Walk up from test output dir to find the main app's CCInfoWindows source root.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "CCInfoWindows", "CCInfoWindows", "App.xaml.cs");
            if (File.Exists(candidate))
            {
                return Path.Combine(dir.FullName, "CCInfoWindows", "CCInfoWindows");
            }
            dir = dir.Parent;
        }
        throw new InvalidOperationException("Could not locate CCInfoWindows C# source directory from test base.");
    }

    private static string FindXamlSourceDir()
    {
        // Walk up from test output dir until we find the Views folder containing MainView.xaml.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "CCInfoWindows", "CCInfoWindows", "Views", "MainView.xaml");
            if (File.Exists(candidate))
            {
                return Path.Combine(dir.FullName, "CCInfoWindows", "CCInfoWindows");
            }
            dir = dir.Parent;
        }
        throw new InvalidOperationException("Could not locate CCInfoWindows source directory from test base.");
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
