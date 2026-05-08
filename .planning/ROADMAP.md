# Roadmap: ccInfo Windows

## Milestones

- ✅ **v1.0 CCInfoWindows MVP** — Phases 1-8 (shipped 2026-03-17)
- ✅ **v1.1 UI Polish & UX Improvements** — Phases 9-11 (shipped 2026-04-01)
- ✅ **v1.2 macOS v1.8.3 Feature Parity** — Phases 12-15 (shipped 2026-04-12)
- ✅ **v1.3 macOS v1.10.0 Feature Parity** — Phases 16-19 (shipped 2026-04-14)
- ✅ **v1.4 macOS v1.11.1 Feature Parity** — Phases 20-23 (shipped 2026-05-07)
- 🚧 **v1.5 macOS v1.12.0 Feature Parity + Hardening** — Phases 24-28 (IN PROGRESS, started 2026-05-08)

## Phases

<details>
<summary>✅ v1.0 CCInfoWindows MVP (Phases 1-8) — SHIPPED 2026-03-17</summary>

- [x] Phase 1: Foundation and Authentication (3/3 plans) — completed 2026-03-09
- [x] Phase 2: Core Monitoring Dashboard (4/4 plans) — completed 2026-03-11
- [x] Phase 3: Area Chart (3/3 plans) — completed 2026-03-11
- [x] Phase 4: Local Data Pipeline (3/3 plans) — completed 2026-03-11
- [x] Phase 5: Cost Analytics (2/2 plans) — completed 2026-03-16
- [x] Phase 6: Export, Polish, and Distribution (4/4 plans) — completed 2026-03-17
- [x] Phase 7: Security Fix & Dead Code Cleanup (1/1 plan) — completed 2026-03-17
- [x] Phase 8: Documentation Hygiene & Verification (1/1 plan) — completed 2026-03-17

Full details: `.planning/milestones/v1.0-ROADMAP.md`
Requirements: `.planning/milestones/v1.0-REQUIREMENTS.md`
Audit: `.planning/milestones/v1.0-MILESTONE-AUDIT.md`

</details>

<details>
<summary>✅ v1.1 UI Polish & UX Improvements (Phases 9-11) — SHIPPED 2026-04-01</summary>

- [x] Phase 9: Layout & Structure (2/2 plans) — completed 2026-03-19
- [x] Phase 10: Visual Styles (2/2 plans) — completed 2026-03-19
- [x] Phase 11: Behavior & Interaction (2/2 plans) — completed 2026-03-20

Full details: `.planning/milestones/v1.1-ROADMAP.md`
Requirements: `.planning/milestones/v1.1-REQUIREMENTS.md`
Audit: `.planning/milestones/v1.1-MILESTONE-AUDIT.md`

</details>

<details>
<summary>✅ v1.2 macOS v1.8.3 Feature Parity (Phases 12-15) — SHIPPED 2026-04-12</summary>

- [x] Phase 12: Model-Based Context Detection (1/1 plans) — completed 2026-04-12
- [x] Phase 13: Sonnet Context Window Setting (2/2 plans) — completed 2026-04-12
- [x] Phase 14: Session Management Polish (1/1 plans) — completed 2026-04-12
- [x] Phase 15: Footer Tooltip & Accessibility (1/1 plans) — completed 2026-04-13

Full details: `.planning/milestones/v1.2-ROADMAP.md`
Requirements: `.planning/milestones/v1.2-REQUIREMENTS.md`
Audit: `.planning/milestones/v1.2-MILESTONE-AUDIT.md`

</details>

<details>
<summary>✅ v1.3 macOS v1.10.0 Feature Parity (Phases 16-19) — SHIPPED 2026-04-14</summary>

**Milestone Goal:** Port macOS v1.9.0 and v1.10.0 features — burn rate prediction with visual warnings and toast notifications, chart gradient rendering, settings UI redesign with Segmented Control, and FileSystemWatcher verification.

- [x] **Phase 16: Burn Rate Warning** — Prediction engine, inline banner, and toast notification when token exhaustion is projected (completed 2026-04-13)
- [x] **Phase 17: Chart Horizontal Gradient** — Smooth green→yellow→orange→red gradient replacing flat zone fills on the 5-hour chart (completed 2026-04-13)
- [x] **Phase 18: Settings Redesign** — Segmented Control with General/Updates/Account/About tabs at 360px width (completed 2026-04-13)
- [x] **Phase 19: Session Watcher Verification** — Confirm FileSystemWatcher catches file-level session metadata changes (completed 2026-04-14)

