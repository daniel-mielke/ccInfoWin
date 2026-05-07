# Phase 20: Auth Flow Stability - Context

**Gathered:** 2026-05-06
**Status:** Ready for planning

<domain>
## Phase Boundary

Robust sign-in/sign-out flow for ccInfoWin. The first HTTP 401 in a session auto-navigates to LoginView, the second 401 falls back to the existing manual InfoBar (no auto-open loop), MainView refreshes immediately after login (no app restart), LoginView always presents a clean login form after logout, a manual reload button is added to LoginView, and navigation works reliably even when the app window is minimized.

In scope: `_autoReauthAttempted` flag in `MainViewModel`, `Receive(AuthStateChangedMessage)` handler extension for both true (login success) and false (session lost) paths, post-login immediate refresh, LoginView reload button, sign-out WebView2 visibility reset, background-window activation in `NavigationService`.

Out of scope: any history persistence work (Phase 21), refresh spinner / inactive session tooltip / About-tab live timestamp (Phase 22), the new resw localization keys themselves are owned by Phase 23 (this phase **uses** the new `LoginReloadButton.Tooltip` and `LoginReloadButton.AutomationName` keys but does not author them).

</domain>

<decisions>
## Implementation Decisions

### 401 Detection & Auto-Reauth Routing

- **D-01:** First-401 detection lives in `MainViewModel.Receive(AuthStateChangedMessage)`. The handler is the single dispatch point for auth-state changes. When `message.Value == false` AND `_autoReauthAttempted == false`: set the flag to true, call `_navigationService.NavigateTo<LoginView>()`, do NOT set `IsSessionExpired`. When `message.Value == false` AND `_autoReauthAttempted == true`: fall through to the existing path (`IsSessionExpired = true`, InfoBar shows). The `WebViewBridge` and `ClaudeApiService` are NOT changed — they continue to throw `UnauthorizedAccessException` and send `AuthStateChangedMessage(false)` respectively.
- **D-02:** `_autoReauthAttempted` resets to `false` on:
  1. The `RefreshUsageAsync` (or the wrapping `PollUsageAsync`) success path — after a successful HTTP 200 the next 401 is treated as a fresh first-attempt
  2. The `Logout` command — preempts the case where the user manually re-logs via InfoBar then logs out
  3. The `AuthStateChangedMessage(true)` handler (post-login-refresh path)
  4. The `MainViewModel` constructor — covers cold-start and any future singleton refactor

### Post-Login Immediate Refresh

- **D-03:** Extend `Receive(AuthStateChangedMessage)` to also handle `message.Value == true`. On true: clear `IsSessionExpired = false`, clear `HasApiError = false`, reset `_autoReauthAttempted = false`, then call `RefreshUsageCommand.ExecuteAsync(null)` to immediately re-fetch usage data. No app restart, no waiting for next poll-tick. (Note: `MainViewModel` is registered as Transient, so a fresh instance is created on each `NavigateTo<MainView>()`; the constructor-default also covers this. The handler-based reset matters when Login → Main transition reuses a long-lived MainViewModel instance, which is currently not the case but defending the invariant is cheap.)

### Reload Button (LoginView)

- **D-04:** Place the reload button as a Top-Right overlay over the existing `LoginWebView`. `HorizontalAlignment="Right"`, `VerticalAlignment="Top"`, `Margin="8"`. Z-order: declared after the WebView2 in the Grid so it floats on top.
- **D-05:** Visual style matches the existing `MainView` footer refresh button — `FontIcon Glyph="&#xE72C;"` (Segoe Fluent Icons Refresh glyph), `FontSize=16`, `Background="Transparent"`, `BorderThickness="0"`, `Padding="6"`. Tooltip and AutomationProperties.Name bind to `LoginReloadButton.Tooltip` and `LoginReloadButton.AutomationName` (the localization keys are owned by Phase 23 — Phase 20 references them and the keys must exist by the time Phase 23 ships, OR Phase 20 can ship first and Phase 23 fills the resw entries; ordering verified by ROADMAP.md showing Phase 23 depends on Phase 20).
- **D-06:** Click handler in `LoginView.xaml.cs` calls `LoginWebView?.CoreWebView2?.Reload()` with both null guards. No retry, no busy state — `CoreWebView2.Reload()` is a one-shot.

### Sign-Out WebView2 Reset

