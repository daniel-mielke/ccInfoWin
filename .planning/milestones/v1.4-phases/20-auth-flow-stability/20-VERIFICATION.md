---
phase: 20-auth-flow-stability
verified: 2026-05-06T19:42:00+02:00
status: human_needed
score: 5/7
overrides_applied: 0
human_verification:
  - test: "Sign out from MainView. Observe LoginView immediately after the logout transition."
    expected: "Loading overlay (ProgressRing on dark background) is the only visible content. No flash of the prior chat URL. WebView2 login form appears only after NavigationCompleted fires for https://claude.ai/login with args.IsSuccess == true."
    why_human: "InvertedBoolToVisibilityConverter binding and NavigationCompleted gate cannot be exercised in headless xUnit — WinUI 3 requires a real window host and a live WebView2 process."
  - test: "While LoginView shows the login form, locate the reload icon button in the top-right corner (8 px margin). Hover it. Tab to it via keyboard."
    expected: "Tooltip text matches the system language: EN 'Reload page' or DE 'Seite neu laden'. Keyboard focus shows a visible focus ring. Narrator announces EN 'Reload login page' or DE 'Login-Seite neu laden'."
    why_human: "WinUI3Localizer runtime resolution, ToolTipService rendering, and Narrator output require a running app with a real compositor — not verifiable via static analysis."
  - test: "Click the reload button while the login form is fully loaded, then click it again immediately after triggering a fresh logout (while the loading overlay is still visible — i.e. before CoreWebView2 is initialized)."
    expected: "First click: WebView2 page reloads visibly (brief network/render flash). Second click (pre-init): silent no-op — no crash, no error InfoBar."
    why_human: "CoreWebView2.Reload() invocation and the double-null-guard behaviour on an uninitialised WebView2 require a live process."
  - test: "Sign in to MainView with a valid session, wait for usage data to appear, then minimize the window. Force a session expiry (e.g. Settings > Logout while minimized is not accessible — instead, invalidate the session externally and wait for the next poll to fire a 401, or trigger the auth message programmatically)."
    expected: "The app window unminimizes (or at minimum comes to foreground with focus) and displays LoginView without user manually restoring the window."
    why_human: "App.MainWindow?.Activate() minimized-window restore behaviour is API-version-dependent (RESEARCH Open Question #2 / Assumption A4) and cannot be verified without a live WinUI 3 process. PARTIAL result is acceptable per Plan 04 — if Activate() only sets focus without restoring, record and queue a follow-up for AppWindow.Show()."
---

# Phase 20: Auth Flow Stability — Verification Report

**Phase Goal:** First HTTP 401 in a session auto-navigates to LoginView, second 401 falls back to the existing InfoBar, post-login refresh is immediate, and sign-out always presents a clean login form. LoginView shows a manual reload button (top-right) calling CoreWebView2.Reload() with null guard, localized in DE and EN. NavigateTo<TPage> activates the App MainWindow before frame navigation (handles minimized-window scenario).

**Verified:** 2026-05-06T19:42:00+02:00
**Status:** human_needed
**Re-verification:** No — initial verification

---

## Goal Achievement

### Observable Truths

