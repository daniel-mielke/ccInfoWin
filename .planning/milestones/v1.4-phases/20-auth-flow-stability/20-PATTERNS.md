# Phase 20: Auth Flow Stability — Pattern Map

**Mapped:** 2026-05-06
**Files analyzed:** 8 (4 source edits + 2 resw edits + 2 test scaffolds)
**Analogs found:** 8 / 8 (all in-repo)

---

## File Classification

| New / Modified File | Role | Data Flow | Closest Analog | Match Quality |
|---------------------|------|-----------|----------------|---------------|
| `ViewModels/MainViewModel.cs` (modify) | viewmodel | event-driven (messenger Receive) + request-response (Refresh command) | `ViewModels/LoginViewModel.cs` (`_loginHandled` flag pattern) | exact (one-shot bool flag) |
| `ViewModels/LoginViewModel.cs` (modify) | viewmodel | event-driven (NavigationCompleted) | self — extend existing `HandleNavigationCompleted` | exact (existing method) |
| `Services/NavigationService.cs` (modify) | service | request-response (one-shot navigate) | self — extend existing `NavigateTo<TPage>` | exact (existing method) |
| `Views/LoginView.xaml` (modify) | view | declarative markup | `Views/MainView.xaml:606-618` (FooterRefreshButton) | exact (icon button) |
| `Views/LoginView.xaml.cs` (modify) | view code-behind | request-response (click handler) | `Views/LoginView.xaml.cs:25-37` (existing `OnLoaded`) | exact (same file, sibling method) |
| `Strings/en-US/Resources.resw` (modify) | localization | static markup | `Resources.resw:101-106` (FooterRefreshButton keys) | exact (same naming convention) |
| `Strings/de-DE/Resources.resw` (modify) | localization | static markup | `Resources.resw:101-106` DE (FooterRefreshButton keys) | exact (same naming convention) |
| `CCInfoWindows.Tests/ViewModels/MainViewModelAuthFlowTests.cs` (NEW) | test | unit | `CCInfoWindows.Tests/ViewModels/SettingsViewModelTests.cs:13-32` (full-DI mock factory) | role-match (better than `MainViewModelStatisticsTests` harness — see note below) |

---

## Pattern Assignments

### `ViewModels/MainViewModel.cs` — `_autoReauthAttempted` flag + `Receive` extension

**Analog:** `ViewModels/LoginViewModel.cs:133` (`_loginHandled` precedent)

**Field declaration pattern** (from `LoginViewModel.cs:133`):
```csharp
private bool _loginHandled;
```
Phase 20 mirror: declare `private bool _autoReauthAttempted;` as a peer of the existing private fields in `MainViewModel` (the constructor block lives at `MainViewModel.cs:257-282`; field can sit near the other private fields above the constructor — match local file convention).

**Reset-on-re-entry pattern** (from `LoginViewModel.cs:94`):
```csharp
// Reset login state for re-entry (e.g., after logout)
_loginHandled = false;
```
Phase 20 mirror — reset at the 4 D-02 sites:

1. **Constructor default** — `bool` field default is already `false`; no code needed.
2. **`PollUsageAsync` success path** — insert immediately after `UpdateUsageProperties(result);` (`MainViewModel.cs:410`):
   ```csharp
   UpdateUsageProperties(result);
   _autoReauthAttempted = false;  // D-02: HTTP 200 resets the auto-reauth budget
   ```
3. **`Logout` command** (`MainViewModel.cs:868-877`) — add before or after `IsSessionExpired = false;`:
   ```csharp
   _autoReauthAttempted = false;  // D-02: explicit reset on user-driven logout
   ```
4. **`Receive(true)` branch** (new — see below).

**Existing `Receive` to extend** (`MainViewModel.cs:929-936`):
```csharp
public void Receive(AuthStateChangedMessage message)
{
    if (!message.Value)
    {
        IsSessionExpired = true;
        StatusMessage = "Session expired. Please re-login to continue.";
    }
}
```

