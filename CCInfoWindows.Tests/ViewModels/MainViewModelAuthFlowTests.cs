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
        var bridge = new Mock<IWebViewBridge>();
        var burnRate = new Mock<IBurnRateNotificationService>();

        var vm = new MainViewModel(
            credentialService.Object,
            navigationService.Object,
            apiService.Object,
            settingsService.Object,
            historyService.Object,
            jsonlService.Object,
            pricingService.Object,
            updateService.Object,
            bridge.Object,
            burnRate.Object,
            new FakeDispatcherQueue());

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
    public void Receive_True_ClearsFlagsAndResetsAutoReauth()
    {
        // Receive(true) calls RefreshCommand.ExecuteAsync(null) fire-and-forget. Without a
        // valid FetchUsageAsync mock the empty-data branch in PollUsageAsync re-flips
        // HasApiError=true on the same sync stack. Provide a non-null UsageResponse so the
        // refresh succeeds silently.
        var (vm, nav) = CreateViewModelWithSuccessfulApi();

        vm.Receive(new AuthStateChangedMessage(false));   // arms _autoReauthAttempted
        vm.Receive(new AuthStateChangedMessage(true));    // post-login refresh path

        Assert.False(vm.IsSessionExpired);
        Assert.False(vm.HasApiError);

        // Flag must be cleared — next 401 routes to LoginView again (not InfoBar)
        vm.Receive(new AuthStateChangedMessage(false));

        nav.Verify(n => n.NavigateTo<LoginView>(), Times.Exactly(2));
    }

    private static (MainViewModel vm, Mock<INavigationService> nav) CreateViewModelWithSuccessfulApi()
    {
        var credentialService = new Mock<ICredentialService>();
        var navigationService = new Mock<INavigationService>();
        var apiService = new Mock<IClaudeApiService>();
        apiService
            .Setup(a => a.FetchUsageAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UsageResponse());
        var settingsService = new Mock<ISettingsService>();
        settingsService.Setup(s => s.LoadSettings()).Returns(new AppSettings());
        var historyService = new Mock<IUsageHistoryService>();
        var jsonlService = new Mock<IJsonlService>();
        jsonlService.Setup(s => s.Sessions).Returns([]);
        var pricingService = new Mock<IPricingService>();
        pricingService.Setup(s => s.EnsurePricesLoadedAsync()).Returns(Task.CompletedTask);
        var updateService = new Mock<IUpdateService>();
        var bridge = new Mock<IWebViewBridge>();
        var burnRate = new Mock<IBurnRateNotificationService>();

        var vm = new MainViewModel(
            credentialService.Object,
            navigationService.Object,
            apiService.Object,
            settingsService.Object,
            historyService.Object,
            jsonlService.Object,
            pricingService.Object,
            updateService.Object,
            bridge.Object,
            burnRate.Object,
            new FakeDispatcherQueue());

        return (vm, navigationService);
    }

    [Fact]
    public void Logout_ResetsAutoReauthFlag_NextFalseNavigatesAgain()
    {
        var (vm, nav) = CreateViewModel();

        vm.Receive(new AuthStateChangedMessage(false));   // 1st nav (auto-reauth)
        vm.LogoutCommand.Execute(null);                   // 2nd nav (Logout itself)

        vm.Receive(new AuthStateChangedMessage(false));   // 3rd nav (flag was reset)

        nav.Verify(n => n.NavigateTo<LoginView>(), Times.Exactly(3));
        Assert.False(vm.IsSessionExpired);
    }
}
