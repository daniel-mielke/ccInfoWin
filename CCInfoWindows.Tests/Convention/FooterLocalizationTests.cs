using System.Text.RegularExpressions;
using System.Xml.Linq;
using CCInfoWindows.Tests.Helpers;

namespace CCInfoWindows.Tests.Convention;

/// <summary>
/// Defect B4: MainView's icon-only buttons carried hardcoded English <c>ToolTipService.ToolTip</c>
/// attributes that outranked the resw values, so a German build showed a tooltip reading "Settings"
/// under the label "Kosten (API-Äqu.)". The same buttons announced nothing to a screen reader.
///
/// Deleting the literals alone made it worse — the buttons then had NO tooltip at all, verified by
/// hovering the running app. WinUI3Localizer never applies the
/// "Control.[using:Namespace]Class.Property" attached-property form of a resw key: those entries pass
/// the resolvability rule in <see cref="Localization.ResourceCoverageTests"/> yet reach no control.
/// Plain-text uids on the same page (section headers) work, which is what made the gap invisible.
///
/// So these buttons are labelled in code from single-segment keys, the way SettingsView already
/// labels its tab strip. The scans below pin all three halves: no literal in the markup, a key in
/// both locales, and a call site that actually applies it.
/// </summary>
public class FooterLocalizationTests
{
    private const string MainViewRelativePath = "Views/MainView.xaml";
    private const string MainViewCodeBehindRelativePath = "Views/MainView.xaml.cs";
    private const string EnUsRelativePath = "Strings/en-US/Resources.resw";
    private const string DeDeRelativePath = "Strings/de-DE/Resources.resw";

    /// <summary>
    /// MainView's icon-only buttons: the x:Name the code-behind labels, and the resw key it reads.
    /// A control that renders a glyph and no text has no other source for either affordance.
    /// </summary>
    private static readonly (string ElementName, string ResourceKey)[] IconOnlyButtons =
    [
        ("RenameSessionButton", "MainViewRenameLabel"),
        ("ExportChartButton", "MainViewExportLabel"),
        ("FooterRefreshButton", "MainViewRefreshLabel"),
        ("FooterSettingsButton", "MainViewSettingsLabel"),
        ("FooterQuitButton", "MainViewQuitLabel"),
    ];

    /// <summary>Captures the attribute value so a markup binding can be told apart from a literal.</summary>
    private static readonly Regex ToolTipAttributePattern =
        new(@"ToolTipService\.ToolTip\s*=\s*""([^""]*)""", RegexOptions.Compiled);

    [Fact]
    public void MainView_DeclaresNoHardcodedToolTipLiteral()
    {
        // A literal here wins over the localized value and freezes one language into the markup. Markup
        // bindings ("{x:Bind TooltipText}") are fine — their text comes from a localized ViewModel.
        var literals = ToolTipAttributePattern
            .Matches(ReadMainViewMarkup())
            .Select(match => match.Groups[1].Value)
            .Where(value => !value.TrimStart().StartsWith('{'))
            .Order()
            .ToList();

        Assert.True(
            literals.Count == 0,
            "MainView.xaml hardcodes tooltip text that the localizer cannot translate: "
            + string.Join(", ", literals.Select(value => $"\"{value}\"")));
    }

    [Fact]
    public void EveryIconOnlyButton_IsNamedInTheMarkup()
    {
        // Guards the two scans below against passing vacuously: a renamed element would leave the resw
        // side green while the button in the app silently loses its name again.
        var markup = ReadMainViewMarkup();

        var missing = IconOnlyButtons
            .Where(button => !markup.Contains($"x:Name=\"{button.ElementName}\"", StringComparison.Ordinal))
            .Select(button => button.ElementName)
            .Order()
            .ToList();

        Assert.True(missing.Count == 0, "MainView.xaml no longer names these buttons: " + string.Join(", ", missing));
    }

    [Fact]
    public void EveryIconOnlyButton_IsLabelledByTheCodeBehind()
    {
        // The keys resolve and the buttons exist, but nothing connects them unless this call is made —
        // exactly the state that shipped a tooltip-less footer.
        var codeBehind = ReadMainViewCodeBehind();

        var unlabelled = IconOnlyButtons
            .Where(button => !codeBehind.Contains(
                $"SetIconButtonLabel({button.ElementName}, \"{button.ResourceKey}\"", StringComparison.Ordinal))
            .Select(button => $"{button.ElementName} -> {button.ResourceKey}")
            .Order()
            .ToList();

        Assert.True(
            unlabelled.Count == 0,
            "MainView.xaml.cs never applies a label to: " + string.Join(", ", unlabelled));

        Assert.Contains("ApplyIconButtonLabels();", codeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryIconOnlyButtonKey_IsASingleSegmentKey_InBothLocales()
    {
        foreach (var (locale, path) in Locales())
        {
            var keyToValue = LoadResw(path);

            foreach (var (elementName, resourceKey) in IconOnlyButtons)
            {
                // A dot would send the key back through the attached-property path that reaches nothing.
                Assert.False(
                    resourceKey.Contains('.', StringComparison.Ordinal),
                    $"'{resourceKey}' ({elementName}) must be a single-segment key.");

                Assert.True(
                    keyToValue.ContainsKey(resourceKey),
                    $"{locale} Resources.resw is missing key '{resourceKey}'.");
                Assert.False(
                    string.IsNullOrWhiteSpace(keyToValue[resourceKey]),
                    $"{locale} key '{resourceKey}' has an empty value.");
            }
        }
    }

    [Fact]
    public void TheDeadAttachedPropertyEntries_AreGone()
    {
        // They resolved in the resource test and reached no control, which is what made the defect
        // survive a review: the evidence pointed at markup that looked correct.
        foreach (var (locale, path) in Locales())
        {
            var stale = LoadResw(path).Keys
                .Where(key => IconOnlyButtons.Any(
                    button => key.StartsWith(button.ElementName + ".[using:", StringComparison.Ordinal)))
                .Order()
                .ToList();

            Assert.True(
                stale.Count == 0,
                $"{locale} still carries attached-property keys the localizer never applies: "
                + string.Join(", ", stale));
        }
    }

    private static string ReadMainViewMarkup() =>
        File.ReadAllText(Path.Combine(ProductionSourceFiles.Root, MainViewRelativePath));

    private static string ReadMainViewCodeBehind() =>
        File.ReadAllText(Path.Combine(ProductionSourceFiles.Root, MainViewCodeBehindRelativePath));

    private static IEnumerable<(string Locale, string Path)> Locales()
    {
        yield return ("en-US", EnUsRelativePath);
        yield return ("de-DE", DeDeRelativePath);
    }

    private static Dictionary<string, string> LoadResw(string relativePath)
    {
        var fullPath = Path.Combine(AppContext.BaseDirectory, relativePath);
        Assert.True(File.Exists(fullPath), $"Resw file not found at: {fullPath}");

        var doc = XDocument.Load(fullPath);
        var dataElements = doc.Root?.Elements("data") ?? Enumerable.Empty<XElement>();

        var result = new Dictionary<string, string>(StringComparer.Ordinal);
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
}
