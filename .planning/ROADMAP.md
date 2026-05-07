# Roadmap: ccInfo Windows

## Milestones

- ✅ **v1.0 CCInfoWindows MVP** — Phases 1-8 (shipped 2026-03-17)
- ✅ **v1.1 UI Polish & UX Improvements** — Phases 9-11 (shipped 2026-04-01)
- ✅ **v1.2 macOS v1.8.3 Feature Parity** — Phases 12-15 (shipped 2026-04-12)
- ✅ **v1.3 macOS v1.10.0 Feature Parity** — Phases 16-19 (shipped 2026-04-14)
- ✅ **v1.4 macOS v1.11.1 Feature Parity** — Phases 20-23 (shipped 2026-05-07)

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

## Active Phases

(None — v1.4 shipped. Run `/gsd-new-milestone` to plan v1.5.)

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
