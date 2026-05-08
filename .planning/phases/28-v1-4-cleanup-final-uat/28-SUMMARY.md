---
phase: 28
plan: cleanup-final-uat
subsystem: cleanup
tags: [cleanup, convention, testing, uat]
dependency_graph:
  requires: [phase-24, phase-25, phase-26, phase-27]
  provides: [v1.5-milestone-complete, G3-convention, final-uat-checklist]
  affects: [MainViewModel, SettingsView, CLAUDE.md]
tech_stack:
  added: []
  patterns: [G-3-brushFactory-testability-seam]
key_files:
  created:
    - CCInfoWindows.Tests/ViewModels/MainViewModelInitialStateTests.cs
    - .planning/phases/28-v1-4-cleanup-final-uat/28-FINAL-UAT-CHECKLIST.md
  modified:
    - CCInfoWindows/CCInfoWindows/ViewModels/MainViewModel.cs
    - CCInfoWindows/CCInfoWindows/Views/SettingsView.xaml.cs
    - CCInfoWindows.Tests/ViewModels/MainViewModelAuthFlowTests.cs
    - CCInfoWindows.Tests/ViewModels/MainViewModelRefreshTests.cs
    - CLAUDE.md
  deleted:
    - CCInfoWindows/CCInfoWindows/Messages/LogoutRequestedMessage.cs
decisions:
  - G-3 default via brushFactory seam (not null!, not field initializer — WinRT COM constraint)
  - N-2 catch removed entirely (ResourceCoverageTests + Phase 27 L10N guarantee key)
  - N-3 AboutTabIndex re-declaration removed; SettingsViewModel.AboutTabIndex used directly
metrics:
  duration: ~45min
  completed: 2026-05-08
  tasks_completed: 5
  files_changed: 8
  files_deleted: 1
  tests_added: 2
  test_baseline: 342/344 (2 pre-existing ClaudeApiServiceTests)
---

# Phase 28: v1.4 Cleanup & Final UAT — Summary

**One-liner:** v1.4 cleanup wave: delete orphaned LogoutRequestedMessage, replace null! badge default via WinRT-safe brushFactory seam, bundle 3 view nits, document G-3 convention, generate Phase 25-27 Final UAT checklist (17 visual items).

---

## What Was Done

### CLEANUP-01 — Delete LogoutRequestedMessage + remove revert comment
- Deleted `Messages/LogoutRequestedMessage.cs` (orphan from the Plan 21-03 WeakReferenceMessenger revert)
- Removed the 3-line revert comment at `MainViewModel.cs:54` (historical context preserved in git log)
- Verified no remaining compile-time references; existing test `SettingsLogoutDirectCallTests` + SettingsViewModel comment retain only string mentions (not code)

### CLEANUP-02 — Replace `null!` default for `_contextModelBadgeColor` (G-3 fix)
- Added `_brushFactory` field (`readonly Func<string, SolidColorBrush>`) to `MainViewModel`
- Added optional `Func<string, SolidColorBrush>? brushFactory = null` parameter to constructor (default = `ParseHexBrush`)
- Initialized `_contextModelBadgeColor = _brushFactory("#9CA3AF")` in constructor body (gray-400 fallback)
- **WinRT constraint discovered:** `SolidColorBrush(Color)` requires COM activation — cannot be used in field initializer OR headless test constructors. `null!` in field initializer avoided; seam pattern enables headless testing without COM.
- Added `MainViewModelInitialStateTests` (2 tests): verify factory called with `#9CA3AF` and called exactly once at construction
- Updated `MainViewModelAuthFlowTests` and `MainViewModelRefreshTests` to pass `_ => null!` as headless brushFactory

