using CCInfoWindows.Messages;
using CCInfoWindows.Models;
using CCInfoWindows.Services.Interfaces;
using CCInfoWindows.ViewModels;
using CCInfoWindows.Views;
using CommunityToolkit.Mvvm.Messaging;
using Moq;

namespace CCInfoWindows.Tests.ViewModels;

/// <summary>
/// 21-03 gap-closure tests (UAT Test 2): the Settings → Abmelden button must trigger
/// the FULL logout sequence — including IUsageHistoryService.ClearHistory() per D-13 —
/// via the new LogoutRequestedMessage round-trip. Locks the "single source of truth"
/// invariant against future drift.
/// </summary>
[Collection("WeakReferenceMessenger")]
public class SettingsLogoutMessageRoundtripTests
{
    private static (
        SettingsViewModel settingsVm,
        MainViewModel mainVm,
        Mock<IUsageHistoryService> historyMock,
        Mock<ICredentialService> mainCredentialMock,
        Mock<INavigationService> settingsNavMock,
        Mock<INavigationService> mainNavMock)
    BuildBothViewModels()
    {
        // Wipe any prior weak-reference registrations from earlier tests in the run.
        WeakReferenceMessenger.Default.Reset();

        // --- Settings VM dependencies ---
        var settingsService = new Mock<ISettingsService>();
        settingsService.Setup(s => s.LoadSettings()).Returns(new AppSettings());
        var settingsCredentialMock = new Mock<ICredentialService>();
        var settingsNavMock = new Mock<INavigationService>();
        var settingsPricingMock = new Mock<IPricingService>();

        var settingsVm = new SettingsViewModel(
            settingsService.Object,
            settingsCredentialMock.Object,
            settingsNavMock.Object,
            settingsPricingMock.Object);

        // --- Main VM dependencies (separate mocks so we can assert who navigated) ---
        var mainCredentialMock = new Mock<ICredentialService>();
        var mainNavMock = new Mock<INavigationService>();
        var apiService = new Mock<IClaudeApiService>();
        var mainSettingsService = new Mock<ISettingsService>();
        mainSettingsService.Setup(s => s.LoadSettings()).Returns(new AppSettings());
        var historyMock = new Mock<IUsageHistoryService>();
        var jsonlService = new Mock<IJsonlService>();
        jsonlService.Setup(s => s.Sessions).Returns([]);
        var pricingService = new Mock<IPricingService>();
        pricingService.Setup(s => s.EnsurePricesLoadedAsync()).Returns(Task.CompletedTask);
        var updateService = new Mock<IUpdateService>();
        var bridge = new Mock<IWebViewBridge>();
        var burnRate = new Mock<IBurnRateNotificationService>();

        var mainVm = new MainViewModel(
            mainCredentialMock.Object,
            mainNavMock.Object,
            apiService.Object,
            mainSettingsService.Object,
            historyMock.Object,
            jsonlService.Object,
            pricingService.Object,
            updateService.Object,
            bridge.Object,
            burnRate.Object);

        return (settingsVm, mainVm, historyMock, mainCredentialMock, settingsNavMock, mainNavMock);
    }

    [Fact]
    public void SettingsLogout_PublishesMessage_TriggersHistoryClearOnMainViewModel()
    {
        var (settingsVm, mainVm, historyMock, mainCredentialMock, _, mainNavMock) = BuildBothViewModels();

        settingsVm.LogoutCommand.Execute(null);

        // D-13: ClearHistory MUST run exactly once on the round-trip — this is the
        // entire point of the gap-closure plan.
        historyMock.Verify(h => h.ClearHistory(), Times.Once,
            "Settings → Abmelden must trigger the full logout sequence including ClearHistory (D-13).");

        // Full sequence assertions — MainViewModel owns these, not SettingsViewModel.
        // NavigateTo<LoginView> fires at least once: directly in Logout() and optionally
        // via the Receive(AuthStateChangedMessage(false)) routing inside the same call.
        mainCredentialMock.Verify(c => c.ClearCredentials(), Times.Once);
        mainNavMock.Verify(n => n.NavigateTo<LoginView>(), Times.AtLeastOnce);

        // Keep the MainViewModel instance reachable until the assertion phase to prevent
        // weak-reference GC from unregistering the recipient mid-test.
        GC.KeepAlive(mainVm);
    }

    [Fact]
    public void SettingsLogout_DoesNotInvokeNavigationDirectly_OnlyViaMainViewModelRoundTrip()
    {
        var (settingsVm, mainVm, _, _, settingsNavMock, mainNavMock) = BuildBothViewModels();

        settingsVm.LogoutCommand.Execute(null);

        // SettingsViewModel must be a publisher only — no direct navigation, no direct
        // credential clearing. The DI mock injected into SettingsViewModel must be
        // untouched.
        settingsNavMock.Verify(n => n.NavigateTo<LoginView>(), Times.Never,
            "SettingsViewModel must not navigate directly — MainViewModel owns the logout sequence.");

        // MainViewModel's nav mock IS the one that navigates (at least once via Logout()
        // directly and optionally via Receive(AuthStateChangedMessage(false))).
        mainNavMock.Verify(n => n.NavigateTo<LoginView>(), Times.AtLeastOnce);

        GC.KeepAlive(mainVm);
    }
}
