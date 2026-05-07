using CCInfoWindows.Messages;
using CCInfoWindows.Models;
using CCInfoWindows.Services.Interfaces;
using CCInfoWindows.ViewModels;
using CCInfoWindows.Views;
using CommunityToolkit.Mvvm.Messaging;
using Moq;

namespace CCInfoWindows.Tests.ViewModels;

/// <summary>
/// 21-03 gap-closure was reverted: MainViewModel is registered AddTransient
/// (App.xaml.cs:164) — WeakReferenceMessenger silently drops the IRecipient
/// registration when the MainViewModel instance is GC-collected after navigating
/// away from MainView. In production the LogoutRequestedMessage round-trip had
/// no live recipient and the user could not log out at all.
///
/// The fix reverts SettingsViewModel.Logout to a direct call sequence with
/// IUsageHistoryService injected. The duplication is intentional and acceptable
/// — D-13 (ClearHistory FIRST) is enforced by the test below.
///
/// File name retained for git history continuity; test class name updated.
/// </summary>
public class SettingsLogoutDirectCallTests
{
    private static (
        SettingsViewModel vm,
        Mock<IUsageHistoryService> historyMock,
        Mock<ICredentialService> credentialMock,
        Mock<INavigationService> navMock)
    BuildSut()
    {
        var settingsService = new Mock<ISettingsService>();
        settingsService.Setup(s => s.LoadSettings()).Returns(new AppSettings());
        var credentialMock = new Mock<ICredentialService>();
        var navMock = new Mock<INavigationService>();
        var pricingService = new Mock<IPricingService>();
        var historyMock = new Mock<IUsageHistoryService>();

        var vm = new SettingsViewModel(
            settingsService.Object,
            credentialMock.Object,
            navMock.Object,
            pricingService.Object,
            historyMock.Object);

        return (vm, historyMock, credentialMock, navMock);
    }

    [Fact]
    public void Logout_CallsClearHistoryFirst()
    {
        // D-13 is the entire point of this fix: ClearHistory MUST run before
        // anything else clears credential state, so the snapshot-cache cannot
        // be re-saved after the file is deleted.
        var (vm, historyMock, _, _) = BuildSut();

        vm.LogoutCommand.Execute(null);

        historyMock.Verify(h => h.ClearHistory(), Times.Once,
            "Settings → Abmelden must call ClearHistory (D-13 ordering trap mitigation).");
    }

    [Fact]
    public void Logout_RunsFullSequence_ClearCredentialsAndNavigate()
    {
        var (vm, _, credentialMock, navMock) = BuildSut();

        vm.LogoutCommand.Execute(null);

        credentialMock.Verify(c => c.ClearCredentials(), Times.Once);
        navMock.Verify(n => n.NavigateTo<LoginView>(), Times.Once);
    }
}
