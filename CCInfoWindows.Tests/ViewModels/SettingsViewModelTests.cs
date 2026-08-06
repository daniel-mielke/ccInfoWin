using CCInfoWindows.Models;
using CCInfoWindows.Services;
using CCInfoWindows.Services.Interfaces;
using CCInfoWindows.ViewModels;
using CommunityToolkit.Mvvm.Input;
using Moq;

namespace CCInfoWindows.Tests.ViewModels;

/// <summary>
/// Unit tests for SettingsViewModel: tab switching, short labels, version text, token status,
/// the Phase 26 session rename surface (SessionRenameItems, SaveSessionCustomName,
/// ClearSessionCustomName) and the wave-2 remediation contracts — Sessions-tab command wiring,
/// persistence-failure surfacing, language-switch ordering and logout cache purge.
/// </summary>
public class SettingsViewModelTests
{
    private static readonly int GermanLanguageIndex =
        AppSettings.SupportedLanguages.IndexOf(AppSettings.GermanLanguage);

    private static readonly int EnglishLanguageIndex =
        AppSettings.SupportedLanguages.IndexOf(AppSettings.EnglishLanguage);

    /// <summary>
    /// A store double with the two members every Sessions-tab code path needs. Moq has no fallback
    /// value for IReadOnlyCollection&lt;string&gt;, so an unconfigured GetKnownSessionIds would return
    /// null and the orphan enumeration would throw before reaching the assertion.
    /// </summary>
    private static Mock<ISessionNameStore> CreateSessionNameStore()
    {
        var store = new Mock<ISessionNameStore>();
        store.Setup(s => s.GetKnownSessionIds()).Returns(Array.Empty<string>());
        store.Setup(s => s.SaveAsync(default)).ReturnsAsync(true);
        return store;
    }

    private static SettingsViewModel CreateViewModel(
        bool hasValidToken = true,
        Mock<ISessionNameStore>? sessionNameStore = null,
        Mock<IJsonlService>? jsonlService = null,
        Mock<IDispatcherQueue>? dispatcherQueue = null,
        Mock<ISettingsService>? settingsServiceMock = null,
        Mock<IClaudeApiService>? apiServiceMock = null)
    {
        var settingsService = settingsServiceMock ?? new Mock<ISettingsService>();
        if (settingsServiceMock == null)
            settingsService.Setup(s => s.LoadSettings()).Returns(new AppSettings());

        var credentialService = new Mock<ICredentialService>();
        credentialService.Setup(s => s.HasValidToken()).Returns(hasValidToken);

        var navigationService = new Mock<INavigationService>();

        var pricingService = new Mock<IPricingService>();
        pricingService.Setup(s => s.Source).Returns(PricingSource.Unknown);
        pricingService.Setup(s => s.LastFetch).Returns((DateTimeOffset?)null);

        var historyService = new Mock<IUsageHistoryService>();

        var store = sessionNameStore ?? CreateSessionNameStore();
        var jsonl = jsonlService ?? new Mock<IJsonlService>();
        // Only set up default Sessions if no custom mock was provided — prevents overwriting caller's setup.
        if (jsonlService == null)
            jsonl.Setup(s => s.Sessions).Returns(Array.Empty<SessionInfo>());
        var dispatcher = dispatcherQueue ?? new Mock<IDispatcherQueue>();

        var apiService = apiServiceMock ?? new Mock<IClaudeApiService>();
        var usageNotifications = new Mock<IUsageNotificationService>();

        return new SettingsViewModel(
            settingsService.Object,
            credentialService.Object,
            navigationService.Object,
            pricingService.Object,
            historyService.Object,
            store.Object,
            jsonl.Object,
            dispatcher.Object,
            apiService.Object,   // ORGID-01
            usageNotifications.Object);
    }

    // ─── Existing tab visibility tests (indexes shifted: About is now 4) ──────────

    [Fact]
    public void TabSwitching_DefaultIndex_GeneralTabVisible()
    {
        var vm = CreateViewModel();

        Assert.Equal(SettingsViewModel.GeneralTabIndex, vm.SelectedTabIndex);
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

        vm.SelectedTabIndex = SettingsViewModel.UpdatesTabIndex;

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

        vm.SelectedTabIndex = SettingsViewModel.AccountTabIndex;

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

        vm.SelectedTabIndex = SettingsViewModel.SessionsTabIndex;

        Assert.False(vm.IsGeneralTabVisible);
        Assert.False(vm.IsUpdatesTabVisible);
        Assert.False(vm.IsAccountTabVisible);
        Assert.True(vm.IsSessionsTabVisible);
        Assert.False(vm.IsAboutTabVisible);
    }