Full details: `.planning/milestones/v1.3-ROADMAP.md`
Requirements: `.planning/milestones/v1.3-REQUIREMENTS.md`
Audit: `.planning/milestones/v1.3-MILESTONE-AUDIT.md`

</details>

<details>
<summary>✅ v1.4 macOS v1.11.1 Feature Parity (Phases 20-23) — SHIPPED 2026-05-07</summary>

- [x] Phase 20: Auth Flow Stability (5/5 plans, incl. gap-closure 20-05) — completed 2026-05-07
- [x] Phase 21: History Persistence Hardening (3/3 plans, incl. gap-closure 21-03) — completed 2026-05-07
- [x] Phase 22: UI Polish (4/4 plans, incl. gap-closure 22-04) — completed 2026-05-07
- [x] Phase 23: Localization Gaps (1/1 plan) — completed 2026-05-07

Full details: `.planning/milestones/v1.4-ROADMAP.md`
Requirements: `.planning/milestones/v1.4-REQUIREMENTS.md`
Audit: `.planning/milestones/v1.4-MILESTONE-AUDIT.md`

</details>

<details open>
<summary>🚧 v1.5 macOS v1.12.0 Feature Parity + Hardening (Phases 24-28) — IN PROGRESS</summary>

**Milestone Goal:** Bring CCInfoWindows to upstream stefanlange/ccInfo v1.12.0 feature parity (next 5h-window start time label + persistent session renaming) while remediating six v1.4 code-review findings and fixing three reproducible cold-start / silent-failure bugs. Build order is research-validated (foundation → dependent UX → cleanup).

- [ ] **Phase 24: Dispatcher Foundation & Marshaling Convention** — `IDispatcherQueue` adapter (mirror of v1.4 `IDispatcherTimer`), C-1/C-2 fix in `MainViewModel.Receive(AuthStateChangedMessage)`, project-wide G-1 marshaling rule documented + enforced
- [ ] **Phase 25: Cold-Start Session Hydration & Visibility Window** — `JsonlService` Cwd hydration fix, data-loss race remediation, configurable `SessionVisibilityWindowDays` setting (7/30/90/unlimited, default 30)
- [ ] **Phase 26: Persistent Session Renaming** — Pencil button + ContentDialog in MainView, new "Sessions" Settings tab (5th segment), `ISessionNameStore` JSON persistence following G-2 pattern
- [ ] **Phase 27: Next-Window Label, Org-ID Picker, Pricing Surfacing & L10N** — A1 label below countdown, B2 multi-account org picker, B3 pricing-error InfoBar, M-2 `LastFetchRelativeTime` localization
- [ ] **Phase 28: v1.4 Cleanup & Final UAT** — Delete orphan `LogoutRequestedMessage`, restore real default for `_contextModelBadgeColor`, opportunistic Nits cleanup, full milestone UAT

</details>

## Active Phases

**Next up: Phase 24 — Dispatcher Foundation & Marshaling Convention**

Run `/gsd-plan-phase 24` to begin.

<details>
<summary>Phase details from completed milestones (collapsed)</summary>

### Phase 16: Burn Rate Warning
**Goal**: Users are warned before their 5-hour token window runs out
**Depends on**: Phase 15 (prior milestone complete)
**Requirements**: BURN-01, BURN-02, BURN-03, BURN-04, BURN-05, BURN-06, BURN-07
**Success Criteria** (what must be TRUE):
  1. A red banner with flame icon appears in the main view when linear regression over the last 15 minutes (minimum 3 data points, minimum 20% utilization) projects exhaustion before the window resets
  2. The banner shows time-until-limit formatted as ~Xh YYmin or ~Xmin, in both German and English
  3. The banner disappears automatically when usage rate drops, the window resets, or the slope reverses
  4. A Windows toast notification fires exactly once when the warning first triggers; it does not re-fire until the warning clears and re-triggers in a new cycle
  5. All banner and toast text is fully localized (German and English)
**Plans**: 2 plans
Plans:
- [x] 16-01-PLAN.md — BurnRatePrediction model, BurnRateCalculator engine with TDD, theme brush, localization strings
- [x] 16-02-PLAN.md — Notification service, ViewModel integration, XAML banner, DI wiring

