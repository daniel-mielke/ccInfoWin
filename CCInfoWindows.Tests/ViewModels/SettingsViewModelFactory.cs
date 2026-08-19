using CCInfoWindows.Models;
using CCInfoWindows.Services.Interfaces;
using CCInfoWindows.ViewModels;
using Moq;

namespace CCInfoWindows.Tests.ViewModels;

/// <summary>
/// The one headless <see cref="SettingsViewModel"/> construction, for the five suites that used to
/// carry their own copy of the eleven-argument call. Same contract as
/// <see cref="MainViewModelFactory"/>: a mock the caller supplies is used exactly as handed over, so
/// the instrumented doubles the logout-ordering suite needs stay expressible; a mock the caller omits
/// is created here with the stubs the union of the call sites needs.
/// </summary>
internal static class SettingsViewModelFactory
{
    internal static SettingsViewModel Create(
        Mock<ISettingsService>? settingsService = null,
        Mock<ICredentialService>? credentialService = null,
        Mock<INavigationService>? navigationService = null,
        Mock<IPricingService>? pricingService = null,
        Mock<IUsageHistoryService>? historyService = null,
        Mock<ISessionNameStore>? sessionNameStore = null,
        Mock<IJsonlService>? jsonlService = null,
        Mock<IDispatcherQueue>? dispatcherQueue = null,
        Mock<IClaudeApiService>? apiService = null,
        Mock<IUsageNotificationService>? usageNotificationService = null,
        Mock<IWebViewBridge>? bridge = null)
        => new(
            (settingsService ?? SettingsService()).Object,
            (credentialService ?? CredentialService()).Object,
            (navigationService ?? new Mock<INavigationService>()).Object,
            (pricingService ?? PricingService()).Object,
            (historyService ?? new Mock<IUsageHistoryService>()).Object,
            (sessionNameStore ?? SessionNameStore()).Object,
            (jsonlService ?? JsonlService()).Object,
            (dispatcherQueue ?? new Mock<IDispatcherQueue>()).Object,
            (apiService ?? new Mock<IClaudeApiService>()).Object,   // ORGID-01
            (usageNotificationService ?? new Mock<IUsageNotificationService>()).Object,
            (bridge ?? new Mock<IWebViewBridge>()).Object);         // Finding 18

    internal static Mock<ISettingsService> SettingsService()
    {
        var settingsService = new Mock<ISettingsService>();
        settingsService.Setup(s => s.LoadSettings()).Returns(new AppSettings());
        return settingsService;
    }

    internal static Mock<ICredentialService> CredentialService(bool hasValidToken = true)
    {
        var credentialService = new Mock<ICredentialService>();
        credentialService.Setup(s => s.HasValidToken()).Returns(hasValidToken);
        return credentialService;
    }

    internal static Mock<IPricingService> PricingService()
    {
        var pricingService = new Mock<IPricingService>();
        pricingService.Setup(s => s.Source).Returns(PricingSource.Unknown);
        pricingService.Setup(s => s.LastFetch).Returns((DateTimeOffset?)null);
        return pricingService;
    }

    /// <summary>
    /// The store double with the two members every Sessions-tab code path needs. Moq has no fallback
    /// value for IReadOnlyCollection&lt;string&gt;, so an unconfigured GetKnownSessionIds would return
    /// null and the orphan enumeration would throw before reaching the assertion.
    /// </summary>
    internal static Mock<ISessionNameStore> SessionNameStore()
    {
        var store = new Mock<ISessionNameStore>();
        store.Setup(s => s.GetKnownSessionIds()).Returns(Array.Empty<string>());
        store.Setup(s => s.SaveAsync(default)).ReturnsAsync(true);
        return store;
    }

    internal static Mock<IJsonlService> JsonlService()
    {
        var jsonlService = new Mock<IJsonlService>();
        jsonlService.Setup(s => s.Sessions).Returns(Array.Empty<SessionInfo>());
        return jsonlService;
    }
}