    // ─── Phase 26 new tests ───────────────────────────────────────────────────────

    /// <summary>Phase 26: the Sessions tab index shows Sessions, not About.</summary>
    [Fact]
    public void TabIndex_Sessions_IsSessionsTab_NotAbout()
    {
        var vm = CreateViewModel();
        vm.SelectedTabIndex = SettingsViewModel.SessionsTabIndex;

        Assert.True(vm.IsSessionsTabVisible);
        Assert.False(vm.IsAboutTabVisible);
    }

    /// <summary>Phase 26: the About tab index shows About, not Sessions.</summary>
    [Fact]
    public void TabIndex_About_IsAboutTab()
    {
        var vm = CreateViewModel();
        vm.SelectedTabIndex = SettingsViewModel.AboutTabIndex;

        Assert.True(vm.IsAboutTabVisible);
        Assert.False(vm.IsSessionsTabVisible);
    }

    /// <summary>
    /// Finding 43: the visibility getters used to hardcode 3 and 4 while the constants sat fifteen
    /// lines above. This pins every getter to its constant, so renumbering a tab cannot make the
    /// panel and the About-tab timer disagree.
    /// </summary>
    [Fact]
    public void TabVisibility_FollowsTheNamedConstants_NotLiterals()
    {
        var vm = CreateViewModel();

        var visibilityByIndex = new Dictionary<int, Func<bool>>
        {
            [SettingsViewModel.GeneralTabIndex]  = () => vm.IsGeneralTabVisible,
            [SettingsViewModel.UpdatesTabIndex]  = () => vm.IsUpdatesTabVisible,
            [SettingsViewModel.AccountTabIndex]  = () => vm.IsAccountTabVisible,
            [SettingsViewModel.SessionsTabIndex] = () => vm.IsSessionsTabVisible,
            [SettingsViewModel.AboutTabIndex]    = () => vm.IsAboutTabVisible,
        };

        Assert.Equal(5, visibilityByIndex.Count);   // all five constants are distinct

        foreach (var (selectedIndex, isVisible) in visibilityByIndex)
        {
            vm.SelectedTabIndex = selectedIndex;

            foreach (var (candidateIndex, candidateVisible) in visibilityByIndex)
            {
                Assert.Equal(candidateIndex == selectedIndex, candidateVisible());
            }
        }
    }

