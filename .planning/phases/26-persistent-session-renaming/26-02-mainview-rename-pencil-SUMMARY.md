---
phase: 26-persistent-session-renaming
plan: "02"
subsystem: MainViewModel + MainView + L10N
tags: [rename, ux, contentdialog, l10n, mvvm, session-display]
dependency_graph:
  requires: [26-01-session-name-store]
  provides: [RENAME-01, RENAME-04, RENAME-05, RENAME-08]
  affects: [MainViewModel, MainView, ResourceCoverageTests]
tech_stack:
  added: []
  patterns: [RelayCommand CanExecute binding, ContentDialog view-side launcher, .NET event subscription G-1 TryEnqueue, display-layer overlay]
key_files:
  created: []
  modified:
    - CCInfoWindows/CCInfoWindows/ViewModels/MainViewModel.cs
    - CCInfoWindows/CCInfoWindows/Views/MainView.xaml
    - CCInfoWindows/CCInfoWindows/Views/MainView.xaml.cs
    - CCInfoWindows/CCInfoWindows/App.xaml.cs
    - CCInfoWindows/CCInfoWindows/Strings/de-DE/Resources.resw
    - CCInfoWindows/CCInfoWindows/Strings/en-US/Resources.resw
    - CCInfoWindows.Tests/ViewModels/MainViewModelRefreshTests.cs
    - CCInfoWindows.Tests/ViewModels/MainViewModelAuthFlowTests.cs
    - CCInfoWindows.Tests/Localization/ResourceCoverageTests.cs
decisions:
  - "ContentDialog launched from view code-behind (XamlRoot requirement); all persistence delegates to ViewModel methods"
  - "OnSessionNameChanged uses named method (not lambda) for symmetric -= cleanup in StopTimers (CD-05)"
  - "New overlay tests use LastActivity -2h to avoid WinUI COM exception in headless test runner (SolidColorBrush requires dispatcher)"
  - "OpenRenameDialogCommand intentionally empty body — exists solely for CanExecute/IsEnabled binding"
metrics:
  duration: "~45 min"
  completed: "2026-05-08"
  tasks_completed: 2
  tasks_total: 3
  files_modified: 9
---

# Phase 26 Plan 02: MainView Rename Pencil Summary

ISessionNameStore wired into MainViewModel (12-arg constructor) with display-layer overlay in RefreshSessionList, NameChanged .NET event subscription with G-1 TryEnqueue marshaling, and pencil button + ContentDialog rename UX in MainView.

## Tasks Completed

| Task | Name | Commit | Status |
|------|------|--------|--------|
| 1 | MainViewModel 12-arg ctor + ISessionNameStore field + display-layer + NameChanged subscribe/unsubscribe | b2ef002 | Done |
| 2 | OpenRenameDialogCommand + ContentDialog + pencil button + 5 resw key pairs | 779c359 | Done |
| 3 | Manual smoke: pencil + ContentDialog rename flow | — | Deferred (see below) |

## What Was Implemented

### Task 1: MainViewModel ISessionNameStore Wiring

- `private readonly ISessionNameStore _sessionNameStore` field added after `_burnRateNotificationService`
- Constructor extended to 12 args: `ISessionNameStore sessionNameStore` as 12th parameter (after `IDispatcherQueue dispatcherQueue`)
- `InitializeAsync`: `_sessionNameStore.NameChanged += OnSessionNameChanged` after the SonnetContextChanged WeakReferenceMessenger block
- `OnSessionNameChanged` is a named private method (not lambda) to enable symmetric `-=` per CD-05:
  ```csharp
  private void OnSessionNameChanged(object? sender, SessionNameChangedEventArgs args)
  {
      _dispatcherQueue.TryEnqueue(RefreshSessionList);
  }
  ```
- `StopTimers`: `_sessionNameStore.NameChanged -= OnSessionNameChanged` before `WeakReferenceMessenger.Default.UnregisterAll(this)`
- `RefreshSessionList` display-layer resolution: `DisplayName = _sessionNameStore.GetCustomName(s.Id) ?? s.DisplayName` (RENAME-08)
- `App.xaml.cs`: factory adds `sp.GetRequiredService<ISessionNameStore>()` as 12th argument
- `MainViewModelRefreshTests.cs` + `MainViewModelAuthFlowTests.cs`: both `new MainViewModel(...)` calls updated with `Mock<ISessionNameStore>().Object` as 12th arg

### Task 2: Rename UX

**MainViewModel additions:**
- `[NotifyPropertyChangedFor(nameof(HasSelectedSession))]` on `_selectedSession` field
- `public bool HasSelectedSession => SelectedSession != null` — gates pencil button IsEnabled
- `[RelayCommand(CanExecute = nameof(HasSelectedSession))] private void OpenRenameDialog()` — intentionally empty body (CanExecute gating is the purpose)
- `public async Task SaveCustomNameAsync(string sessionId, string newName)` — sanitizes via `SessionNameSanitizer.Strip`, calls `SetCustomName` or `ClearCustomName`, then `SaveAsync`
- `public async Task ClearCustomNameAsync(string sessionId)` — calls `ClearCustomName` + `SaveAsync`
- `public bool HasCustomName(string sessionId)` — view helper for Reset button visibility

