# Project Retrospective

*A living document updated after each milestone. Lessons feed forward into future planning.*

## Milestone: v1.0 — CCInfoWindows MVP

**Shipped:** 2026-03-17
**Phases:** 8 | **Plans:** 21 | **Commits:** 75

### What Was Built
- WebView2-based authentication with Cloudflare bypass via postMessage bridge pattern
- Real-time 5-hour/weekly usage dashboard with color-coded progress bars and auto-refresh
- Interactive Win2D area chart with gradient fills, glow indicator, and persistent history
- Local JSONL data pipeline with FileSystemWatcher for session/context/token monitoring
- Cost analytics with live LiteLLM pricing, tiered rate calculation, time-period aggregation
- Feature-complete desktop app with DE/EN localization, chart export, Inno Setup installer

### What Worked
- GSD workflow (discuss → plan → execute → verify) kept each phase focused and shippable
- WebView2 bridge pattern solved the Cloudflare 403 problem elegantly — no manual cookie handling needed
- Phase dependency graph (4 depends on 1, not 3) allowed parallel development of chart and data pipeline
- Gap closure phases 7-8 cleaned up tech debt identified by milestone audit — effective quality gate

### What Was Inefficient
- Phase 4 introduced a parameter naming mismatch (`sessionId` vs `projectDirName`) that broke 13 unit tests — caught late, now tech debt
- REQUIREMENTS.md checkboxes fell out of sync with implementation during phases 2-6 — required dedicated phase 8 to fix
- Some VERIFICATION.md files were not created during execute-phase (phases 2, 4) — required retroactive creation

### Patterns Established
- WebView2 bridge pattern: JS `fetch()` → `postMessage` → `WebMessageReceived` → `TaskCompletionSource` for any Cloudflare-protected API
- `l:Uids.Uid` for WinUI3Localizer runtime language switching (not `x:Uid`)
- `[ObservableProperty]` + `[RelayCommand]` source generators as standard MVVM pattern
- Win2D offscreen rendering for chart export (PNG/clipboard)

### Key Lessons
1. Cloudflare bot protection detects .NET HttpClient TLS fingerprint — always use WebView2 bridge for claude.ai API calls
2. Interface parameter names should match implementation semantics from day one — renaming later breaks all tests
3. Requirement checkbox tracking needs automation, not manual discipline — checkboxes drift during rapid development
4. WinUI 3 unpackaged apps need explicit `WindowsPackageType=None` and `asInvoker` manifest for non-admin installation

### Cost Observations
- Model mix: Primarily Sonnet for research/planning/execution agents, Opus for orchestration
- Sessions: ~20+ across 9 days of development
- Notable: Phase 7+8 (gap closure) were highly efficient — infrastructure phases with no user-facing decisions needed

---

## Milestone: v1.1 — UI Polish & UX Improvements

**Shipped:** 2026-04-01
**Phases:** 3 | **Plans:** 6 | **Commits:** 41

