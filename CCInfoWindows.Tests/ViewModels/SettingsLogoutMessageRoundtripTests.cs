using CCInfoWindows.Models;
using CCInfoWindows.Services.Interfaces;
using CCInfoWindows.ViewModels;
using CCInfoWindows.Views;
using Moq;

namespace CCInfoWindows.Tests.ViewModels;

/// <summary>
/// 21-03 gap-closure was reverted: MainViewModel is registered AddTransient —
/// WeakReferenceMessenger silently drops the IRecipient registration when the
/// MainViewModel instance is GC-collected after navigating away from MainView.
/// In production the LogoutRequestedMessage round-trip had no live recipient and
/// the user could not log out at all.
///
/// SettingsViewModel.Logout is therefore a direct call sequence, and that sequence
/// has a mandatory ORDER, not just a mandatory set of members (finding 32.1): the
/// D-13 ordering trap is a save racing the credential clear, which re-persists
/// usage-history.json after deletion and leaks the previous account's usage.
///
/// File name retained for git history continuity; test class name updated.
/// </summary>
public class SettingsLogoutDirectCallTests
{
    private const string ClearHistoryCall = "ClearHistory";
    private const string BridgeResetCall = "Bridge.Reset";
    private const string ClearCredentialsCall = "ClearCredentials";
    private const string ClearCacheCall = "ClearCache";
    private const string CancelAllCall = "CancelAll";

    /// <summary>
    /// The whole logout sequence in the order production must run it. Asserted as a sequence because
    /// the previous Times.Once assertions passed for every permutation — including the one that
    /// reintroduces D-13.
    /// </summary>
    private static readonly string[] ExpectedLogoutOrder =
    [
        ClearHistoryCall,
        BridgeResetCall,
        ClearCredentialsCall,
        ClearCacheCall,
        CancelAllCall
    ];

    private static (SettingsViewModel vm, List<string> calls, Mock<INavigationService> navMock) BuildSut()
    {
        var settingsService = new Mock<ISettingsService>();
        settingsService.Setup(s => s.LoadSettings()).Returns(new AppSettings());
        var navMock = new Mock<INavigationService>();
        var pricingService = new Mock<IPricingService>();

        var sessionNameStore = new Mock<ISessionNameStore>();
        var jsonlService = new Mock<IJsonlService>();
        jsonlService.Setup(s => s.Sessions).Returns(Array.Empty<SessionInfo>());
        var dispatcherQueue = new Mock<IDispatcherQueue>();

        // One shared recorder across four mocks — Moq's MockSequence cannot span mocks, and the suite
        // has no other ordering harness.
        var calls = new List<string>();

        var historyMock = new Mock<IUsageHistoryService>();
        historyMock.Setup(h => h.ClearHistory()).Callback(() => calls.Add(ClearHistoryCall));

        var bridgeMock = new Mock<IWebViewBridge>();
        bridgeMock.Setup(b => b.Reset()).Callback(() => calls.Add(BridgeResetCall));

        var credentialMock = new Mock<ICredentialService>();
        credentialMock.Setup(c => c.ClearCredentials()).Callback(() => calls.Add(ClearCredentialsCall));

        var apiMock = new Mock<IClaudeApiService>();
        apiMock.Setup(a => a.ClearCache()).Callback(() => calls.Add(ClearCacheCall));

        var usageNotifications = new Mock<IUsageNotificationService>();
        usageNotifications.Setup(u => u.CancelAll()).Callback(() => calls.Add(CancelAllCall));

        var vm = new SettingsViewModel(
            settingsService.Object,
            credentialMock.Object,
            navMock.Object,
            pricingService.Object,
            historyMock.Object,
            sessionNameStore.Object,
            jsonlService.Object,
            dispatcherQueue.Object,
            apiMock.Object,   // ORGID-01
            usageNotifications.Object,
            bridgeMock.Object);   // Finding 18

        return (vm, calls, navMock);
    }

    [Fact]
    public void Logout_RunsTheWholeSequenceInOrder()
    {
        var (vm, calls, _) = BuildSut();

        vm.LogoutCommand.Execute(null);

        Assert.Equal(ExpectedLogoutOrder, calls);
    }

    [Fact]
    public void Logout_ClearsHistoryBeforeTheCredentials()
    {
        // The D-13 invariant on its own, spelled out so a reorder fails with the reason attached.
        var (vm, calls, _) = BuildSut();

        vm.LogoutCommand.Execute(null);

        Assert.True(
            calls.IndexOf(ClearHistoryCall) < calls.IndexOf(ClearCredentialsCall),
            "ClearHistory must run before ClearCredentials (D-13 ordering trap): a save racing the "
            + "credential clear re-persists usage-history.json after deletion and leaks the previous "
            + $"account's usage. Observed order: {string.Join(" -> ", calls)}");
    }

    [Fact]
    public void Logout_ResetsTheBridgeBeforeThePersistedSnapshotIsDropped()
    {
        // Finding 18: an in-flight bridge fetch that completes after ClearCache would write
        // usage_cache.json back out, and the next account would see the previous one's figures.
        var (vm, calls, _) = BuildSut();

        vm.LogoutCommand.Execute(null);

        Assert.True(
            calls.IndexOf(BridgeResetCall) < calls.IndexOf(ClearCacheCall),
            $"Bridge.Reset must drain in-flight fetches before ClearCache. Observed order: {string.Join(" -> ", calls)}");
    }

    [Fact]
    public void Logout_NavigatesToLoginLast()
    {
        // LoginView's WebView2 init deletes the claude.ai cookies from the shared user data folder,
        // which is what actually ends the browser-side session — it must not run while the bridge
        // still holds that CoreWebView2.
        var (vm, calls, navMock) = BuildSut();

        navMock.Setup(n => n.NavigateTo<LoginView>())
               .Callback(() => Assert.Equal(ExpectedLogoutOrder, calls));

        vm.LogoutCommand.Execute(null);

        navMock.Verify(n => n.NavigateTo<LoginView>(), Times.Once);
    }
}
