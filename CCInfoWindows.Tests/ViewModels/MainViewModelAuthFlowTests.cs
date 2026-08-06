using CCInfoWindows.Messages;
using CCInfoWindows.Models;
using CCInfoWindows.Services.Interfaces;
using CCInfoWindows.Tests.Helpers;
using CCInfoWindows.ViewModels;
using CCInfoWindows.Views;
using Moq;

namespace CCInfoWindows.Tests.ViewModels;

/// <summary>
/// Phase 20 auth-flow routing tests (AUTH-01..AUTH-04).
/// Tests drive the real MainViewModel constructed with a full-DI mock factory.
/// Tests are RED in Plan 01 (no production routing yet); turn GREEN when Plan 02 ships
/// the _autoReauthAttempted flag + extended Receive(AuthStateChangedMessage).
/// </summary>
[Collection("WeakReferenceMessenger")]
public class MainViewModelAuthFlowTests
{
    private static (MainViewModel vm, Mock<INavigationService> nav) CreateViewModel()
    {
        var credentialService = new Mock<ICredentialService>();
        var navigationService = new Mock<INavigationService>();
        var apiService = new Mock<IClaudeApiService>();
        var settingsService = new Mock<ISettingsService>();
        settingsService.Setup(s => s.LoadSettings()).Returns(new AppSettings());
        var historyService = new Mock<IUsageHistoryService>();
        var jsonlService = new Mock<IJsonlService>();
        jsonlService.Setup(s => s.Sessions).Returns([]);
        var pricingService = new Mock<IPricingService>();
        pricingService.Setup(s => s.EnsurePricesLoadedAsync()).Returns(Task.CompletedTask);
        var updateService = new Mock<IUpdateService>();
        var burnRate = new Mock<IUsageNotificationService>();
        var sessionNameStore = new Mock<ISessionNameStore>();
        sessionNameStore.Setup(s => s.GetCustomName(It.IsAny<string>())).Returns((string?)null);

        var vm = new MainViewModel(
            credentialService.Object,
            navigationService.Object,
            apiService.Object,
            settingsService.Object,
            historyService.Object,
            jsonlService.Object,
            pricingService.Object,
            updateService.Object,
            burnRate.Object,
            new FakeDispatcherQueue(),
            sessionNameStore.Object,
            _ => null!);   // headless brushFactory seam — SolidColorBrush requires WinRT COM

        return (vm, navigationService);
    }

    [Fact]
    public void Receive_FirstFalse_NavigatesToLoginView_WithoutSettingSessionExpired()
    {
        var (vm, nav) = CreateViewModel();

        vm.Receive(new AuthStateChangedMessage(false));

        nav.Verify(n => n.NavigateTo<LoginView>(), Times.Once);
        Assert.False(vm.IsSessionExpired);
    }

    [Fact]
    public void Receive_SecondFalse_OpensInfoBar_WithoutSecondNavigation()
    {
        var (vm, nav) = CreateViewModel();

        vm.Receive(new AuthStateChangedMessage(false));   // first → auto-navigate
        vm.Receive(new AuthStateChangedMessage(false));   // second → InfoBar fallback

        nav.Verify(n => n.NavigateTo<LoginView>(), Times.Once);
        Assert.True(vm.IsSessionExpired);
    }

    [Fact]
    public void Receive_True_IsIgnored_AndNeverRoutesToLogin()
    {
        // BEHAVIOUR CHANGE (finding 37): Receive(true) no longer clears the error flags and fires a
        // refresh. That branch was unreachable in production — LoginViewModel sends the message and
        // then navigates to MainView, so the ViewModel that would have handled it did not exist yet,
        // and the one built a moment later starts with default flags and polls from InitializeAsync.
        // The guard remains so a stray `true` cannot be misread as a 401 and bounce the user out.
        var (vm, nav) = CreateViewModel();

        vm.Receive(new AuthStateChangedMessage(true));

        nav.Verify(n => n.NavigateTo<LoginView>(), Times.Never);
        Assert.False(vm.IsSessionExpired);
    }

    [Fact]
    public void Receive_True_LeavesTheAutoReauthBudgetUntouched()
    {
        var (vm, nav) = CreateViewModel();

        vm.Receive(new AuthStateChangedMessage(false));   // arms _autoReauthAttempted → 1st nav
        vm.Receive(new AuthStateChangedMessage(true));    // ignored
        vm.Receive(new AuthStateChangedMessage(false));   // second 401 → InfoBar, no further nav

        nav.Verify(n => n.NavigateTo<LoginView>(), Times.Once);
        Assert.True(vm.IsSessionExpired);
    }

    [Fact]
    public void MainViewModel_ExposesNoLogoutCommand()
    {
        // REPLACES Logout_ResetsAutoReauthFlag_NextFalseNavigatesAgain (finding 18). The command that
        // test drove was bound in no XAML file: the only reachable logout is SettingsViewModel.Logout.
        // Keeping the duplicate meant a maintainer could fix a logout bug here, watch this file go
        // green, and ship a change no user could reach — so the guard is now the absence of the
        // command. If a logout belongs on the dashboard, bind it in MainView.xaml and delete this test.
        Assert.Null(typeof(MainViewModel).GetProperty("LogoutCommand"));

        // The flag reset that test asserted is unnecessary: logout navigates away, and the dashboard
        // that comes back is a new transient ViewModel whose _autoReauthAttempted starts false.
        var (vm, nav) = CreateViewModel();

        vm.Receive(new AuthStateChangedMessage(false));

        nav.Verify(n => n.NavigateTo<LoginView>(), Times.Once);
    }
}