### What Was Built
- Consistent layout: equal 16px padding all sides, Active Session header, Context Window before 5-Hour Window, scrollable footer with separator
- Unified visual system: 6px progress bars with semi-transparent gray track (#72808080), pill model badges, rounded ComboBox + tab bar
- Chart axis labels corrected to match SecondaryTextBrush (#8E8E93 dark / #6E6E73 light) — now visually consistent with timer text
- Timer format extended: values ≥24h display as "Xd Yh" with localized unit abbreviations (DE: "3T 22h")
- Interaction polish: logout button red with icon, login button with icon, refresh icon always completes full 360° rotation before stopping

### What Worked
- AppTheme.xaml as single source of truth — all STYLE changes went through one file, no scattered XAML edits
- TDD for CountdownFormatter — tests written first, logic followed; edge cases caught before integration
- Phase decomposition by change type (layout / style / behavior) enabled clean, non-overlapping edits
- `_stopOnComplete` flag pattern for Storyboard — elegant WinUI 3 solution to the "snap mid-rotation" problem

### What Was Inefficient
- CornerRadius=999 for pill badges worked visually but left spec drift — should have aligned SUMMARY artifact before closing Phase 10
- `fix(10)` commit needed post-phase for ProgressBar track height — the ProgressBarTrackHeight override was missed in the original plan
- MILESTONES.md `one_liner` field for 10-02 came back empty from gsd-tools (blank YAML field) — required manual cleanup

### Patterns Established
- AppTheme.xaml ResourceDictionary as the only location for visual style constants — no hardcoded colors in views
- WinUI 3 Storyboard deferred-stop: `_stopOnComplete = true` → animation's `Completed` event calls `Stop()` — prevents snap-halt
- `ProgressBarTrackHeight` template property override via `Style.Setters` for track height (not `MinHeight` on `ProgressBar`)

### Key Lessons
1. XAML-only phases are fast and low-risk — grouping by structural type (layout / style / behavior) is more effective than grouping by feature
2. Control template properties in WinUI 3 (track height, corner radius on ComboBox) require ResourceDictionary overrides, not direct property sets
3. Spec drift between documentation and live code accumulates silently — SUMMARY glyph values and CornerRadius values should be verified against live code before phase closure

### Cost Observations
- Sessions: ~15 across ~2 weeks (lower velocity than v1.0, primarily XAML-heavy work)
- Notable: Phase 9 required structural XAML rearrangement; Phases 10-11 were faster due to narrower change surface

---

## Milestone: v1.2 — macOS v1.8.3 Feature Parity

**Shipped:** 2026-04-13
**Phases:** 4 | **Plans:** 6 | **Commits:** 14

### What Was Built
- ModelContextLimits rewritten from token-count heuristic to ModelFamily enum — Opus=1M, Sonnet=configurable, Haiku/Unknown=200K
- Sonnet context window setting (200K/1M ComboBox in Settings) with live refresh via WeakReferenceMessenger
- ISettingsService→JsonlService→GetMaxContextTokens pipeline for runtime Sonnet context size passthrough
- Session orphan filtering (IsValidProjectDirectory guard with UNC path short-circuit) and alphabetical subagent sort
- Footer tooltip fix: explicit ToolTipService.ToolTip attributes to compensate for WinUI3Localizer Uid-only limitation

### What Worked
- TDD for session filtering — 4 tests written first, implementation followed cleanly
- UAT caught a real bug (tooltips not displaying) that static verification missed — the verify-work→diagnose→plan→fix cycle worked exactly as designed
- Single-day execution: all 4 phases planned, executed, and verified in one session — lean phases with clear scope
- Optional constructor parameter pattern (`settingsService = null`) preserved all 13+ existing test constructors unchanged

### What Was Inefficient
- Phase 15 Plan 01 was verification-only (zero code changes) — the tooltip wiring was assumed complete from prior research but runtime behavior wasn't tested until UAT
- The compiled XAML showed ToolTipService.ToolTip correctly, which was misleading — the build artifact doesn't reflect runtime behavior for Uid-injected properties

### Patterns Established
- ModelFamily enum with substring matching (`lower.Contains("opus")`) — consistent with GetBadgeColorHex pattern
- Flat buffer constants (33K autocompact, 20K warning) instead of percentage thresholds — model-size-independent UX
- IsValidProjectDirectory guard order: IsNullOrEmpty → IsPathRooted → UNC check → Directory.Exists (prevents network hang)
- WinUI3Localizer ToolTip rule: always add explicit `ToolTipService.ToolTip` in source XAML when using `l:Uids.Uid` — Uid-only injection doesn't create tooltip UI infrastructure

### Key Lessons
1. WinUI3Localizer Uid-only property injection works for data properties (AutomationProperties.Name) but NOT for properties that create UI elements (ToolTipService.ToolTip) — always provide an explicit placeholder attribute
2. Compiled XAML output is not a reliable proxy for runtime behavior — build-time and runtime property resolution have different lifecycles
3. Static method parameters vs instance field access: when a static helper needs DI state, pass as parameter from non-static callers rather than refactoring to instance method
4. UNC path guard is mandatory before Directory.Exists on Windows — unreachable servers cause indefinite hangs

### Cost Observations
- Sessions: ~5 across single day
- Notable: Fastest milestone yet — lean scope, clear dependency chain (12→13→14, 15 independent), zero ambiguity in requirements

---

## Milestone: v1.3 — macOS v1.10.0 Feature Parity

**Shipped:** 2026-04-14
**Phases:** 4 | **Plans:** 7 | **Commits:** 20

### What Was Built
- Burn rate prediction with linear regression over the last 15 minutes — red banner with flame icon, one-shot toast notification fired exactly once per warning cycle
- Chart horizontal gradient at 25 % opacity over data range — green→yellow→orange→red transitions with 100 % line stroke (2.0 px live, 2.5 px export), correct gap handling
- Settings rewritten with Segmented Control — General/Updates/Account/About tabs, colored icon badges, 40 px row layout, smooth tab switching without page reload
- FileSystemWatcher session detection verified — `NotifyFilter` and `IncludeSubdirectories` confirmed via integration tests with `IAsyncDisposable` cleanup pattern

### What Worked
- TDD on `BurnRateCalculator` — pure-math engine fully testable without WinUI plumbing; edge cases (insufficient points, low utilization) covered before integration
- `CanvasAlphaMode.Premultiplied` on the Win2D gradient brush prevented desaturation artifacts in both themes — discovered through visual review, fixed once, no regression
- `IsXxxTabVisible` computed bools backed by `SegmentedItem.Content` — reused existing `InvertedBoolToVisibilityConverter`, zero new converter classes

### What Was Inefficient
- Phase 19 was almost a no-op — the FileSystemWatcher was already correctly configured; the verification test added value but the phase scope could have been a single plan inside Phase 18

### Patterns Established
- Linear regression over rolling window for prediction features — testable, deterministic, low overhead
- Single one-shot toast pattern via boolean flag on the warning cycle — fire-once-per-cycle, no rapid-fire spam
- Segmented Control with content-driven badge rendering (vs `Icon=` which only accepts `IconElement`)

### Key Lessons
1. Win2D gradient rendering requires `CanvasAlphaMode.Premultiplied` to avoid desaturation in dark themes
2. AppNotificationManager hooks must subscribe `NotificationInvoked` BEFORE `Register()`, only once — order matters
3. `SegmentedItem.Content` accepts arbitrary visuals (use this for colored badges); `Icon=` is restrictive

### Cost Observations
- Sessions: ~8 across 2 days
- Notable: Verification-only Phase 19 was the cheapest phase yet — confirms that "is this already correct?" deserves a phase when uncertainty is real

---

## Milestone: v1.4 — macOS v1.11.1 Feature Parity

**Shipped:** 2026-05-07
**Phases:** 4 | **Plans:** 13 (10 base + 3 gap-closure) | **Commits:** 51

### What Was Built
- Auth flow stability — `_autoReauthAttempted` state machine (first 401 → auto-navigate to LoginView, second 401 → InfoBar fallback), `App.MainWindow.Activate()` before frame navigation, LoginView reload button with Slate-900 pill wrapper for cross-surface contrast
- History persistence hardening — `IUsageHistoryService.SaveHistoryAsync` (poll path, byte-identical to sync), `PeekLastSnapshot` (termination guard), `SemaphoreSlim(1,1)` write protection, `MainWindow.OnClosing` synchronous flush, ResetsAt-based 5-hour-window clear
- UI polish — refresh-spinner anti-flicker via `PollUsageCoreAsync` extraction + 250 ms `Task.Delay` floor, inactive-session ComboBox tooltip recomputing via `SessionTimeoutChangedMessage`, About-tab `IDispatcherTimer` adapter with full lifecycle management
- Localization gaps — 4 new resw keys (DE/EN), `ResourceCoverageTests` validates 6 L10N-01 keys × 2 locales structurally via `XDocument`; codebase already used `l:Uids.Uid` exclusively (zero XAML migration needed)

### What Worked
- Gap-closure as additional wave within parent phase (20-05, 21-03, 22-04) preserved REQ-ID-to-phase mapping cleanly — three real UAT bugs fixed without breaking phase numbering
- `IDispatcherTimer` adapter pattern made the About-tab timer fully testable in headless xUnit — 6 lifecycle tests caught a tab-switch leak that visual smoke would have missed
- TDD on `MainViewModelAuthFlowTests` (Wave 0 RED → Wave 1+ GREEN) verified the auth state machine in 4 tests; Manual smoke for AUTH-01/02 deferred but the state machine is fully proven

### What Was Inefficient
- Plan 21-03 shipped twice — first with `LogoutRequestedMessage` + `IRecipient<>` round-trip (architecturally cleaner), then HOTFIXED with revert to direct DI call after `WeakReferenceMessenger` + `AddTransient<MainViewModel>` recipient-GC dropped the message in production. Should have flagged the WeakReference + Transient combination during code review
- POLISH-04 / POLISH-07 visual smoke blocked by separate backlog items (cold-start session scanning, pricing-service silent failure) — UAT discovered these but they couldn't be fixed inside v1.4 scope; deferred with full unit-test coverage as compensation
- AUTH-01/02 manual smoke deferred — dev build can't easily force a 401; needed a chaos-monkey style test harness that wasn't planned

### Patterns Established
- Gap-closure as "additional wave within parent phase" — keeps requirement mapping stable across UAT-discovered bugs, no decimal-phase renumbering churn
- Hybrid sync+async persistence — sync at termination (no async-completion guarantee on `Window.Closed`), async during poll (no UI stutter); guard byte-identity with a unit test
- `IDispatcherTimer` adapter wrapping WinRT `DispatcherTimer` as a .NET `EventHandler` relay — enables `FakeDispatcherTimer` for headless tests without XAML
- Belt-and-suspenders `IsEnabled` x:Bind on `[RelayCommand]` buttons when `CanExecute` chain shows binding-priority quirks

### Key Lessons
1. **WeakReferenceMessenger + AddTransient ViewModels = silent message drop** — recipients get GC'd when their owning ViewModel is transient; never use cross-VM messaging for exactly-once flows like logout. Use direct DI injection instead.
2. UAT catches runtime regressions that static analysis cannot — three real bugs in v1.4 were only visible in the running app (cosmetic glyph contrast, logout side-effect, refresh-button binding race)
3. Architecturally "cleaner" doesn't mean "production-correct" — 21-03's first version was elegant but broke under real DI lifetimes; the boring direct call shipped
4. Gap-closure plans with explicit UAT-fail context (e.g. "22-UAT.md Test 1 fail") are faster to plan and execute than retrofit refactors — the scope is already constrained

### Cost Observations
- Sessions: ~12 across 2 days
- Notable: Highest plan count of any milestone (13) but tightest LOC discipline — most plans were single-file changes; gap-closure pattern compressed iteration cycles

---

## Cross-Milestone Trends

### Process Evolution

| Milestone | Commits | Phases | Key Change |
|-----------|---------|--------|------------|
| v1.0 | 75 | 8 | First milestone — established GSD workflow, gap closure phases |
| v1.1 | 41 | 3 | UI-only milestone — AppTheme.xaml single source of truth pattern |
| v1.2 | 14 | 4 | Feature parity milestone — model-based detection, UAT-driven gap closure |
| v1.3 | 20 | 4 | Visual + prediction features — TDD on pure-math engines, Win2D gradient mastery |
| v1.4 | 51 | 4 | Auth/persistence stability — gap-closure-as-wave pattern, WeakReferenceMessenger pitfall caught in production |

### Cumulative Quality

| Milestone | Tests | Passing | Known Failures |
|-----------|-------|---------|----------------|
| v1.0 | 177 | 164 | 13 (JsonlServiceTests parameter mismatch) |
| v1.1 | 177+ | 164+ | 13 (same — not addressed in v1.1 scope) |
| v1.2 | 200+ | 198+ | 2 (ClaudeApiService — pre-existing, unrelated) |
| v1.3 | 247+ | 245+ | 2 (ClaudeApiService — same pre-existing 2) |
| v1.4 | 273+ | 271+ | 2 (ClaudeApiService — same pre-existing 2) |

### Top Lessons (Verified Across Milestones)

1. WebView2 bridge is the only reliable way to call Cloudflare-protected APIs from .NET desktop apps
2. Gap closure phases at milestone end are an effective quality gate — catch integration issues before shipping
3. Grouping XAML changes by structural type (layout / style / behavior) produces cleaner, reviewable phases than grouping by feature
4. WinUI3Localizer Uid-only injection doesn't work for all attached properties — ToolTipService.ToolTip needs explicit XAML attribute (v1.2 lesson)
5. UAT (user acceptance testing) catches runtime bugs that static verification and builds cannot — always test with the running app
6. **Cross-VM messaging requires careful lifetime analysis** — `WeakReferenceMessenger` + `AddTransient` ViewModels causes silent message drops via recipient GC; for exactly-once flows (logout, save-on-close), use direct DI injection (v1.4 lesson)
7. **Gap-closure as additional wave** preserves requirement-to-phase stability better than decimal phase insertion — proven across 3 v1.4 UAT-discovered bugs (20-05, 21-03, 22-04)
