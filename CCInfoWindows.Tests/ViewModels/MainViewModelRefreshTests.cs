using System.Diagnostics;
using System.Reflection;
using CCInfoWindows.Models;
using CCInfoWindows.Services.Interfaces;
using CCInfoWindows.Tests.Helpers;
using CCInfoWindows.ViewModels;
using Moq;

namespace CCInfoWindows.Tests.ViewModels;

/// <summary>
/// Unit tests covering the anti-flicker refresh-spinner contract: 250ms floor on manual Refresh,
/// no floor on auto-poll, CanExecute disable during refresh, and D-03 IsRefreshing isolation.
/// </summary>
public class MainViewModelRefreshTests
{
    private static readonly UsageResponse MinimalUsageResponse = new();

    private static MainViewModel CreateSut(Mock<IClaudeApiService>? apiMock = null)
    {
        apiMock ??= new Mock<IClaudeApiService>();
        apiMock.Setup(x => x.FetchUsageAsync(It.IsAny<CancellationToken>()))
               .ReturnsAsync(MinimalUsageResponse);

        var credentialService = new Mock<ICredentialService>();
        var navigationService = new Mock<INavigationService>();
        var settingsService = new Mock<ISettingsService>();
        settingsService.Setup(s => s.LoadSettings()).Returns(new AppSettings());

        var historyService = new Mock<IUsageHistoryService>();
        historyService.Setup(s => s.LoadHistory()).Returns(new UsageHistory());

        var jsonlService = new Mock<IJsonlService>();
        jsonlService.Setup(s => s.Sessions).Returns([]);
        jsonlService.Setup(s => s.IsScanning).Returns(false);
        jsonlService.Setup(s => s.InitializeAsync()).Returns(Task.CompletedTask);

        var pricingService = new Mock<IPricingService>();
        pricingService.Setup(s => s.EnsurePricesLoadedAsync()).Returns(Task.CompletedTask);

        var updateService = new Mock<IUpdateService>();
        updateService.Setup(s => s.CheckForUpdateAsync()).Returns(Task.CompletedTask);

        var bridge = new Mock<IWebViewBridge>();
        var burnRateService = new Mock<IBurnRateNotificationService>();
        var sessionNameStore = new Mock<ISessionNameStore>();
        sessionNameStore.Setup(s => s.GetCustomName(It.IsAny<string>())).Returns((string?)null);

        return new MainViewModel(
            credentialService.Object,
            navigationService.Object,
            apiMock.Object,
            settingsService.Object,
            historyService.Object,
            jsonlService.Object,
            pricingService.Object,
            updateService.Object,
            bridge.Object,
            burnRateService.Object,
            new FakeDispatcherQueue(),
            sessionNameStore.Object);
    }

    [Fact]
    public async Task RefreshCommand_AppliesMinimumDisplayFloor()
    {
        var sut = CreateSut();

        var sw = Stopwatch.StartNew();
        await sut.RefreshCommand.ExecuteAsync(null);
        sw.Stop();

        Assert.InRange(sw.ElapsedMilliseconds, 250, 750);
    }

    [Fact]
    public async Task PollUsageAsync_DoesNotApplyMinimumDisplayFloor()
    {
        var sut = CreateSut();

        var method = typeof(MainViewModel).GetMethod(
            "PollUsageAsync",
            BindingFlags.NonPublic | BindingFlags.Instance);

        Assert.NotNull(method);

        var sw = Stopwatch.StartNew();
        await (Task)method.Invoke(sut, null)!;
        sw.Stop();

        Assert.True(
            sw.ElapsedMilliseconds < 200,
            $"Auto-poll path took {sw.ElapsedMilliseconds}ms — should not apply 250ms floor.");
    }

    [Fact]
    public async Task RefreshCommand_DisabledWhileRefreshing()
    {
        var tcs = new TaskCompletionSource<UsageResponse?>();
        var apiMock = new Mock<IClaudeApiService>();
        apiMock.Setup(x => x.FetchUsageAsync(It.IsAny<CancellationToken>()))
               .Returns(tcs.Task);

        var sut = CreateSut(apiMock);

        // Start the refresh but do not await it — it will block on the open TCS
        var refreshTask = sut.RefreshCommand.ExecuteAsync(null);

        // Yield to allow the async state machine to advance to the first await inside Refresh()
        await Task.Yield();

        Assert.False(sut.RefreshCommand.CanExecute(null),
            "RefreshCommand.CanExecute should be false while a refresh is in-flight.");

        // Release the API mock so the refresh can complete
        tcs.SetResult(MinimalUsageResponse);
        await refreshTask;

        Assert.True(sut.RefreshCommand.CanExecute(null),
            "RefreshCommand.CanExecute should be true after refresh completes.");
    }

    [Fact]
    public async Task PollUsageCoreAsync_LeavesIsRefreshingFalse()
    {
        var sut = CreateSut();

        var method = typeof(MainViewModel).GetMethod(
            "PollUsageCoreAsync",
            BindingFlags.NonPublic | BindingFlags.Instance);

        Assert.NotNull(method);

        Assert.False(sut.IsRefreshing,
            "IsRefreshing should be false before calling PollUsageCoreAsync.");

        await (Task)method.Invoke(sut, null)!;

        Assert.False(sut.IsRefreshing,
            "IsRefreshing should remain false after PollUsageCoreAsync — D-03: core method does not own IsRefreshing.");
    }

