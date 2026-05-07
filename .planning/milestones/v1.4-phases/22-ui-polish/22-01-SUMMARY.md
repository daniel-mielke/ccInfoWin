---
phase: 22-ui-polish
plan: 01
subsystem: ui
tags: [winui3, mvvm, communitytooltkit, anti-flicker, relay-command, task-whenall]

# Dependency graph
requires:
  - phase: 20-auth-flow-stability
    provides: IsRefreshing ObservableProperty reserved for Phase 22 (Phase 20 CONTEXT.md D-04)
  - phase: 21-history-persistence-hardening
    provides: IUsageHistoryService singleton invariant unchanged
provides:
  - PollUsageCoreAsync private method (API-fetch body, zero IsRefreshing assignments)
  - PollUsageAsync thin wrapper (auto-poll path, no 250ms floor)
  - Refresh RelayCommand with Task.WhenAll 250ms anti-flicker floor (manual path only)
  - CanRefresh predicate with [RelayCommand(CanExecute)] + [NotifyCanExecuteChangedFor]
  - MinimumSpinnerDisplayMs = 250 named constant
  - 4 xUnit tests covering POLISH-02 (floor + negative), POLISH-03 (CanExecute), D-03 (IsRefreshing isolation)
affects:
  - 22-02 (tooltip plan — same MainViewModel file, no overlap in modified methods)
  - 22-03 (about-tab timer plan — different file, no overlap)

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Task.WhenAll(work, Task.Delay(floor)) anti-flicker minimum-display pattern — first use in codebase"
    - "[RelayCommand(CanExecute = nameof(...))] + [NotifyCanExecuteChangedFor] auto-disable pattern — first use in codebase"

key-files:
  created:
    - CCInfoWindows.Tests/ViewModels/MainViewModelRefreshTests.cs
  modified:
    - CCInfoWindows/CCInfoWindows/ViewModels/MainViewModel.cs

key-decisions:
  - "D-01: Existing v1.1 SpinnerStoryboard + RefreshIcon FontIcon preserved unchanged — zero XAML delta"
  - "D-02: 250ms floor applied ONLY to manual Refresh() RelayCommand, NOT to auto-poll PollUsageAsync()"
  - "D-03: PollUsageCoreAsync owns zero IsRefreshing assignments; both wrappers own IsRefreshing lifetime"
  - "D-04 Option A: [RelayCommand(CanExecute = nameof(CanRefresh))] + [NotifyCanExecuteChangedFor] chosen over InvertedBooleanConverter (fewer files, zero XAML change)"
  - "Rule 1 bug fix: _contextModelBadgeColor field initializer changed from new(Colors.Gray) to null! to enable MainViewModel instantiation in COM-free test environment"

patterns-established:
  - "Anti-flicker floor: await Task.WhenAll(work, Task.Delay(MinimumMs)) — race-free, single-line"
  - "[NotifyCanExecuteChangedFor(nameof(XxxCommand))] on [ObservableProperty] field — canonical CommunityToolkit.Mvvm 8.4 CanExecute pattern"
  - "Extract PollXxxCoreAsync() from PollXxxAsync() to isolate API-fetch body from IsRefreshing lifecycle management"

requirements-completed: [POLISH-01, POLISH-02, POLISH-03]

# Metrics
duration: 35min
completed: 2026-05-06
---

# Phase 22 Plan 01: Refresh Spinner Hardening Summary

**Anti-flicker refresh-spinner contract: 250ms WhenAll floor on manual Refresh, CanExecute auto-disable via NotifyCanExecuteChangedFor, PollUsageCoreAsync extraction — all backed by 4 green xUnit tests**

## Performance

- **Duration:** 35 min
- **Started:** 2026-05-06T~09:00Z
- **Completed:** 2026-05-06
- **Tasks:** 2 (both atomic TDD tasks)
- **Files modified:** 2 (MainViewModel.cs, new test file)

## Accomplishments