| # | Truth | Status | Evidence |
|---|-------|--------|----------|
| 1 | First 401 triggers automatic navigation to LoginView; InfoBar does NOT appear | ✓ VERIFIED | `MainViewModel.cs:956-960` — `if (!_autoReauthAttempted)` branch sets flag and calls `_navigationService.NavigateTo<LoginView>()` without touching `IsSessionExpired`. Test `Receive_FirstFalse_NavigatesToLoginView_WithoutSettingSessionExpired` GREEN. |
| 2 | Second 401 in the same session opens the InfoBar fallback; navigation does not fire again | ✓ VERIFIED | `MainViewModel.cs:963-965` — else path sets `IsSessionExpired = true`. `_autoReauthAttempted` guard blocks re-navigation. Test `Receive_SecondFalse_OpensInfoBar_WithoutSecondNavigation` GREEN (nav verified `Times.Once`, `IsSessionExpired` true). |
| 3 | After successful login, MainView refreshes immediately via AuthStateChangedMessage handler; `_autoReauthAttempted` resets to false | ✓ VERIFIED | `MainViewModel.cs:939-950` — `Receive(true)` path clears `IsSessionExpired`, `HasApiError`, resets flag, fires `RefreshCommand.ExecuteAsync(null)`. Test `Receive_True_ClearsFlagsAndResetsAutoReauth` GREEN (subsequent `Receive(false)` navigates again, confirming flag reset). |
| 4 | LoginView shows a manual reload button top-right with localized tooltip and AutomationProperties.Name in DE and EN | ✓ VERIFIED (code) / ? HUMAN_NEEDED (runtime) | `LoginView.xaml:48-60` — Button present with `l:Uids.Uid="LoginReloadButton"`, `HorizontalAlignment="Right"`, `VerticalAlignment="Top"`, `Margin="8"`, `Glyph="&#xE72C;"`, `FontSize="16"`. `en-US/Resources.resw:121-126` contains `LoginReloadButton.[using:Microsoft.UI.Xaml.Controls]ToolTipService.ToolTip` = "Reload page" and `AutomationProperties.Name` = "Reload login page". `de-DE/Resources.resw:121-126` contains "Seite neu laden" / "Login-Seite neu laden". Handler `OnReloadLoginClicked` present in `LoginView.xaml.cs:44-47` with double null guard. Tooltip rendering and Narrator output require human check. |
| 5 | After logout, LoginView's WebView2 shows loading overlay only until login URL loads — no leftover chat URL flash | ✓ VERIFIED (code) / ? HUMAN_NEEDED (runtime) | `LoginViewModel.cs:101-106` — premature `IsLoading = false` removed after `Navigate()`. `LoginViewModel.cs:155-159` — `IsLoading = false` set ONLY when `args.IsSuccess && source.StartsWith("https://claude.ai/login", OrdinalIgnoreCase)`. `LoginView.xaml:22` — `LoginWebView.Visibility` bound to `InvertedBoolToVisibilityConverter` (IsLoading=true → Collapsed). Visual confirmation requires human check. |
| 6 | NavigateTo<TPage> activates App.MainWindow before frame navigation | ✓ VERIFIED | `NavigationService.cs:29-30` — `App.MainWindow?.Activate()` appears on line 29, `_frame?.Navigate(...)` on line 30. Ordering confirmed. Runtime minimized-window restore requires human check (AUTH-05). |
| 7 | `_autoReauthAttempted` resets at all four lifecycle sites | ✓ VERIFIED | `MainViewModel.cs:250` — field declared. `:417` — reset after `UpdateUsageProperties(result)` in PollUsageAsync HTTP-200 path. `:883` — reset inside `Logout()`. `:944` — reset in `Receive(AuthStateChangedMessage(true))`. 4 sites confirmed. |

**Score:** 5/7 truths fully verified automatically; 2 additionally need human runtime confirmation.

---

### Required Artifacts

