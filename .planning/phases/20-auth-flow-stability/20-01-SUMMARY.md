---
phase: 20-auth-flow-stability
plan: "01"
subsystem: viewmodels/localization/tests
tags: [auth-flow, unit-tests, localization, wave-0, nyquist-gate]
dependency_graph:
  requires: []
  provides: [MainViewModelAuthFlowTests, LoginReloadButton-resw-keys]
  affects: [CCInfoWindows.Tests/ViewModels/MainViewModelAuthFlowTests.cs, Strings/en-US/Resources.resw, Strings/de-DE/Resources.resw]
tech_stack:
  added: []
  patterns: [full-DI mock factory (mirrors SettingsViewModelTests), resw key insertion after footer-button block]
key_files:
  created:
    - CCInfoWindows.Tests/ViewModels/MainViewModelAuthFlowTests.cs
  modified:
    - CCInfoWindows/CCInfoWindows/Strings/en-US/Resources.resw
    - CCInfoWindows/CCInfoWindows/Strings/de-DE/Resources.resw
    - CCInfoWindows/CCInfoWindows/ViewModels/MainViewModel.cs
decisions:
  - "Test factory uses real MainViewModel via 10-mock DI constructor (not MainViewModelTestHarness) to exercise Receive(AuthStateChangedMessage) directly"
  - "Tests are RED in Plan 01 (Nyquist gate); turn GREEN when Plan 02 ships _autoReauthAttempted routing"
  - "Phase 20 self-contains LoginReloadButton.* keys (2 EN + 2 DE) per RESEARCH Open Question #1; Phase 23 owns unrelated keys"
metrics:
  duration: "~15 minutes"
  completed: "2026-05-06"
  tasks_completed: 2
  files_changed: 4
---

# Phase 20 Plan 01: Wave 0 Foundation (Test Scaffold + Resw Keys) Summary

Wave 0 foundation for Phase 20 auth-flow stability: full-DI test scaffold for `MainViewModel.Receive(AuthStateChangedMessage)` (AUTH-01..AUTH-04) + `LoginReloadButton.*` localization keys absorbed by Phase 20 per spec FEAT-16.

## Tasks Completed

| Task | Name | Commit | Files |
|------|------|--------|-------|
| 1 | Create MainViewModelAuthFlowTests.cs scaffold | 21a73bb | CCInfoWindows.Tests/ViewModels/MainViewModelAuthFlowTests.cs |
| 2 | Add LoginReloadButton.* keys to en-US and de-DE resw | 728b983 | Strings/en-US/Resources.resw, Strings/de-DE/Resources.resw |

## Test Results (Wave 0 Expected RED State)

`dotnet test --filter "FullyQualifiedName~MainViewModelAuthFlow"` enumerates exactly **4 tests**, all RED:

| Test | Failure Type | Expected to turn GREEN in |
|------|-------------|---------------------------|
| `Receive_FirstFalse_NavigatesToLoginView_WithoutSettingSessionExpired` | Moq: NavigateTo<LoginView> never called | Plan 02 |
| `Receive_SecondFalse_OpensInfoBar_WithoutSecondNavigation` | Moq: NavigateTo<LoginView> never called | Plan 02 |
| `Receive_True_ClearsFlagsAndResetsAutoReauth` | Assert.False: IsSessionExpired is True | Plan 02 |
| `Logout_ResetsAutoReauthFlag_NextFalseNavigatesAgain` | Moq: NavigateTo<LoginView> not called 3x | Plan 02 |

This RED state is the Nyquist gate that validates Plan 02's `_autoReauthAttempted` implementation is complete.

## Resw Keys Added

| Key | en-US Value | de-DE Value |
|-----|-------------|-------------|
| `LoginReloadButton.[using:Microsoft.UI.Xaml.Controls]ToolTipService.ToolTip` | Reload page | Seite neu laden |
| `LoginReloadButton.[using:Microsoft.UI.Xaml.Automation]AutomationProperties.Name` | Reload login page | Login-Seite neu laden |

Inserted after `FooterQuitButton.*` block, before `<!-- MainView Session Expired InfoBar -->` comment in both files.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] SolidColorBrush field initializer caused COMException in unit tests**
- **Found during:** Task 1 (running tests to verify RED state)
- **Issue:** `MainViewModel._contextModelBadgeColor = new(Microsoft.UI.Colors.Gray)` calls `SolidColorBrush(Color)` constructor which requires COM/WinRT context (not available in xUnit test runner). All 4 tests failed with `COMException` instead of expected Assertion/Moq failures — they could never turn GREEN even after Plan 02 ships routing logic.
- **Fix:** Changed field initializer to `null!`. `SolidColorBrush` is always set via `ClearSessionData()` → `ParseHexBrush()` before any XAML binding reads it in production. In tests, the property is never accessed.
- **Files modified:** `CCInfoWindows/CCInfoWindows/ViewModels/MainViewModel.cs`
- **Commit:** 4993a23

## Known Stubs

None — the test scaffold contains no stubs. The RED tests exercise real production code paths.

## Threat Flags

None — new surfaces are test-only (in-memory, no I/O) and static resw strings (no user input, no dynamic content).

## Self-Check: PASSED

- File exists: `CCInfoWindows.Tests/ViewModels/MainViewModelAuthFlowTests.cs` — FOUND (git ls-files confirmed)
- Commit 21a73bb exists — FOUND
- Commit 728b983 exists — FOUND
- Commit 4993a23 exists — FOUND
- [Fact] count == 4: FOUND (grep -c confirmed 4)
- AuthStateChangedMessage references >= 7: FOUND (8 occurrences)
- No MainViewModelTestHarness references: CONFIRMED (0 matches)
- en-US tooltip key: FOUND (1 match)
- de-DE automation name key: FOUND (1 match)
- Test project build: EXIT 0 (0 errors, 67 warnings — all pre-existing)
- Main project build: EXIT 0 (0 errors)
- 4 tests enumerated RED: CONFIRMED (Fehler: 4, gesamt: 4)
