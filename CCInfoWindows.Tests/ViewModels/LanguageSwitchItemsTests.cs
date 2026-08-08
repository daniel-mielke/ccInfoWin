using System.Text.RegularExpressions;
using CCInfoWindows.Models;
using CCInfoWindows.Services;
using CCInfoWindows.Services.Interfaces;
using CCInfoWindows.Tests.Helpers;
using CCInfoWindows.ViewModels;
using Moq;

namespace CCInfoWindows.Tests.ViewModels;

/// <summary>
/// Defect B5 (2026-08-07 UAT): a runtime language switch translated the labels but left every
/// ComboBox VALUE in the previous language ("60 Minuten" under English headings) until a restart.
/// WinUI3Localizer re-applies uids by walking the visual tree, and a closed ComboBox keeps its items
/// in a popup that was never realized, so the walk cannot reach them.
///
/// The fix has two halves and this file asserts both as far as a headless host allows:
///   - ViewModel seam: a LanguageApplied event raised only after a successful switch, plus the
///     VM-owned manual refresh label refreshed in place rather than by rebuilding the collection.
///   - View: the code-behind re-reads the captions from that signal. xUnit cannot instantiate a
///     WinUI Page, so the View half is asserted against its source — including the property that
///     matters most, that the refresh never rebuilds an item collection or reassigns SelectedIndex,
///     which would silently change the setting the user picked.
/// </summary>
public class LanguageSwitchItemsTests
{
    private static readonly int GermanLanguageIndex =
        AppSettings.SupportedLanguages.IndexOf(AppSettings.GermanLanguage);

    private static readonly int EnglishLanguageIndex =
        AppSettings.SupportedLanguages.IndexOf(AppSettings.EnglishLanguage);

    private const string SettingsViewXamlFileName = "SettingsView.xaml";
    private const string SettingsViewCodeBehindFileName = "SettingsView.xaml.cs";

    private const string SubscribesToLanguageApplied = "ViewModel.LanguageApplied += OnLanguageApplied";
    private const string UnsubscribesFromLanguageApplied = "ViewModel.LanguageApplied -= OnLanguageApplied";

    /// <summary>The in-place caption assignment; a rebuild would be Items.Clear() plus Items.Add().</summary>
    private const string RebuildsItems = "Items.Clear()";

    private const string SettingsViewModelFileName = "SettingsViewModel.cs";
    private const string RefreshCall = "RefreshOptionLabels";

    /// <summary>The dropdowns whose entries are translated and addressed by position.</summary>
    private static readonly string[] PositionalOptionCollections =
    [
        "SessionTimeoutOptions",
        "VisibilityWindowOptions",
    ];

    /// <summary>
    /// An item with no Content of its own: the code-behind is its only source of a caption, so it is
    /// exactly the kind the refresh has to reach — and one that is missed comes up BLANK, not merely
    /// stale. An item that spells out its own text (Content="Deutsch") is deliberately not
    /// translated — a language name is written in its own language — and must stay out of the scan.
    ///
    /// These items carry no uid either: a uid is applied once at creation and never re-applied to a
    /// closed ComboBox's unrealized popup items, which is the defect itself.
    /// </summary>
    private static readonly Regex LocalizedComboBoxItemPattern =
        new(@"<ComboBoxItem(?![^>]*\bContent\s*=)[^>]*/?>", RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex ElementNamePattern =
        new(@"x:Name=""([^""]+)""", RegexOptions.Compiled);

    /// <summary>Assignment, not comparison — "SelectedIndex ==" is a legitimate tab-index read.</summary>
    private static readonly Regex SelectedIndexAssignmentPattern =
        new(@"SelectedIndex\s*=(?!=)", RegexOptions.Compiled);

    private static SettingsViewModel CreateSut()
    {
        var settingsService = new Mock<ISettingsService>();
        settingsService.Setup(s => s.LoadSettings()).Returns(new AppSettings());

        var credentialService = new Mock<ICredentialService>();
        credentialService.Setup(s => s.HasValidToken()).Returns(true);

        var pricingService = new Mock<IPricingService>();
        pricingService.Setup(s => s.Source).Returns(PricingSource.Unknown);
        pricingService.Setup(s => s.LastFetch).Returns((DateTimeOffset?)null);

        var sessionNameStore = new Mock<ISessionNameStore>();
        sessionNameStore.Setup(s => s.GetKnownSessionIds()).Returns(Array.Empty<string>());

        var jsonlService = new Mock<IJsonlService>();
        jsonlService.Setup(s => s.Sessions).Returns(Array.Empty<SessionInfo>());

        return new SettingsViewModel(
            settingsService.Object,
            credentialService.Object,
            new Mock<INavigationService>().Object,
            pricingService.Object,
            new Mock<IUsageHistoryService>().Object,
            sessionNameStore.Object,
            jsonlService.Object,
            new Mock<IDispatcherQueue>().Object,
            new Mock<IClaudeApiService>().Object,
            new Mock<IUsageNotificationService>().Object,
            new Mock<IWebViewBridge>().Object);
    }

    private static string ReadSettingsViewXaml()
    {
        var candidates = Directory
            .EnumerateFiles(ProductionSourceFiles.Root, SettingsViewXamlFileName, SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(path))
            .ToList();

        return File.ReadAllText(Assert.Single(candidates));
    }

    // obj\ and bin\ hold stale MSBuild copies of the very file being scanned.
    private static bool IsBuildOutput(string path) =>
        path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
        || path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);