| Artifact | Expected | Status | Details |
|----------|----------|--------|---------|
| `CCInfoWindows/CCInfoWindows/ViewModels/MainViewModel.cs` | `_autoReauthAttempted` field + extended `Receive` handler | ✓ VERIFIED | Field at line 250. `Receive` body lines 937-966 with both D-01 and D-03 branches. |
| `CCInfoWindows.Tests/ViewModels/MainViewModelAuthFlowTests.cs` | 4 `[Fact]` methods, full-DI factory | ✓ VERIFIED | All 4 `[Fact]`s present. `CreateViewModel()` uses 10-service mock factory. `CreateViewModelWithSuccessfulApi()` added for Test 3 (async refresh race fix). 4/4 GREEN. |
| `CCInfoWindows/CCInfoWindows/Strings/en-US/Resources.resw` | `LoginReloadButton.*` keys with EN values | ✓ VERIFIED | Lines 121-126: ToolTip = "Reload page", AutomationName = "Reload login page". |
| `CCInfoWindows/CCInfoWindows/Strings/de-DE/Resources.resw` | `LoginReloadButton.*` keys with DE values | ✓ VERIFIED | Lines 121-126: ToolTip = "Seite neu laden", AutomationName = "Login-Seite neu laden". |
| `CCInfoWindows/CCInfoWindows/Views/LoginView.xaml` | Reload button, InvertedBoolToVisibilityConverter binding, WinUI3Localizer xmlns | ✓ VERIFIED | `xmlns:l="using:WinUI3Localizer"` at line 8. `InvertedBoolToVisibilityConverter` on `LoginWebView.Visibility` at line 22. Reload `Button` at lines 48-60. |
| `CCInfoWindows/CCInfoWindows/Views/LoginView.xaml.cs` | `OnReloadLoginClicked` with double null guard | ✓ VERIFIED | Lines 44-47: `private void OnReloadLoginClicked(object sender, RoutedEventArgs e)` with `LoginWebView?.CoreWebView2?.Reload();`. No try/catch. |
| `CCInfoWindows/CCInfoWindows/ViewModels/LoginViewModel.cs` | `IsLoading = false` only on login-URL NavigationCompleted success; premature flip removed | ✓ VERIFIED | No `IsLoading = false` after `Navigate()` call. `HandleNavigationCompleted` at lines 155-159 gates on `args.IsSuccess && source.StartsWith("https://claude.ai/login", OrdinalIgnoreCase)`. |
| `CCInfoWindows/CCInfoWindows/Services/NavigationService.cs` | `App.MainWindow?.Activate()` before `_frame?.Navigate(...)` | ✓ VERIFIED | Lines 29-30 confirm correct ordering. D-09 comment present. |

---

### Key Link Verification

| From | To | Via | Status | Details |
|------|----|-----|--------|---------|
| `MainViewModel.Receive(false)` first call | `INavigationService.NavigateTo<LoginView>()` | `if (!_autoReauthAttempted)` guard | ✓ WIRED | `MainViewModel.cs:956-960`. Test 1 GREEN. |
| `MainViewModel.Receive(true)` | `RefreshCommand.ExecuteAsync(null)` | fire-and-forget in `Receive` body | ✓ WIRED | `MainViewModel.cs:948`. `RefreshUsageCommand` absent (correct). Test 3 GREEN. |
| `LoginView.xaml` reload `Button` click | `LoginView.xaml.cs:OnReloadLoginClicked` → `CoreWebView2.Reload()` | `Click="OnReloadLoginClicked"` routed event | ✓ WIRED | XAML line 49, code-behind line 44. |
| `LoginView.xaml` `l:Uids.Uid="LoginReloadButton"` | `en-US/de-DE Resources.resw` `LoginReloadButton.*` keys | WinUI3Localizer runtime lookup | ✓ WIRED (static) / ? HUMAN (runtime) | Keys exist in both resw files with correct names and values. Runtime resolution requires live app. |
| `LoginView.xaml` `LoginWebView.Visibility` | `LoginViewModel.IsLoading` via `InvertedBoolToVisibilityConverter` | `{x:Bind ViewModel.IsLoading, Converter=...}` | ✓ WIRED | `LoginView.xaml:22`. Converter registered in `App.xaml`. |
| `LoginViewModel.HandleNavigationCompleted` | `IsLoading = false` | `args.IsSuccess && Source.StartsWith(login-url)` gate | ✓ WIRED | `LoginViewModel.cs:155-159`. No premature flip exists. |
| `NavigationService.NavigateTo<TPage>` | `App.MainWindow?.Activate()` | Static property call before `_frame.Navigate` | ✓ WIRED | `NavigationService.cs:29`, before line 30. |

