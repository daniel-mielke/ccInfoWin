using CCInfoWindows.Models;
using CCInfoWindows.Services;
using CCInfoWindows.Services.Interfaces;
using CCInfoWindows.ViewModels;
using Moq;

namespace CCInfoWindows.Tests.ViewModels;

/// <summary>
/// Unit tests for SettingsViewModel: tab switching, short labels, version text, token status,
/// and Phase 26 session rename surface (SessionRenameItems, SaveSessionCustomName, ClearSessionCustomName).
/// </summary>
public class SettingsViewModelTests
{
    private static SettingsViewModel CreateViewModel(
        bool hasValidToken = true,
        Mock<ISessionNameStore>? sessionNameStore = null,
        Mock<IJsonlService>? jsonlService = null,
        Mock<IDispatcherQueue>? dispatcherQueue = null)
    {
        var settingsService = new Mock<ISettingsService>();
        settingsService.Setup(s => s.LoadSettings()).Returns(new AppSettings());

        var credentialService = new Mock<ICredentialService>();
        credentialService.Setup(s => s.HasValidToken()).Returns(hasValidToken);

        var navigationService = new Mock<INavigationService>();

        var pricingService = new Mock<IPricingService>();
        pricingService.Setup(s => s.Source).Returns(PricingSource.Unknown);
        pricingService.Setup(s => s.LastFetch).Returns((DateTimeOffset?)null);

        var historyService = new Mock<IUsageHistoryService>();

        var store = sessionNameStore ?? new Mock<ISessionNameStore>();
        var jsonl = jsonlService ?? new Mock<IJsonlService>();
        // Only set up default Sessions if no custom mock was provided — prevents overwriting caller's setup.
        if (jsonlService == null)
            jsonl.Setup(s => s.Sessions).Returns(Array.Empty<SessionInfo>());
        var dispatcher = dispatcherQueue ?? new Mock<IDispatcherQueue>();

        return new SettingsViewModel(
            settingsService.Object,
            credentialService.Object,
            navigationService.Object,
            pricingService.Object,
            historyService.Object,
            store.Object,
            jsonl.Object,
            dispatcher.Object);
    }

    // ─── Existing tab visibility tests (indexes shifted: About is now 4) ──────────

    [Fact]
    public void TabSwitching_DefaultIndex_GeneralTabVisible()
    {
        var vm = CreateViewModel();

        Assert.Equal(0, vm.SelectedTabIndex);
        Assert.True(vm.IsGeneralTabVisible);
        Assert.False(vm.IsUpdatesTabVisible);
        Assert.False(vm.IsAccountTabVisible);
        Assert.False(vm.IsSessionsTabVisible);
        Assert.False(vm.IsAboutTabVisible);
    }

    [Fact]
    public void TabSwitching_SetIndex1_UpdatesTabVisible()
    {
        var vm = CreateViewModel();

        vm.SelectedTabIndex = 1;

        Assert.False(vm.IsGeneralTabVisible);
        Assert.True(vm.IsUpdatesTabVisible);
        Assert.False(vm.IsAccountTabVisible);
        Assert.False(vm.IsSessionsTabVisible);
        Assert.False(vm.IsAboutTabVisible);
    }

    [Fact]
    public void TabSwitching_SetIndex2_AccountTabVisible()
    {
        var vm = CreateViewModel();

        vm.SelectedTabIndex = 2;

        Assert.False(vm.IsGeneralTabVisible);
        Assert.False(vm.IsUpdatesTabVisible);
        Assert.True(vm.IsAccountTabVisible);
        Assert.False(vm.IsSessionsTabVisible);
        Assert.False(vm.IsAboutTabVisible);
    }

    [Fact]
    public void TabSwitching_SetIndex3_AboutTabVisible_WasOldBehavior_NowIsSessionsTab()
    {
        // Phase 26: index 3 is now the Sessions tab (About shifted to 4).
        var vm = CreateViewModel();

        vm.SelectedTabIndex = 3;

        Assert.False(vm.IsGeneralTabVisible);
        Assert.False(vm.IsUpdatesTabVisible);
        Assert.False(vm.IsAccountTabVisible);
        Assert.True(vm.IsSessionsTabVisible);
        Assert.False(vm.IsAboutTabVisible);
    }