    /// <summary>The signal the View refreshes from — exactly once per switch, or captions flicker.</summary>
    [Fact]
    public void LanguageSwitch_WhenLocalizerSucceeds_RaisesLanguageAppliedOnce()
    {
        var vm = CreateSut();
        vm.LanguageSwitcher = _ => Task.CompletedTask;

        var raised = 0;
        vm.LanguageApplied += (_, _) => raised++;

        vm.SelectedLanguageIndex = EnglishLanguageIndex;

        Assert.Equal(1, raised);
    }

    /// <summary>
    /// A failed switch leaves the previous language on screen, so re-reading the captions would
    /// replace correct text with text from a dictionary that was never loaded.
    /// </summary>
    [Fact]
    public void LanguageSwitch_WhenLocalizerThrows_DoesNotRaiseLanguageApplied()
    {
        var vm = CreateSut();
        vm.LanguageSwitcher = _ => Task.FromException(new InvalidOperationException("RPC_E_WRONG_THREAD"));

        var raised = 0;
        vm.LanguageApplied += (_, _) => raised++;

        vm.SelectedLanguageIndex = EnglishLanguageIndex;

        Assert.Equal(0, raised);
        Assert.Equal(GermanLanguageIndex, vm.SelectedLanguageIndex);
    }

    /// <summary>
    /// The refresh-interval dropdown is fed from the ViewModel, and its manual entry is the one
    /// translated label in it. Refreshing it must not replace the item: ComboBox.SelectedItem is
    /// bound TwoWay, so a replaced instance would write null back into SelectedRefreshOption.
    /// </summary>
    [Fact]
    public void LanguageSwitch_RefreshesTheManualRefreshLabel_WithoutDisturbingTheSelection()
    {
        var vm = CreateSut();
        vm.LanguageSwitcher = _ => Task.CompletedTask;

        var optionsBefore = vm.RefreshOptions;
        var manualBefore = vm.RefreshOptions.Single(o => o.Seconds == AppSettings.ManualRefreshSeconds);
        vm.SelectedRefreshOption = manualBefore;

        vm.SelectedLanguageIndex = EnglishLanguageIndex;

        Assert.Same(optionsBefore, vm.RefreshOptions);
        Assert.Same(manualBefore, vm.RefreshOptions.Single(o => o.Seconds == AppSettings.ManualRefreshSeconds));
        Assert.Same(manualBefore, vm.SelectedRefreshOption);
        Assert.False(string.IsNullOrWhiteSpace(manualBefore.Label));
    }

    /// <summary>The label is observable, or DisplayMemberPath would never see the new text.</summary>
    [Fact]
    public void RefreshOptionLabel_RaisesPropertyChanged()
    {
        var option = CreateManualRefreshOption();
        var changed = new List<string?>();
        option.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        option.Label = "Manual";

        Assert.Contains(nameof(SettingsViewModel.RefreshOption.Label), changed);
    }

