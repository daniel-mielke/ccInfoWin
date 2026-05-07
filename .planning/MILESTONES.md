# Milestones

## v1.4 macOS v1.11.1 Feature Parity (Shipped: 2026-05-07)

**Phases completed:** 4 phases (20-23), 13 plans (10 base + 3 gap-closure), 51 commits
**Changes:** 64 files, +11,115/-42 lines
**Tests added:** 4 new test classes (`MainViewModelAuthFlowTests`, expanded `UsageHistoryServiceTests`, `SettingsViewModelTimerTests`, `ResourceCoverageTests`); 26+ tests GREEN on modified surface
**Audit:** PASSED (21/23 fully verified, 2 deferred with full unit-test coverage)

**Key accomplishments:**

- Auth flow stability — `_autoReauthAttempted` state machine routes first 401 automatically to LoginView, second 401 to existing InfoBar fallback; `App.MainWindow.Activate()` injected before every frame navigation activates minimized windows reliably; LoginView reload button with 30×30 Slate-900 pill wrapper for cross-surface contrast on cream-white claude.ai login page
- History persistence hardening — `IUsageHistoryService` extended with `SaveHistoryAsync` (poll path, byte-identical to sync) + `PeekLastSnapshot` (termination guard); `SemaphoreSlim(1,1)` prevents concurrent writes; `MainWindow.OnClosing` flushes synchronously via snapshot before process exit; ResetsAt comparison clears Points on 5-hour window reset
- UI polish — `PollUsageCoreAsync` extraction with 250 ms `Task.Delay` floor eliminates spinner flicker; `SessionDisplayItem.TooltipText` recomputes reactively via `SessionTimeoutChangedMessage`; `IDispatcherTimer` adapter on the About-tab makes the timer headless-testable (6 lifecycle tests GREEN)
- Localization gaps — 4 new resw keys (NotSignedIn / NoData / Loading / InactiveSessionTooltip) in DE+EN; `ResourceCoverageTests` validates all 6 L10N-01 keys × 2 locales structurally via `XDocument` — no XAML migration needed (codebase already used `l:Uids.Uid` exclusively)
- Production hotfix pitfall captured — `WeakReferenceMessenger` + `AddTransient<ViewModel>` led to recipient GC and broke logout in production; reverted to direct DI call in `SettingsViewModel.Logout`; architectural memory written for v1.5 review (`architecture_weakreferencemessenger_with_transient_vms.md`)
- Three real bugs caught by UAT, all fixed via gap-closure plans (20-05 reload-button contrast, 21-03 logout history-clearing, 22-04 refresh-button IsEnabled override) — `gap-closure-as-additional-wave` pattern preserved phase-numbering stability
- All 23/23 v1.4 requirements satisfied (AUTH-01..07, HIST-01..05, POLISH-01..08, L10N-01..03)

---

## v1.3 macOS v1.10.0 Feature Parity (Shipped: 2026-04-14)

**Phases completed:** 4 phases (16-19), 7 plans, 20 commits
**Changes:** +1879/-185 lines, 49 new unit tests

**Key accomplishments:**

- Burn rate prediction engine — linear regression over last 15 minutes (min 3 points, min 20% utilization), red banner with flame icon, one-shot toast notification fired exactly once per warning cycle
- BurnRatePrediction model + BurnRateCalculator engine fully TDD'd, theme brush + DE/EN localization for "~Xh YYmin" / "~Xmin" formats
- Chart horizontal gradient — green→yellow→orange→red transitions at 25% opacity over data range, 100% line stroke (2.0px live, 2.5px export), correct gap handling, no bleed into empty chart space
- CanvasLinearGradientBrush wrapped per draw cycle (not cached); CanvasAlphaMode.Premultiplied prevents desaturation in Win2D
- Settings view rewritten with Segmented Control — General/Updates/Account/About tabs at 360px width, colored icon badges, 40px row layout, smooth tab switching without page reload
- IsXxxTabVisible computed bools backed by SegmentedItem.Content (not Icon) for badge rendering; existing InvertedBoolToVisibilityConverter reused
- FileSystemWatcher session detection verified — NotifyFilter and IncludeSubdirectories confirmed via integration tests with IAsyncDisposable cleanup pattern
- All 21/21 v1.3 requirements satisfied (BURN-01..07, CHRT-01..05, SETT-01..08, SESW-01)

---

## v1.2 macOS v1.8.3 Feature Parity (Shipped: 2026-04-13)

**Phases completed:** 4 phases, 6 plans, 14 commits
**Changes:** 23 files, +966/-106 lines

**Key accomplishments:**

- ModelContextLimits rewritten from token-count heuristic to ModelFamily enum — Opus returns 1M, Sonnet uses configurable size, Haiku/Unknown default to 200K; flat 33K buffer and 20K warning threshold
- Sonnet context window setting added to Settings view (200K/1M ComboBox) with live refresh via WeakReferenceMessenger — context bars update immediately on setting change
- ISettingsService injected into JsonlService for SonnetContextSize passthrough to GetMaxContextTokens, completing the end-to-end settings→display pipeline
- IsValidProjectDirectory guard filters orphaned sessions (deleted project directories, UNC paths) and subagent context bars sorted alphabetically by AgentId
- Footer tooltip fix: explicit ToolTipService.ToolTip attributes added to all footer buttons — WinUI3Localizer Uid-only injection doesn't create tooltip infrastructure at XAML parse time
- All 17 v1.2 requirements satisfied (CTX-01..06, SET-01..05, SES-01..03, ACC-01..03)

---

## v1.1 UI Polish & UX Improvements (Shipped: 2026-04-01)

**Phases completed:** 3 phases, 6 plans, 10 tasks

**Key accomplishments:**

- MainView section order restructured: Active Session header added, equal 16px padding applied, Context Window moved before 5-Hour Window per macOS reference layout
- Footer relocated into ScrollViewer (scrolls with content), Statistics grid gains Models/Input separator — completing all 6 LAYOUT requirements from Plans 01 and 02
- Pure XAML styling: semi-transparent progress track (#72808080), pill badges (CornerRadius=999), ComboBox with SegmentedBackgroundBrush, and secondary-colored statistics labels with normal weight
- AxisLabelBrush corrected to match SecondaryTextBrush (#8E8E93 dark / #6E6E73 light) — chart axis labels now visually consistent with timer text
- CountdownFormatter extended with >=24h "Xd Yh" branch (TDD), logout button styled red with F3B1 icon, ReLogin button decorated with login icon
- WinUI 3 Storyboard deferred-stop pattern using _stopOnComplete flag — refresh icon always completes its current 360-degree rotation before halting

---

## v1.0 CCInfoWindows MVP (Shipped: 2026-03-17)

**Phases completed:** 8 phases, 21 plans, 2 tasks

**Key accomplishments:**

- (none recorded)

---
