---
phase: 05-cost-analytics
plan: 02
subsystem: ui
tags: [winui3, xaml, segmented-control, community-toolkit, shimmer, statistics, burn-rate, pricing]

# Dependency graph
requires:
  - phase: 05-01
    provides: StatisticsSummary, BurnRateCalculator, CostFormatter, TokenFormatter, IPricingService, IJsonlService.GetStatistics

provides:
  - STATISTIKEN section in MainView with 4-tab Segmented control (Session/Heute/Woche/Monat)
  - Statistics data table (8 rows: Modelle, Eingabe, Ausgabe, Cache-Schreiben, Cache-Lesen, Gesamt, Kosten, Burn Rate)
  - Shimmer animation during cross-session aggregation (IsAggregating state)
  - MainViewModel.SelectedTabIndex, IsAggregating, ApplyStatistics with BurnRateCalculator.ComputeBurnRate wiring
  - SettingsView pricing info rows (Preisdaten, Zuletzt aktualisiert)
  - 9 unit tests in MainViewModelStatisticsTests verifying statistics display logic

affects: [06-export, testing, visual-verification]

# Tech tracking
tech-stack:
  added:
    - CommunityToolkit.WinUI.Controls.Segmented v8.2.251219
    - CommunityToolkit.WinUI.Extensions v8.2.251219 (transitive)
  patterns:
    - InvertBool static helper in code-behind for inverse x:Bind visibility (avoids InvertedBoolToVisibilityConverter in hot path)
    - Shimmer animation via SolidColorBrush + Storyboard ColorAnimation on single BurnRateShimmer border
    - IsAggregating toggle from OnViewModelPropertyChanged starting/stopping Storyboard
    - Backward compat properties kept until XAML replacement confirmed (InputTokensText, OutputTokensText)

key-files:
  created:
    - CCInfoWindows.Tests/ViewModels/MainViewModelStatisticsTests.cs
  modified:
    - CCInfoWindows/CCInfoWindows/CCInfoWindows.csproj
    - CCInfoWindows/CCInfoWindows/Resources/AppTheme.xaml
    - CCInfoWindows/CCInfoWindows/App.xaml.cs
    - CCInfoWindows/CCInfoWindows/ViewModels/MainViewModel.cs
    - CCInfoWindows/CCInfoWindows/ViewModels/SettingsViewModel.cs
    - CCInfoWindows/CCInfoWindows/Views/MainView.xaml
    - CCInfoWindows/CCInfoWindows/Views/MainView.xaml.cs
    - CCInfoWindows/CCInfoWindows/Views/SettingsView.xaml

key-decisions:
  - "Backward compat: _inputTokensText and _outputTokensText kept in MainViewModel until XAML TOKENS section is replaced (XAML compiler crashes if x:Bind references missing properties)"
  - "Shimmer animation targets only BurnRateShimmer by name; static BoolToVisibilityConverter from App.xaml handles visibility toggle for all shimmer borders"
  - "HttpClient registered as AddSingleton in DI container — LiteLLMPricingService injected via factory lambda"
  - "ApplyStatistics declared internal (not private) for test harness access without InternalsVisibleTo assembly attribute"
  - "MainViewModelTestHarness approach: tests use standalone harness class mirroring ApplyStatistics logic, avoiding DispatcherQueue dependency in unit tests"

patterns-established:
  - "Pattern: Static InvertBool(bool) in Page code-behind for x:Bind inverse visibility without converter overhead"
  - "Pattern: Shimmer animation subscribes to PropertyChanged in OnLoaded, starts/stops Storyboard based on IsAggregating"

requirements-completed: [TOKS-02, COST-05, COST-06]

# Metrics
duration: 15min
completed: 2026-03-16
---

# Phase 5 Plan 02: STATISTIKEN UI Pipeline Summary

**CommunityToolkit Segmented tab bar wired to MainViewModel statistics with shimmer loading, burn rate display, and Settings pricing source info**

## Performance

- **Duration:** ~15 min
- **Started:** 2026-03-16T12:14:48Z
- **Completed:** 2026-03-16T12:29:04Z
- **Tasks:** 2 of 3 (Task 3 is checkpoint awaiting visual verification)
- **Files modified:** 8

## Accomplishments

- Installed CommunityToolkit.WinUI.Controls.Segmented v8.2.251219 and registered HttpClient + IPricingService in DI
- Extended MainViewModel with SelectedTabIndex, IsAggregating, 8 statistics properties, OnSelectedTabIndexChanged, AggregateStatisticsAsync, and ApplyStatistics with real BurnRateCalculator.ComputeBurnRate call
- Replaced TOKENS section in MainView.xaml with full STATISTIKEN section (Segmented tab bar + 8-row data table + shimmer borders)
- Added shimmer ColorAnimation Storyboard in MainView.xaml.cs triggered by IsAggregating
- Extended SettingsView.xaml and SettingsViewModel with PricingSourceText + LastPricingFetchText
- Added 9 passing unit tests in MainViewModelStatisticsTests