- **D-07:** Hide the WebView2 until the post-logout navigation to the login URL completes. `LoginView.xaml`: `LoginWebView` starts with `Visibility="Collapsed"`. The existing loading overlay (`ProgressRing` over `ApplicationPageBackgroundThemeBrush`) covers the user-visible region during this window.
- **D-08:** Show condition: in the `NavigationCompleted` handler chain (already wired in `LoginViewModel.HandleNavigationCompleted`), check if `args.IsSuccess == true` AND the `CoreWebView2.Source` starts with `https://claude.ai/login`. Only then flip the WebView2 to `Visibility="Visible"` (and stop the loading overlay). Implementation choice: extend the existing `IsLoading` ObservableProperty semantics so it stays `true` until the login-URL NavigationCompleted fires — no second visibility flag needed, the existing overlay-vs-WebView2 visibility binding becomes the single source of truth.

### Background-Window Activation

- **D-09:** `NavigationService.NavigateTo<TPage>` calls `App.MainWindow?.Activate()` BEFORE `_frame.Navigate(...)`. Global behavior — applies to every navigation, not just auto-reauth. Cost when window is already foreground: zero. Benefit when window is minimized during a background poll → 401 → auto-reauth: the user sees the login page immediately on the next focus rather than discovering it later. This satisfies AUTH-05 without coupling activation logic to the auto-reauth path specifically.

### Claude's Discretion

- Exact `IsLoading` extension shape (rename to `IsWebViewReady` vs. keep `IsLoading` and invert) — Claude picks the cleaner read at implementation time
- Whether to add a `NavigationFailed` fallback path for the rare case where the login URL never loads (e.g., offline) — Claude can add a defensive timeout if needed, but FEAT-11's reload button is already the user-facing recovery path
- Test-mock strategy for `WeakReferenceMessenger.Default` in unit tests of `Receive` — depends on existing test patterns, planner picks
- Order of Logout()'s side effects in the multi-line method — current order is fine; if a test reveals a race, planner may reorder

</decisions>

<canonical_refs>
## Canonical References

**Downstream agents MUST read these before planning or implementing.**

### Phase 20 source spec & requirements
- `spec/v1.11.1-macOS/spec-release-1.10.0-to-1.11.1.md` §FEAT-07 — Auto Re-Auth on Session Invalidation (FEAT-07a flag, FEAT-07b post-login refresh, FEAT-07c background activation). NOTE: spec assumes `HttpFetchException(401)` exception path; actual code uses `UnauthorizedAccessException` + `AuthStateChangedMessage(false)`. D-01 resolves this drift in favor of the existing message-based path.
- `spec/v1.11.1-macOS/spec-release-1.10.0-to-1.11.1.md` §FEAT-11 — Login Window Reload Button (placement, glyph, null-guards)
- `spec/v1.11.1-macOS/spec-release-1.10.0-to-1.11.1.md` §FEAT-13 — Sign-Out Resets to Login Form
- `spec/v1.11.1-macOS/spec-release-1.10.0-to-1.11.1.md` §"Resolved Design Decisions" #1 — Auto-reauth aggressiveness already locked: auto-open on first 401, manual fallback after
- `.planning/milestones/v1.4-REQUIREMENTS.md` §AUTH-01..AUTH-07 — Acceptance criteria
- `.planning/milestones/v1.4-ROADMAP.md` §Phase 20 — Goal, success criteria, depends-on, FEAT-IDs

### Localization keys (used by Phase 20, authored by Phase 23)
- `spec/v1.11.1-macOS/spec-release-1.10.0-to-1.11.1.md` §FEAT-16 — `LoginReloadButton.Tooltip` and `LoginReloadButton.AutomationName` resw keys (DE + EN values defined there)

### Codebase conventions (project-wide, from CLAUDE.md)
- `CLAUDE.md` — MVVM conventions (`[ObservableProperty]`, `[RelayCommand]`), async patterns, build commands (Release builds use `dotnet build -c Release`, NEVER `dotnet publish` with trimming), bash permission rules

</canonical_refs>

<code_context>
## Existing Code Insights

### Reusable Assets
- **`MainViewModel.IsRefreshing`** (line 154): already an `[ObservableProperty]` — Phase 22 (FEAT-09) will repurpose this for refresh-spinner UI; Phase 20 must NOT remove or rename it
- **`MainViewModel.Logout()`** (line 869-877): already calls `_bridge.Reset()`, sends `AuthStateChangedMessage(false)`, and `NavigateTo<LoginView>()`. Phase 20 adds `_autoReauthAttempted = false` reset and may extend the message-send semantics
- **`MainViewModel.ReLoginCommand`** (line 879-884): the existing InfoBar manual-relogin command — this is the second-attempt fallback path; Phase 20 must NOT change its behavior
- **`MainViewModel.Receive(AuthStateChangedMessage)`** (line 929-936): currently only handles `message.Value == false` → `IsSessionExpired = true`. Phase 20 extends this to handle the auto-reauth dispatch and the new `true` path (post-login refresh)
- **`LoginViewModel.HandleNavigationCompleted`** (line 138-150): existing post-init handler — Phase 20 hooks the visibility-flip logic here (or in a sibling private method called from it)
- **`LoginViewModel._loginHandled`** flag pattern (line 133): existing precedent for one-shot lifecycle flags inside the ViewModel — `_autoReauthAttempted` follows the same pattern