    // ─── Phase 26 new tests ───────────────────────────────────────────────────────

    /// <summary>Phase 26: tab index 3 → Sessions tab visible, About not.</summary>
    [Fact]
    public void TabIndex_Three_IsSessionsTab_NotAbout()
    {
        var vm = CreateViewModel();
        vm.SelectedTabIndex = 3;

        Assert.True(vm.IsSessionsTabVisible);
        Assert.False(vm.IsAboutTabVisible);
    }

    /// <summary>Phase 26: tab index 4 → About tab visible, Sessions not.</summary>
    [Fact]
    public void TabIndex_Four_IsAboutTab()
    {
        var vm = CreateViewModel();
        vm.SelectedTabIndex = 4;

        Assert.True(vm.IsAboutTabVisible);
        Assert.False(vm.IsSessionsTabVisible);
    }

    /// <summary>Phase 26: SessionRenameItems populated from IJsonlService.Sessions with correct CustomName values.</summary>
    [Fact]
    public void RefreshSessionRenameItems_PopulatesFromJsonlService()
    {
        var store = new Mock<ISessionNameStore>();
        store.Setup(s => s.GetCustomName("session-1")).Returns("Custom1");
        store.Setup(s => s.GetCustomName("session-2")).Returns((string?)null);

        var jsonl = new Mock<IJsonlService>();
        jsonl.Setup(s => s.Sessions).Returns(new[]
        {
            new SessionInfo { Id = "session-1", Cwd = "/projects/alpha", DisplayName = "Project Alpha" },
            new SessionInfo { Id = "session-2", Cwd = "/projects/beta",  DisplayName = "Project Beta"  }
        });

        var vm = CreateViewModel(sessionNameStore: store, jsonlService: jsonl);

        // Activate Sessions tab to trigger snapshot refresh
        vm.SelectedTabIndex = SettingsViewModel.SessionsTabIndex;

        Assert.Equal(2, vm.SessionRenameItems.Count);
        var alpha = vm.SessionRenameItems.First(r => r.SessionId == "session-1");
        var beta  = vm.SessionRenameItems.First(r => r.SessionId == "session-2");
        Assert.Equal("Custom1", alpha.CustomName);
        Assert.Equal(string.Empty, beta.CustomName);
        Assert.False(alpha.IsOrphan);
        Assert.False(beta.IsOrphan);
    }

    /// <summary>Phase 26 RENAME-05: control characters are stripped before persistence.</summary>
    [Fact]
    public async Task SaveSessionCustomName_StripsControlCharsAndPersists()
    {
        var store = new Mock<ISessionNameStore>();
        store.Setup(s => s.SaveAsync(default)).ReturnsAsync(true);

        var vm = CreateViewModel(sessionNameStore: store);
        var item = new SessionRenameItem
        {
            SessionId = "proj-1",
            DefaultName = "Project",
            CustomName = "Bad	X"   // U+0009 (TAB) must be stripped
        };

        await vm.SaveSessionCustomNameCommand.ExecuteAsync(item);

        store.Verify(s => s.SetCustomName("proj-1", "BadX"), Times.Once);
        store.Verify(s => s.SaveAsync(default), Times.Once);
    }

