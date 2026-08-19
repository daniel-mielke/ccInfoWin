using CCInfoWindows.Models;
using CCInfoWindows.Services.Interfaces;
using CCInfoWindows.Tests.Helpers;
using CCInfoWindows.ViewModels;
using Microsoft.UI.Xaml.Media;
using Moq;

namespace CCInfoWindows.Tests.ViewModels;

/// <summary>
/// The one headless <see cref="MainViewModel"/> construction. Five test classes used to carry their own
/// copy of the twelve-argument call plus eleven mocks, and their stub sets had already drifted apart —
/// a production path that started reading <c>IJsonlService.IsScanning</c> got Moq's default in some
/// files and a stubbed value in others, so the same code passed in one suite and failed in another.
///
/// Every parameter is optional. A mock the caller supplies is used exactly as handed over — no default
/// stub is applied on top of it, so a caller that instruments a member keeps full control of it. A
/// mock the caller omits is created here with the stubs the union of the call sites needs.
/// </summary>
internal static class MainViewModelFactory
{
    internal static MainViewModel Create(
        Mock<ICredentialService>? credentialService = null,
        Mock<INavigationService>? navigationService = null,
        Mock<IClaudeApiService>? apiService = null,
        Mock<ISettingsService>? settingsService = null,
        Mock<IUsageHistoryService>? historyService = null,
        Mock<IJsonlService>? jsonlService = null,
        Mock<IPricingService>? pricingService = null,
        Mock<IUpdateService>? updateService = null,
        Mock<IUsageNotificationService>? usageNotificationService = null,
        Mock<ISessionNameStore>? sessionNameStore = null,
        IDispatcherQueue? dispatcherQueue = null,
        Func<string, SolidColorBrush>? brushFactory = null)
        => new(
            (credentialService ?? new Mock<ICredentialService>()).Object,
            (navigationService ?? new Mock<INavigationService>()).Object,
            (apiService ?? ApiService()).Object,
            (settingsService ?? SettingsService()).Object,
            (historyService ?? HistoryService()).Object,
            (jsonlService ?? JsonlService()).Object,
            (pricingService ?? PricingService()).Object,
            (updateService ?? UpdateService()).Object,
            (usageNotificationService ?? new Mock<IUsageNotificationService>()).Object,
            dispatcherQueue ?? new FakeDispatcherQueue(),
            (sessionNameStore ?? SessionNameStore()).Object,
            brushFactory ?? HeadlessBrushFactory);

    /// <summary>SolidColorBrush needs WinRT COM activation, which the xUnit host cannot provide.</summary>
    private static readonly Func<string, SolidColorBrush> HeadlessBrushFactory = _ => null!;

    /// <summary>Answers a poll with a response whose every usage window is absent, but which is non-null.</summary>
    internal static Mock<IClaudeApiService> ApiService()
    {
        var apiService = new Mock<IClaudeApiService>();
        apiService.Setup(s => s.FetchUsageAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new UsageResponse());
        return apiService;
    }

    internal static Mock<ISettingsService> SettingsService()
    {
        var settingsService = new Mock<ISettingsService>();
        settingsService.Setup(s => s.LoadSettings()).Returns(new AppSettings());
        return settingsService;
    }

    internal static Mock<IUsageHistoryService> HistoryService()
    {
        var historyService = new Mock<IUsageHistoryService>();
        historyService.Setup(s => s.LoadHistory()).Returns(new UsageHistory());
        return historyService;
    }

    /// <summary>No sessions, no scan in flight, nothing recorded for any period.</summary>
    internal static Mock<IJsonlService> JsonlService()
    {
        var jsonlService = new Mock<IJsonlService>();
        jsonlService.Setup(s => s.Sessions).Returns([]);
        jsonlService.Setup(s => s.IsScanning).Returns(false);
        jsonlService.Setup(s => s.GetStatistics(It.IsAny<TimePeriod>(), It.IsAny<string?>()))
            .Returns(StatisticsSummary.Empty);
        return jsonlService;
    }

    internal static Mock<IPricingService> PricingService()
    {
        var pricingService = new Mock<IPricingService>();
        pricingService.Setup(s => s.Source).Returns(PricingSource.Unknown);
        pricingService.Setup(s => s.LastFetch).Returns((DateTimeOffset?)null);
        pricingService.Setup(s => s.EnsurePricesLoadedAsync()).Returns(Task.CompletedTask);
        return pricingService;
    }

    internal static Mock<IUpdateService> UpdateService()
    {
        var updateService = new Mock<IUpdateService>();
        updateService.Setup(s => s.CheckForUpdateAsync()).Returns(Task.CompletedTask);
        return updateService;
    }

    /// <summary>A store that has never renamed anything, which is the state every session starts in.</summary>
    internal static Mock<ISessionNameStore> SessionNameStore()
    {
        var sessionNameStore = new Mock<ISessionNameStore>();
        sessionNameStore.Setup(s => s.GetCustomName(It.IsAny<string>())).Returns((string?)null);
        return sessionNameStore;
    }
}