**Phase 20 extended shape** (D-01 + D-03):
```csharp
public void Receive(AuthStateChangedMessage message)
{
    if (message.Value)
    {
        // D-03: post-login refresh
        IsSessionExpired = false;
        HasApiError = false;
        _autoReauthAttempted = false;
        RefreshCommand.ExecuteAsync(null);   // generated symbol — see RESEARCH §Pattern 4
        return;
    }

    // D-01: first 401 → auto-navigate
    if (!_autoReauthAttempted)
    {
        _autoReauthAttempted = true;
        _navigationService.NavigateTo<LoginView>();
        return;
    }

    // Second 401 (and beyond): existing InfoBar fallback
    IsSessionExpired = true;
    StatusMessage = "Session expired. Please re-login to continue.";
}
```

**`[RelayCommand]` from `Receive` precedent** — the existing `Refresh` method at `MainViewModel.cs:850-854`:
```csharp
[RelayCommand]
private async Task Refresh()
{
    await PollUsageAsync();
}
```
generates `RefreshCommand` (NOT `RefreshUsageCommand`). Call shape from `Receive`: `RefreshCommand.ExecuteAsync(null);` (fire-and-forget is acceptable here per CommunityToolkit.Mvvm `IAsyncRelayCommand` contract; `Receive` is `void`).

---

### `Services/NavigationService.cs` — Background-window activation (D-09)

**Analog:** self (extend existing method).

**Existing `NavigateTo<TPage>`** (`NavigationService.cs:22-29`):
```csharp
public void NavigateTo<TPage>() where TPage : Page
{
    Debug.Assert(_frame is not null, "NavigationService.Initialize must be called before NavigateTo");
    _frame?.Navigate(
        typeof(TPage),
        null,
        new SlideNavigationTransitionInfo { Effect = SlideNavigationTransitionEffect.FromRight });
}
```

**Phase 20 extended shape:**
```csharp
public void NavigateTo<TPage>() where TPage : Page
{
    Debug.Assert(_frame is not null, "NavigationService.Initialize must be called before NavigateTo");
    App.MainWindow?.Activate();   // D-09: AUTH-05 background-minimized window restoration
    _frame?.Navigate(
        typeof(TPage),
        null,
        new SlideNavigationTransitionInfo { Effect = SlideNavigationTransitionEffect.FromRight });
}
```

**`App.MainWindow` is already exposed as static** (`App.xaml.cs:19`):
```csharp
public static Window? MainWindow { get; private set; }
```
and assigned in `OnLaunched` at line 60 (`MainWindow = _window;`). Activation precedent: `App.xaml.cs:61` (`_window.Activate();`) is Microsoft's WinUI 3 sample idiom.

**Add using** if not already imported: `using CCInfoWindows;` is implicit (same root namespace via `namespace CCInfoWindows.Services;`) — no new using directive needed. The `App` class is reachable as `CCInfoWindows.App` and the `App.MainWindow?.Activate()` call compiles directly.

---

### `Views/LoginView.xaml` — Reload button overlay + WebView2 visibility gate

**Analog:** `Views/MainView.xaml:606-618` (FooterRefreshButton).

**Footer refresh button reference excerpt** (mirror this exactly, swap glyph + Uid):
```xml
<Button l:Uids.Uid="FooterRefreshButton"
        ToolTipService.ToolTip="Refresh"
        Command="{x:Bind ViewModel.RefreshCommand}"
        Background="Transparent" BorderThickness="0"
        Padding="8" CornerRadius="6">
    <FontIcon x:Name="RefreshIcon" Glyph="&#xE895;" FontSize="16"
              Foreground="{ThemeResource SecondaryTextBrush}"
              RenderTransformOrigin="0.5,0.5">
        <FontIcon.RenderTransform>
            <RotateTransform x:Name="RefreshIconTransform" Angle="0" />
        </FontIcon.RenderTransform>
    </FontIcon>
</Button>
```

