# Phase 20: Auth Flow Stability - Research

**Researched:** 2026-05-06
**Domain:** WinUI 3 / .NET 9 / WebView2 / MVVM Toolkit messaging â€” auth flow plumbing
**Confidence:** HIGH (stack and code paths verified directly in repo; spec/CONTEXT/UI-SPEC pre-locked)

## Summary

Phase 20 is **plumbing-heavy / UI-light**. The visible delta is one icon button on `LoginView` plus extending an existing `Visibility` gate. All other work routes existing 401/Auth-state plumbing through new conditional branches in code Claude has already shipped (`MainViewModel.Receive`, `LoginViewModel.HandleNavigationCompleted`, `NavigationService.NavigateTo`, `MainViewModel.Logout`).

The phase boundary is exceptionally well-defined by `20-CONTEXT.md` (D-01..D-09 locked) and `20-UI-SPEC.md` (visual contract approved 6/6 by gsd-ui-checker). This research therefore focuses on **wiring details** the planner needs â€” exact line numbers, exception flows, the existing `_loginHandled` precedent for `_autoReauthAttempted`, and the resw key contract with Phase 23.

**Primary recommendation:** Plan as four small, near-independent task groups: (1) `MainViewModel` 401-routing + flag-reset, (2) `MainViewModel.Receive(true)` post-login refresh, (3) `LoginView` reload button + WebView2 visibility gate (XAML + code-behind + `LoginViewModel`), (4) `NavigationService` window activation. Wire test scaffolding for `Receive(AuthStateChangedMessage)` first â€” the existing test harness pattern (`MainViewModelTestHarness` + Moq + xUnit) already covers everything needed.

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions

**D-01 â€” First-401 detection in `MainViewModel.Receive(AuthStateChangedMessage)`:**
The handler is the single dispatch point for auth-state changes. When `message.Value == false` AND `_autoReauthAttempted == false`: set the flag to true, call `_navigationService.NavigateTo<LoginView>()`, do NOT set `IsSessionExpired`. When `message.Value == false` AND `_autoReauthAttempted == true`: fall through to the existing path (`IsSessionExpired = true`, InfoBar shows). The `WebViewBridge` and `ClaudeApiService` are NOT changed â€” they continue to throw `UnauthorizedAccessException` and send `AuthStateChangedMessage(false)` respectively.

**D-02 â€” `_autoReauthAttempted` resets to `false` on:**
1. The `RefreshUsageAsync` (or wrapping `PollUsageAsync`) success path â€” after a successful HTTP 200 the next 401 is a fresh first-attempt
2. The `Logout` command â€” preempts the case where the user manually re-logs via InfoBar then logs out
3. The `AuthStateChangedMessage(true)` handler (post-login-refresh path)
4. The `MainViewModel` constructor â€” covers cold-start and any future singleton refactor

**D-03 â€” Post-login immediate refresh:** Extend `Receive(AuthStateChangedMessage)` to also handle `message.Value == true`. On true: clear `IsSessionExpired = false`, clear `HasApiError = false`, reset `_autoReauthAttempted = false`, then call `RefreshUsageCommand.ExecuteAsync(null)` to immediately re-fetch usage data. No app restart, no waiting for next poll-tick.

**D-04 â€” Reload button placement:** Top-Right overlay over the existing `LoginWebView`. `HorizontalAlignment="Right"`, `VerticalAlignment="Top"`, `Margin="8"`. Z-order: declared after the WebView2 in the Grid so it floats on top.

**D-05 â€” Reload button visual style:** matches `MainView` footer refresh button. UI-SPEC overrides D-05's `Padding=6`/`FontSize=14` with **`Padding=8`, `FontSize=16`, `CornerRadius=6`** (locks visual coherence with footer button per `MainView.xaml:606-618`). Glyph `&#xE72C;` (Segoe Fluent Reload), `Background="Transparent"`, `BorderThickness="0"`. Tooltip and `AutomationProperties.Name` bind via `l:Uids.Uid="LoginReloadButton"`.

**D-06 â€” Click handler:** `LoginView.xaml.cs` `OnReloadLoginClicked` calls `LoginWebView?.CoreWebView2?.Reload()` with both null guards. No retry, no busy state.

**D-07 â€” Sign-out WebView2 reset:** `LoginView.xaml`: `LoginWebView` starts with `Visibility="Collapsed"`. The existing loading overlay covers the user-visible region during this window.

**D-08 â€” Show condition:** in the `NavigationCompleted` handler chain (already wired in `LoginViewModel.HandleNavigationCompleted`), check if `args.IsSuccess == true` AND the `CoreWebView2.Source` starts with `https://claude.ai/login`. Only then flip the WebView2 to `Visibility="Visible"`. Implementation: extend the existing `IsLoading` ObservableProperty semantics so it stays `true` until the login-URL NavigationCompleted fires â€” no second visibility flag needed.

**D-09 â€” Background-window activation:** `NavigationService.NavigateTo<TPage>` calls `App.MainWindow?.Activate()` BEFORE `_frame.Navigate(...)`. Global behavior â€” applies to every navigation. Cost when window is already foreground: zero.

### Claude's Discretion

- Exact `IsLoading` extension shape (rename to `IsWebViewReady` vs. keep `IsLoading` and invert) â€” pick the cleaner read at implementation time
- Whether to add a `NavigationFailed` fallback path for the rare case where the login URL never loads (offline) â€” defensive timeout optional, FEAT-11 reload button is the user-facing recovery path
- Test-mock strategy for `WeakReferenceMessenger.Default` in unit tests of `Receive` â€” depends on existing test patterns
- Order of `Logout()`'s side effects in the multi-line method â€” current order is fine; reorder if a test reveals a race

### Deferred Ideas (OUT OF SCOPE)