---

### Behavioral Spot-Checks

| Behavior | Command | Result | Status |
|----------|---------|--------|--------|
| 4 AUTH-flow unit tests | `dotnet test --filter "FullyQualifiedName~MainViewModelAuthFlow"` | 4/4 passed, 85 ms | ✓ PASS |
| Main project builds | `dotnet build CCInfoWindows/CCInfoWindows/CCInfoWindows.csproj` | 0 errors, 67 pre-existing warnings | ✓ PASS |
| `_autoReauthAttempted` field declared once | `grep -c "_autoReauthAttempted"` | 6 lines (1 declaration + 5 usages) | ✓ PASS |
| `_autoReauthAttempted = true` exactly once | grep pattern | `MainViewModel.cs:958` only | ✓ PASS |
| `_autoReauthAttempted = false` three explicit sites | grep pattern | Lines 417, 883, 944 | ✓ PASS |
| `RefreshCommand.ExecuteAsync` present, not `RefreshUsageCommand` | grep | Line 948, zero `RefreshUsageCommand` hits | ✓ PASS |
| `App.MainWindow?.Activate()` before Navigate | grep line ordering | Line 29 < line 30 | ✓ PASS |
| No `IsWebViewVisible` field | grep | Zero matches in LoginView.xaml and LoginViewModel.cs | ✓ PASS |

---

### Anti-Patterns Found

| File | Line | Pattern | Severity | Impact |
|------|------|---------|----------|--------|
| `MainViewModel.cs` | 878-883 | `_autoReauthAttempted = false` appears AFTER `Send(AuthStateChangedMessage(false))` in `Logout()`, whereas Plan 02 specified it as the FIRST statement | ⚠ Warning | No user-visible impact. `Send()` calls `Receive(false)` synchronously, which sets the flag to `true`. The reset on line 883 then clears it again. The final state (flag=false, NavigateTo called twice) is identical to the plan's intent and confirmed correct by Test 4 (`Times.Exactly(3)` GREEN). The deviation is implementation-order only, not behavioral. |

---

### Requirements Coverage

| Requirement | Source Plan | Description | Status | Evidence |
|-------------|-------------|-------------|--------|----------|
| AUTH-01 | Plan 20-02 | First 401 → auto-nav to LoginView, no InfoBar | ✓ SATISFIED | `Receive` D-01 branch + Test 1 GREEN |
| AUTH-02 | Plan 20-02 | Second 401 → InfoBar fallback, no nav loop | ✓ SATISFIED | `_autoReauthAttempted` guard + Test 2 GREEN |
| AUTH-03 | Plan 20-02 | Post-login refresh immediate, no app restart | ✓ SATISFIED | `RefreshCommand.ExecuteAsync(null)` in `Receive(true)` + Test 3 GREEN |
| AUTH-04 | Plan 20-02 | `_autoReauthAttempted` resets to false on `Receive(true)` | ✓ SATISFIED | `MainViewModel.cs:944` + Test 3 confirms flag cleared |
| AUTH-05 | Plan 20-04 | Window activation before navigation (minimized scenario) | ✓ SATISFIED (code) / ? NEEDS HUMAN (runtime) | `NavigationService.cs:29`. Whether `Activate()` restores a minimized window requires live test. |
| AUTH-06 | Plans 20-01, 20-03 | Reload button on LoginView with localized tooltip + AutomationName DE/EN | ✓ SATISFIED (code) / ? NEEDS HUMAN (runtime) | XAML, code-behind, and resw all verified. WinUI3Localizer rendering and Narrator require live app. |
| AUTH-07 | Plan 20-03 | Post-logout WebView2 collapsed until login URL loads successfully | ✓ SATISFIED (code) / ? NEEDS HUMAN (runtime) | `InvertedBoolToVisibilityConverter` binding + `HandleNavigationCompleted` gate verified. Visual confirmation requires live app. |

