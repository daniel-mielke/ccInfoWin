using CCInfoWindows.Models;
using CCInfoWindows.Services.Interfaces;
using CCInfoWindows.Tests.Helpers;
using CCInfoWindows.ViewModels;
using Microsoft.UI.Xaml.Media;
using Moq;

namespace CCInfoWindows.Tests.ViewModels;

/// <summary>
/// Verifies initial field defaults on MainViewModel construction (G-3 convention compliance).
///
/// SolidColorBrush is a WinRT-backed type that requires COM activation — it cannot be
/// instantiated in the headless xUnit test runner. The brushFactory testability seam in
/// MainViewModel accepts a Func{string, SolidColorBrush} parameter so tests can intercept
/// the factory call without constructing a real brush.
///
/// The tests here verify the INTENT (correct hex requested, factory called once) rather
/// than the runtime value of ContextModelBadgeColor.
/// </summary>
[Collection("WeakReferenceMessenger")]
public class MainViewModelInitialStateTests
{
    private static MainViewModel CreateSut(Func<string, SolidColorBrush> brushFactory)
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
        var burnRate = new Mock<IUsageNotificationService>();
        var sessionNameStore = new Mock<ISessionNameStore>();
        sessionNameStore.Setup(s => s.GetCustomName(It.IsAny<string>())).Returns((string?)null);

        return new MainViewModel(
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
            new FakeDispatcherQueue(),
            sessionNameStore.Object,
            brushFactory);
    }

    // CLEANUP-02 / G-3: constructor must request gray-400 (#9CA3AF) as the initial badge fallback.
    // Verifies the seam is called with the correct hex — the intent of the G-3 fix.
    [Fact]
    public void ContextModelBadgeColor_AtConstruction_RequestsGray400Hex()
    {
        string? capturedHex = null;
        _ = CreateSut(hex =>
        {
            capturedHex = hex;
            return null!;   // headless: SolidColorBrush cannot be instantiated without WinRT COM
        });

        Assert.Equal("#9CA3AF", capturedHex);
    }

    // CLEANUP-02 / G-3: brushFactory is called exactly once during construction (not zero times
    // which would mean null! was still used as the default initialization).
    [Fact]
    public void ContextModelBadgeColor_AtConstruction_BrushFactoryCalledOnce()
    {
        int callCount = 0;
        _ = CreateSut(hex =>
        {
            callCount++;
            return null!;
        });

        Assert.Equal(1, callCount);
    }
}