- Test-strategy for `WeakReferenceMessenger.Default` mocking in `Receive` unit tests â€” surface to planner only
- Edge case: `TryMigrateOrgIdAsync` 401 path in `ClaudeApiService.cs:182-184` sends the same `AuthStateChangedMessage(false)` â€” code review must verify no double-trigger if both `FetchUsageAsync` and `TryMigrateOrgIdAsync` 401 in quick succession. **Listed as a follow-up note for the planner.**
- `NavigationFailed` fallback for offline login URL â€” reload button is the recovery path
- Per-401-counter instead of single bool â€” spec already settled "first 401 only" semantics
- `NavigateAndActivate<TPage>()` overload â€” rejected (D-09 chose global activation in `NavigateTo`)
</user_constraints>

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|------------------|
| AUTH-01 | First HTTP 401 in a session triggers automatic navigation to LoginView | D-01 routing in `MainViewModel.Receive(AuthStateChangedMessage)`. Existing emitter: `WebViewBridge.OnWebMessageReceived:158-162` raises `UnauthorizedAccessException`; `ClaudeApiService.FetchUsageAsync:86-90` catches it and sends `AuthStateChangedMessage(false)`. |
| AUTH-02 | Second 401 falls back to existing InfoBar | D-01 fall-through to current logic at `MainViewModel.Receive:929-936` (sets `IsSessionExpired = true`). Existing `SessionExpiredInfoBar` at `MainView.xaml:56-72` with `ReLoginCommand` action. |
| AUTH-03 | `_autoReauthAttempted` flag resets on HTTP 200, logout, app start | D-02 four reset locations: `PollUsageAsync` success path (`MainViewModel.cs:407-411`), `Logout` (`869-877`), `Receive(true)` (new â€” D-03), constructor default (already covered by `bool` field default). |
| AUTH-04 | Successful login refreshes MainView immediately via `AuthStateChangedMessage` handler | D-03 â€” extend `Receive` to handle `message.Value == true`: clear flags + `RefreshUsageCommand.ExecuteAsync(null)`. Emitter exists: `LoginViewModel.TryExtractSessionCookieAsync:190` already sends `AuthStateChangedMessage(true)`. |
| AUTH-05 | NavigateTo<LoginView> activates a minimized window | D-09 â€” `App.MainWindow?.Activate()` before `_frame.Navigate(...)` in `NavigationService.NavigateTo`. `App.MainWindow` is already `public static` (`App.xaml.cs:19`). |
| AUTH-06 | LoginView reload button (top-right) calls `CoreWebView2.Reload()` (null-guarded) | D-04..D-06 â€” XAML overlay button + code-behind handler. Glyph `&#xE72C;` (Reload), tooltip via `l:Uids.Uid="LoginReloadButton"`. |
| AUTH-07 | After logout, LoginView's WebView2 navigates to login URL before display | D-07/D-08 â€” `LoginWebView` starts `Visibility="Collapsed"`, revealed only after `HandleNavigationCompleted` confirms `args.IsSuccess == true` AND `CoreWebView2.Source` starts with `https://claude.ai/login`. |
</phase_requirements>

## Architectural Responsibility Map

| Capability | Primary Tier | Secondary Tier | Rationale |
|------------|-------------|----------------|-----------|
| HTTP 401 detection | Service (WebViewBridge) | â€” | Status code parsing happens at the network boundary [VERIFIED: `WebViewBridge.cs:158-162`] |
| 401 â†’ AuthState event publication | Service (ClaudeApiService) | â€” | Service catches `UnauthorizedAccessException` and publishes via `WeakReferenceMessenger` [VERIFIED: `ClaudeApiService.cs:86-90`, `:182-184`] |
| 401 routing decision (first vs. second) | ViewModel (MainViewModel) | â€” | The flag-state machine is UI logic, not service logic â€” `MainViewModel.Receive` owns the dispatch point per D-01 |
| Auto-reauth flag lifetime | ViewModel (MainViewModel) | â€” | Per-session state held by the ViewModel; resets across the same paths the ViewModel already mutates |
| Window activation | Service (NavigationService) | â€” | All navigation flows through one method (`NavigateTo`) â€” central activation per D-09 [VERIFIED: `NavigationService.cs:22`] |
| Post-login refresh trigger | ViewModel (MainViewModel) | â€” | `RefreshUsageCommand` is a `[RelayCommand]` on `MainViewModel`; messenger handler invokes it directly |
| Login WebView2 visibility gate | View (LoginView.xaml) â†” ViewModel (LoginViewModel) | â€” | Bound to `IsLoading` `ObservableProperty`; the View has no logic, the ViewModel mutates the flag in `HandleNavigationCompleted` |
| Reload button click | View code-behind (LoginView.xaml.cs) | â€” | WebView2 control reference is required (`LoginWebView.CoreWebView2.Reload()`); per project pattern (`LoginView.xaml.cs:31` already passes `LoginWebView` directly to ViewModel), code-behind owns direct WebView2 calls |
| Localization | Resources.resw + WinUI3Localizer | â€” | Phase 20 references keys `LoginReloadButton.*`; Phase 23 authors them |

## Standard Stack

### Core (already in project â€” no installs needed)

| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| `CommunityToolkit.Mvvm` | 8.4 | `[ObservableProperty]`, `[RelayCommand]`, `WeakReferenceMessenger` | Project-wide convention [VERIFIED: `App.xaml.cs:163-175` DI, `MainViewModel.cs:281` registration] |
| `Microsoft.Web.WebView2.Core` | (Windows App SDK 1.8) | `CoreWebView2.Reload()`, `NavigationCompleted` events | Already used in `LoginView`/`LoginViewModel` [VERIFIED: `LoginViewModel.cs:8`] |
| `WinUI3Localizer` | (project) | `l:Uids.Uid` runtime localization | Required for the new tooltip key per UI-SPEC [VERIFIED: `MainView.xaml:12`, `LoginViewModel.cs:16` import] |
| `Microsoft.Extensions.DependencyInjection` | (Microsoft.Extensions) | DI for ViewModels/Services | Project standard [VERIFIED: `App.xaml.cs:137-178`] |

### Test stack (already in project)

| Library | Version | Purpose | Source |
|---------|---------|---------|--------|
| `xunit` | 2.9.3 | Test framework | [VERIFIED: `CCInfoWindows.Tests.csproj:19`] |
| `xunit.runner.visualstudio` | 3.0.2 | Test runner | [VERIFIED: `CCInfoWindows.Tests.csproj:20`] |
| `Moq` | 4.20.72 | Service mocking | [VERIFIED: `CCInfoWindows.Tests.csproj:21`] |
| `Microsoft.NET.Test.Sdk` | 17.12.0 | Test SDK | [VERIFIED: `CCInfoWindows.Tests.csproj:18`] |

### Alternatives Considered â€” none

This phase introduces no new dependencies. All required APIs ship in libraries already referenced.

**Installation:** N/A â€” no `dotnet add package` actions in this phase.

## Architecture Patterns

### System Architecture Diagram