**MainView.xaml:**
- Replaced bare `<ComboBox>` with 2-column `<Grid ColumnSpacing="6">` containing ComboBox (col 0, `Width="*"`) and pencil button (col 1, `Width="32"`)
- Pencil button: `x:Name="RenameSessionButton"`, `l:Uids.Uid="MainViewRenameButton"`, `Click="OnRenamePencilClicked"`, `IsEnabled="{x:Bind ViewModel.HasSelectedSession, Mode=OneWay}"`, Segoe MDL2 glyph `&#xE70F;`

**MainView.xaml.cs:**
- `private async void OnRenamePencilClicked` handler: creates `TextBox` (MaxLength=100) + `ContentDialog` with localized strings from `Localizer.Get().GetLocalizedString(...)`, `CloseButtonText` conditional on `ViewModel.HasCustomName(sessionId)`, delegates to `ViewModel.SaveCustomNameAsync` or `ClearCustomNameAsync`

**Resources.resw (DE + EN):**
| Key | DE | EN |
|-----|----|----|
| `Dialog.RenameSession.Title` | Sitzung umbenennen | Rename Session |
| `Dialog.RenameSession.SaveButton` | Speichern | Save |
| `Dialog.RenameSession.CancelButton` | Abbrechen | Cancel |
| `Dialog.RenameSession.ResetButton` | Zurücksetzen | Reset |
| `MainViewRenameButton.[using:Microsoft.UI.Xaml.Controls]ToolTipService.ToolTip` | Sitzung umbenennen | Rename session |

## Test Updates

| Test File | Change |
|-----------|--------|
| `MainViewModelRefreshTests.cs` | Added `Mock<ISessionNameStore>` as 12th arg; added `CreateSutWithNameStore` + `InvokeRefreshSessionList` helpers; added 2 new overlay tests |
| `MainViewModelAuthFlowTests.cs` | Added `Mock<ISessionNameStore>` as 12th arg in both `CreateViewModel` and `CreateViewModelWithSuccessfulApi` |
| `ResourceCoverageTests.cs` | Extended `RequiredKeys`, `ExpectedEnUs`, `ExpectedDeDe` with 5 Phase 26 RENAME-01 keys |

### Test Results

- Total: 316 tests (2 new overlay tests added in Task 1)
- Passed: 314
- Failed: 2 (pre-existing `ClaudeApiServiceTests` failures, unchanged from Wave 1 baseline)
- `MessengerThreadingConventionTests`: 2/2 passed
- `ResourceCoverageTests`: 4/4 passed (including 5 new Phase 26 keys)

## Deviations from Plan

### Auto-fixed Issues

**[Rule 1 - Bug] New overlay tests triggered WinUI COM exception in headless runner**
- **Found during:** Task 1 TDD test authoring
- **Issue:** `RefreshSessionList` with an "active" session auto-selects it and calls `UpdateSessionData` → `ParseHexBrush` → `SolidColorBrush(Color)` → COM exception (no WinUI XAML dispatcher in test runner)
- **Fix:** Set `LastActivity = DateTimeOffset.UtcNow.AddHours(-2)` so the session is within 30-day visibility window but outside 30-min activity threshold — appears in `SortedSessions` but is not auto-selected, avoiding `UpdateSessionData`
- **Files modified:** `CCInfoWindows.Tests/ViewModels/MainViewModelRefreshTests.cs`
- **Commit:** b2ef002 (fix applied inline before the task commit)

## Visual Smoke Deferred

Per user directive "nie pausieren bei human_needed", Task 3 (manual smoke) was not executed. The following scenarios remain to be verified manually before the Phase 26 milestone is closed:

| Step | Scenario | Expected |
|------|----------|----------|
| 1 | App launched, session dropdown populated | Pencil glyph visible right of ComboBox |
| 2 | No session selected | Pencil button is disabled (greyed) |
| 3 | Session selected | Pencil becomes enabled |
| 4 | Click pencil | Dialog "Sitzung umbenennen" opens, TextBox pre-filled with current display name, Reset button HIDDEN |
| 5 | Type new name + Save | ComboBox updates without restart (RENAME-04) |
| 6 | Verify session-names.json | `%LOCALAPPDATA%\CCInfoWindows\session-names.json` exists with correct mapping |
| 7 | Open dialog again | Reset button NOW visible (custom name exists) |
| 8 | Click Reset | ComboBox reverts to auto-derived name; session-names.json entry removed |
| 9 | Type control chars + Save | Saved name has control chars stripped (RENAME-05) |
| 10 | Close + relaunch | Custom names persist across restart (RENAME-03) |

## Open Issues for Plan 03

None expected. Plan 03 (Sessions Settings tab) is the secondary "manage all" rename surface and builds on the same `ISessionNameStore` singleton.

## Self-Check: PASSED

- [x] `CCInfoWindows/CCInfoWindows/ViewModels/MainViewModel.cs` — FOUND
- [x] `CCInfoWindows/CCInfoWindows/Views/MainView.xaml` — FOUND
- [x] `CCInfoWindows/CCInfoWindows/Views/MainView.xaml.cs` — FOUND
- [x] `CCInfoWindows/CCInfoWindows/Strings/de-DE/Resources.resw` — FOUND
- [x] `CCInfoWindows/CCInfoWindows/Strings/en-US/Resources.resw` — FOUND
- [x] Commit b2ef002 — FOUND (feat(26-02): wire ISessionNameStore into MainViewModel)
- [x] Commit 779c359 — FOUND (feat(26-02): add OpenRenameDialog + pencil button + ContentDialog)
- [x] Build: 0 errors
- [x] Tests: 314 passed / 2 pre-existing failures / 0 new regressions
