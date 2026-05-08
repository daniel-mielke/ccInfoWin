---
phase: 26-persistent-session-renaming
plan: "03"
subsystem: settings-sessions-tab
tags: [rename, settings, ui, mvvm, localization]
dependency_graph:
  requires: [26-02-mainview-rename-pencil]
  provides: [Settings Sessions tab with bulk session rename management]
  affects: [SettingsView, SettingsViewModel, App.xaml, AppTheme.xaml, ResourceCoverageTests]
tech_stack:
  added: [OrphanOpacityConverter, ZeroToVisibilityConverter, SessionRenameItem]
  patterns: [CD-03 snapshot refresh, G-1 NameChanged dispatch, G-2 store, D-08 orphan visibility]
key_files:
  created:
    - CCInfoWindows/CCInfoWindows/Models/SessionRenameItem.cs
    - CCInfoWindows/CCInfoWindows/Converters/OrphanOpacityConverter.cs
    - CCInfoWindows/CCInfoWindows/Converters/ZeroToVisibilityConverter.cs
  modified:
    - CCInfoWindows/CCInfoWindows/ViewModels/SettingsViewModel.cs
    - CCInfoWindows/CCInfoWindows/Views/SettingsView.xaml
    - CCInfoWindows/CCInfoWindows/Views/SettingsView.xaml.cs
    - CCInfoWindows/CCInfoWindows/App.xaml
    - CCInfoWindows/CCInfoWindows/Resources/AppTheme.xaml
    - CCInfoWindows/CCInfoWindows/Strings/de-DE/Resources.resw
    - CCInfoWindows/CCInfoWindows/Strings/en-US/Resources.resw
    - CCInfoWindows/CCInfoWindows/App.xaml.cs
    - CCInfoWindows.Tests/ViewModels/SettingsViewModelTests.cs
    - CCInfoWindows.Tests/ViewModels/SettingsViewModelTimerTests.cs
    - CCInfoWindows.Tests/ViewModels/SettingsLogoutMessageRoundtripTests.cs
    - CCInfoWindows.Tests/Localization/ResourceCoverageTests.cs
decisions:
  - "D-04 honored: LostFocus + Enter both commit via SaveSessionCustomNameCommand; empty value clears"
  - "D-08 honored: orphan entries visible in Sessions tab as greyed-out rows (Opacity=0.5, OrphanLabel subtitle)"
  - "CD-01 honored: direct 5th SegmentedControlItem (no TabControl wrapper); 30x30 badges unchanged"
  - "CD-03 honored: snapshot refresh on tab activation + on NameChanged (NOT live ObservableCollection sync)"
  - "EnumerateOrphanIds extracted to TryReadSessionNamesKeys helper (Rule 1: yield-in-try/catch C# compile error)"
  - "AboutTabIndex shifted 3→4; SessionsTabIndex=3 added; SettingsViewModelTimerTests updated (6 pass)"
  - "SettingsViewModel DI registration changed to factory pattern in App.xaml.cs (required for 3 new params)"
  - "ResourceCoverageTests extended with 5 Plan 03 keys; all 4 resw tests pass"
metrics:
  duration_minutes: 70
  completed_date: "2026-05-08"
  tasks_completed: 2
  tasks_total: 2
  files_created: 3
  files_modified: 12
  tests_added: 9
  tests_passing: 321
  tests_failing_pre_existing: 2
---

# Phase 26 Plan 03: Settings Sessions Tab Summary

Settings Sessions tab shipped — 5th SegmentedControl item (purple badge, index 3) between Account and About, listing all sessions with inline-editable custom name TextBoxes, orphan visibility, and snapshot refresh via ISessionNameStore.NameChanged.

## Tasks Completed

| Task | Commit | Description |
|------|--------|-------------|
| 1 — SessionRenameItem + SettingsViewModel backend | ae64b59 | Row model, SessionRenameItems collection, Save/Clear commands, Activate/Deactivate, AboutTabIndex 3→4 |
| 2 — SettingsView XAML + code-behind + resw + converters | c5eb8a7 | 5th SegmentedItem, Sessions panel, LostFocus/Enter handlers, 5 resw pairs, OrphanOpacityConverter, ZeroToVisibilityConverter |

## Test Results