### Phase 17: Chart Horizontal Gradient
**Goal**: The 5-hour area chart renders a smooth horizontal color gradient instead of flat zone fills
**Depends on**: Phase 16
**Requirements**: CHRT-01, CHRT-02, CHRT-03, CHRT-04, CHRT-05
**Success Criteria** (what must be TRUE):
  1. The chart area fill transitions smoothly from green to yellow to orange to red across the horizontal data range at 25% opacity
  2. The line stroke renders at 100% opacity (2.0 px live, 2.5 px export) over the gradient fill
  3. The gradient spans only the actual data range, with correct gap handling — no gradient bleed into empty chart space
  4. Exported PNG matches the live gradient exactly, including theme (dark and light)
  5. The gradient renders correctly without desaturation artifacts in both dark and light themes
**Plans**: 2 plans
Plans:
- [x] 17-01-PLAN.md — Color interpolation lookup, gradient stop calculation, contiguous spans (pure math + TDD)
- [x] 17-02-PLAN.md — Win2D gradient brush rendering in ChartDrawing, ExportHelper line width, visual verification
**UI hint**: yes

### Phase 18: Settings Redesign
**Goal**: The Settings view uses a Segmented Control with four tabs, replacing the single-page layout
**Depends on**: Phase 16
**Requirements**: SETT-01, SETT-02, SETT-03, SETT-04, SETT-05, SETT-06, SETT-07, SETT-08
**Success Criteria** (what must be TRUE):
  1. A Segmented Control with General, Updates, Account, and About tabs (colored icon badges) is visible at the top of the Settings view at 360px width
  2. The General tab contains all existing settings in uniform 40px rows (label left, control right) with short time notation (30s, 1min, 5min, etc.)
  3. The Updates tab shows app version, pricing source info, and last pricing fetch timestamp
  4. The Account tab shows token status and the logout button; the About tab shows app name, version, GitHub link, and macOS original credits
  5. Switching tabs is smooth without page reload and all labels and content are localized in German and English
**Plans**: 2 plans
Plans:
- [x] 18-01-PLAN.md — ViewModel tab switching + short labels + version/token properties, theme badge brushes, localization keys, unit tests
- [x] 18-02-PLAN.md — Complete XAML rewrite with Segmented Control + 4 tab panels, visual verification
**UI hint**: yes

### Phase 19: Session Watcher Verification
**Goal**: FileSystemWatcher is confirmed to catch file-level session metadata changes
**Depends on**: Phase 16
**Requirements**: SESW-01
**Success Criteria** (what must be TRUE):
  1. Code review confirms NotifyFilter includes file-level change flags and IncludeSubdirectories is set correctly — or a targeted fix is applied if the configuration is wrong
  2. No regression is introduced to session refresh behavior
**Plans**: 1 plan
Plans:
- [x] 19-01-PLAN.md — Integration tests verifying FileSystemWatcher catches file-level .jsonl changes in subdirectories

### Phase 20: Auth Flow Stability
**Goal**: First HTTP 401 in a session auto-navigates to LoginView, second 401 falls back to the existing InfoBar, post-login refresh is immediate, and sign-out always presents a clean login form
**Depends on**: Phase 19 (prior milestone complete)
**Requirements**: AUTH-01, AUTH-02, AUTH-03, AUTH-04, AUTH-05, AUTH-06, AUTH-07
**Success Criteria** (what must be TRUE):
  1. The first HTTP 401 in a session triggers automatic navigation to LoginView with no user interaction; the existing InfoBar does not appear on this first attempt
  2. A second HTTP 401 within the same session is handled by the existing InfoBar fallback (no auto-open loop) — the `_autoReauthAttempted` flag prevents repeat auto-navigation
  3. After successful login, MainView refreshes immediately via the AuthStateChangedMessage handler (no app restart) and `_autoReauthAttempted` resets to false
  4. LoginView shows a manual reload button in the top-right that calls `CoreWebView2.Reload()` with a null guard, with localized tooltip and AutomationProperties.Name in DE and EN
  5. After logout, LoginView's WebView2 navigates to the login URL before display — no leftover chat URL from the prior session
**Plans**: 5 plans
Plans:
**Wave 1**
- [x] 20-01-PLAN.md — Wave 0: MainViewModelAuthFlow test scaffold + LoginReloadButton resw keys (en-US + de-DE)

**Wave 2** *(blocked on Wave 1 completion)*
- [x] 20-02-PLAN.md — _autoReauthAttempted flag + extended Receive(AuthStateChangedMessage) for D-01/D-02/D-03
- [x] 20-03-PLAN.md — LoginView reload button overlay + WebView2 Visibility-Collapsed gate (D-04..D-08)