### CLEANUP-03 — Bundle 3 Nits (N-1, N-2, N-3)
- **N-1:** Removed `if (ViewModel == null) return;` guard in `SettingsView.OnSegmentedSelectionChanged` — ViewModel is `GetRequiredService`-injected; null guard was misleading
- **N-2:** Removed bare `catch` in `MainViewModel.ComputeTooltipText` — Phase 23 + 27 `ResourceCoverageTests` guarantees `InactiveSessionTooltip` key exists in both locales
- **N-3:** Deleted `private const int AboutTabIndex = SettingsViewModel.AboutTabIndex` re-declaration in `SettingsView.xaml.cs`; both usages now reference `SettingsViewModel.AboutTabIndex` directly (value is 4 post-Phase-26)

### CLEANUP-04 — Document G-3 convention in CLAUDE.md
- Added G-3 paragraph to MVVM Conventions section after G-1
- Convention: prefer real initializers over `null!`; use `Func<..., T>? brushFactory = null` seam for WinRT types that require COM
- Cites CLEANUP-02 (`MainViewModel._contextModelBadgeColor`) as the canonical precedent

### Final UAT Checklist
- Generated `28-FINAL-UAT-CHECKLIST.md` with 27 items total:
  - 3 from Phase 25 (toast, dismiss persistence, visibility filter)
  - 7 from Phase 26 (pencil dialog, persist, reset, 5-tab, 360px fit, cross-tab live, orphan)
  - 10 from Phase 27 (NextWindow, hide-when-null, L10N, pricing error/clear/suppress, OrgPicker dialog, OrgPicker switch, OrgMismatch 5-poll, Don't-show-again)
  - Phase 24 and Phase 28 items auto-checked (automated tests cover them)

---

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] SolidColorBrush field initializer breaks headless tests**
- **Found during:** CLEANUP-02 implementation
- **Issue:** `_contextModelBadgeColor = ParseHexBrush("#9CA3AF")` as a field initializer calls `SolidColorBrush(Color)` at class construction time, which requires WinRT COM activation. This broke all existing `MainViewModel` unit tests (`AuthFlowTests`, `RefreshTests`) that previously worked because `null!` never triggered COM.
- **Fix:** Introduced `_brushFactory` seam (optional ctor parameter, default = `ParseHexBrush`). Tests pass `_ => null!` to avoid COM; production code uses real `ParseHexBrush`. Test file limited to verifying the seam call arguments (hex string + call count), not the brush value itself.
- **Files modified:** `MainViewModel.cs`, `MainViewModelAuthFlowTests.cs`, `MainViewModelRefreshTests.cs`, `MainViewModelInitialStateTests.cs` (new)
- **Commit:** 1b5a562

---

## Test Results

| Suite | Before (Ph27) | After (Ph28) | Delta |
|-------|--------------|--------------|-------|
| Total tests | 342 | 344 | +2 |
| Passed | 340 | 342 | +2 |
| Failed (pre-existing) | 2 | 2 | 0 |
| New failures | 0 | 0 | — |

Pre-existing failures: `ClaudeApiServiceTests.FetchUsageAsync_OnTransientNullResponse_RetriesAndSucceeds` + `FetchUsageAsync_OnPersistentNullResponse_ThrowsAfterRetries` — parameter naming mismatch, pre-date Phase 24, deferred to v1.6+.

Convention tests all pass:
- `MessengerThreadingConventionTests`: 2/2
- `ResourceCoverageTests`: 4/4
- `BannerStackPolicyTests`: 5/5

---

## Known Stubs

None. All Phase 28 changes are pure cleanup with no data-flow dependencies.

---

## Commits

| Hash | Type | Description |
|------|------|-------------|
| 7ba5afe | chore | CLEANUP-01: delete LogoutRequestedMessage + remove revert comment |
| 1b5a562 | fix | CLEANUP-02: replace null! default for _contextModelBadgeColor (G-3) |
| 050c7be | chore | CLEANUP-03: bundle v1.4 nits (N-1, N-2, N-3) |
| 2ba094e | docs | CLEANUP-04: document G-3 ObservableProperty default value convention |
| db78555 | docs | Final UAT checklist (17 deferred visual items, phases 25-27) |

---

## Self-Check: PASSED