- Extracted `PollUsageCoreAsync()` from `PollUsageAsync()` body — core method owns zero `IsRefreshing` assignments (D-03 contract)
- `PollUsageAsync()` becomes a thin guard+wrapper; auto-poll timer path has no 250ms floor
- `Refresh()` RelayCommand now uses `Task.WhenAll(PollUsageCoreAsync(), Task.Delay(250ms))` anti-flicker floor — first use of this pattern in codebase
- `[RelayCommand(CanExecute = nameof(CanRefresh))]` + `[NotifyCanExecuteChangedFor(nameof(RefreshCommand))]` on `_isRefreshing` — first use of this Toolkit attribute in codebase; button auto-disables without XAML changes (D-04 Option A)
- `MinimumSpinnerDisplayMs = 250` named constant (CLAUDE.md Clean Code: no magic numbers)
- 4 xUnit tests: timing floor, no-floor negative, CanExecute mid-flight, D-03 IsRefreshing isolation — all GREEN
- Zero changes to `MainView.xaml` (D-01: v1.1 SpinnerStoryboard preserved)

## Task Commits

1. **Task 1: Refactor MainViewModel** - `a605fe7` (refactor)
2. **Task 2: Add MainViewModelRefreshTests** - `7334ad9` (test + bug fix)

## Files Created/Modified

- `CCInfoWindows/CCInfoWindows/ViewModels/MainViewModel.cs` — PollUsageCoreAsync extracted, PollUsageAsync thinned, Refresh extended with WhenAll floor, NotifyCanExecuteChangedFor added on _isRefreshing, MinimumSpinnerDisplayMs constant, _contextModelBadgeColor initializer fix
- `CCInfoWindows.Tests/ViewModels/MainViewModelRefreshTests.cs` — 4 xUnit tests covering POLISH-02 (floor + negative), POLISH-03 (CanExecute), D-03 (IsRefreshing isolation)

## Decisions Made

- D-04 Option A selected (over Option B InvertedBooleanConverter): zero new files, zero XAML changes, idiomatic CommunityToolkit.Mvvm 8.4 — Button.IsEnabled auto-driven by Command.CanExecute
- Test strategy: `Stopwatch` with tolerance window (`>= 250 && < 750ms`) per F.I.R.S.T. — no `ITimeProvider` abstraction needed
- TCS-based mock for in-flight CanExecute test (holds API open via `TaskCompletionSource<UsageResponse?>`)
- Deferred: `WithMinimumDuration(Task, TimeSpan)` static helper extraction — no second consumer yet

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Fixed SolidColorBrush field initializer blocking test instantiation**
- **Found during:** Task 2 (MainViewModelRefreshTests — all 4 tests failing with COMException)
- **Issue:** `_contextModelBadgeColor = new(Microsoft.UI.Colors.Gray)` field initializer calls WinRT COM API (`SolidColorBrush` constructor). COM not available in xUnit test runner without WinUI app bootstrap.
- **Fix:** Changed initializer to `null!` — field is always set before UI renders (via `ClearSessionData()` or `UpdateSessionData()` which both call `ParseHexBrush`). No null-dereference risk in production; test instantiation unblocked.
- **Files modified:** `CCInfoWindows/CCInfoWindows/ViewModels/MainViewModel.cs` (Zeile 205)
- **Verification:** 4/4 tests GREEN after fix; `dotnet build` still 0 errors
- **Committed in:** `7334ad9` (Task 2 commit)

---

**Total deviations:** 1 auto-fixed (Rule 1 bug — WinUI COM initializer in field, blocked testability)
**Impact on plan:** Fix necessary for test suite green. No scope creep; production behavior unchanged since `ClearSessionData`/`UpdateSessionData` always set the field before render.

## Issues Encountered

- Research Example 1 in 22-RESEARCH.md referenced `_autoReauthAttempted` and `UpdateUsagePropertiesAsync` — these do not exist in the current codebase (they were from an earlier version). Actual `PollUsageAsync` used `UpdateUsageProperties` (synchronous) — extracted correctly.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- Plan 22-02 (Inactive Session Tooltip) can proceed — touches `SessionDisplayItem`, `RefreshSessionList`, and `MainView.xaml` ComboBox DataTemplate. No overlap with Plan 22-01 changes.
- Plan 22-03 (About-Tab Timer) can proceed — touches `SettingsViewModel` and `SettingsView.xaml.cs`. No overlap.
- Deferred: Extract `WithMinimumDuration(Task, TimeSpan)` helper when a second consumer emerges.

## Self-Check

- `MainViewModel.cs` modified: FOUND
- `MainViewModelRefreshTests.cs` created: FOUND
- Commit `a605fe7` exists: FOUND
- Commit `7334ad9` exists: FOUND
- `MainView.xaml` untouched: CONFIRMED (git diff shows 0 hunks)
- 4 tests GREEN: CONFIRMED

## Self-Check: PASSED

---
*Phase: 22-ui-polish*
*Completed: 2026-05-06*