**Wave 3** *(blocked on Wave 2 completion)*
- [x] 20-04-PLAN.md — NavigationService.NavigateTo App.MainWindow.Activate (D-09) + manual smoke battery AUTH-05/06/07

**Wave 4** *(gap closure — 20-UAT.md Test 4 cosmetic fail: reload glyph illegible against cream-white claude.ai login page)*
- [x] 20-05-PLAN.md — D-05 amendment via Border wrapper: 30x30 px Slate-900 (#0F172A) pill around LoginReloadButton for cross-surface contrast (AUTH-06)
**UI hint**: yes

### Phase 21: History Persistence Hardening
**Goal**: Usage history survives unexpected app termination, poll-cycle saves no longer block the UI thread, and 5-hour window resets clear the chart cleanly without a vertical cliff
**Depends on**: Phase 20
**Requirements**: HIST-01, HIST-02, HIST-03, HIST-04, HIST-05
**Success Criteria** (what must be TRUE):
  1. Closing MainWindow via the X button persists history points appended after the last poll save before process exit (verified by append → close → restart → points present)
  2. The poll cycle uses `SaveHistoryAsync` (`File.WriteAllTextAsync`) with no perceptible UI-thread stutter on slow disks; the termination hook continues to use synchronous `SaveHistory`
  3. `IUsageHistoryService` exposes both synchronous (`SaveHistory`) and async (`SaveHistoryAsync`) variants and both produce byte-identical JSON for the same input
  4. When the API returns a new `ResetsAt` greater than the previously observed one, `UsageHistory.Points` is cleared and persisted immediately — no vertical cliff after a 5-hour window reset
  5. The first poll after app start does not erase history (null-previous-`ResetsAt` guard verified)
**Plans**: 3 plans

Plans:
**Wave 1**
- [x] 21-01-PLAN.md -- IUsageHistoryService extension (SaveHistoryAsync + PeekLastSnapshot), SemaphoreSlim guard, snapshot cache, 9 new unit tests for HIST-02..HIST-05

**Wave 2** *(blocked on Wave 1 completion)*
- [x] 21-02-PLAN.md -- MainViewModel async cascade (UpdateUsagePropertiesAsync + AppendHistoryPointAsync), MainWindow.OnClosing termination flush, HIST-01 manual smoke

**Wave 3** *(gap closure -- 21-UAT.md Test 2 D-13 violation on Settings -> Abmelden path)*
- [x] 21-03-PLAN.md -- LogoutRequestedMessage + IRecipient round-trip (HOTFIXED to direct DI call after WeakReferenceMessenger + AddTransient recipient-GC pitfall in production); SettingsViewModel.Logout now injects IUsageHistoryService directly (D-13 honored on every UI surface)

### Phase 22: UI Polish
**Goal**: The refresh button shows an anti-flicker spinner during refreshes, inactive sessions display a tooltip with the current threshold, and the About-tab pricing timestamp stays current while the tab is visible
**Depends on**: Phase 20
**Requirements**: POLISH-01, POLISH-02, POLISH-03, POLISH-04, POLISH-05, POLISH-06, POLISH-07, POLISH-08
**Success Criteria** (what must be TRUE):
  1. While a refresh is in progress, the refresh button shows a ProgressRing in place of the arrow glyph; the button is disabled and the spinner remains visible for at least 250ms even on cached sub-100ms refreshes
  2. Inactive session ComboBox items show a two-line tooltip — path on line one, "Inactive for > {threshold}min" on line two — using the current `SessionTimeoutMinutes`; active sessions retain their existing single-line path tooltip
  3. Changing `SessionTimeoutMinutes` in settings causes the next refresh to display the updated threshold in inactive-session tooltips
  4. While the About tab is the active Settings tab, the pricing timestamp ("X minutes ago") refreshes every minute via a `DispatcherTimer`
  5. Switching to a different Settings tab or unloading the Settings page stops the `DispatcherTimer` (no background ticking, no memory leak)
**Plans**: 4 plans

Plans:
**Wave 1**
- [x] 22-01-PLAN.md — Refresh spinner anti-flicker (POLISH-01..03 / D-01..D-04): PollUsageCoreAsync extraction + 250ms Task.Delay floor + [NotifyCanExecuteChangedFor] on _isRefreshing — touches MainViewModel.cs only

**Wave 2** *(blocked on Wave 1 — shares MainViewModel.cs)*
- [x] 22-02-PLAN.md — Inactive-session ComboBox tooltip (POLISH-04..06 / D-05..D-08): SessionDisplayItem.TooltipText + ComputeTooltipText helper + IsActive bug fix + SessionTimeoutChangedMessage messenger

**Wave 3** *(blocked on Wave 2 — shares SettingsViewModel.cs)*
- [x] 22-03-PLAN.md — About-tab DispatcherTimer (POLISH-07..08 / D-09..D-11): SettingsViewModel timer ownership + 3 view-lifecycle hooks (extended OnLoaded, OnSegmentedSelectionChanged, OnUnloaded)

**Wave 4** *(gap closure — 22-UAT.md Test 1 fail: refresh button stays clickable during refresh)*
- [x] 22-04-PLAN.md — Belt-and-suspenders explicit IsEnabled x:Bind on FooterRefreshButton + public CanRefresh + [NotifyPropertyChangedFor(nameof(CanRefresh))] on _isRefreshing — reinforces D-04 Option A from 22-01 (POLISH-03)
**UI hint**: yes

### Phase 23: Localization Gaps
**Goal**: Four new resource keys (DE + EN) cover the inactive-session tooltip threshold and three generic state placeholders (`Not signed in`, `No data`, `Loading`); the two `LoginReloadButton.*` keys authored by Phase 20 are verified intact; runtime language switch works for all six L10N-01 keys
**Depends on**: Phase 20
**Requirements**: L10N-01, L10N-02, L10N-03
**Success Criteria** (what must be TRUE):
  1. Both `Strings/de-DE/Resources.resw` and `Strings/en-US/Resources.resw` contain the six L10N-01 keys: `NotSignedIn.Text`, `NoData.Text`, `Loading.Text`, `InactiveSessionTooltip`, `LoginReloadButton.[using:...]ToolTipService.ToolTip`, `LoginReloadButton.[using:...]AutomationProperties.Name` (the last two were authored by Phase 20 — Phase 23 verifies, does not re-author)
  2. No hardcoded `"Loading"`, `"No data"`, or `"Not signed in"` strings remain in any XAML view (RESEARCH-verified: 0 hardcoded sites; L10N-02 trivially satisfied — codebase uses `l:Uids.Uid` exclusively)
  3. A runtime language switch from English to German (and back) shows correct translations for all six new keys without an app restart
**Plans**: 1 plan
Plans:
- [x] 23-01-PLAN.md — Author 4 new resw keys (NotSignedIn.Text / NoData.Text / Loading.Text / InactiveSessionTooltip) in en-US + de-DE; verify 2 pre-existing LoginReloadButton.* keys; add ResourceCoverageTests xUnit class with XDocument structural validation
**UI hint**: yes

</details>

## Phase Details

### Phase 24: Dispatcher Foundation & Marshaling Convention
**Goal**: Cross-VM messenger handlers safely mutate UI state from any thread, and the project enforces a documented marshaling rule that prevents the v1.4 fire-and-forget / off-thread regression from recurring
**Depends on**: Phase 23 (prior milestone complete) — first phase of v1.5
**Requirements**: DISPATCH-01, DISPATCH-02, DISPATCH-03, DISPATCH-04, DISPATCH-05, DISPATCH-06
**Success Criteria** (what must be TRUE):
  1. `IDispatcherQueue` interface with `bool TryEnqueue(Action)` + `bool HasThreadAccess` exists, a `WinuiDispatcherQueueAdapter` is registered as singleton in DI, and a `FakeDispatcherQueue` exists in the test project and replaces every `DispatcherQueue.TryEnqueue` test seam in headless xUnit tests
  2. `MainViewModel.Receive(AuthStateChangedMessage)` always wraps its body in `_dispatcherQueue.TryEnqueue(...)` (no `if (!HasThreadAccess)` shortcut) and surfaces post-login refresh failures via `HasApiError` instead of swallowing them in a fire-and-forget Task — both C-1 and C-2 fixed in a single edit, asserted by a regression test that fails when off-thread mutation is reintroduced
  3. `CLAUDE.md` documents convention G-1 (every `IRecipient<T>.Receive(T)` body that mutates `[ObservableProperty]`, calls `INavigationService`, or touches XAML must wrap in `IDispatcherQueue.TryEnqueue`; exception only via `[ThreadSafeReceive]` attribute or inline justification comment)
  4. `MessengerThreadingConventionTests` xUnit class passes — every `IRecipient<>` implementation in the codebase respects G-1 (verified by reflection or `[RequiresMarshal]` / `[ThreadSafeReceive]` attribute pair after a 30-min Phase-24 spike decides the mechanism)
  5. Two patch bumps (CommunityToolkit.Mvvm 8.4.0→8.4.2, Microsoft.WindowsAppSDK 1.8.260209005→1.8.260416003) ship cleanly with all existing tests green
**Plans**: 3 plans
Plans:
- [x] 24-01-dispatcher-adapter-PLAN.md — IDispatcherQueue interface + ThreadSafeReceiveAttribute + WinuiDispatcherQueueAdapter + FakeDispatcherQueue + DI singleton (Wave 1)
- [x] 24-02-mainviewmodel-refactor-PLAN.md — MainViewModel constructor injection + Receive(AuthStateChangedMessage) C-1/C-2 fix + UnregisterAll + line-318 lambda audit + line-1032 cleanup + line-1008 explicit discard + MainWindow [ThreadSafeReceive] (Wave 2)
- [x] 24-03-convention-test-and-docs-PLAN.md — MessengerThreadingConventionTests xUnit class + CLAUDE.md G-1 paragraph + NuGet patch bumps (Wave 3)

### Phase 25: Cold-Start Session Hydration & Visibility Window
**Goal**: After cold start, the session ComboBox lists every relevant session whose JSONL files exist within the user-configurable visibility window — the silent dropping of recently-active sessions and the underlying file-watcher data-loss race are both eliminated
**Depends on**: Phase 24 (G-1 + `IDispatcherQueue` foundation required for `SessionVisibilityChangedMessage` handler)
**Requirements**: DROPDOWN-01, DROPDOWN-02, DROPDOWN-03, DROPDOWN-04, DROPDOWN-05, DROPDOWN-06
**Success Criteria** (what must be TRUE):
  1. After cold start, the "Aktive Sitzung" / "Active Session" ComboBox lists ALL sessions whose JSONL files were modified within `SessionVisibilityWindowDays` — not only sessions that received tool events since launch (manually verified by smoke + automated `JsonlServiceColdStartTests`)
  2. `JsonlService.ParseFileIntoProject` resolves `Cwd` from the FIRST non-empty `cwd` field across ALL parsed entries, falling back to `SessionNameHelper.DecodeProjectDirectory(projectDirName)` when none is present; `RebuildSessionsList` no longer drops sessions because Cwd was empty (only because the project directory was deleted)
  3. A new General-tab `SessionVisibilityWindowDays` ComboBox (7 / 30 / 90 / 0=unlimited, default 30) is wired through `SessionVisibilityChangedMessage`; the filter applies at the display layer in `MainViewModel.RefreshSessionList` only, and `JsonlService` continues to aggregate stats across ALL sessions (no data lost from totals)
  4. Existing installs see a one-time toast on first launch after upgrade ("Sessions older than 30 days are now hidden — adjustable in Settings"), tracked by `SessionVisibilityMigrationShown` in `AppSettings`
  5. The cold-start data-loss regression test passes: lines written between `Directory.GetFiles` and the file-position capture in `JsonlService` are NOT silently dropped (fix verified by an explicit data-loss regression test that writes lines during the race window — DROPDOWN-06 is not allowed to land silently)
**Plans**: TBD
**UI hint**: yes

### Phase 26: Persistent Session Renaming
**Goal**: Users can rename any session via a pencil button next to the MainView switcher or via a new "Sessions" Settings tab, and custom names persist across app restarts in `session-names.json` while staying decoupled from `JsonlService`'s storage-free design
**Depends on**: Phase 25 (stable session list from B1 Cwd fix is the substrate the rename UI binds to)
**Requirements**: RENAME-01, RENAME-02, RENAME-03, RENAME-04, RENAME-05, RENAME-06, RENAME-07, RENAME-08
**Success Criteria** (what must be TRUE):
  1. A pencil button next to the MainView session switcher opens a `ContentDialog` (TextBox pre-filled with current display name + Save/Cancel); Save persists the new name immediately and the MainView ComboBox updates without app restart
  2. A new "Sessions" Settings tab — inserted as the 5th segment between Account and About — lists all known sessions with inline-editable name fields; edits save on focus-loss or Enter, clearing reverts to auto-derived display name; the 5-tab Segmented Control fits at 360px (badges 30×30 with documented 28×28 fallback if clipping is observed during the Phase-26 layout spike)
  3. Custom names persist to `%LOCALAPPDATA%\CCInfoWindows\session-names.json` (schema: `Dictionary<projectDirName, customName>`, key is encoded `SessionInfo.Id` not decoded `Cwd`) and survive an app restart — a kill-and-relaunch smoke test confirms persistence
  4. Display-layer integration is `_sessionNameStore.GetCustomName(s.Id) ?? s.DisplayName` in `MainViewModel.RefreshSessionList`; `JsonlService` stays storage-free; the rename → refresh propagation uses `ISessionNameStore.NameChanged` event marshalled through `IDispatcherQueue.TryEnqueue` (NOT a `WeakReferenceMessenger` broadcast — D-13 lesson honored)
  5. `ISessionNameStore` follows convention G-2 (`SemaphoreSlim` write guard, sync + async write methods, atomic-rename via `tmp + File.Move`, `_lastSavedSnapshot` cache); control characters U+0000..U+001F and U+007F are stripped before persistence (CVE-2021-42574 mitigation); deleted sessions leave their custom name orphaned in v1.5 (no auto-prune)
**Plans**: TBD
**UI hint**: yes

### Phase 27: Next-Window Label, Org-ID Picker, Pricing Surfacing & L10N
**Goal**: Three mid-risk feature additions ship together because their file surfaces don't overlap — the next 5h-window start time is visible below the countdown, multi-account users can switch organizations without losing all metrics silently, pricing failures are surfaced instead of swallowed, and `LastFetchRelativeTime` is no longer hardcoded English
**Depends on**: Phase 26 (uses `IDispatcherQueue` foundation for new InfoBar handlers and shares the Settings Segmented Control extended in Phase 26)
**Requirements**: NEXTWIN-01, NEXTWIN-02, NEXTWIN-03, ORGID-01, ORGID-02, ORGID-03, ORGID-04, ORGID-05, PRICING-01, PRICING-02, PRICING-03, L10N-01, L10N-02, L10N-03
**Success Criteria** (what must be TRUE):
  1. A second time label below the 5h-window countdown shows the absolute reset time using `UsageResponse.FiveHour.ResetsAt` ("Mo 1.5. 16:30" or "Wed 14:30" depending on `CultureInfo.CurrentUICulture`); the label is hidden — not "—" — when `ResetsAt` is null; format auto-switches DE/EN via `l:Uids.Uid`
  2. A new "Re-detect organization" button on the Settings Account tab calls `IClaudeApiService.ListAvailableOrganizationsAsync` (existing `/api/organizations` endpoint at `ClaudeApiService.cs:163`), shows orgs in a `ContentDialog` (name + uuid), and on selection persists the new org-id and triggers the full `MainViewModel.Logout` sequence — switching orgs requires re-authentication because the WebView2 cookie jar is per-org-context (verified by smoke: select different org → land on LoginView)
  3. After 5 consecutive polls returning `utilization: 0` while an active session exists (`OrgMismatchPollThreshold = 5`), a dismissable InfoBar soft-prompt appears in MainView ("Detected possible organization mismatch — re-resolve?") with a button that opens the Account → Re-detect dialog; the "Don't show again this session" checkbox suppresses it in-memory only (resets on app restart, NOT persisted)
  4. Pricing failures surface via a dedicated `IsPricingError` warning InfoBar in MainView ("Pricing data unavailable — cost figures may be inaccurate"); the banner clears automatically when a subsequent retry succeeds; the banner stack policy caps visible banners at 2 and suppresses `IsPricingError` rendering when `IsSessionExpired == true` (auth banner takes priority — verified by a banner-stack policy test); decision is documented in PROJECT.md after Phase 27 ships
  5. `SettingsViewModel.LastFetchRelativeTime` reads from new resw keys `LastFetchRelative.JustNow`, `LastFetchRelative.MinutesAgo`, `LastFetchRelative.HoursAgo`, `LastFetchRelative.DaysAgo`, `LastFetchRelative.Never` in both `de-DE/Resources.resw` and `en-US/Resources.resw`; all ~30 new resw keys across NEXTWIN/ORGID/PRICING/L10N exist in both locales and are validated by the extended `ResourceCoverageTests`
**Plans**: TBD
**UI hint**: yes

### Phase 28: v1.4 Cleanup & Final UAT
**Goal**: All remaining v1.4 code-review remediation lands, the `[ObservableProperty]` default-value convention is documented, opportunistic Nits are bundled into a single commit, and a full milestone-level UAT pass confirms v1.5 ships clean
**Depends on**: Phase 27 (cleanup ships last to keep the test surface stable while feature work is in flight)
**Requirements**: CLEANUP-01, CLEANUP-02, CLEANUP-03, CLEANUP-04
**Success Criteria** (what must be TRUE):
  1. `Messages/LogoutRequestedMessage.cs` is deleted and no references remain (orphan dead code from reverted Plan 21-03 fully removed)
  2. `MainViewModel._contextModelBadgeColor = null!` is replaced with a real default initializer (e.g. `ParseHexBrush(...)` matching the gray fallback shown when no model is yet detected); the visible behavior matches the pre-`null!` regression and is asserted by a unit test
  3. The three opportunistic minor cleanups bundled from `2026-05-07-nits-v14-code-review-cleanups.md` land in a single commit with no behavioral change
  4. `CLAUDE.md` documents convention G-3 ("prefer `= string.Empty;`, `= "--";`, or `= ParseHexBrush(...)` initializers over `null!` for `[ObservableProperty]` fields"), and the M-3 fix is cited as the precedent
  5. Full milestone UAT runs green: every v1.5 success criterion above (24-27) is re-verified in a single end-to-end pass; no new test failures appear on the v1.5-modified surface beyond the documented pre-existing baselines
**Plans**: TBD

## Progress

| Phase | Milestone | Plans Complete | Status | Completed |
|-------|-----------|----------------|--------|-----------|
| 1. Foundation and Authentication | v1.0 | 3/3 | Complete | 2026-03-09 |
| 2. Core Monitoring Dashboard | v1.0 | 4/4 | Complete | 2026-03-11 |
| 3. Area Chart | v1.0 | 3/3 | Complete | 2026-03-11 |
| 4. Local Data Pipeline | v1.0 | 3/3 | Complete | 2026-03-11 |
| 5. Cost Analytics | v1.0 | 2/2 | Complete | 2026-03-16 |
| 6. Export, Polish, and Distribution | v1.0 | 4/4 | Complete | 2026-03-17 |
| 7. Security Fix & Dead Code Cleanup | v1.0 | 1/1 | Complete | 2026-03-17 |
| 8. Documentation Hygiene & Verification | v1.0 | 1/1 | Complete | 2026-03-17 |
| 9. Layout & Structure | v1.1 | 2/2 | Complete | 2026-03-19 |
| 10. Visual Styles | v1.1 | 2/2 | Complete | 2026-03-19 |
| 11. Behavior & Interaction | v1.1 | 2/2 | Complete | 2026-03-20 |
| 12. Model-Based Context Detection | v1.2 | 1/1 | Complete | 2026-04-12 |
| 13. Sonnet Context Window Setting | v1.2 | 2/2 | Complete | 2026-04-12 |
| 14. Session Management Polish | v1.2 | 1/1 | Complete | 2026-04-12 |
| 15. Footer Tooltip & Accessibility | v1.2 | 2/2 | Complete | 2026-04-13 |
| 16. Burn Rate Warning | v1.3 | 2/2 | Complete    | 2026-04-13 |
| 17. Chart Horizontal Gradient | v1.3 | 2/2 | Complete    | 2026-04-13 |
| 18. Settings Redesign | v1.3 | 2/2 | Complete    | 2026-04-14 |
| 19. Session Watcher Verification | v1.3 | 1/1 | Complete    | 2026-04-14 |
| 20. Auth Flow Stability | v1.4 | 5/5 | Complete | 2026-05-07 |
| 21. History Persistence Hardening | v1.4 | 3/3 | Complete | 2026-05-07 |
| 22. UI Polish | v1.4 | 4/4 | Complete | 2026-05-07 |
| 23. Localization Gaps | v1.4 | 1/1 | Complete | 2026-05-07 |
| 24. Dispatcher Foundation & Marshaling Convention | v1.5 | 3/3 | Complete   | 2026-05-08 |
| 25. Cold-Start Session Hydration & Visibility Window | v1.5 | 0/0 | Not started | - |
| 26. Persistent Session Renaming | v1.5 | 0/0 | Not started | - |
| 27. Next-Window Label, Org-ID Picker, Pricing Surfacing & L10N | v1.5 | 0/0 | Not started | - |
| 28. v1.4 Cleanup & Final UAT | v1.5 | 0/0 | Not started | - |
