using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using CCInfoWindows.Helpers;
using CCInfoWindows.ViewModels;

namespace CCInfoWindows.Tests.Localization;

/// <summary>
/// Structural validation of the two resw locales. Deliberately does NOT duplicate the
/// translated values — that made every new key a double-edit in two dictionaries and
/// threw KeyNotFoundException instead of a useful message when one was forgotten.
///
/// What is asserted instead:
///   - RequiredKeys (keys with a hard contract elsewhere in the code) exist in both locales
///   - EN and DE expose the identical key set — a missing translation is a test failure
///   - No value is empty
///   - Placeholder arity per key is identical across locales ("vor {0} Minuten" vs "{0} minutes ago")
///   - No duplicate &lt;data name&gt; entries
///   - Every l:Uids.Uid and GetLocalizedString() argument is single-segment
///   - Every &lt;data name&gt; is itself resolvable by the localizer's key-splitting rule
///
/// Strategy: XDocument-based (per RESEARCH Pitfall 1 — xUnit cannot initialize the
/// WinUI3Localizer host, so we read the resw files directly).
///
/// IMPORTANT: WinUI3Localizer 2.3.0 only resolves Foo.Property keys (Length==2 split on '.').
/// Three-segment keys like "MainView.Foo.Title" are silently dropped — controls render
/// with null Title/Message. See the two scanner tests below for enforcement.
/// </summary>
public class ResourceCoverageTests
{
    private const string EnUsRelativePath = "Strings/en-US/Resources.resw";
    private const string DeDeRelativePath = "Strings/de-DE/Resources.resw";

    /// <summary>
    /// Keys that C# code or XAML looks up by name. A missing one is a silent empty string
    /// at runtime, so presence is asserted explicitly rather than left to key-set symmetry.
    /// </summary>
    private static readonly string[] RequiredKeys =
    [
        "InactiveSessionTooltip",
        "LoginReloadButton.[using:Microsoft.UI.Xaml.Controls]ToolTipService.ToolTip",
        "LoginReloadButton.[using:Microsoft.UI.Xaml.Automation]AutomationProperties.Name",
        // Phase 26 RENAME-01: session rename dialog + pencil button tooltip
        "RenameSessionDialogTitle",
        "RenameSessionDialogSaveButton",
        "RenameSessionDialogCancelButton",
        "RenameSessionDialogResetButton",
        "MainViewRenameButton.[using:Microsoft.UI.Xaml.Controls]ToolTipService.ToolTip",
        // Review remediation 2026-08-06: failure messages read by key from code, so a missing
        // entry degrades silently to the English fallback baked into the call site.
        "SessionsClearNameTooltip",
        "SettingsSessionNameSaveFailed",
        "SettingsLanguageChangeFailed",
        "DashboardStartupFailedMessage",
        MainViewModel.ChartExportFailedUid,
        // Finding 34: the dashboard's API-error banner had a hardcoded English Title in MainView.xaml.
        "ApiErrorInfoBar.Title",
        // Phase 26 RENAME-02: Settings Sessions tab content (Plan 03)
        "SettingsTabSessions",
        "SettingsSessionsHeader.Text",
        "SettingsSessionsNoSessions.Text",
        "SettingsSessionsOrphanLabel.Text",
        "SessionsClearNameTooltip",
        // Phase 27 L10N-01: localized last-fetch relative time on About tab
        "LastFetchJustNow",
        "LastFetchMinutesAgo",
        "LastFetchHoursAgo",
        "LastFetchDaysAgo",
        "LastFetchNever",
        // Phase 27 NEXTWIN-01..03: absolute next-window start label (D-NW-03 / CD-01)
        MainViewModel.NextWindowPatternUid,
        // Phase 27 PRICING-01..03: pricing-service silent-failure surfacing
        "PricingErrorInfoBar.Title",
        "PricingErrorInfoBar.Message",
        // Phase 27 ORGID-01..05 (D-OG-06): org-id picker localization
        "SettingsAccountRedetectButton.Text",
        "OrgPickerDialogTitle",
        "OrgPickerDialogSwitchButton",
        "OrgPickerDialogCancelButton",
        "OrgPickerDialogNoOrgs",
        // v1.6 Phase 4: threshold + window-reset notifications (all single-segment)
        "WindowThresholdNotificationTitle",
        "FiveHourThresholdNotificationBody",
        "WeeklyThresholdNotificationBody",
        "WindowResetNotificationTitle",
        "FiveHourResetNotificationBody",
        "WeeklyResetNotificationBody",
        // Finding 21: strings that used to be German literals in C#. SettingsViewModel reads the
        // first three, CountdownFormatter the pattern.
        "PricingSourceFallback",
        "PricingSourceUnknown",
        "RefreshIntervalManual",
        CountdownFormatter.ResetDatePatternUid,
        // Finding 21: ExportHelper paints these two into the PNG, resolved by Uid (the text before
        // the first '.'), so both the entry and its .Text suffix have to stay.
        "SectionHeaderFiveHour.Text",
        "ResetInLabel.Text",
    ];