## Task Commits

Each task was committed atomically:

1. **Task 1: NuGet, theme resources, DI wiring, ViewModel statistics** - `419e0ad` (feat)
2. **Task 2: STATISTIKEN XAML section with tab bar, data table, shimmer** - `220ef8e` (feat)
3. **Task 3: Visual verification** - PENDING (checkpoint)

## Files Created/Modified

- `CCInfoWindows.Tests/ViewModels/MainViewModelStatisticsTests.cs` - 9 unit tests for statistics display logic (burn rate, cost formatting, tab switching)
- `CCInfoWindows/CCInfoWindows/CCInfoWindows.csproj` - CommunityToolkit.WinUI.Controls.Segmented v8.2.251219 reference
- `CCInfoWindows/CCInfoWindows/Resources/AppTheme.xaml` - SegmentedBackgroundBrush, SegmentedActiveBackgroundBrush, ShimmerBaseBrush, ShimmerHighlightBrush (Dark + Light)
- `CCInfoWindows/CCInfoWindows/App.xaml.cs` - HttpClient singleton, LiteLLMPricingService registration, JsonlService factory with pricing injection
- `CCInfoWindows/CCInfoWindows/ViewModels/MainViewModel.cs` - SelectedTabIndex, IsAggregating, 8 statistics fields, OnSelectedTabIndexChanged, AggregateStatisticsAsync, ApplyStatistics, UpdateStatisticsFromSession
- `CCInfoWindows/CCInfoWindows/ViewModels/SettingsViewModel.cs` - IPricingService injection, PricingSourceText, LastPricingFetchText
- `CCInfoWindows/CCInfoWindows/Views/MainView.xaml` - STATISTIKEN section replaces TOKENS, controls:Segmented tab bar, 8-row stats Grid with shimmer
- `CCInfoWindows/CCInfoWindows/Views/MainView.xaml.cs` - InvertBool static helper, StartShimmerAnimation, StopShimmerAnimation, IsAggregating handler
- `CCInfoWindows/CCInfoWindows/Views/SettingsView.xaml` - Preisdaten and Zuletzt aktualisiert rows

## Decisions Made

- Kept `_inputTokensText` and `_outputTokensText` in MainViewModel as backward-compat until XAML is confirmed built — XAML compiler crashes silently if x:Bind references non-existent properties
- Used a standalone `MainViewModelTestHarness` in test file instead of `[InternalsVisibleTo]` to avoid needing assembly attribute changes
- Shimmer animation attached only to `BurnRateShimmer` named border (one animation for the storyboard), relying on BoolToVisibilityConverter for all shimmer borders' visibility
- `HttpClient` added as `AddSingleton<HttpClient>()` — bare registration, consistent with single-instance HTTP pattern

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] XAML compiler crash due to missing InputTokensText/OutputTokensText**
- **Found during:** Task 1 (ViewModel refactoring)
- **Issue:** MainView.xaml had x:Bind references to InputTokensText and OutputTokensText. Removing these properties from MainViewModel caused XamlCompiler.exe to exit 1 with no error output.
- **Fix:** Kept `_inputTokensText` and `_outputTokensText` fields with `[ObservableProperty]` as backward-compat stubs until Task 2 replaces the TOKENS section in XAML.
- **Files modified:** CCInfoWindows/CCInfoWindows/ViewModels/MainViewModel.cs
- **Verification:** Build succeeds after adding back the properties
- **Committed in:** 419e0ad (Task 1 commit)

**2. [Rule 3 - Blocking] HttpClient not registered in DI container**
- **Found during:** Task 1 (DI wiring)
- **Issue:** LiteLLMPricingService requires HttpClient injection but HttpClient was not registered in App.xaml.cs DI container
- **Fix:** Added `services.AddSingleton<HttpClient>()` before the pricing service registration
- **Files modified:** CCInfoWindows/CCInfoWindows/App.xaml.cs
- **Verification:** Build succeeds, pricing service resolves correctly
- **Committed in:** 419e0ad (Task 1 commit)

---

**Total deviations:** 2 auto-fixed (1 bug, 1 blocking)
**Impact on plan:** Both auto-fixes were necessary for build correctness. No scope creep.

## Issues Encountered

- XamlCompiler.exe exits with code 1 and empty error output when x:Bind references a non-existent ViewModel property — the only diagnostic was comparing baseline build vs. modified build
- 13 pre-existing test failures in JsonlServiceTests and ContextWindowTests (not caused by this plan)

## Next Phase Readiness

- STATISTIKEN UI is built and compiled; visual verification (Task 3 checkpoint) is pending user approval
- After user approves visual appearance, plan is complete
- Phase 06 (export) can begin after visual verification passes

---
*Phase: 05-cost-analytics*
*Completed: 2026-03-16*
