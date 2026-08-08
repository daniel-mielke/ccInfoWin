using System.Text.RegularExpressions;
using CCInfoWindows.Models;
using CCInfoWindows.Services;
using CCInfoWindows.Services.Interfaces;
using CCInfoWindows.Tests.Helpers;
using CCInfoWindows.ViewModels;
using Moq;

namespace CCInfoWindows.Tests.ViewModels;

/// <summary>
/// Defect B1 (2026-08-07 UAT): renaming a session in Settings &gt; Sessions was impossible. The row's
/// TextBox bound <c>Text="{x:Bind CustomName, Mode=TwoWay}"</c>, and x:Bind writes TextBox.Text back
/// on LostFocus — after the LostFocus/Enter handlers have already run the save command. Every commit
/// therefore read the previous (empty) CustomName, took the clear branch, and blanked the field.
///
/// The command's branch selection is asserted here, and the binding that feeds it is asserted against
/// the XAML source: nothing observable at runtime distinguishes the two triggers headlessly, and the
/// missing UpdateSourceTrigger is exactly what shipped.
/// </summary>
public class SessionRenameCommitTests
{
    private const string SettingsViewXamlFileName = "SettingsView.xaml";
    private const string SettingsViewCodeBehindFileName = "SettingsView.xaml.cs";

    /// <summary>The binding whose write-back timing the whole rename flow depends on.</summary>
    private static readonly Regex CustomNameBindingPattern =
        new(@"\{x:Bind\s+CustomName[^}]*\}", RegexOptions.Compiled);

    private const string ImmediateWriteBack = "UpdateSourceTrigger=PropertyChanged";

    /// <summary>Enter's one-shot echo guard, by the two lines that make it work.</summary>
    private const string EnterCommitRecordsTheRow = "_rowCommittedByEnter = item;";
    private const string LostFocusConsultsTheRecord = "ReferenceEquals(item, _rowCommittedByEnter)";

    private static Mock<ISessionNameStore> CreateSessionNameStore()
    {
        var store = new Mock<ISessionNameStore>();
        store.Setup(s => s.GetKnownSessionIds()).Returns(Array.Empty<string>());
        store.Setup(s => s.SaveAsync(default)).ReturnsAsync(true);
        return store;
    }

    private static SettingsViewModel CreateSut(Mock<ISessionNameStore> sessionNameStore)
    {
        var settingsService = new Mock<ISettingsService>();
        settingsService.Setup(s => s.LoadSettings()).Returns(new AppSettings());

        var credentialService = new Mock<ICredentialService>();
        credentialService.Setup(s => s.HasValidToken()).Returns(true);

        var pricingService = new Mock<IPricingService>();
        pricingService.Setup(s => s.Source).Returns(PricingSource.Unknown);
        pricingService.Setup(s => s.LastFetch).Returns((DateTimeOffset?)null);

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

    private static SessionRenameItem CreateRow(string sessionId, string customName) => new()
    {
        SessionId = sessionId,
        DefaultName = "Project",
        CustomName = customName
    };

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

    /// <summary>What the user types has to reach the store, which is the defect in one sentence.</summary>
    [Fact]
    public async Task SaveSessionCustomName_WithATypedName_ReachesTheStore()
    {
        var store = CreateSessionNameStore();
        var vm = CreateSut(store);

        await vm.SaveSessionCustomNameCommand.ExecuteAsync(CreateRow("proj-b1", "Release branch"));

        store.Verify(s => s.SetCustomName("proj-b1", "Release branch"), Times.Once);
        store.Verify(s => s.ClearCustomName(It.IsAny<string>()), Times.Never);
        store.Verify(s => s.SaveAsync(default), Times.Once);
    }

    /// <summary>Emptying the box stays the affordance for dropping a custom name.</summary>
    [Fact]
    public async Task SaveSessionCustomName_WithAnEmptyBox_ClearsInsteadOfSetting()
    {
        var store = CreateSessionNameStore();
        var vm = CreateSut(store);

        await vm.SaveSessionCustomNameCommand.ExecuteAsync(CreateRow("proj-b1", string.Empty));

        store.Verify(s => s.ClearCustomName("proj-b1"), Times.Once);
        store.Verify(s => s.SetCustomName(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        store.Verify(s => s.SaveAsync(default), Times.Once);
    }

    /// <summary>Whitespace is not a name — it trims to empty and takes the same clear branch.</summary>
    [Fact]
    public async Task SaveSessionCustomName_WithWhitespaceOnly_ClearsInsteadOfSetting()
    {
        var store = CreateSessionNameStore();
        var vm = CreateSut(store);

        await vm.SaveSessionCustomNameCommand.ExecuteAsync(CreateRow("proj-b1", "   "));

        store.Verify(s => s.ClearCustomName("proj-b1"), Times.Once);
        store.Verify(s => s.SetCustomName(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    /// <summary>The row keeps the value that was persisted, so the field does not blank itself.</summary>
    [Fact]
    public async Task SaveSessionCustomName_WithATypedName_LeavesTheRowShowingIt()
    {
        var store = CreateSessionNameStore();
        var vm = CreateSut(store);
        var row = CreateRow("proj-b1", "Release branch");

        await vm.SaveSessionCustomNameCommand.ExecuteAsync(row);

        Assert.Equal("Release branch", row.CustomName);
        Assert.False(vm.IsErrorVisible);
    }

    /// <summary>
    /// The source-level half of B1: without UpdateSourceTrigger the command above is fed a stale
    /// value, and no headless assertion about the ViewModel can see that.
    /// </summary>
    [Fact]
    public void SessionRenameTextBox_WritesItsValueBackOnEveryKeystroke()
    {
        var binding = CustomNameBindingPattern.Match(ReadSettingsViewXaml());

        Assert.True(binding.Success, $"{SettingsViewXamlFileName} no longer binds a session row's CustomName.");
        Assert.True(
            binding.Value.Contains(ImmediateWriteBack, StringComparison.Ordinal),
            $"The CustomName binding must carry {ImmediateWriteBack}: x:Bind writes TextBox.Text back on "
            + $"LostFocus by default, i.e. after the commit handlers have read it. Found: {binding.Value}");
    }

    /// <summary>
    /// Enter commits and then disables the TextBox to move focus, which makes WinUI raise LostFocus
    /// for the same row. Without the one-shot record the commit runs twice, the second time against
    /// whatever the row holds after the store rebuilt the snapshot.
    /// </summary>
    [Fact]
    public void EnterCommit_IsNotRepeatedByTheFocusEchoItCauses()
    {
        var codeBehind = ProductionSourceFiles.Read(SettingsViewCodeBehindFileName);

        Assert.True(
            codeBehind.Contains(EnterCommitRecordsTheRow, StringComparison.Ordinal),
            $"The Enter handler must record the committed row ({EnterCommitRecordsTheRow}).");
        Assert.True(
            codeBehind.Contains(LostFocusConsultsTheRecord, StringComparison.Ordinal),
            $"The LostFocus handler must skip the row Enter just committed ({LostFocusConsultsTheRecord}).");
    }
}