    /// <summary>Phase 26: SessionRenameItems populated from IJsonlService.Sessions with correct CustomName values.</summary>
    [Fact]
    public void RefreshSessionRenameItems_PopulatesFromJsonlService()
    {
        var store = CreateSessionNameStore();
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
        var store = CreateSessionNameStore();

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
        var store = CreateSessionNameStore();

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
        var store = CreateSessionNameStore();

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

        var store = CreateSessionNameStore();
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

    // ─── Remediation wave 2: Sessions-tab wiring, persistence failures, language switch ──────────

    /// <summary>
    /// Finding 11: the Clear button used to bind through DataContext.ClearSessionCustomNameCommand
    /// with ElementName — a page-level target no DataTemplate namescope can reach, on a page that
    /// never sets DataContext, so the command resolved to null and the button did nothing. The
    /// command now travels on the row, which is what makes the wiring assertable here; the XAML side
    /// is a compile-checked x:Bind against these two members.
    /// </summary>
    [Fact]
    public void RefreshSessionRenameItems_WiresClearCommandAndLabelOntoEveryRow()
    {
        var store = CreateSessionNameStore();
        store.Setup(s => s.GetCustomName("session-1")).Returns("Custom1");
        store.Setup(s => s.GetKnownSessionIds()).Returns(new[] { "session-1", "gone-session" });
        store.Setup(s => s.GetCustomName("gone-session")).Returns("Orphan name");

        var jsonl = new Mock<IJsonlService>();
        jsonl.Setup(s => s.Sessions).Returns(new[]
        {
            new SessionInfo { Id = "session-1", Cwd = "/projects/alpha", DisplayName = "Project Alpha" }
        });

        var vm = CreateViewModel(sessionNameStore: store, jsonlService: jsonl);
        vm.SelectedTabIndex = SettingsViewModel.SessionsTabIndex;

        Assert.Equal(2, vm.SessionRenameItems.Count);   // one live, one orphan
        Assert.Contains(vm.SessionRenameItems, r => r.IsOrphan && r.SessionId == "gone-session");

        foreach (var row in vm.SessionRenameItems)
        {
            Assert.Same(vm.ClearSessionCustomNameCommand, row.ClearCustomNameCommand);
            Assert.False(string.IsNullOrWhiteSpace(row.ClearCustomNameLabel));
        }
    }

    /// <summary>
    /// Finding 11 continued: invoking the row's own command must reach the store, i.e. the projected
    /// reference really is the executable command and not a stale copy.
    /// </summary>
    [Fact]
    public async Task RowClearCommand_ExecutesAgainstTheStore()
    {
        var store = CreateSessionNameStore();
        var jsonl = new Mock<IJsonlService>();
        jsonl.Setup(s => s.Sessions).Returns(new[]
        {
            new SessionInfo { Id = "session-1", Cwd = "/projects/alpha", DisplayName = "Project Alpha" }
        });

        var vm = CreateViewModel(sessionNameStore: store, jsonlService: jsonl);
        vm.SelectedTabIndex = SettingsViewModel.SessionsTabIndex;
        var row = vm.SessionRenameItems.Single();

        // The command the Button actually invokes, reached exactly as x:Bind reaches it.
        var command = Assert.IsAssignableFrom<IAsyncRelayCommand>(row.ClearCustomNameCommand);
        await command.ExecuteAsync(row);

        store.Verify(s => s.ClearCustomName("session-1"), Times.Once);
        store.Verify(s => s.SaveAsync(default), Times.Once);
    }

    /// <summary>
    /// Finding 30: orphan discovery asks the store for its keys instead of rebuilding the
    /// session-names.json path and re-parsing the file behind the store's back.
    /// </summary>
    [Fact]
    public void RefreshSessionRenameItems_ReadsOrphansFromTheStore_NotFromDisk()
    {
        var store = CreateSessionNameStore();
        store.Setup(s => s.GetKnownSessionIds()).Returns(new[] { "orphan-1" });
        store.Setup(s => s.GetCustomName("orphan-1")).Returns("Renamed orphan");

        var vm = CreateViewModel(sessionNameStore: store);
        vm.SelectedTabIndex = SettingsViewModel.SessionsTabIndex;

        var orphan = Assert.Single(vm.SessionRenameItems);
        Assert.True(orphan.IsOrphan);
        Assert.Equal("orphan-1", orphan.SessionId);
        Assert.Equal("Renamed orphan", orphan.CustomName);
        store.Verify(s => s.GetKnownSessionIds(), Times.Once);
    }

    /// <summary>
    /// Finding 25: a failed write is rolled back inside the store, so the row must re-read the
    /// persisted value instead of keeping the optimistic one, and the user must be told.
    /// </summary>
    [Fact]
    public async Task SaveSessionCustomName_WhenPersistenceFails_RestoresStoreValueAndSurfacesError()
    {
        var store = CreateSessionNameStore();
        store.Setup(s => s.SaveAsync(default)).ReturnsAsync(false);
        store.Setup(s => s.GetCustomName("proj-9")).Returns("Name that is on disk");

        var vm = CreateViewModel(sessionNameStore: store);
        var item = new SessionRenameItem
        {
            SessionId = "proj-9",
            DefaultName = "Project",
            CustomName = "Name that never landed"
        };

        await vm.SaveSessionCustomNameCommand.ExecuteAsync(item);

        Assert.Equal("Name that is on disk", item.CustomName);
        Assert.True(vm.IsErrorVisible);
        Assert.False(string.IsNullOrWhiteSpace(vm.ErrorMessage));
    }

    /// <summary>Finding 25: the same contract for the Clear path, where the rollback restores a name.</summary>
    [Fact]
    public async Task ClearSessionCustomName_WhenPersistenceFails_RestoresStoreValueAndSurfacesError()
    {
        var store = CreateSessionNameStore();
        store.Setup(s => s.SaveAsync(default)).ReturnsAsync(false);
        store.Setup(s => s.GetCustomName("proj-9")).Returns("Still named on disk");

        var vm = CreateViewModel(sessionNameStore: store);
        var item = new SessionRenameItem
        {
            SessionId = "proj-9",
            DefaultName = "Project",
            CustomName = "Still named on disk"
        };

        await vm.ClearSessionCustomNameCommand.ExecuteAsync(item);

        Assert.Equal("Still named on disk", item.CustomName);
        Assert.True(vm.IsErrorVisible);
    }

    /// <summary>Finding 25: a successful save leaves no error behind and keeps the sanitized value.</summary>
    [Fact]
    public async Task SaveSessionCustomName_WhenPersistenceSucceeds_KeepsValueAndShowsNoError()
    {
        var store = CreateSessionNameStore();

        var vm = CreateViewModel(sessionNameStore: store);
        var item = new SessionRenameItem
        {
            SessionId = "proj-10",
            DefaultName = "Project",
            CustomName = "Fresh name"
        };

        await vm.SaveSessionCustomNameCommand.ExecuteAsync(item);

        Assert.Equal("Fresh name", item.CustomName);
        Assert.False(vm.IsErrorVisible);
        store.Verify(s => s.GetCustomName(It.IsAny<string>()), Times.Never);
    }

    /// <summary>
    /// Finding 22: the language is persisted only after the switch succeeded. The old code called
    /// SaveSettings unconditionally, so settings.json could claim a language the screen never showed.
    /// </summary>
    [Fact]
    public void LanguageSwitch_WhenLocalizerSucceeds_PersistsTheCode()
    {
        var persisted = new AppSettings();
        var settingsService = new Mock<ISettingsService>();
        settingsService.Setup(s => s.LoadSettings()).Returns(persisted);

        var vm = CreateViewModel(settingsServiceMock: settingsService);
        vm.LanguageSwitcher = _ => Task.CompletedTask;

        vm.SelectedLanguageIndex = EnglishLanguageIndex;

        Assert.Equal(AppSettings.EnglishLanguage, persisted.Language);
        settingsService.Verify(s => s.SaveSettings(persisted), Times.Once);
        Assert.False(vm.IsErrorVisible);
        Assert.Equal(EnglishLanguageIndex, vm.SelectedLanguageIndex);
    }

    /// <summary>
    /// Finding 22: a failing switch must not be persisted, must not be swallowed, and must leave the
    /// dropdown on the language that is actually active.
    /// </summary>
    [Fact]
    public void LanguageSwitch_WhenLocalizerThrows_DoesNotPersist_RevertsAndSurfacesError()
    {
        var persisted = new AppSettings();
        var settingsService = new Mock<ISettingsService>();
        settingsService.Setup(s => s.LoadSettings()).Returns(persisted);

        var vm = CreateViewModel(settingsServiceMock: settingsService);
        vm.LanguageSwitcher = _ => Task.FromException(new InvalidOperationException("RPC_E_WRONG_THREAD"));

        vm.SelectedLanguageIndex = EnglishLanguageIndex;

        settingsService.Verify(s => s.SaveSettings(It.IsAny<AppSettings>()), Times.Never);
        Assert.Equal(AppSettings.DefaultLanguage, persisted.Language);
        Assert.True(vm.IsErrorVisible);
        Assert.False(string.IsNullOrWhiteSpace(vm.ErrorMessage));
        Assert.Equal(GermanLanguageIndex, vm.SelectedLanguageIndex);
    }

    /// <summary>Finding 22: the revert must not re-enter the failing switch in a loop.</summary>
    [Fact]
    public void LanguageSwitch_WhenLocalizerThrows_AttemptsTheSwitchExactlyOnce()
    {
        var vm = CreateViewModel();
        var attempts = 0;
        vm.LanguageSwitcher = _ =>
        {
            attempts++;
            return Task.FromException(new InvalidOperationException("boom"));
        };

        vm.SelectedLanguageIndex = EnglishLanguageIndex;

        Assert.Equal(1, attempts);
    }

    /// <summary>
    /// Finding 18: usage_cache.json outlived the session, so the next account saw the previous
    /// account's utilization until its first poll returned. ClearHistory must still run first (D-13).
    /// </summary>
    [Fact]
    public void Logout_PurgesTheUsageCache()
    {
        var apiService = new Mock<IClaudeApiService>();

        var vm = CreateViewModel(apiServiceMock: apiService);
        vm.LogoutCommand.Execute(null);

        apiService.Verify(s => s.ClearCache(), Times.Once);
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

        // Finding 21: the last label was the German literal "Manuell" on an app whose default
        // language is en-US. It now comes from the RefreshIntervalManual resw key, and headless tests
        // have no localizer host — so only its presence is asserted here, exactly as the
        // LastFetchRelativeTime tests do. The translated values are covered by ResourceCoverageTests.
        Assert.False(string.IsNullOrWhiteSpace(vm.RefreshOptions[5].Label));
        Assert.Equal(AppSettings.ManualRefreshSeconds, vm.RefreshOptions[5].Seconds);
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