    /// <summary>Phase 26 D-04: empty value → ClearCustomName called, not SetCustomName.</summary>
    [Fact]
    public async Task SaveSessionCustomName_EmptyValueClears()
    {
        var store = new Mock<ISessionNameStore>();
        store.Setup(s => s.SaveAsync(default)).ReturnsAsync(true);

        var vm = CreateViewModel(sessionNameStore: store);
        var item = new SessionRenameItem
        {
            SessionId = "proj-2",
            DefaultName = "Project",
            CustomName = string.Empty
        };

        await vm.SaveSessionCustomNameCommand.ExecuteAsync(item);

        store.Verify(s => s.ClearCustomName("proj-2"), Times.Once);
        store.Verify(s => s.SetCustomName(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        store.Verify(s => s.SaveAsync(default), Times.Once);
    }

    /// <summary>Phase 26: ClearSessionCustomName removes entry and resets bound property.</summary>
    [Fact]
    public async Task ClearSessionCustomName_RemovesEntry()
    {
        var store = new Mock<ISessionNameStore>();
        store.Setup(s => s.SaveAsync(default)).ReturnsAsync(true);

        var vm = CreateViewModel(sessionNameStore: store);
        var item = new SessionRenameItem
        {
            SessionId = "proj-3",
            DefaultName = "Project",
            CustomName = "My Name"
        };

        await vm.ClearSessionCustomNameCommand.ExecuteAsync(item);

        store.Verify(s => s.ClearCustomName("proj-3"), Times.Once);
        store.Verify(s => s.SaveAsync(default), Times.Once);
        Assert.Equal(string.Empty, item.CustomName);
    }

    /// <summary>Phase 26 G-1: Activate subscribes, Deactivate unsubscribes — NameChanged triggers refresh.</summary>
    [Fact]
    public void Activate_SubscribesToNameChanged_DeactivateUnsubscribes()
    {
        EventHandler<SessionNameChangedEventArgs>? capturedHandler = null;

        var store = new Mock<ISessionNameStore>();
        store.SetupAdd(s => s.NameChanged += It.IsAny<EventHandler<SessionNameChangedEventArgs>>())
             .Callback<EventHandler<SessionNameChangedEventArgs>>(h => capturedHandler = h);
        store.SetupRemove(s => s.NameChanged -= It.IsAny<EventHandler<SessionNameChangedEventArgs>>())
             .Callback<EventHandler<SessionNameChangedEventArgs>>(h => capturedHandler = null);

        var jsonl = new Mock<IJsonlService>();
        jsonl.Setup(s => s.Sessions).Returns(Array.Empty<SessionInfo>());

        // IDispatcherQueue.TryEnqueue must invoke the action synchronously in tests
        var dispatcher = new Mock<IDispatcherQueue>();
        dispatcher.Setup(d => d.TryEnqueue(It.IsAny<Action>()))
                  .Callback<Action>(a => a());

        var vm = CreateViewModel(sessionNameStore: store, jsonlService: jsonl, dispatcherQueue: dispatcher);

        // Before Activate — no subscription
        Assert.Null(capturedHandler);

        vm.Activate();
        Assert.NotNull(capturedHandler);

        vm.Deactivate();
        Assert.Null(capturedHandler);
    }

    // ─── Existing tests unchanged below ──────────────────────────────────────────

    [Fact]
    public void RefreshOptions_UseShortNotation()
    {
        var vm = CreateViewModel();

        Assert.Equal("30s", vm.RefreshOptions[0].Label);
        Assert.Equal("1min", vm.RefreshOptions[1].Label);
        Assert.Equal("2min", vm.RefreshOptions[2].Label);
        Assert.Equal("5min", vm.RefreshOptions[3].Label);
        Assert.Equal("10min", vm.RefreshOptions[4].Label);
        Assert.Equal("Manuell", vm.RefreshOptions[5].Label);
    }

    [Fact]
    public void RefreshOptions_SecondsValuesUnchanged()
    {
        var vm = CreateViewModel();

        Assert.Equal(30, vm.RefreshOptions[0].Seconds);
        Assert.Equal(60, vm.RefreshOptions[1].Seconds);
        Assert.Equal(120, vm.RefreshOptions[2].Seconds);
        Assert.Equal(300, vm.RefreshOptions[3].Seconds);
        Assert.Equal(600, vm.RefreshOptions[4].Seconds);
        Assert.Equal(0, vm.RefreshOptions[5].Seconds);
    }

    [Fact]
    public void AppVersionText_ReturnsNonEmptyVersion()
    {
        var vm = CreateViewModel();

        Assert.NotEmpty(vm.AppVersionText);
        Assert.Contains(".", vm.AppVersionText);
    }

    [Fact]
    public void IsTokenValid_WhenHasToken_ReturnsTrue()
    {
        var vm = CreateViewModel(hasValidToken: true);

        Assert.True(vm.IsTokenValid);
    }

    [Fact]
    public void IsTokenValid_WhenNoToken_ReturnsFalse()
    {
        var vm = CreateViewModel(hasValidToken: false);

        Assert.False(vm.IsTokenValid);
    }
}