### Established Patterns
- **MVVM via `[ObservableProperty]` / `[RelayCommand]`** — CommunityToolkit.Mvvm 8.4 source generators; no manual `INotifyPropertyChanged` plumbing
- **`WeakReferenceMessenger.Default` for cross-VM communication** — used for `AuthStateChangedMessage`, `RefreshIntervalChangedMessage`, `SonnetContextChangedMessage`, `ChartInvalidateMessage`. Phase 20 stays on this pattern; no new message type is introduced.
- **DI: ViewModels Transient, Services Singleton** (App.xaml.cs ConfigureServices) — `MainViewModel` is `AddTransient`, which means each `NavigateTo<MainView>()` constructs a fresh instance. Constructor-default of `_autoReauthAttempted = false` covers app-restart and re-navigation cases automatically.
- **WinUI3Localizer with `l:Uids.Uid`** for runtime language switching — required for the new reload button's tooltip and AutomationName

### Integration Points
- `App.xaml.cs` — DI configuration; no changes needed for Phase 20 (FEAT-08 in Phase 21 will add the `Closed` handler)
- `Services/NavigationService.cs:22` (NavigateTo) — extend with `App.MainWindow?.Activate()` (D-09)
- `ViewModels/MainViewModel.cs:929` (Receive method) — extend handler logic for both `true` and `false` paths
- `ViewModels/MainViewModel.cs:869` (Logout) — add `_autoReauthAttempted = false` reset
- `ViewModels/MainViewModel.cs:398` (PollUsageAsync) — add `_autoReauthAttempted = false` on success path
- `Views/LoginView.xaml` — add `Visibility="Collapsed"` to `LoginWebView`, add the reload Button as a sibling Grid child
- `Views/LoginView.xaml.cs` — add `OnReloadLoginClicked` handler with null guards
- `ViewModels/LoginViewModel.cs:138` (HandleNavigationCompleted) — extend to flip WebView2 visibility (or `IsLoading`) when login URL loads successfully

### Architectural Constraints
- **No HttpClient for Claude API** — bridge pattern is mandatory due to Cloudflare. Phase 20 does not touch the API path; the `WebViewBridge` 401 handling stays as-is per D-01
- **Network allowlist**: `claude.ai`, `raw.githubusercontent.com`, `api.github.com` only. Reload button reuses existing claude.ai connection — no new endpoints
- **Bash discipline**: every command in its own tool call (CLAUDE.md rule) — applies to all phase commits

</code_context>

<specifics>
## Specific Ideas

- The reload button should look like the MainView footer refresh button — same glyph, same size, same transparent style — for visual coherence across the app
- The user wants the post-logout flash of the previous chat URL fully eliminated, not just minimized — hence the visibility-hide approach over a timing-based one
- The auto-reauth flag's reset story should be belt-and-suspenders — even though the Transient lifetime of `MainViewModel` makes some resets redundant, defending the invariant explicitly is preferred (D-02)

</specifics>

<deferred>
## Deferred Ideas

- **Test-strategy for `WeakReferenceMessenger.Default` mocking in `Receive` unit tests** — surfaced during discussion but deferred to the planner; existing test patterns in the project are the right reference
- **Edge case: `TryMigrateOrgIdAsync` 401 path** in `ClaudeApiService` (line 182-184) sends the same `AuthStateChangedMessage(false)` — Phase 20's auto-reauth handling will trigger correctly here too, but a code review should verify no double-trigger if both `FetchUsageAsync` and `TryMigrateOrgIdAsync` 401 in quick succession. Listed as a follow-up note for the planner.
- **`NavigationFailed` fallback for offline login URL** — if the login URL never loads (no internet), the WebView2 stays Collapsed and only the loading overlay is visible. The reload button is the user-facing recovery path. A timeout-based fallback (e.g., show WebView2 anyway after 30s) was considered but deferred — the reload button is enough for v1.4
- **Per-401-counter instead of single bool** — surfaced as alternative but deferred to backlog: a counter would let us cap auto-reauth attempts to N (>1) before falling back, but the spec design decision #1 already settled on "first 401 only" semantics
- **`NavigateAndActivate<TPage>()` overload** — discussed and rejected (D-09 chose global activation in NavigateTo instead). Documented here in case a future phase wants more granular control, the alternative API shape is documented

</deferred>

---

*Phase: 20-auth-flow-stability*
*Context gathered: 2026-05-06*