    /// <summary>
    /// Resw entries holding a .NET custom date/time format string rather than display text. A typo
    /// here renders a date nobody can read, or throws FormatException into a UI update.
    /// </summary>
    private static readonly string[] DatePatternKeys =
    [
        CountdownFormatter.ResetDatePatternUid,
        MainViewModel.NextWindowPatternUid,
    ];

    private static readonly Regex PlaceholderPattern = new(@"\{(\d+)\}", RegexOptions.Compiled);

    [Fact]
    public void RequiredKeys_ExistInBothLocales_WithNonEmptyValues()
    {
        foreach (var (locale, path) in Locales())
        {
            var keyToValue = LoadResw(path);
            foreach (var key in RequiredKeys)
            {
                Assert.True(keyToValue.ContainsKey(key), $"{locale} Resources.resw is missing key '{key}'.");
                Assert.False(string.IsNullOrWhiteSpace(keyToValue[key]), $"{locale} key '{key}' has an empty value.");
            }
        }
    }

    [Fact]
    public void EnUs_And_DeDe_ExposeIdenticalKeySets()
    {
        var enKeys = LoadResw(EnUsRelativePath).Keys.ToHashSet();
        var deKeys = LoadResw(DeDeRelativePath).Keys.ToHashSet();

        var missingInDe = enKeys.Except(deKeys).OrderBy(k => k).ToList();
        var missingInEn = deKeys.Except(enKeys).OrderBy(k => k).ToList();

        Assert.True(
            missingInDe.Count == 0 && missingInEn.Count == 0,
            $"Locale key sets diverge. Missing in de-DE: [{string.Join(", ", missingInDe)}]. " +
            $"Missing in en-US: [{string.Join(", ", missingInEn)}].");
    }

    [Fact]
    public void AllValues_AreNonEmpty()
    {
        foreach (var (locale, path) in Locales())
        {
            var empty = LoadResw(path)
                .Where(kv => string.IsNullOrWhiteSpace(kv.Value))
                .Select(kv => kv.Key)
                .OrderBy(k => k)
                .ToList();

            Assert.True(empty.Count == 0, $"{locale} has empty values for: [{string.Join(", ", empty)}].");
        }
    }

    [Fact]
    public void PlaceholderArity_MatchesAcrossLocales()
    {
        // A translation that drops or invents a {0} makes string.Format throw or render wrong.
        var en = LoadResw(EnUsRelativePath);
        var de = LoadResw(DeDeRelativePath);

        var mismatches = new List<string>();
        foreach (var (key, enValue) in en)
        {
            if (!de.TryGetValue(key, out var deValue)) continue; // covered by the key-set test

            var enSlots = PlaceholderIndices(enValue);
            var deSlots = PlaceholderIndices(deValue);
            if (!enSlots.SetEquals(deSlots))
            {
                mismatches.Add($"'{key}': en-US uses {{{string.Join(",", enSlots.Order())}}}, " +
                               $"de-DE uses {{{string.Join(",", deSlots.Order())}}}");
            }
        }

        Assert.True(mismatches.Count == 0, string.Join(Environment.NewLine, mismatches));
    }