| Suite | Before | After | Delta |
|-------|--------|-------|-------|
| SettingsViewModelTests | 9 | 16 | +7 new (tab index shift + rename surface) |
| SettingsViewModelTimerTests | 6 | 6 | 0 (AboutTabIndex shift propagates via constant) |
| SettingsLogoutMessageRoundtripTests | 2 | 2 | 0 (constructor updated, behavior unchanged) |
| ResourceCoverageTests | 4 | 4 | 0 (5 new keys validated in all 4 tests) |
| MessengerThreadingConventionTests | 2 | 2 | 0 |
| **Total** | **314** | **321** | **+7** |
| Pre-existing failures (ClaudeApiService) | 2 | 2 | 0 regression |

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] yield-in-try/catch is invalid C#**
- **Found during:** Task 1 — first build attempt
- **Issue:** Plan code had `yield return` inside a `try/catch` block in `EnumerateOrphanIds`. C# compiler error CS1626.
- **Fix:** Extracted `TryReadSessionNamesKeys()` as a separate method that returns `List<string>` (no yield); `EnumerateOrphanIds` iterates the list without try/catch.
- **Files modified:** `SettingsViewModel.cs`
- **Commit:** ae64b59

**2. [Rule 3 - Blocking] SettingsViewModelTimerTests + LogoutTests used old 5-param constructor**
- **Found during:** Task 1 test run
- **Issue:** Both test files called `new SettingsViewModel(5 args)` — compiler error after constructor extended to 8 params.
- **Fix:** Updated both test files to pass the 3 new mock services (ISessionNameStore, IJsonlService, IDispatcherQueue).
- **Files modified:** `SettingsViewModelTimerTests.cs`, `SettingsLogoutMessageRoundtripTests.cs`
- **Commit:** ae64b59

**3. [Rule 1 - Bug] CreateViewModel mock setup overwrote caller's IJsonlService.Sessions setup**
- **Found during:** Task 1 — `RefreshSessionRenameItems_PopulatesFromJsonlService` returned 0 items
- **Issue:** `CreateViewModel` called `jsonl.Setup(Sessions).Returns(Array.Empty<>())` unconditionally even when a pre-configured mock was passed in, erasing the test-specific setup.
- **Fix:** Guard `if (jsonlService == null)` before the default setup.
- **Files modified:** `SettingsViewModelTests.cs`
- **Commit:** ae64b59

## Visual Smoke Deferred

Per user directive "nie pausieren bei human_needed", the Task 3 checkpoint (manual smoke) is deferred. The following visual verifications were not performed at runtime:

| Check | Status | Notes |
|-------|--------|-------|
| 5-tab Segmented Control fits at 360px window width (CD-01) | DEFERRED | 30×30 badges used (default); fallback 28×28 not triggered based on prior phase measurements |
| Sessions tab lists sessions with inline TextBox (live) | DEFERRED | Verified structurally via XAML + unit tests |
| LostFocus / Enter commit persists custom name | DEFERRED | Covered by SaveSessionCustomName unit tests |
| Cross-tab live update (Settings rename → MainView dropdown) | DEFERRED | ISessionNameStore.NameChanged event path verified by Activate_SubscribesToNameChanged test |
| Orphan rows greyed-out with "Sitzung nicht gefunden" subtitle | DEFERRED | Verified structurally via Opacity=0.5 + OrphanOpacityConverter + OrphanLabel TextBlock |
| About tab DispatcherTimer still updates after index shift | DEFERRED | SettingsViewModelTimerTests 6/6 pass post-shift |
| App restart preserves orphan entries | DEFERRED | SessionNameStore persists via session-names.json; orphan detection via TryReadSessionNamesKeys |

## Phase 26 Delivery Status

Phase 26 (Persistent Session Renaming) is now fully delivered across 3 plans:

| Requirement | Plan | Status |
|-------------|------|--------|
| RENAME-01: Pencil button + ContentDialog | 26-02 | Done |
| RENAME-02: Settings Sessions tab (5th segment) | 26-03 | Done |
| RENAME-03: session-names.json persistence | 26-01 | Done |
| RENAME-04: Cross-tab live update (NameChanged → dispatch) | 26-02/03 | Done |
| RENAME-05: Control char stripping (SessionNameSanitizer) | 26-01/02/03 | Done |
| RENAME-06: Orphan entries kept across launches + visible | 26-01/03 | Done |
| RENAME-07: ISessionNameStore singleton DI | 26-01 | Done |
| RENAME-08: Display-layer resolution in MainViewModel.RefreshSessionList | 26-02 | Done |

## Known Stubs

None — SessionRenameItems is populated from live IJsonlService.Sessions + best-effort orphan detection.

## Threat Flags

No new network endpoints or auth paths introduced. All TextBox input sanitized via SessionNameSanitizer.Strip before persistence (T-26-11 mitigated). EnumerateOrphanIds failure returns empty enumeration (T-26-12 mitigated).

## Self-Check: PASSED

Files verified to exist:
- CCInfoWindows/CCInfoWindows/Models/SessionRenameItem.cs ✓
- CCInfoWindows/CCInfoWindows/Converters/OrphanOpacityConverter.cs ✓
- CCInfoWindows/CCInfoWindows/Converters/ZeroToVisibilityConverter.cs ✓

Commits verified:
- ae64b59 (feat 26-03 backend) ✓
- c5eb8a7 (feat 26-03 XAML+resw) ✓