---

### Human Verification Required

The following checks require a running WinUI 3 process. They cannot be verified by static analysis or headless unit tests.

#### 1. AUTH-07 — Post-logout no chat-URL flash

**Test:** Sign out from MainView (Settings > Account > Logout or `vm.LogoutCommand`). Watch LoginView appear.
**Expected:** ProgressRing on `ApplicationPageBackgroundThemeBrush` is the only visible content from the moment of transition. The WebView2 login form appears only after the claude.ai login URL has fully loaded. Zero frames of cached chat content.
**Why human:** `InvertedBoolToVisibilityConverter` binding and the `HandleNavigationCompleted` `args.IsSuccess` gate require a live WebView2 renderer.

#### 2. AUTH-06 — Reload button tooltip + AutomationName + pre-init silent no-op

**Test:** While LoginView shows the login form: hover the reload button (top-right) and inspect tooltip. Tab to it via keyboard and confirm focus ring. Activate Narrator and tab to the button. Then trigger a fresh logout and immediately click the reload button while the loading overlay is still visible (before `EnsureCoreWebView2Async` completes).
**Expected:** Tooltip = "Reload page" (EN) or "Seite neu laden" (DE). Narrator announces "Reload login page" (EN) or "Login-Seite neu laden" (DE). Click while loaded reloads the page visibly. Click before init is a silent no-op — no crash, no error InfoBar.
**Why human:** WinUI3Localizer runtime key resolution, ToolTipService rendering, Narrator output, and pre-init null-guard behaviour all require a live compositor and WebView2 process.

#### 3. AUTH-05 — Minimized-window activation on background 401

**Test:** Sign in to MainView, wait for usage data. Minimize the app window. Force a session expiry externally and wait for the next background poll to trigger a 401 (or invoke `WeakReferenceMessenger.Default.Send(new AuthStateChangedMessage(false))` programmatically).
**Expected:** The app window unminimizes (restores from taskbar) and shows LoginView without the user needing to manually click the taskbar icon.
**Why human:** `App.MainWindow?.Activate()` minimized-window restore behaviour is WinUI 3 API-version-dependent (RESEARCH Open Question #2 / Assumption A4). PARTIAL is acceptable per Plan 04 — if `Activate()` only focuses without restoring, record and queue follow-up for `AppWindow.Show()`.

#### 4. AUTH-01/02 — First vs. second 401 visual confirmation (end-to-end)

**Test:** Cold start with valid token. Invalidate the session out of band. Trigger a refresh (footer Refresh button). Observe. Then sign in successfully. Invalidate again and trigger a second refresh.
**Expected:** First 401 — LoginView appears automatically, SessionExpiredInfoBar does NOT show. Sign-in succeeds, MainView refreshes within ~2 seconds (AUTH-03 implicit check). Second 401 — SessionExpiredInfoBar appears at top of MainView; Re-Login button navigates to LoginView.
**Why human:** End-to-end WebView2 session flow, InfoBar visual state, and the automatic refresh timing require a live process with real authentication.

---

### Gaps Summary

No automated gaps found. All 7 requirements have complete code-level implementations confirmed by grep and by 4/4 unit tests passing. The `human_needed` status reflects AUTH-05/06/07 requiring operator runtime confirmation — as explicitly documented in `20-VALIDATION.md` and `20-04-SUMMARY.md` — not missing implementation.

**Implementation deviation noted (non-blocking):** In `Logout()`, `_autoReauthAttempted = false` appears after `Send(AuthStateChangedMessage(false))` rather than before it as Plan 02 specified. The observed runtime behaviour is identical to the spec (confirmed by Test 4 passing), making this a WARNING-level ordering note, not a gap.

---

_Verified: 2026-05-06T19:42:00+02:00_
_Verifier: Claude (gsd-verifier)_