**Existing `LoginView.xaml` to modify** (full file is short — lines 1-38):
```xml
<Grid>
    <!-- Full-window WebView2 for claude.ai login -->
    <WebView2
        x:Name="LoginWebView"
        HorizontalAlignment="Stretch"
        VerticalAlignment="Stretch" />

    <!-- Loading overlay -->
    <Grid
        Background="{ThemeResource ApplicationPageBackgroundThemeBrush}"
        Visibility="{x:Bind ViewModel.IsLoading, Mode=OneWay, Converter={StaticResource BoolToVisibilityConverter}}">
        <ProgressRing ... />
    </Grid>

    <!-- Error message -->
    <InfoBar ... VerticalAlignment="Top" Margin="8" />
</Grid>
```

**Phase 20 deltas to `LoginView.xaml`:**

1. **Add the `WinUI3Localizer` xmlns** at the top (page declaration block, lines 2-8). Mirror the import seen in `MainView.xaml:12` (where `l:Uids.Uid` is used). The exact namespace prefix declaration pattern in MainView is the canonical form — copy that line into LoginView.

2. **Add `Visibility="Collapsed"`** to `LoginWebView` (D-07):
   ```xml
   <WebView2
       x:Name="LoginWebView"
       HorizontalAlignment="Stretch"
       VerticalAlignment="Stretch"
       Visibility="Collapsed" />
   ```
   (D-08 alternative — bind Visibility to the inverse of `IsLoading` via a converter; planner picks the simpler shape. The existing loading-overlay binding at line 20 already uses `BoolToVisibilityConverter` against `IsLoading`, so the inverse-bound shape on `LoginWebView` would be a sibling pattern.)

3. **Add the reload button** as the LAST child of the root `Grid` (so Z-order floats on top per D-04):
   ```xml
   <!-- Reload button overlay (top-right) -->
   <Button l:Uids.Uid="LoginReloadButton"
           Click="OnReloadLoginClicked"
           Background="Transparent" BorderThickness="0"
           Padding="8" CornerRadius="6"
           HorizontalAlignment="Right" VerticalAlignment="Top" Margin="8">
       <FontIcon Glyph="&#xE72C;" FontSize="16"
                 Foreground="{ThemeResource SecondaryTextBrush}" />
   </Button>
   ```

   Differences from FooterRefreshButton:
   - Glyph: `&#xE72C;` (page reload) instead of `&#xE895;` (data refresh sync)
   - No `RotateTransform` (no spin animation — D-06 explicit)
   - `Click="OnReloadLoginClicked"` instead of `Command=` (need direct WebView2 access in code-behind per D-06)
   - Top-right overlay alignment (D-04)
   - `l:Uids.Uid="LoginReloadButton"` resolves the resw keys authored below

---

### `Views/LoginView.xaml.cs` — `OnReloadLoginClicked` handler

**Analog:** self — sibling of existing `OnLoaded` at `LoginView.xaml.cs:25-37`.

**Existing `OnLoaded` shape** (the precedent for "code-behind owns direct WebView2 reference"):
```csharp
private async void OnLoaded(object sender, RoutedEventArgs e)
{
    try
    {
        await ViewModel.InitializeWebViewAsync(LoginWebView);
    }
    catch (Exception ex)
    {
        Debug.WriteLine($"[LoginView] OnLoaded failed: {ex.Message}");
    }
}
```

**Phase 20 sibling method** (D-06 — double null guard, no try/catch needed; `?.` chain is the guard):
```csharp
private void OnReloadLoginClicked(object sender, RoutedEventArgs e)
{
    LoginWebView?.CoreWebView2?.Reload();
}
```

`CoreWebView2` is null until `EnsureCoreWebView2Async` resolves (`LoginViewModel.cs:212-220`); the `?.` chain handles the early-click pitfall. No retry, no busy state per D-06.

---

### `ViewModels/LoginViewModel.cs` — `IsLoading` semantics extension (D-08)

**Analog:** self — extend `HandleNavigationCompleted` at line 138-150.