```
                  â”Œâ”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”
                  â”‚        Background poll tick         â”‚
                  â”‚   (DispatcherQueueTimer in MainVM)  â”‚
                  â””â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”¬â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”˜
                                    â–¼
   â”Œâ”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”
   â”‚  ClaudeApiService.FetchUsageAsync                            â”‚
   â”‚   â†’ WebViewBridge.FetchJsonAsync(url)                        â”‚
   â”‚       â†’ JS fetch in WebView2 â†’ Cloudflare â†’ claude.ai        â”‚
   â”‚       â†’ status 401 â†’ throw UnauthorizedAccessException       â”‚
   â”‚   â†’ catches it â†’ WeakReferenceMessenger.Send(Auth(false))    â”‚  â† unchanged
   â””â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”¬â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”˜
                                     â–¼
   â”Œâ”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”
   â”‚  MainViewModel.Receive(AuthStateChangedMessage)              â”‚
   â”‚  â”Œâ”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”  â”‚
   â”‚  â”‚  if (!message.Value):                                  â”‚  â”‚
   â”‚  â”‚     if (!_autoReauthAttempted):       â† NEW (D-01)     â”‚  â”‚
   â”‚  â”‚        _autoReauthAttempted = true                     â”‚  â”‚
   â”‚  â”‚        navService.NavigateTo<LoginView>()              â”‚  â”‚
   â”‚  â”‚     else:                              (existing path) â”‚  â”‚
   â”‚  â”‚        IsSessionExpired = true   â†’ InfoBar opens       â”‚  â”‚
   â”‚  â”‚  else (message.Value == true):       â† NEW (D-03)      â”‚  â”‚
   â”‚  â”‚     IsSessionExpired = false                           â”‚  â”‚
   â”‚  â”‚     HasApiError = false                                â”‚  â”‚
   â”‚  â”‚     _autoReauthAttempted = false                       â”‚  â”‚
   â”‚  â”‚     RefreshUsageCommand.ExecuteAsync(null)             â”‚  â”‚
   â”‚  â””â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”˜  â”‚
   â””â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”¬â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”˜
                            â–¼
   â”Œâ”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”
   â”‚  NavigationService.NavigateTo<LoginView>                     â”‚
   â”‚   â†’ App.MainWindow?.Activate()         â† NEW (D-09)          â”‚
   â”‚   â†’ _frame.Navigate(typeof(LoginView), ...)                  â”‚
   â””â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”¬â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”˜
                            â–¼
   â”Œâ”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”
   â”‚  LoginView OnLoaded â†’ LoginViewModel.InitializeWebViewAsync  â”‚
   â”‚   â†’ IsLoading = true (extended: stays true until login URL)  â”‚
   â”‚   â†’ LoginWebView.Visibility = Collapsed (XAML default)       â”‚
   â”‚   â†’ CoreWebView2.Navigate("https://claude.ai/login")         â”‚
   â”‚   â†’ HandleNavigationCompleted fires:                         â”‚
   â”‚       if args.IsSuccess && Source starts "claude.ai/login":  â”‚
   â”‚          IsLoading = false â†’ WebView2 becomes Visible        â”‚
   â””â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”¬â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”˜
                            â–¼
   â”Œâ”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”
   â”‚  User logs in â†’ SourceChanged/HistoryChanged                 â”‚
   â”‚   â†’ TryExtractSessionCookieAsync: sessionKey captured        â”‚
   â”‚   â†’ CredentialService.SaveSessionToken(...)                  â”‚
   â”‚   â†’ bridge.Initialize(coreWebView, dispatcherQueue)          â”‚
   â”‚   â†’ WeakReferenceMessenger.Send(Auth(true))                  â”‚
   â”‚   â†’ navService.NavigateTo<MainView>()                        â”‚
   â””â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”¬â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”˜
                            â–¼
            (back to MainViewModel.Receive(true) path above â€”
             RefreshUsageCommand fires immediately)
```

### Component Responsibilities

| Component | File | Existing role | Phase 20 delta |
|-----------|------|---------------|----------------|
| `WebViewBridge` | `Services/WebViewBridge.cs` | Routes API calls through Chromium fetch; raises `UnauthorizedAccessException` on 401 | **NONE** (D-01 explicit) |
| `ClaudeApiService` | `Services/ClaudeApiService.cs` | Catches 401 exception, sends `AuthStateChangedMessage(false)` | **NONE** (D-01 explicit). Edge case noted below for `TryMigrateOrgIdAsync` (deferred). |
| `MainViewModel` | `ViewModels/MainViewModel.cs` | Polling, `Receive(false)` â†’ InfoBar, `Logout`, `ReLogin`, `Refresh` commands | Add `_autoReauthAttempted` field. Extend `Receive` for both branches. Reset flag at 4 sites (D-02). |
| `NavigationService` | `Services/NavigationService.cs` | `Frame.Navigate` wrapper | Add `App.MainWindow?.Activate()` before `_frame.Navigate(...)` (D-09). |
| `LoginView.xaml` | `Views/LoginView.xaml` | Full-window WebView2 + loading overlay + ErrorMessage InfoBar | Add `Visibility="Collapsed"` to `LoginWebView` (bind to inverse of `IsLoading`). Add reload `Button` overlay. |
| `LoginView.xaml.cs` | `Views/LoginView.xaml.cs` | Loaded handler â†’ calls VM init | Add `OnReloadLoginClicked(object sender, RoutedEventArgs e)`. |
| `LoginViewModel` | `ViewModels/LoginViewModel.cs` | WebView2 init, cookie extraction, `IsLoading` flag | Extend `IsLoading` semantics: stays `true` until login URL `NavigationCompleted` succeeds. Update `HandleNavigationCompleted` to flip `IsLoading=false` only when login URL loaded. |
| `Resources.resw` (DE+EN) | `Strings/{de-DE,en-US}/Resources.resw` | 130+ existing keys | **Authored by Phase 23** â€” NOT this phase. Phase 20 only references `LoginReloadButton.[ToolTipService.ToolTip]` and `LoginReloadButton.[AutomationProperties.Name]`. |

### Recommended Project Structure

No new folders or files â€” all changes go to existing files listed above.

### Pattern 1: Single-shot lifecycle flag (precedent: `_loginHandled`)

**What:** A private `bool` field on the ViewModel that gates a one-time action and is reset when the lifecycle returns to "fresh." `_autoReauthAttempted` is structurally identical.

**Why this exists:** Receivers of cross-VM messages (`SourceChanged`, `HistoryChanged`, `NavigationCompleted`, `AuthStateChangedMessage`) can fire multiple times for one logical action. The flag de-bounces.

**Example (existing pattern):**
```csharp
// Source: ViewModels/LoginViewModel.cs:133, :94, :109, :123, :142, :159, :174
private bool _loginHandled;

// Reset on re-entry:
_loginHandled = false;        // line 94, in InitializeWebViewAsync after cookie purge

// Guarded check:
if (_loginHandled) return;    // lines 109, 123, 142, 159

// Set after one-shot action:
_loginHandled = true;         // line 174, inside TryExtractSessionCookieAsync
```

**Phase 20 application:** `_autoReauthAttempted` follows the same shape â€” declared as `private bool _autoReauthAttempted;` on `MainViewModel`, defaulting to `false`. Reset at the 4 sites in D-02. Set at the first-401 branch in `Receive`.

### Pattern 2: `Receive` as central dispatch point

**What:** A ViewModel implements `IRecipient<TMessage>` and the `Receive` method is the **only** place that decides what to do with the message. Branching logic lives there, not in the senders.

**Existing precedent:**
```csharp
// Source: ViewModels/MainViewModel.cs:929-936
public void Receive(AuthStateChangedMessage message)
{
    if (!message.Value)
    {
        IsSessionExpired = true;
        StatusMessage = "Session expired. Please re-login to continue.";
    }
}
```

**Phase 20 extension shape (illustrative, planner picks final form):**
```csharp
public void Receive(AuthStateChangedMessage message)
{
    if (message.Value)
    {
        // D-03: post-login refresh
        IsSessionExpired = false;
        HasApiError = false;
        _autoReauthAttempted = false;
        RefreshUsageCommand.ExecuteAsync(null);
        return;
    }

    // D-01: first vs. second 401 routing
    if (!_autoReauthAttempted)
    {
        _autoReauthAttempted = true;
        _navigationService.NavigateTo<LoginView>();
        return;
    }

    // Fall-through: existing second-401 path
    IsSessionExpired = true;
    StatusMessage = "Session expired. Please re-login to continue.";
}
```