    private static SettingsViewModel.RefreshOption CreateManualRefreshOption() =>
        CreateSut().RefreshOptions.Single(o => o.Seconds == AppSettings.ManualRefreshSeconds);

    /// <summary>The View has to be listening, and must stop listening with the page (CD-05).</summary>
    [Fact]
    public void SettingsView_SubscribesAndUnsubscribesFromLanguageApplied()
    {
        var codeBehind = ProductionSourceFiles.Read(SettingsViewCodeBehindFileName);

        Assert.True(
            codeBehind.Contains(SubscribesToLanguageApplied, StringComparison.Ordinal),
            $"{SettingsViewCodeBehindFileName} must subscribe to LanguageApplied, or ComboBox captions "
            + "keep the previous language until a restart.");
        Assert.True(
            codeBehind.Contains(UnsubscribesFromLanguageApplied, StringComparison.Ordinal),
            $"{SettingsViewCodeBehindFileName} must unsubscribe on Unloaded — the ViewModel outlives "
            + "nothing here, but an asymmetric handler is how zombie pages start.");
    }

    /// <summary>
    /// The user's setting must survive the refresh. Rebuilding a ComboBox's items or assigning
    /// SelectedIndex would move the selection, which is a worse defect than the stale caption it
    /// replaced — the setting is persisted from that index.
    /// </summary>
    [Fact]
    public void CaptionRefresh_NeverMovesTheSelection()
    {
        var codeBehind = ProductionSourceFiles.Read(SettingsViewCodeBehindFileName);

        Assert.False(
            codeBehind.Contains(RebuildsItems, StringComparison.Ordinal),
            $"{SettingsViewCodeBehindFileName} must not rebuild a ComboBox's items — that resets "
            + "SelectedIndex and silently rewrites the user's setting.");
        Assert.False(
            SelectedIndexAssignmentPattern.IsMatch(codeBehind),
            $"{SettingsViewCodeBehindFileName} must not assign SelectedIndex — the selection belongs "
            + "to the ViewModel's TwoWay binding.");
    }

    /// <summary>
    /// No ComboBoxItem on this page may be declared without its own Content. A caption assigned to a
    /// declared item after the fact never reaches the CLOSED control: the selection box is cached when
    /// the selection is set, so the dropdown renders blank on a fresh page and stale after a switch —
    /// both were observed in the running app. Translated entries belong in an ItemsSource whose items
    /// raise PropertyChanged.
    /// </summary>
    [Fact]
    public void NoComboBoxItem_IsDeclaredWithoutItsOwnCaption()
    {
        var captionless = LocalizedComboBoxItemPattern
            .Matches(ReadSettingsViewXaml())
            .Select(match => match.Value.Trim())
            .ToList();

        Assert.True(
            captionless.Count == 0,
            "SettingsView.xaml declares ComboBoxItems with no Content; they render blank when closed: "
            + string.Join(", ", captionless));
    }

    /// <summary>
    /// Every positional dropdown must be refreshed by the language switch. Adding a third one and
    /// forgetting it reproduces B5 for that control, and nothing else in the suite would notice.
    /// </summary>
    [Fact]
    public void EveryPositionalDropdown_IsRefreshedByTheLanguageSwitch()
    {
        var viewModel = ProductionSourceFiles.Read(SettingsViewModelFileName);

        foreach (var collection in PositionalOptionCollections)
        {
            Assert.True(
                viewModel.Contains($"{RefreshCall}({collection},", StringComparison.Ordinal),
                $"{collection} is never passed to {RefreshCall} — its captions stay in the previous "
                + "language after a runtime switch.");
        }
    }

    /// <summary>
    /// The caption refresh must mutate the existing entries. Rebuilding the collection would drop the
    /// instance ComboBox.SelectedItem points at and silently rewrite the user's setting.
    /// </summary>
    [Fact]
    public void CaptionRefresh_MutatesEntriesRatherThanRebuildingThem()
    {
        var viewModel = ProductionSourceFiles.Read(SettingsViewModelFileName);

        Assert.Contains("].Label = Localize(", viewModel, StringComparison.Ordinal);
        Assert.DoesNotContain("SessionTimeoutOptions = ", viewModel, StringComparison.Ordinal);
        Assert.DoesNotContain("VisibilityWindowOptions = ", viewModel, StringComparison.Ordinal);
    }
}
