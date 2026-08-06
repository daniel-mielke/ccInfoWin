using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using CCInfoWindows.Helpers;
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
        var burnRateService = new Mock<IUsageNotificationService>();
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
            sessionNameStore.Object,
            _ => null!);   // headless brushFactory seam — SolidColorBrush requires WinRT COM
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
        var burnRateService = new Mock<IUsageNotificationService>();

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
            sessionNameStore.Object,
            _ => null!);   // headless brushFactory seam — SolidColorBrush requires WinRT COM
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

    [Fact]
    public void RefreshSessionList_ClearsTheSessionPanels_WhenTheSelectedSessionDisappears()
    {
        // Finding 6: the "had a selection and it vanished" case used to drop the guard and return, so
        // ClearSessionData never ran. The ComboBox went blank through its TwoWay null write-back while
        // KONTEXTFENSTER kept rendering the gone session's percentage, model badge and autocompact
        // warning, and STATISTIKEN kept its token counts.
        var sessions = new List<SessionInfo>
        {
            new()
            {
                Id = "vanishing",
                Cwd = "D:\\projects\\test",
                DisplayName = "test",
                LastActivity = DateTimeOffset.UtcNow
            }
        };

        var jsonlService = new Mock<IJsonlService>();
        jsonlService.Setup(s => s.Sessions).Returns(() => sessions);
        jsonlService.Setup(s => s.IsScanning).Returns(false);
        jsonlService.Setup(s => s.GetContextWindow(It.IsAny<string>())).Returns(new ContextWindowData
        {
            TotalTokens = 150_000,
            MaxTokens = 200_000,
            ModelName = "claude-opus-5",
            ShouldWarnAutocompact = true
        });
        jsonlService
            .Setup(s => s.GetStatistics(It.IsAny<TimePeriod>(), It.IsAny<string?>()))
            .Returns(new StatisticsSummary { InputTokens = 4_200 });

        var sessionNameStore = new Mock<ISessionNameStore>();
        sessionNameStore.Setup(s => s.GetCustomName(It.IsAny<string>())).Returns((string?)null);

        var sut = CreateSutWithNameStore(sessionNameStore, jsonlService);
        // Session tab keeps statistics on the synchronous path — the aggregating tabs hop through
        // Task.Run, which would race the assertions below.
        sut.SelectedTabIndex = (int)TimePeriod.Session;

        InvokeRefreshSessionList(sut);

        Assert.NotNull(sut.SelectedSession);
        Assert.True(sut.HasActiveSession);
        Assert.True(sut.ShowAutocompactWarning);
        Assert.NotEqual("\u2013", sut.StatisticsTotal);

        // The visibility window narrows in Settings, or the project directory is deleted.
        sessions.Clear();
        InvokeRefreshSessionList(sut);

        Assert.Null(sut.SelectedSession);
        Assert.False(sut.HasActiveSession);
        Assert.False(sut.ShowAutocompactWarning);
        Assert.Equal("--", sut.ContextPercentageText);
        Assert.Equal(string.Empty, sut.ContextModelBadge);
        Assert.Equal("\u2013", sut.StatisticsTotal);
    }

    [Theory]
    [InlineData(AppSettings.ManualRefreshSeconds, false)]
    [InlineData(-1, false)]
    [InlineData(30, true)]
    [InlineData(AppSettings.DefaultRefreshIntervalSeconds, true)]
    public void ShouldPollAutomatically_TreatsTheManualSentinelAsNoPolling(int seconds, bool expected)
    {
        // The sentinel is 0 seconds. Handing that to DispatcherQueueTimer.Interval is not a fast poll;
        // the timer has to be stopped instead. SettingsService clamps the persisted value, so the
        // negative case can only come from a value it could not repair.
        Assert.Equal(expected, MainViewModel.ShouldPollAutomatically(seconds));
    }

    private static UsageResponse WeeklyResponse(UsageWindow? opus, UsageWindow? sevenDay)
        => new() { SevenDayOpus = opus, SevenDay = sevenDay };

    private static UsageWindow UsageWindowAt(double utilization, DateTimeOffset resetsAt)
        => new() { Utilization = utilization, ResetsAt = resetsAt };

    [Fact]
    public void PinWeeklyNotificationWindow_HoldsThePinnedSource_WhenOneResponseOmitsIt()
    {
        // Finding 20a: seven_day_opus and seven_day carry independent resets_at, so silently falling
        // back handed the notification service a different window identity — indistinguishable from a
        // real rotation, which fired a bogus reset toast and re-armed the 80/95 thresholds.
        var sut = CreateSut();
        var opus = UsageWindowAt(40, DateTimeOffset.UtcNow.AddDays(3));
        var fallback = UsageWindowAt(10, DateTimeOffset.UtcNow.AddDays(5));

        Assert.Same(opus, sut.PinWeeklyNotificationWindow(WeeklyResponse(opus, fallback)));
        Assert.Null(sut.PinWeeklyNotificationWindow(WeeklyResponse(null, fallback)));
        Assert.Same(opus, sut.PinWeeklyNotificationWindow(WeeklyResponse(opus, fallback)));
    }

    [Fact]
    public void PinWeeklyNotificationWindow_AdoptsTheFallback_WhenThePinnedSourceStaysAway()
    {
        var sut = CreateSut();
        var opus = UsageWindowAt(40, DateTimeOffset.UtcNow.AddDays(3));
        var fallback = UsageWindowAt(10, DateTimeOffset.UtcNow.AddDays(5));

        sut.PinWeeklyNotificationWindow(WeeklyResponse(opus, fallback));

        Assert.Null(sut.PinWeeklyNotificationWindow(WeeklyResponse(null, fallback)));
        Assert.Same(fallback, sut.PinWeeklyNotificationWindow(WeeklyResponse(null, fallback)));
    }

    [Fact]
    public void PinWeeklyNotificationWindow_ReappliesThePreference_OnceThePinnedWindowIsOver()
    {
        var sut = CreateSut();
        var expiredOpus = UsageWindowAt(90, DateTimeOffset.UtcNow.AddMinutes(-1));
        var freshOpus = UsageWindowAt(5, DateTimeOffset.UtcNow.AddDays(7));
        var fallback = UsageWindowAt(10, DateTimeOffset.UtcNow.AddDays(4));

        sut.PinWeeklyNotificationWindow(WeeklyResponse(expiredOpus, fallback));

        Assert.Same(freshOpus, sut.PinWeeklyNotificationWindow(WeeklyResponse(freshOpus, fallback)));
    }

    [Fact]
    public void PinWeeklyNotificationWindow_ReportsNothing_WhenNeitherWeeklyFieldIsPresent()
    {
        Assert.Null(CreateSut().PinWeeklyNotificationWindow(WeeklyResponse(null, null)));
    }

    [Fact]
    public async Task SaveCustomNameAsync_RaisesTheActionBanner_WhenPersistingFails()
    {
        // Finding 25: on failure the store rolls the in-memory map back and re-raises NameChanged, so
        // the displayed name self-corrects — but every call site discarded the bool, so nothing told
        // the user their rename would be gone after a restart.
        var sut = CreateSutForRename(saveSucceeds: false);

        await sut.SaveCustomNameAsync("sessionA", "My Name");

        Assert.True(sut.HasActionError);
        Assert.NotEmpty(sut.ActionErrorMessage);
    }

    [Fact]
    public async Task ClearCustomNameAsync_LeavesTheActionBannerClosed_WhenPersistingSucceeds()
    {
        var sut = CreateSutForRename(saveSucceeds: true);

        await sut.ClearCustomNameAsync("sessionA");

        Assert.False(sut.HasActionError);
        Assert.Equal(string.Empty, sut.ActionErrorMessage);
    }

    [Fact]
    public void FormatNextWindowLabel_LetsThePatternDecideTheFieldOrder()
    {
        // Localisation follow-up: the pattern used to be chosen by a `culture.Name.StartsWith("de")`
        // branch in code, so a third language would silently have rendered English's layout. It now
        // comes from the active language's resw entry — a locale that wants a date in the label gets
        // one, and a locale that does not, does not.
        var resetsAt = new DateTimeOffset(2026, 2, 27, 10, 0, 0, TimeSpan.Zero);
        var culture = new CultureInfo("en-US");

        var timeOnly = MainViewModel.FormatNextWindowLabel(resetsAt, "ddd HH:mm", culture);
        var withDate = MainViewModel.FormatNextWindowLabel(resetsAt, "ddd d.M. HH:mm", culture);

        Assert.DoesNotContain(".", timeOnly);
        Assert.Contains(".", withDate);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void FormatNextWindowLabel_FallsBackToACultureDerivedPattern_WhenTheDictionaryCannotAnswer(string? pattern)
    {
        // An unbuilt localizer echoes the uid back and a built one returns empty for an unknown uid.
        // Handing an empty pattern to ToString would silently produce DateTime's general format.
        var resetsAt = new DateTimeOffset(2026, 2, 27, 10, 0, 0, TimeSpan.Zero);
        var culture = new CultureInfo("de-DE");
        var localTime = resetsAt.LocalDateTime;

        var label = MainViewModel.FormatNextWindowLabel(resetsAt, pattern, culture);

        Assert.NotEqual(localTime.ToString(culture), label);
        Assert.Equal(localTime.ToString(CountdownFormatter.CultureDefaultPattern(culture), culture), label);
    }

    private static MainViewModel CreateSutForRename(bool saveSucceeds)
    {
        var sessionNameStore = new Mock<ISessionNameStore>();
        sessionNameStore.Setup(s => s.GetCustomName(It.IsAny<string>())).Returns((string?)null);
        sessionNameStore.Setup(s => s.SaveAsync(It.IsAny<CancellationToken>())).ReturnsAsync(saveSucceeds);

        var jsonlService = new Mock<IJsonlService>();
        jsonlService.Setup(s => s.Sessions).Returns([]);
        jsonlService.Setup(s => s.IsScanning).Returns(false);

        return CreateSutWithNameStore(sessionNameStore, jsonlService);
    }
}