### Pattern 3: WebView2 control reference threading

**What:** Code-behind owns the direct `WebView2` reference and either passes it to the ViewModel (init flow) or invokes a single method on it directly (reload). Project precedent: `LoginView.xaml.cs:31` passes `LoginWebView` to `ViewModel.InitializeWebViewAsync`.

**Phase 20 application â€” reload click handler:**
```csharp
// Source: target file Views/LoginView.xaml.cs (new method)
private void OnReloadLoginClicked(object sender, RoutedEventArgs e)
{
    LoginWebView?.CoreWebView2?.Reload();   // double null-guard (D-06)
}
```

The `?.` chain guards both: (a) `LoginWebView` x:Name reference is null before `InitializeComponent` finishes, (b) `CoreWebView2` is null until `EnsureCoreWebView2Async` completes (lazy init confirmed at `LoginViewModel.cs:212-220`).

### Pattern 4: `[RelayCommand].ExecuteAsync` from non-command callsites

**What:** Generated commands expose `ExecuteAsync` (for async commands) and `Execute` (for sync). Callable from a `Receive` handler or other command bodies.

**Phase 20 application:** `RefreshUsageCommand.ExecuteAsync(null)` from `Receive(true)`. The generator produces `RefreshCommand` from `private async Task Refresh()` at `MainViewModel.cs:850-854` â€” confirm exact name in code; the `[RelayCommand]` over `Refresh()` produces `RefreshCommand`, so the call is `RefreshCommand.ExecuteAsync(null)` (NOT `RefreshUsageCommand` â€” CONTEXT D-03 mentions `RefreshUsageCommand` but the actual generated symbol is `RefreshCommand`).

**Planner note:** The actual `[RelayCommand]` method is named `Refresh` â†’ generated `RefreshCommand`. CONTEXT/UI-SPEC say `RefreshUsageCommand`; this is a naming drift but functionally equivalent. **Plans must use `RefreshCommand` to match the actual generated symbol.**

### Anti-Patterns to Avoid

- **Coupling activation logic to auto-reauth path specifically.** D-09 explicitly chose global `Activate()` in `NavigateTo` to avoid this â€” every navigation gets it.
- **Adding a second visibility flag on `LoginWebView`.** D-08 explicit: `IsLoading` is the single source of truth â€” extend its semantics, don't add a sibling.
- **Suppressing the existing `SessionExpiredInfoBar`.** AUTH-02 requires it â€” the second-401 fallback **must continue** rendering identically.
- **Mutating `IsSessionExpired` from `WebViewBridge` or `ClaudeApiService`.** D-01 explicit: those layers don't change. The flag transitions only happen inside `MainViewModel`.
- **Calling `_frame.Navigate` from a non-UI thread without `DispatcherQueue` marshalling.** `MainViewModel.Receive` runs on whatever thread the messenger fires from; if a 401 originates from `WebViewBridge.OnWebMessageReceived` (UI thread per `_dispatcherQueue.TryEnqueue` in `WebViewBridge.cs:100`), this is fine. **Verify in tests.**

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Cross-VM auth-state notification | New event/delegate | Existing `AuthStateChangedMessage` + `WeakReferenceMessenger.Default` [VERIFIED: `Messages/AuthStateChangedMessage.cs`] | Project standard; both ends already wired |
| WebView2 reload | Manual reload via JS injection | `CoreWebView2.Reload()` | Native API; handles cache and dirty state correctly |
| Window activation | New WinAPI P/Invoke | `App.MainWindow?.Activate()` (existing static) [VERIFIED: `App.xaml.cs:19`] | The `Window` class exposes `Activate()`; no P/Invoke needed |
| Single-shot flag | Lock + counter + reset thread | Plain `bool` field with explicit reset sites (precedent: `_loginHandled`) | Project precedent; no concurrency guards needed because `Receive` is dispatched serially via messenger |
| Bool-to-Visibility conversion | Custom IValueConverter | Existing `BoolToVisibilityConverter` (referenced in `LoginView.xaml:20` and `MainView.xaml`) | Already in `App.xaml` resources |
| Localization key resolution | Hardcoded strings | `l:Uids.Uid="LoginReloadButton"` binding (Phase 23 authors values) | Project standard |
| 401 status detection | New error parsing | Existing `WebViewBridge.OnWebMessageReceived:158-162` raises `UnauthorizedAccessException` | Already correctly typed; ClaudeApiService catches it |

**Key insight:** Phase 20 reuses 100% of the existing auth/messaging infrastructure. Net new code is conditional branches in already-existing methods plus 1 `Button` element and 1 click handler.

## Runtime State Inventory