    [Fact]
    public void CanRefresh_RaisesPropertyChanged_WhenIsRefreshingFlips()
    {
        var sut = CreateSut();
        var canRefreshChanges = 0;

        sut.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.CanRefresh))
                canRefreshChanges++;
        };

        // Act: flip IsRefreshing twice via the public setter
        sut.IsRefreshing = true;
        sut.IsRefreshing = false;

        // Assert: PropertyChanged("CanRefresh") fired on both flips —
        // proves [NotifyPropertyChangedFor(nameof(CanRefresh))] is wired on _isRefreshing.
        // This is the gap-closure invariant: the explicit IsEnabled x:Bind binding
        // in MainView.xaml relies on this notification to re-evaluate.
        Assert.True(canRefreshChanges >= 2,
            $"PropertyChanged(\"CanRefresh\") fired {canRefreshChanges} times — expected >= 2 (one per IsRefreshing flip). " +
            "Verify [NotifyPropertyChangedFor(nameof(CanRefresh))] is on the _isRefreshing field.");
    }

    private static MainViewModel CreateSutWithNameStore(Mock<ISessionNameStore> sessionNameStore, Mock<IJsonlService> jsonlService)
    {
        var apiMock = new Mock<IClaudeApiService>();
        apiMock.Setup(x => x.FetchUsageAsync(It.IsAny<CancellationToken>()))
               .ReturnsAsync(MinimalUsageResponse);

        var credentialService = new Mock<ICredentialService>();
        var navigationService = new Mock<INavigationService>();
        var settingsService = new Mock<ISettingsService>();
        settingsService.Setup(s => s.LoadSettings()).Returns(new AppSettings());

        var historyService = new Mock<IUsageHistoryService>();
        historyService.Setup(s => s.LoadHistory()).Returns(new UsageHistory());

        var pricingService = new Mock<IPricingService>();
        pricingService.Setup(s => s.EnsurePricesLoadedAsync()).Returns(Task.CompletedTask);

        var updateService = new Mock<IUpdateService>();
        updateService.Setup(s => s.CheckForUpdateAsync()).Returns(Task.CompletedTask);

        var bridge = new Mock<IWebViewBridge>();
        var burnRateService = new Mock<IBurnRateNotificationService>();

        return new MainViewModel(
            credentialService.Object,
            navigationService.Object,
            apiMock.Object,
            settingsService.Object,
            historyService.Object,
            jsonlService.Object,
            pricingService.Object,
            updateService.Object,
            bridge.Object,
            burnRateService.Object,
            new FakeDispatcherQueue(),
            sessionNameStore.Object);
    }

    private static void InvokeRefreshSessionList(MainViewModel vm)
    {
        var method = typeof(MainViewModel).GetMethod(
            "RefreshSessionList",
            BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(method);
        method.Invoke(vm, null);
    }

    [Fact]
    public void RefreshSessionList_AppliesCustomNameOverlay_WhenStoreReturnsValue()
    {
        // Arrange
        const string SessionId = "sessionA";
        const string AutoDerived = "auto-derived";
        const string CustomName = "My Custom Name";

        var sessionNameStore = new Mock<ISessionNameStore>();
        sessionNameStore.Setup(s => s.GetCustomName(SessionId)).Returns(CustomName);
        sessionNameStore.Setup(s => s.GetCustomName(It.Is<string>(id => id != SessionId))).Returns((string?)null);

        // Use LastActivity 2 hours ago: within 30-day visibility window but outside 30-minute
        // activity threshold, so the session appears in SortedSessions but is not auto-selected
        // (avoids UpdateSessionData → ParseHexBrush → WinUI COM exception in headless tests).
        var session = new SessionInfo
        {
            Id = SessionId,
            Cwd = "D:\\projects\\test",
            DisplayName = AutoDerived,
            LastActivity = DateTimeOffset.UtcNow.AddHours(-2)
        };

        var jsonlService = new Mock<IJsonlService>();
        jsonlService.Setup(s => s.Sessions).Returns([session]);
        jsonlService.Setup(s => s.IsScanning).Returns(false);
        jsonlService.Setup(s => s.InitializeAsync()).Returns(Task.CompletedTask);

        var sut = CreateSutWithNameStore(sessionNameStore, jsonlService);

        // Act
        InvokeRefreshSessionList(sut);

        // Assert
        Assert.Single(sut.SortedSessions);
        Assert.Equal(CustomName, sut.SortedSessions.First().DisplayName);
    }

    [Fact]
    public void RefreshSessionList_FallsBackToAutoDerived_WhenStoreReturnsNull()
    {
        // Arrange
        const string SessionId = "sessionB";
        const string AutoDerived = "auto-derived";

        var sessionNameStore = new Mock<ISessionNameStore>();
        sessionNameStore.Setup(s => s.GetCustomName(It.IsAny<string>())).Returns((string?)null);

        var session = new SessionInfo
        {
            Id = SessionId,
            Cwd = "D:\\projects\\test",
            DisplayName = AutoDerived,
            LastActivity = DateTimeOffset.UtcNow.AddHours(-2)
        };

        var jsonlService = new Mock<IJsonlService>();
        jsonlService.Setup(s => s.Sessions).Returns([session]);
        jsonlService.Setup(s => s.IsScanning).Returns(false);
        jsonlService.Setup(s => s.InitializeAsync()).Returns(Task.CompletedTask);

        var sut = CreateSutWithNameStore(sessionNameStore, jsonlService);

        // Act
        InvokeRefreshSessionList(sut);

        // Assert
        Assert.Single(sut.SortedSessions);
        Assert.Equal(AutoDerived, sut.SortedSessions.First().DisplayName);
    }
}