    [Fact]
    public void InactiveSessionTooltip_ContainsSinglePositionalPlaceholderAndNoNewline()
    {
        // D-05: single {0} placeholder — Phase 22's string.Format substitutes the threshold integer.
        // D-07: no \n in the resw value — Phase 22 owns the multi-line composition (path + "\n" + threshold).
        foreach (var (locale, path) in Locales())
        {
            var template = LoadResw(path)["InactiveSessionTooltip"];

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

        var uidPattern = new Regex(@"l:Uids\.Uid\s*=\s*""([^""]+)""", RegexOptions.Compiled);

        var violations = new List<string>();
        foreach (var xamlPath in xamlFiles)
        {
            var content = File.ReadAllText(xamlPath);
            foreach (Match m in uidPattern.Matches(content))
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

        var callPattern = new Regex(@"GetLocalizedString\s*\(\s*""([^""]+)""\s*\)", RegexOptions.Compiled);

        var violations = new List<string>();
        foreach (var csPath in csFiles)
        {
            var content = File.ReadAllText(csPath);
            foreach (Match m in callPattern.Matches(content))
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

    [Fact]
    public void ReswKeyNames_AreResolvableByTheLocalizersSplittingRule()
    {
        // LocalizerBuilder.CreateLanguageDictionaryItem splits a <data name> at the FIRST '.':
        // the part before it becomes the Uid the dictionary is keyed on, everything after it becomes
        // "<rest>Property". A second '.' outside the [using:] attached-property syntax therefore asks
        // for a dependency property that does not exist and the entry renders as nothing.
        //
        // This closes the hole the GetLocalizedString scanner structurally cannot see: keys reached
        // through a variable (ToastRequest.TitleKey/BodyKey, MainViewModel's formatKey) never appear
        // as a literal in source, so only the resw side can be checked. Regression guard for
        // "Settings.Sessions.ClearButton.[using:...]", which parsed as Uid "Settings".
        foreach (var (locale, path) in Locales())
        {
            var unresolvable = LoadResw(path).Keys
                .Where(name => !IsLocalizerResolvable(name))
                .OrderBy(k => k)
                .ToList();

            Assert.True(
                unresolvable.Count == 0,
                $"{locale} has keys the localizer cannot resolve: [{string.Join(", ", unresolvable)}].");
        }
    }

    [Fact]
    public void DatePatternKeys_AreValidCustomFormatStringsInBothLocales()
    {
        var reference = new DateTime(2026, 2, 27, 10, 0, 0, DateTimeKind.Unspecified);

        foreach (var (locale, path) in Locales())
        {
            var keyToValue = LoadResw(path);
            var culture = new CultureInfo(locale);

            foreach (var key in DatePatternKeys)
            {
                var pattern = keyToValue[key];
                var rendered = reference.ToString(pattern, culture);

                // Unrecognised characters are copied through verbatim rather than rejected, so an
                // output still equal to the pattern means nothing was interpreted as a date at all.
                Assert.NotEqual(pattern, rendered);
                Assert.Contains("10:00", rendered);
            }
        }
    }

    [Fact]
    public void WeeklyResetDatePattern_OrdersItsFieldsPerLocale()
    {
        // Finding 21: CountdownFormatter hardcoded new CultureInfo("de-DE") plus "ddd dd.MM., HH:mm",
        // so an English user read the weekly reset "Mi. 06.08., 10:00" as June 8th.
        var reference = new DateTime(2026, 2, 27, 10, 0, 0, DateTimeKind.Unspecified);
        var key = CountdownFormatter.ResetDatePatternUid;

        var en = reference.ToString(LoadResw(EnUsRelativePath)[key], new CultureInfo("en-US"));
        var de = reference.ToString(LoadResw(DeDeRelativePath)[key], new CultureInfo("de-DE"));

        Assert.DoesNotContain("27.02.", en);
        Assert.Contains("Feb", en);
        Assert.Contains("27.02.", de);
    }

    [Fact]
    public void NextWindowLabelPattern_IsPerLocale_WithNoGermanFieldOrderForEnglish()
    {
        // Localisation follow-up: MainViewModel used to pick between NextWindowLabelDe and
        // NextWindowLabelEn with `culture.Name.StartsWith("de")`, so every third language would have
        // silently rendered English's layout. One key per locale removes the branch — and the en-US
        // value must not render the German day-first order.
        var reference = new DateTime(2026, 2, 27, 10, 0, 0, DateTimeKind.Unspecified);
        var key = MainViewModel.NextWindowPatternUid;

        var en = reference.ToString(LoadResw(EnUsRelativePath)[key], new CultureInfo("en-US"));
        var de = reference.ToString(LoadResw(DeDeRelativePath)[key], new CultureInfo("de-DE"));

        Assert.DoesNotContain("27.2.", en);
        Assert.DoesNotContain("27.02.", en);
        Assert.Contains("27.2.", de);
    }

    /// <summary>
    /// Mirrors LocalizerBuilder.CreateLanguageDictionaryItem: a name without '.' is looked up whole
    /// by GetLocalizedString; otherwise the remainder after the first '.' must be a single dependency
    /// property name, or the "[using:Namespace]Class.Property" attached-property form.
    /// </summary>
    private static bool IsLocalizerResolvable(string keyName)
    {
        var firstDot = keyName.IndexOf('.');
        if (firstDot < 0) return true;

        var dependencyPropertyPath = keyName[(firstDot + 1)..];

        return dependencyPropertyPath.StartsWith("[using:", StringComparison.Ordinal)
            || !dependencyPropertyPath.Contains('.');
    }

    private static IEnumerable<(string Locale, string Path)> Locales()
    {
        yield return ("en-US", EnUsRelativePath);
        yield return ("de-DE", DeDeRelativePath);
    }

    private static HashSet<int> PlaceholderIndices(string value) =>
        PlaceholderPattern.Matches(value).Select(m => int.Parse(m.Groups[1].Value)).ToHashSet();

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
        Assert.True(duplicates.Count == 0, $"{locale} has duplicate keys: [{string.Join(", ", duplicates)}].");
    }
}