> Phase 20 is a code-only change (XAML + C# edits). No data migrations, no service-side state, no OS-level state.

| Category | Items Found | Action Required |
|----------|-------------|------------------|
| Stored data | None â€” `_autoReauthAttempted` is in-memory only; no persistence | None |
| Live service config | None â€” claude.ai is the only external service and no config there changes | None |
| OS-registered state | None â€” no Task Scheduler / launchd / startup keys touched | None |
| Secrets/env vars | None â€” Credential Manager DPAPI store is **read** (`HasValidToken`) but not changed by this phase. `Logout` already calls `_credentialService.ClearCredentials()` (existing behavior, unchanged) | None |
| Build artifacts | Standard `bin/`/`obj/` rebuild after C# edits. No `.egg-info`-style stale artifacts. | None â€” normal `dotnet build` rebuild |

**Nothing found in any category â€” verified by:**
- Grep `App.MainWindow|MainWindow.Activate|Activate()` â€” only `App.xaml.cs:61` and `MainViewModel.cs:889`, both pre-existing
- No new resw keys authored in this phase (Phase 23 owns) â€” but **two referenced keys `LoginReloadButton.Tooltip` and `LoginReloadButton.AutomationName` MUST exist by phase end** (see Open Question #1)
- No new DI registrations needed â€” all services Phase 20 touches are already registered (`App.xaml.cs:137-178`)

## Common Pitfalls

### Pitfall 1: WebView2 `CoreWebView2` is null until `EnsureCoreWebView2Async` resolves
**What goes wrong:** `LoginWebView.CoreWebView2.Reload()` throws `NullReferenceException` if user clicks the reload button before WebView2 has initialized.
**Why it happens:** WebView2 lazy-inits via `EnsureCoreWebView2Async` (verified `LoginViewModel.cs:220`). The `LoginWebView` X:Name resolves before that method returns.
**How to avoid:** Always `LoginWebView?.CoreWebView2?.Reload()` (the `?.` chain). D-06 mandates this.
**Warning signs:** Crash log entries with `NullReferenceException` and stack trace through `OnReloadLoginClicked`.

### Pitfall 2: `Receive` running off UI thread when calling `_frame.Navigate`
**What goes wrong:** `WeakReferenceMessenger` invokes `Receive` synchronously on the sender's thread. If `WebViewBridge.OnWebMessageReceived` runs on a non-UI thread (it actually marshals via `_dispatcherQueue.TryEnqueue` in `WebViewBridge.cs:100`, so the `fetch()` callback fires UI-side, but the `OnWebMessageReceived` event itself is posted to the UI thread by WebView2), `_frame.Navigate` could in theory be called from the wrong thread.
**Why it happens:** `_frame` is a UI control â€” must be touched on UI thread.
**How to avoid:** In practice the messenger send originates from `OnWebMessageReceived` which WebView2 raises on the UI thread (Microsoft.Web.WebView2 contract). Test by triggering 401 in `mcp__windows-mcp` and confirming no `COMException` / `WrongThreadException`. If a regression appears, marshal via `DispatcherQueue.TryEnqueue` inside `Receive`.
**Warning signs:** `COMException 0x8001010E` (RPC_E_WRONG_THREAD) in crash.log.

### Pitfall 3: Double-trigger of `AuthStateChangedMessage(false)` from `TryMigrateOrgIdAsync`
**What goes wrong:** `ClaudeApiService.cs:182-184` sends `AuthStateChangedMessage(false)` from the org-migration path. If `FetchUsageAsync` and `TryMigrateOrgIdAsync` both 401 in quick succession during a single poll cycle, `Receive` could be invoked twice â€” first call sets `_autoReauthAttempted=true` and navigates; second call hits the second-401 path and opens InfoBar **on the now-LoginView page**.
**Why it happens:** Two independent send sites for the same message; CONTEXT calls this out as deferred.
**How to avoid:** This is a deferred edge case (CONTEXT Â§Deferred Ideas item 2). Planner should add a defensive check in `Receive`: if `_navigationService` has already been told to go to LoginView in this dispatch chain, skip the second branch. Easiest: after `_autoReauthAttempted = true`, the second `false` message hitting the same instance correctly takes the InfoBar branch â€” but the InfoBar is on `MainView` which is no longer the current page. Net effect: harmless but the `IsSessionExpired` flag is set on a stale ViewModel. Since `MainViewModel` is `AddTransient` (`App.xaml.cs:164`), the next `NavigateTo<MainView>()` after re-login spins up a fresh instance and the flag is gone. **Acceptable for v1.4** but planner should add a comment noting the design.
**Warning signs:** User reports seeing InfoBar briefly flash before LoginView appears, or sees InfoBar on returning to MainView with `IsSessionExpired=true` after a successful re-login. (Mitigation: D-03 explicitly resets `IsSessionExpired = false` in `Receive(true)`.)

### Pitfall 4: Forgetting `args.IsSuccess` check on `NavigationCompleted`
**What goes wrong:** Login URL fails to load (offline) but `NavigationCompleted` still fires with `args.IsSuccess == false`. If the visibility-flip ignores `IsSuccess`, the broken page becomes visible.
**Why it happens:** `NavigationCompleted` fires for both success and failure.
**How to avoid:** D-08 explicit â€” check `args.IsSuccess == true` AND `Source` URL match.
**Warning signs:** Blank/error page visible after offline launch instead of loading overlay.

### Pitfall 5: `_loginHandled = false` reset on re-entry but NOT cookie cleanup
**What goes wrong:** `LoginViewModel.InitializeWebViewAsync` resets `_loginHandled = false` (line 94) AFTER the cookie deletion (lines 87-91). If logout doesn't trigger a re-entry through `InitializeWebViewAsync`, stale cookies remain and the user sees a logged-in state inside `LoginView`.
**Why it happens:** `LoginView` may be cached by the WinUI Frame (`SlideNavigationTransitionInfo`).
**How to avoid:** `LoginView.OnLoaded` always calls `InitializeWebViewAsync(LoginWebView)` (verified at `LoginView.xaml.cs:31`), which always reaches the cookie cleanup. Phase 20's `Visibility="Collapsed"` start guarantees user never sees the cached chat URL even if the WebView2 has stale state until after navigate-to-login completes.
**Warning signs:** After Logout â†’ LoginView opens, briefly shows previous chat content before login form. (This is precisely what AUTH-07 fixes.)

### Pitfall 6: `RefreshCommand.ExecuteAsync(null)` fired from `Receive(true)` while `IsRefreshing=true`
**What goes wrong:** `PollUsageAsync` has `if (IsRefreshing) return;` at line 400. If `Receive(true)` fires immediately after a poll started, the refresh is silently skipped â€” defeating the "post-login refresh is immediate" success criterion.
**Why it happens:** Reentrancy guard on the polling.
**How to avoid:** Two options: (a) accept that the running poll IS the refresh and the next data update covers it (since the user just logged in, the poll already in-flight uses the new bridge state); (b) await the in-flight poll then re-fire. **Planner pick.** Option (a) is fine for v1.4 because the bridge's new initialization happens BEFORE `AuthStateChangedMessage(true)` is sent (`LoginViewModel.cs:188-190`), so any in-flight poll uses fresh credentials.
**Warning signs:** Post-login MainView shows stale "--" placeholders for â‰¥1 poll-interval before data appears.

## Code Examples

### Example 1: Existing single-shot flag pattern (precedent for `_autoReauthAttempted`)

```csharp
// Source: D:\myProjects\ccInfoWin\CCInfoWindows\CCInfoWindows\ViewModels\LoginViewModel.cs:133, 159-193
private bool _loginHandled;

private async Task TryExtractSessionCookieAsync(CoreWebView2 coreWebView, string currentUrl)
{
    if (_loginHandled) return;                 // guard
    if (!IsPostLoginUrl(currentUrl)) return;

    var cookies = await coreWebView.CookieManager.GetCookiesAsync("https://claude.ai");
    var sessionCookie = cookies.FirstOrDefault(c => string.Equals(c.Name, "sessionKey", StringComparison.Ordinal));

    if (sessionCookie is not null)
    {
        _loginHandled = true;                  // set after one-shot
        // ... do work ...
    }
}
```

### Example 2: Existing `Receive` pattern (Phase 20 extends)

```csharp
// Source: D:\myProjects\ccInfoWin\CCInfoWindows\CCInfoWindows\ViewModels\MainViewModel.cs:929-936
public void Receive(AuthStateChangedMessage message)
{
    if (!message.Value)
    {
        IsSessionExpired = true;
        StatusMessage = "Session expired. Please re-login to continue.";
    }
}
```

### Example 3: Existing footer refresh button visual (UI-SPEC reference target)

```xml
<!-- Source: D:\myProjects\ccInfoWin\CCInfoWindows\CCInfoWindows\Views\MainView.xaml:606-618 -->
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

**Phase 20 reload button mirrors this** but with: (a) glyph `&#xE72C;` instead of `&#xE895;`, (b) `l:Uids.Uid="LoginReloadButton"` instead of `FooterRefreshButton`, (c) no `RotateTransform` (no spin animation), (d) `HorizontalAlignment="Right" VerticalAlignment="Top" Margin="8"`, (e) `Click="OnReloadLoginClicked"` (no `Command` because the click target is in code-behind to access `LoginWebView`).

### Example 4: Existing resw key pattern (mirror for `LoginReloadButton.*`)

```xml
<!-- Source: D:\myProjects\ccInfoWin\CCInfoWindows\CCInfoWindows\Strings\en-US\Resources.resw:101-106 -->
<data name="FooterRefreshButton.[using:Microsoft.UI.Xaml.Controls]ToolTipService.ToolTip" xml:space="preserve">
    <value>Refresh</value>
</data>
<data name="FooterRefreshButton.[using:Microsoft.UI.Xaml.Automation]AutomationProperties.Name" xml:space="preserve">
    <value>Refresh</value>
</data>
```

**Phase 23 will author** identical-shape entries for `LoginReloadButton`. Phase 20 references them via `l:Uids.Uid="LoginReloadButton"` and they MUST exist before runtime or the tooltip/AutomationName will be empty.

### Example 5: Existing test harness pattern (target for `Receive` tests)

```csharp
// Source: D:\myProjects\ccInfoWin\CCInfoWindows.Tests\ViewModels\MainViewModelStatisticsTests.cs:15-29
private static MainViewModelTestHarness CreateHarness()
{
    var jsonlService = new Mock<IJsonlService>();
    jsonlService.Setup(s => s.Sessions).Returns([]);
    // ... other mocks ...
    return new MainViewModelTestHarness(jsonlService.Object, pricingService.Object);
}

[Fact]
public void ApplyStatistics_WithHasEstimatedCostsTrueAndNonZeroCost_ProducesTildePrefixedCost() { /* ... */ }
```

**Phase 20 tests follow this shape** â€” extend or sibling-create a harness that exposes `_autoReauthAttempted` (likely via `internal` accessor or by indirect observation through `IsSessionExpired` post-`Receive` calls). Mock `INavigationService` with `Mock<INavigationService>` to verify `NavigateTo<LoginView>()` was called the expected number of times.

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| Direct `HttpClient` for claude.ai | `WebViewBridge` routing through Chromium fetch | Phase 2 (Cloudflare fix, MEMORY 2026-03-10) | All API calls bypass Cloudflare TLS fingerprint detection |
| Bare 401 â†’ InfoBar | First 401 â†’ auto-navigate; second 401 â†’ InfoBar | Phase 20 (this phase) | Better UX; eliminates manual button click after timeout |

**Deprecated/outdated for this phase:**
- Manual `HttpClient` reading 401s â€” no longer the auth path; do not touch.
- The `ReLoginCommand` is **not deprecated** â€” it remains as the second-401 fallback action button.

## Assumptions Log

| # | Claim | Section | Risk if Wrong |
|---|-------|---------|---------------|
| A1 | `RefreshCommand.ExecuteAsync(null)` from `Receive(true)` will not deadlock if `PollUsageAsync` is currently running â€” `IsRefreshing` guard returns early | Pitfall 6 | Post-login refresh skipped silently; user sees stale data for 1 poll-interval |
| A2 | `WeakReferenceMessenger` invokes `Receive` on the UI thread when sender is `WebViewBridge.OnWebMessageReceived` (because WebView2 raises `WebMessageReceived` on UI thread per Microsoft contract) | Pitfall 2 | If wrong, `_frame.Navigate` from `Receive` throws `RPC_E_WRONG_THREAD`; need `DispatcherQueue.TryEnqueue` wrap |
| A3 | `LoginReloadButton.Tooltip` / `LoginReloadButton.AutomationName` resw keys can ship in a Phase 20 commit OR in Phase 23, whichever ships first â€” both phases reference the spec FEAT-16 values | Open Question #1 | If Phase 20 ships first without the keys in resw, the tooltip and Narrator name render as the literal `Uid` â†’ broken UX. **MUST be coordinated with Phase 23.** |
| A4 | `Window.Activate()` on a minimized window restores it to its previous size/state (Windows shell standard behavior) | D-09 / AUTH-05 | If `Activate()` only "raises" without restoring, AUTH-05 still fails on minimized window â€” would need `WindowEx.Restore()` or P/Invoke `ShowWindow(SW_RESTORE)`. **Recommend manual smoke test with minimized window before signoff.** |
| A5 | The actual `[RelayCommand]` generated symbol is `RefreshCommand` (from `private async Task Refresh()` at line 850), NOT `RefreshUsageCommand` as CONTEXT/UI-SPEC say | Pattern 4 | Plans using `RefreshUsageCommand` will fail to compile â€” must use `RefreshCommand` |

## Open Questions (RESOLVED)

> All three questions below were resolved during planning revision (2026-05-06). Phase 20 plans 01-04 implement the resolutions cited under each item.

1. **Resw key delivery timing â€” Phase 20 vs. Phase 23.**
   - What we know: Phase 23 is the canonical authoring phase; Phase 20 references the keys via `l:Uids.Uid="LoginReloadButton"`.
   - What's unclear: Which phase ships first chronologically? `STATE.md` shows Phase 20 next; Phase 23 is downstream. UI-SPEC Â§"Diff Summary for executor" line 292 explicitly raises this: *"Phase 20 ships first with placeholder `ToolTipService.ToolTip="Reload"` strings and Phase 23 swaps in the resw bindings"* OR Phase 23 lands its keys first.
   - Recommendation: **Plan Phase 20 to also write the two `LoginReloadButton.*` keys into both `Strings/de-DE/Resources.resw` and `Strings/en-US/Resources.resw`** (4 XML entries total â€” values from Spec FEAT-16: EN `Reload page` / `Reload login page`; DE `Seite neu laden` / `Login-Seite neu laden`). Phase 23 will then add the OTHER 4 keys (NotSignedIn, NoData, Loading, InactiveSessionTooltip) without colliding. This makes Phase 20 self-contained; planner should confirm in Wave 0.
   - **RESOLVED:** Phase 20 absorbs the keys (self-contained); Plan 01 Task 2 writes both DE+EN entries (2 keys Ã— 2 locales = 4 `<data>` entries). Phase 23 owns only the OTHER unrelated keys and will not collide.

2. **`Window.Activate()` behavior on minimized window.**
   - What we know: `App.MainWindow?.Activate()` is the WinUI 3 `Microsoft.UI.Xaml.Window.Activate()` method.
   - What's unclear: Documentation states "Attempts to activate the application window by bringing it to the foreground and setting the input focus to it." Whether this RESTORES a minimized window vs. only setting focus is API-version dependent.
   - Recommendation: Smoke-test manually via `mcp__windows-mcp`: minimize app â†’ trigger 401 (revoke session via DevTools or wait for natural expiry) â†’ confirm window unminimizes AND shows LoginView. If `Activate()` alone is insufficient, add `App.MainWindow.AppWindow.Show()` (WinUI 3 way) per AUTH-05.
   - **RESOLVED:** Plan 04 Task 2's manual smoke battery (Check 3, AUTH-05) verifies the minimized-window path. If `Activate()` alone proves insufficient, the documented follow-up is `App.MainWindow.AppWindow.Show()` â€” recorded in Plan 04 SUMMARY as a backlog item, NOT blocking phase signoff.

3. **Edge case: stacked 401s during a single poll (Pitfall 3).**
   - What we know: `ClaudeApiService` has 2 sites that send `AuthStateChangedMessage(false)`.
   - What's unclear: Real-world frequency. Likely rare (org-migration only fires when `lastActiveOrg` cookie is missing, which is rare post-Phase 2).
   - Recommendation: Document the edge case in code comments at `MainViewModel.Receive`. Do not block Phase 20 on it. Defer to backlog if it manifests.
   - **RESOLVED:** Documented as inline code comment in Plan 02's `Receive` body (`MainViewModel.cs`, citing `FetchUsageAsync:88` and `TryMigrateOrgIdAsync:184` send sites); explicitly deferred per CONTEXT Â§Deferred Ideas item 2. `Receive(true)` resets `IsSessionExpired = false` so any stale flag clears at next login.

## Environment Availability

| Dependency | Required By | Available | Version | Fallback |
|------------|------------|-----------|---------|----------|
| .NET 9 SDK | Build/run | âœ“ (assumed â€” project compiled successfully per recent commits) | 9.x | â€” |
| Windows App SDK 1.8 | WinUI 3 + WebView2 | âœ“ | 1.8 | â€” |
| `Microsoft.Web.WebView2` runtime | `CoreWebView2.Reload()`, `NavigationCompleted` | âœ“ | (Edge WebView2 system component) | â€” |
| `CommunityToolkit.Mvvm` 8.4 | `[ObservableProperty]`, `[RelayCommand]`, `WeakReferenceMessenger` | âœ“ | 8.4 | â€” |
| `WinUI3Localizer` | `l:Uids.Uid` runtime tooltip resolution | âœ“ | (project) | â€” |
| xUnit + Moq + Test SDK | Unit tests | âœ“ | 2.9.3 / 4.20.72 / 17.12.0 | â€” |
| `mcp__windows-mcp` server | Manual UAT: minimized-window activation, post-logout flash, reload-button click | âœ“ (per CLAUDE.md) | (mcp) | Manual smoke test if MCP unavailable |

**Missing dependencies with no fallback:** None.
**Missing dependencies with fallback:** None.

## Validation Architecture

### Test Framework
| Property | Value |
|----------|-------|
| Framework | xUnit 2.9.3 + Moq 4.20.72 + Microsoft.NET.Test.Sdk 17.12.0 |
| Config file | `CCInfoWindows.Tests/CCInfoWindows.Tests.csproj` |
| Quick run command | `dotnet test CCInfoWindows.Tests/CCInfoWindows.Tests.csproj --filter "FullyQualifiedName~MainViewModel"` |
| Full suite command | `dotnet test CCInfoWindows.Tests/CCInfoWindows.Tests.csproj` |

### Phase Requirements â†’ Test Map

| Req ID | Behavior | Test Type | Automated Command | File Exists? |
|--------|----------|-----------|-------------------|-------------|
| AUTH-01 | First `AuthStateChangedMessage(false)` sets `_autoReauthAttempted=true` and calls `INavigationService.NavigateTo<LoginView>()` once; does NOT set `IsSessionExpired` | unit | `dotnet test --filter "Receive_FirstFalse_NavigatesToLoginView"` | âŒ Wave 0: `CCInfoWindows.Tests/ViewModels/MainViewModelAuthFlowTests.cs` |
| AUTH-02 | Second consecutive `AuthStateChangedMessage(false)` sets `IsSessionExpired=true` and does NOT navigate | unit | `dotnet test --filter "Receive_SecondFalse_OpensInfoBar"` | âŒ Wave 0: same file |
| AUTH-03 | After `RefreshCommand` success / `Logout` / `Receive(true)` / new instance, the next `false` message routes to LoginView (flag reset) | unit (4 separate tests) | `dotnet test --filter "AutoReauthFlag_ResetsOn"` | âŒ Wave 0: same file |
| AUTH-04 | `Receive(AuthStateChangedMessage(true))` clears `IsSessionExpired`/`HasApiError`, resets flag, fires `RefreshCommand` | unit | `dotnet test --filter "Receive_True_RefreshesAndClearsFlags"` | âŒ Wave 0: same file |
| AUTH-05 | `NavigationService.NavigateTo<TPage>()` calls `App.MainWindow.Activate()` before `_frame.Navigate(...)` | manual smoke (UI thread + minimize required) | manual via `mcp__windows-mcp` (minimize window, trigger logout-driven nav, confirm unminimized) | manual-only |
| AUTH-06 | Reload button click invokes `CoreWebView2.Reload()`; null-guarded against early click | manual smoke + code-review | manual via `mcp__windows-mcp` (click reload button on LoginView; observe URL re-fetch in DevTools) | manual-only |
| AUTH-07 | After `Logout`, `LoginView` shows loading overlay (not chat URL) until `https://claude.ai/login` `NavigationCompleted` fires successfully | manual smoke | manual via `mcp__windows-mcp` (logout from MainView; observe LoginView shows ProgressRing only, never the chat URL) | manual-only |

### Sampling Rate
- **Per task commit:** `dotnet test CCInfoWindows.Tests/CCInfoWindows.Tests.csproj --filter "FullyQualifiedName~MainViewModelAuthFlow" -v normal`
- **Per wave merge:** `dotnet test CCInfoWindows.Tests/CCInfoWindows.Tests.csproj`
- **Phase gate:** Full unit suite green + manual smoke battery (AUTH-05/06/07) confirmed via `mcp__windows-mcp` before `/gsd-verify-work`

### Wave 0 Gaps

- [ ] `CCInfoWindows.Tests/ViewModels/MainViewModelAuthFlowTests.cs` â€” covers AUTH-01..AUTH-04 (extends `MainViewModelTestHarness` pattern from `MainViewModelStatisticsTests.cs`)
- [ ] (Optional) `CCInfoWindows.Tests/Services/NavigationServiceTests.cs` â€” only if `App.MainWindow?.Activate()` interaction is mockable; likely **skip** because `App.MainWindow` is a static reference that's hard to mock without refactoring. Cover via manual smoke instead.
- [ ] No new `conftest.py`-style fixtures needed â€” Moq + xUnit pattern is sufficient

## Project Constraints (from CLAUDE.md)

- **Bash discipline:** Every command in its own `Bash` tool call. No `;`, `&&`, `||`, `|` chaining. Applies to all phase commits and test runs.
- **Build commands:** `dotnet build CCInfoWindows/CCInfoWindows/CCInfoWindows.csproj` for debug; `-c Release -o CCInfoWindows/CCInfoWindows/bin/x64/Release/net9.0-windows10.0.19041.0/` for release. **NEVER `dotnet publish` with trimming** â€” `PublishTrimmed=true` breaks `System.Text.Json` reflection.
- **MVVM conventions:** `[ObservableProperty]` over `_camelCase` fields â†’ generates `PascalCase` property; `[RelayCommand]` over methods â†’ generates `XxxCommand`. No code-behind logic in Views except where direct control reference is required (`LoginView.xaml.cs` is the precedent).
- **Async patterns:** Always `async/await`; never fire-and-forget. `DispatcherQueue.TryEnqueue` for UI thread marshalling.
- **Naming:** PascalCase publics, `_camelCase` private fields, `I-prefix` interfaces, conventional commits (`feat:`, `fix:`, `chore:`, `refactor:`, `test:`, `docs:`).
- **Security:** No secrets in source. Credential Manager via `AdysTech.CredentialManager` only. Network allowlist: `claude.ai`, `raw.githubusercontent.com`, `api.github.com`. Phase 20 introduces zero new endpoints.
- **Clean Code:** Small functions; no magic numbers; meaningful names; DRY; minimal comments; delete commented-out code; F.I.R.S.T. tests.
- **Secure Coding:** Validate all external data; no sensitive data in error UI/logs; logout must fully terminate session â€” verify `Logout` already does this (it does: `_credentialService.ClearCredentials()` + `_bridge.Reset()` + `AuthStateChangedMessage(false)` + `NavigateTo<LoginView>()`).

## Security Domain

> `security_enforcement` is not explicitly disabled â€” included.

### Applicable ASVS Categories

| ASVS Category | Applies | Standard Control |
|---------------|---------|-----------------|
| V2 Authentication | yes | Existing claude.ai OAuth via WebView2 + `sessionKey` cookie capture (unchanged in Phase 20) |
| V3 Session Management | yes | Existing `_credentialService.ClearCredentials()` on `Logout` (unchanged); `_bridge.Reset()` clears WebView2 reference. Phase 20 adds: WebView2 visibility reset hides any pre-purge state from user |
| V4 Access Control | yes | `_navigationService.NavigateTo<LoginView>()` is the gate when token is missing/expired (existing). First-401 path routes here automatically (D-01) |
| V5 Input Validation | minimal | `WebViewBridge.FetchJsonAsync` validates URL prefix `https://claude.ai` (existing `WebViewBridge.cs:54-61`) â€” Phase 20 doesn't add user input |
| V6 Cryptography | yes | DPAPI via `AdysTech.CredentialManager` (existing); Phase 20 doesn't touch storage |

### Known Threat Patterns for WinUI 3 + WebView2

| Pattern | STRIDE | Standard Mitigation |
|---------|--------|---------------------|
| Stale session token visible in WebView2 (post-logout flash) | Information Disclosure | D-07/D-08 â€” `Visibility="Collapsed"` until login URL loaded |
| Auto-reauth loop (infinite 401 â†’ navigate â†’ 401) | Denial of Service | D-01 single-bool flag â€” second 401 falls back to manual InfoBar |
| Reload-button XSS / script injection | Tampering | `CoreWebView2.Reload()` is a native API call, no user input passed |
| Window-activation forced focus theft | Denial of Service (UX) | D-09 only fires on `NavigateTo`, which is user-driven (logout, re-login) or 401-driven (auto-reauth) â€” bounded surface |

## Sources

### Primary (HIGH confidence)
- `D:\myProjects\ccInfoWin\.planning\phases\20-auth-flow-stability\20-CONTEXT.md` â€” locked decisions D-01..D-09
- `D:\myProjects\ccInfoWin\.planning\phases\20-auth-flow-stability\20-UI-SPEC.md` â€” visual contract approved by gsd-ui-checker (6/6)
- `D:\myProjects\ccInfoWin\.planning\milestones\v1.4-REQUIREMENTS.md` â€” AUTH-01..AUTH-07 acceptance criteria
- `D:\myProjects\ccInfoWin\CCInfoWindows\CCInfoWindows\ViewModels\MainViewModel.cs` â€” `Receive` (929-936), `Logout` (869-877), `Refresh` (850-854), `PollUsageAsync` (398-434), constructor (257-282)
- `D:\myProjects\ccInfoWin\CCInfoWindows\CCInfoWindows\ViewModels\LoginViewModel.cs` â€” `_loginHandled` precedent (133), `HandleNavigationCompleted` (138-150), `IsLoading` flag (22-23), cookie cleanup (87-91)
- `D:\myProjects\ccInfoWin\CCInfoWindows\CCInfoWindows\Views\LoginView.xaml` â€” current shape (1-38)
- `D:\myProjects\ccInfoWin\CCInfoWindows\CCInfoWindows\Views\LoginView.xaml.cs` â€” code-behind pattern (17-37)
- `D:\myProjects\ccInfoWin\CCInfoWindows\CCInfoWindows\Services\NavigationService.cs` â€” `NavigateTo` (22-29)
- `D:\myProjects\ccInfoWin\CCInfoWindows\CCInfoWindows\Services\WebViewBridge.cs` â€” 401 â†’ `UnauthorizedAccessException` (158-162)
- `D:\myProjects\ccInfoWin\CCInfoWindows\CCInfoWindows\Services\ClaudeApiService.cs` â€” message send sites (86-90, 182-184)
- `D:\myProjects\ccInfoWin\CCInfoWindows\CCInfoWindows\Messages\AuthStateChangedMessage.cs` â€” message shape
- `D:\myProjects\ccInfoWin\CCInfoWindows\CCInfoWindows\App.xaml.cs` â€” DI registrations (137-178), `MainWindow` static (19), `_window.Activate()` (61)
- `D:\myProjects\ccInfoWin\CCInfoWindows\CCInfoWindows\Views\MainView.xaml` â€” `SessionExpiredInfoBar` (56-72), footer refresh button (605-618)
- `D:\myProjects\ccInfoWin\CCInfoWindows\CCInfoWindows\Strings\en-US\Resources.resw` â€” resw key naming pattern (101-106)
- `D:\myProjects\ccInfoWin\CCInfoWindows.Tests\CCInfoWindows.Tests.csproj` â€” test stack (xUnit 2.9.3 + Moq 4.20.72)
- `D:\myProjects\ccInfoWin\CCInfoWindows.Tests\ViewModels\MainViewModelStatisticsTests.cs` â€” `MainViewModelTestHarness` precedent (15-29)
- `D:\myProjects\ccInfoWin\CLAUDE.md` â€” project conventions

### Secondary (MEDIUM confidence)
- `spec/v1.11.1-macOS/spec-release-1.10.0-to-1.11.1.md` â€” FEAT-07/11/13/16 (cited by CONTEXT, not re-read this session)

### Tertiary (LOW confidence)
- WinUI 3 `Window.Activate()` semantics for minimized windows â€” see Open Question #2 / Assumption A4 (needs manual smoke test verification)

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH â€” all libraries verified in `csproj` and source imports
- Architecture / file paths / line numbers: HIGH â€” all touchpoints read directly this session
- Pitfalls: HIGH for items 1, 4, 5; MEDIUM for items 2, 3, 6 (race / edge-case scenarios)
- Resw key delivery timing: MEDIUM â€” depends on Phase 23 sequencing decision (recommended to absorb into Phase 20)
- `Window.Activate()` minimized-window behavior: LOW â€” recommend manual smoke before AUTH-05 signoff

**Research date:** 2026-05-06
**Valid until:** 2026-06-05 (stack is stable; only WebView2 runtime updates would invalidate, and those are backward-compatible)