**Existing handler:**
```csharp
public async void HandleNavigationCompleted(WebView2 sender, CoreWebView2NavigationCompletedEventArgs args)
{
    try
    {
        if (_loginHandled || sender.CoreWebView2 is null) return;
        await TryExtractSessionCookieAsync(sender.CoreWebView2, sender.CoreWebView2.Source ?? "");
    }
    catch (Exception ex)
    {
        ErrorMessage = "Login processing failed.";
        System.Diagnostics.Debug.WriteLine($"[LoginViewModel] HandleNavigationCompleted: {ex.Message}");
    }
}
```

**Existing `InitializeWebViewAsync` flips `IsLoading=false` at line 102** — Phase 20 must REMOVE that immediate flip and instead defer it to `HandleNavigationCompleted` once the login URL has loaded successfully.

**Phase 20 extended `HandleNavigationCompleted`:**
```csharp
public async void HandleNavigationCompleted(WebView2 sender, CoreWebView2NavigationCompletedEventArgs args)
{
    try
    {
        if (_loginHandled || sender.CoreWebView2 is null) return;

        // D-08: reveal WebView2 only when the login URL itself has loaded successfully
        var source = sender.CoreWebView2.Source ?? "";
        if (args.IsSuccess &&
            source.StartsWith("https://claude.ai/login", StringComparison.OrdinalIgnoreCase))
        {
            IsLoading = false;
        }

        await TryExtractSessionCookieAsync(sender.CoreWebView2, source);
    }
    catch (Exception ex)
    {
        ErrorMessage = "Login processing failed.";
        System.Diagnostics.Debug.WriteLine($"[LoginViewModel] HandleNavigationCompleted: {ex.Message}");
    }
}
```

**Corresponding edit to `InitializeWebViewAsync`** — REMOVE `IsLoading = false;` at line 102 (the line right after `webView.CoreWebView2.Navigate("https://claude.ai/login");`). The flag now stays `true` until the login URL navigates successfully.

**Note on Discretion §1** (CONTEXT): planner can rename `IsLoading` to `IsWebViewReady` (inverted) if the read becomes confusing. The existing XAML binding `Visibility="{x:Bind ViewModel.IsLoading, Mode=OneWay, Converter={StaticResource BoolToVisibilityConverter}}"` (line 20) is `Visible when IsLoading=true` — keeping `IsLoading` is the cheaper edit because the binding doesn't need to change.

---

### `Strings/en-US/Resources.resw` and `Strings/de-DE/Resources.resw` — `LoginReloadButton` keys

**Analog:** `Strings/en-US/Resources.resw:101-106` (and DE counterpart at 101-106).

**Existing FooterRefreshButton entries (en-US):**
```xml
<data name="FooterRefreshButton.[using:Microsoft.UI.Xaml.Controls]ToolTipService.ToolTip" xml:space="preserve">
    <value>Refresh</value>
</data>
<data name="FooterRefreshButton.[using:Microsoft.UI.Xaml.Automation]AutomationProperties.Name" xml:space="preserve">
    <value>Refresh</value>
</data>
```

**Phase 20 mirrors (en-US — values from spec FEAT-16):**
```xml
<!-- LoginView reload button -->
<data name="LoginReloadButton.[using:Microsoft.UI.Xaml.Controls]ToolTipService.ToolTip" xml:space="preserve">
    <value>Reload page</value>
</data>
<data name="LoginReloadButton.[using:Microsoft.UI.Xaml.Automation]AutomationProperties.Name" xml:space="preserve">
    <value>Reload login page</value>
</data>
```

**Phase 20 mirrors (de-DE):**
```xml
<!-- LoginView reload button -->
<data name="LoginReloadButton.[using:Microsoft.UI.Xaml.Controls]ToolTipService.ToolTip" xml:space="preserve">
    <value>Seite neu laden</value>
</data>
<data name="LoginReloadButton.[using:Microsoft.UI.Xaml.Automation]AutomationProperties.Name" xml:space="preserve">
    <value>Login-Seite neu laden</value>
</data>
```

