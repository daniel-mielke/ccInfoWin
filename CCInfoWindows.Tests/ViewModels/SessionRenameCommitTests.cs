using System.Text.RegularExpressions;
using CCInfoWindows.Models;
using CCInfoWindows.Services;
using CCInfoWindows.Services.Interfaces;
using CCInfoWindows.Tests.Convention;
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

    private static SettingsViewModel CreateSut(Mock<ISessionNameStore> sessionNameStore)
        => SettingsViewModelFactory.Create(sessionNameStore: sessionNameStore);

    private static SessionRenameItem CreateRow(string sessionId, string customName) => new()
    {
        SessionId = sessionId,
        DefaultName = "Project",
        CustomName = customName
    };

    /// <summary>What the user types has to reach the store, which is the defect in one sentence.</summary>
    [Fact]
    public async Task SaveSessionCustomName_WithATypedName_ReachesTheStore()
    {
        var store = SettingsViewModelFactory.SessionNameStore();
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
        var store = SettingsViewModelFactory.SessionNameStore();
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
        var store = SettingsViewModelFactory.SessionNameStore();
        var vm = CreateSut(store);

        await vm.SaveSessionCustomNameCommand.ExecuteAsync(CreateRow("proj-b1", "   "));

        store.Verify(s => s.ClearCustomName("proj-b1"), Times.Once);
        store.Verify(s => s.SetCustomName(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    /// <summary>The row keeps the value that was persisted, so the field does not blank itself.</summary>
    [Fact]
    public async Task SaveSessionCustomName_WithATypedName_LeavesTheRowShowingIt()
    {
        var store = SettingsViewModelFactory.SessionNameStore();
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
        var binding = CustomNameBindingPattern.Match(SourceTree.ReadSettingsViewXaml());

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