**Placement:** add as a new block near the existing footer-button block (after line 118 in both files — append before the closing `</root>` tag). Match the indentation (2 spaces) and the `<!-- comment -->` block-comment style used by the existing footer block.

**Coordination with Phase 23:** RESEARCH Open Question §1 recommends Phase 20 self-contains these 2 keys × 2 locales = 4 entries. Planner adopts that recommendation — Phase 23 will author OTHER unrelated keys (e.g., `NotSignedIn`, `NoData`, `Loading`, `InactiveSessionTooltip`) and will not collide.

---

### `CCInfoWindows.Tests/ViewModels/MainViewModelAuthFlowTests.cs` (NEW) — Wave-0 unit tests

**Analog:** `CCInfoWindows.Tests/ViewModels/SettingsViewModelTests.cs:13-32` (full-DI mock factory) — **NOT** the `MainViewModelTestHarness` at `MainViewModelStatisticsTests.cs:105-131`.

**Why not the harness?** The existing `MainViewModelTestHarness` is a stub class that only re-implements `ApplyStatistics` independently; it does NOT instantiate a real `MainViewModel`. For Phase 20 we need to drive the actual `Receive` method on a real `MainViewModel` instance and verify `INavigationService.NavigateTo<LoginView>()` invocations — a real VM with all 10 mocked dependencies is required.

**Test factory pattern from `SettingsViewModelTests.cs:13-32`** (mock + return new VM):
```csharp
private static SettingsViewModel CreateViewModel(bool hasValidToken = true)
{
    var settingsService = new Mock<ISettingsService>();
    settingsService.Setup(s => s.LoadSettings()).Returns(new AppSettings());

    var credentialService = new Mock<ICredentialService>();
    credentialService.Setup(s => s.HasValidToken()).Returns(hasValidToken);

    var navigationService = new Mock<INavigationService>();
    var pricingService = new Mock<IPricingService>();
    pricingService.Setup(s => s.Source).Returns(PricingSource.Unknown);
    pricingService.Setup(s => s.LastFetch).Returns((DateTimeOffset?)null);

    return new SettingsViewModel(
        settingsService.Object,
        credentialService.Object,
        navigationService.Object,
        pricingService.Object);
}
```

**Phase 20 mirror** — `MainViewModel` constructor at `MainViewModel.cs:257-282` takes 10 services. Mock all 10:
```csharp
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
        burnRate.Object);

    return (vm, navigationService);
}
```

**`[Fact]` shape from `SettingsViewModelTests.cs:34-44`:**
```csharp
[Fact]
public void TabSwitching_DefaultIndex_GeneralTabVisible()
{
    var vm = CreateViewModel();
    Assert.Equal(0, vm.SelectedTabIndex);
}
```

**Phase 20 test contracts** (one `[Fact]` per AUTH requirement, AUTH-01..AUTH-04):
```csharp
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
    vm.Receive(new AuthStateChangedMessage(false));   // first → navigate
    vm.Receive(new AuthStateChangedMessage(false));   // second → InfoBar
    nav.Verify(n => n.NavigateTo<LoginView>(), Times.Once);
    Assert.True(vm.IsSessionExpired);
}

[Fact]
public void Receive_True_ClearsFlagsAndFiresRefresh()
{
    var (vm, nav) = CreateViewModel();
    vm.Receive(new AuthStateChangedMessage(false));   // arm: _autoReauthAttempted = true
    vm.Receive(new AuthStateChangedMessage(true));    // disarm + refresh
    Assert.False(vm.IsSessionExpired);
    Assert.False(vm.HasApiError);
    // Optional: verify RefreshCommand fired (apiService.Verify FetchUsageAsync called)
}

// AUTH-03 reset sites:
//   - PollUsageAsync success: harder to drive without DispatcherQueue — defer or use Logout proxy
//   - Logout: drive vm.LogoutCommand.Execute(null), then verify next Receive(false) navigates again
//   - Receive(true) reset: covered by 3rd test above
//   - Constructor default: implicit (covered by 1st test — fresh VM hits the auto-nav branch)
```

**File location:** `D:\myProjects\ccInfoWin\CCInfoWindows.Tests\ViewModels\MainViewModelAuthFlowTests.cs`

**Namespace:** `CCInfoWindows.Tests.ViewModels` (matches `SettingsViewModelTests.cs:6`).

**Optional second file:** RESEARCH §Wave 0 Gaps recommends SKIPPING `NavigationServiceTests.cs` because `App.MainWindow` is a static reference and refactoring for testability is out of scope. Cover AUTH-05 via manual smoke test with `mcp__windows-mcp` instead.

---

## Shared Patterns

### MVVM source-generator conventions
**Source:** `CLAUDE.md` §"MVVM Conventions"; precedent throughout `MainViewModel.cs`, `LoginViewModel.cs`.
**Apply to:** All ViewModel edits.

- `[ObservableProperty]` over `_camelCase` field → generated `PascalCase` property
- `[RelayCommand]` over async/sync method → generated `XxxCommand` (e.g., `Refresh` → `RefreshCommand`)
- Plain private bool fields (no observable wrapping) for one-shot lifecycle flags — `_loginHandled`, new `_autoReauthAttempted`
- No code-behind logic in Views except where direct WebView2 reference is required (`LoginView.xaml.cs` is the only precedent)

### `WeakReferenceMessenger.Default` cross-VM messaging
**Source:** Existing — `MainViewModel.cs:281` (Register), `MainViewModel.cs:929` (Receive), `LoginViewModel.cs:190` (Send), `MainViewModel.cs:874` (Send).
**Apply to:** No new message types in Phase 20 — reuse the existing `AuthStateChangedMessage`.

### Localization via `l:Uids.Uid`
**Source:** `MainView.xaml:42, 51, 58, 68, 90, 606` (samples).
**Apply to:** `LoginView.xaml` reload button — use `l:Uids.Uid="LoginReloadButton"` exactly mirroring `l:Uids.Uid="FooterRefreshButton"`.

### Bash command discipline (project-wide)
**Source:** `CLAUDE.md` §"Bash Permission Rules".
**Apply to:** All build, test, kill commands in plan actions — every command in its own `Bash` tool call; no `;`, `&&`, `||`, `|` chaining.

### Build commands
**Source:** `CLAUDE.md` §"Build Commands".
**Apply to:** Phase 20 verification.

- Debug build: `dotnet build CCInfoWindows/CCInfoWindows/CCInfoWindows.csproj`
- Test run: `dotnet test CCInfoWindows.Tests/CCInfoWindows.Tests.csproj --filter "FullyQualifiedName~MainViewModelAuthFlow"`
- Release build (NEVER `publish`): `dotnet build CCInfoWindows/CCInfoWindows/CCInfoWindows.csproj -c Release -o CCInfoWindows/CCInfoWindows/bin/x64/Release/net9.0-windows10.0.19041.0/`

### Naming + commit conventions
**Source:** `CLAUDE.md`.
**Apply to:** All new commits.

- Conventional commits: `feat:`, `fix:`, `chore:`, `refactor:`, `test:`, `docs:`
- Phase 20 commits will likely use `feat:` (auto-reauth, reload button, post-login refresh) and `test:` (Wave-0 tests)

---

## No Analog Found

None. All 8 files have direct in-repo analogs. Phase 20 is a pure plumbing extension — zero net-new patterns introduced.

---

## Metadata

**Analog search scope:**
- `CCInfoWindows/CCInfoWindows/ViewModels/`
- `CCInfoWindows/CCInfoWindows/Views/`
- `CCInfoWindows/CCInfoWindows/Services/`
- `CCInfoWindows/CCInfoWindows/Strings/{de-DE,en-US}/Resources.resw`
- `CCInfoWindows/CCInfoWindows/App.xaml.cs`
- `CCInfoWindows.Tests/ViewModels/`

**Files scanned:** 11 (all read-confirmed; line numbers are real, not placeholder)

**Pattern extraction date:** 2026-05-06
