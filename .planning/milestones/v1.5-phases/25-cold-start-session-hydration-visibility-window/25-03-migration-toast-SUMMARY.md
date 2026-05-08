---
phase: 25-cold-start-session-hydration-visibility-window
plan: "03"
subsystem: ui/migration-toast
tags: [infobar, migration, localization, settings-persistence, dropdown-05]
dependency_graph:
  requires: [25-02-visibility-window-settings]
  provides: [DROPDOWN-05]
  affects: [MainViewModel, MainView, Resources.resw]
tech_stack:
  added: []
  patterns: [ObservableProperty, RelayCommand, InfoBar-TwoWay-binding, synchronous-SaveSettings]
key_files:
  created: []
  modified:
    - CCInfoWindows/CCInfoWindows/ViewModels/MainViewModel.cs
    - CCInfoWindows/CCInfoWindows/Views/MainView.xaml
    - CCInfoWindows/CCInfoWindows/Views/MainView.xaml.cs
    - CCInfoWindows/CCInfoWindows/Strings/de-DE/Resources.resw
    - CCInfoWindows/CCInfoWindows/Strings/en-US/Resources.resw
decisions:
  - "D-04 honored: WinUI InfoBar (NOT Windows Toast Notification); Severity=Informational; IsClosable=true; l:Uids.Uid pattern for localization"
  - "CD-02 honored: DismissMigrationToast RelayCommand calls SaveSettings synchronously before returning — crash-safe"
  - "CD-05 honored: migration check in MainViewModel.InitializeAsync after settings load, not App.xaml.cs"
  - "Closed event (not Closing) used — fires after InfoBar collapses; no double-save risk from TwoWay IsOpen binding"
  - "Task 3 (checkpoint:human-verify) deferred to Phase 28 Final UAT per user directive"
metrics:
  duration: "~10 min"
  completed: "2026-05-08"
  tasks_completed: 2
  files_changed: 5
---

# Phase 25 Plan 03: Migration Toast (DROPDOWN-05) Summary

One-time Informational InfoBar in MainView for existing installs upgrading to v1.5 — shown when `SessionVisibilityMigrationShown == false`, dismissed with synchronous `SaveSettings` (CD-02), localized DE+EN via `l:Uids.Uid` pattern.

## What Was Built

### MainViewModel Additions (MainViewModel.cs)

**New `[ObservableProperty]`** inserted after `_isSessionExpired` (auth-state block):

```csharp
// DROPDOWN-05 / D-04: one-time migration toast for existing installs.
// True only on first launch after upgrade -- persisted via SaveSettings on dismiss (CD-02).
[ObservableProperty]
private bool _isSessionVisibilityMigrationToastVisible;
```

**Migration check in `InitializeAsync`** (CD-05 site — after `settings.RefreshIntervalSeconds` load, before `WeakReferenceMessenger.Default.Register<RefreshIntervalChangedMessage>`):

```csharp
// DROPDOWN-05 / D-04 / CD-05: first-launch migration toast.
if (!settings.SessionVisibilityMigrationShown)
{
    IsSessionVisibilityMigrationToastVisible = true;
}
```

Note: `InitializeAsync` is called from `MainView.OnLoaded` on the UI thread, so no `TryEnqueue` wrapper is required for the property assignment.

**`[RelayCommand]` `DismissMigrationToast`** (placed after `ReLogin`, before `ExportChartAsPng`):

```csharp
[RelayCommand]
private void DismissMigrationToast()
{
    IsSessionVisibilityMigrationToastVisible = false;

    var settings = _settingsService.LoadSettings();
    settings.SessionVisibilityMigrationShown = true;
    _settingsService.SaveSettings(settings);   // synchronous — CD-02 crash-safe
}
```

Generated command property: `DismissMigrationToastCommand`.

### InfoBar in MainView.xaml

**Location:** After the API error InfoBar (lines 75-82), before the closing `</StackPanel>` of Row 0.

```xml
<!-- DROPDOWN-05 / D-04: Session visibility migration toast (one-time, dismissable) -->
<InfoBar
    x:Name="MigrationToastInfoBar"
    l:Uids.Uid="Toast.SessionVisibilityMigration"
    Severity="Informational"
    IsOpen="{x:Bind ViewModel.IsSessionVisibilityMigrationToastVisible, Mode=TwoWay}"
    Visibility="{x:Bind ViewModel.IsSessionVisibilityMigrationToastVisible, Mode=OneWay, Converter={StaticResource BoolToVisibilityConverter}}"
    IsClosable="True"
    Closed="OnMigrationToastClosed"
    Margin="0,0,0,12" />
```

**Localization pattern:** `l:Uids.Uid="Toast.SessionVisibilityMigration"` — the WinUI3Localizer framework resolves `Toast.SessionVisibilityMigration.Title` and `Toast.SessionVisibilityMigration.Message` automatically by dotted-suffix convention. This is the same pattern used by `SessionExpiredInfoBar`, `UpdateInfoBar`, etc.

**Binding explanation:**
- `IsOpen` uses `Mode=TwoWay` so the InfoBar collapse (X click) feeds back to the VM property.
- `Visibility` uses `Mode=OneWay` + `BoolToVisibilityConverter` to collapse the layout slot when hidden (prevents dead space).
- `Closed` fires AFTER the InfoBar collapses — the `OnMigrationToastClosed` handler then calls the VM command for persistence.

### Code-Behind Handler (MainView.xaml.cs)

```csharp
private void OnMigrationToastClosed(InfoBar sender, InfoBarClosedEventArgs args)
{
    if (ViewModel.DismissMigrationToastCommand.CanExecute(null))
    {
        ViewModel.DismissMigrationToastCommand.Execute(null);
    }
}
```

Added after `OnUpdateInfoBarClosing`. No business logic in code-behind — the handler is a thin relay to the VM command per CLAUDE.md MVVM conventions.

### Resw Key Pairs (2 keys × 2 locales)

| Key | de-DE | en-US |
|-----|-------|-------|
| `Toast.SessionVisibilityMigration.Title` | `Sichtbarkeitsfenster aktiviert` | `Visibility window enabled` |
| `Toast.SessionVisibilityMigration.Message` | `Sitzungen älter als 30 Tage werden jetzt ausgeblendet — anpassbar in Einstellungen.` | `Sessions older than 30 days are now hidden — adjustable in Settings.` |

Keys inserted before the `<!-- Burn Rate Warning -->` comment block in both files.

## Visual Smoke Deferred

**Task 3 (checkpoint:human-verify)** is deferred to Phase 28 Final UAT per user directive to never pause on `human-verify` checkpoints during autonomous plan execution. The following manual smoke steps must be performed in Phase 28:

1. **First-launch trigger:** Set `"sessionVisibilityMigrationShown": false` in `%LOCALAPPDATA%\CCInfoWindows\settings.json` (or delete file). Run the app. Confirm an Informational (blue) InfoBar appears at top of MainView with:
   - DE: Title "Sichtbarkeitsfenster aktiviert", Message "Sitzungen älter als 30 Tage werden jetzt ausgeblendet — anpassbar in Einstellungen."
   - EN: Title "Visibility window enabled", Message "Sessions older than 30 days are now hidden — adjustable in Settings."
2. **Close button visible:** Confirm the X button appears on the InfoBar.
3. **Dismiss persistence:** Click X. Immediately check `settings.json` — confirm `"sessionVisibilityMigrationShown": true` is written BEFORE app shutdown.
4. **No reappear:** Close app normally. Restart. Confirm InfoBar does NOT appear on second launch.
5. **Crash-resilient dismiss (CD-02):** Set flag back to false, run app, click X, kill via Task Manager (hard kill). Restart — confirm toast does NOT reappear (synchronous SaveSettings survived the crash).
6. **Locale toggle:** Repeat steps 1-4 with DE↔EN language toggle to verify both translations render.

## Deviations from Plan

None — plan executed exactly as written. The `l:Uids.Uid` pattern for the InfoBar was confirmed as matching existing precedent (`SessionExpiredInfoBar`, `UpdateInfoBar`) in the codebase.

## Test Results

| Suite | Before 25-03 | After 25-03 |
|-------|-------------|-------------|
| Total | 290 | 290 |
| Passing | 288 | 288 |
| Failing | 2 | 2 |
| New failures | — | 0 |

- `ResourceCoverageTests`: 4 passed (DE/EN parity for 2 new toast keys + existing 5 keys — all 7 pairs verified)
- `MessengerThreadingConventionTests`: 2 passed (G-1 compliance unchanged)
- `JsonlServiceColdStartTests`: 4 passed (Plan 25-01 regression check clean)
- Pre-existing failures: 2 × `ClaudeApiServiceTests` (parameter-naming mismatches, out of scope)
- `BurnRateCalculatorTests.Predict_FlatUsage_ReturnsNull`: flaky timing-dependent test (passes on re-run; not caused by Plan 25-03 changes)

## Phase 25 Milestone Completion

All 6 DROPDOWN requirements (DROPDOWN-01 through DROPDOWN-06) are now closed across Plans 25-01, 25-02, and 25-03:

| REQ-ID | Plan | Deliverable |
|--------|------|-------------|
| DROPDOWN-01 | 25-02 | Session ComboBox visible in MainView (pre-existing, confirmed) |
| DROPDOWN-02 | 25-01 | Per-entry Cwd hydration with DecodeProjectDirectory fallback |
| DROPDOWN-03 | 25-01 | Softened empty-Cwd filter in RebuildSessionsList |
| DROPDOWN-04 | 25-02 | SessionVisibilityWindowDays ComboBox in Settings + display-layer filter |
| DROPDOWN-05 | 25-03 | One-time migration toast InfoBar — this plan |
| DROPDOWN-06 | 25-01 | stream.Position race fix in JsonlService |

Phase 25 is complete and ready for phase verification.

## Threat Flags

None — no new network endpoints, auth paths, file access patterns, or schema changes at trust boundaries introduced.

## Self-Check

- `CCInfoWindows/CCInfoWindows/ViewModels/MainViewModel.cs` contains `_isSessionVisibilityMigrationToastVisible` — FOUND
- `CCInfoWindows/CCInfoWindows/ViewModels/MainViewModel.cs` contains `if (!settings.SessionVisibilityMigrationShown)` — FOUND
- `CCInfoWindows/CCInfoWindows/ViewModels/MainViewModel.cs` contains `DismissMigrationToast` — FOUND
- `CCInfoWindows/CCInfoWindows/ViewModels/MainViewModel.cs` contains `SessionVisibilityMigrationShown = true` — FOUND
- `CCInfoWindows/CCInfoWindows/Views/MainView.xaml` contains `MigrationToastInfoBar` — FOUND
- `CCInfoWindows/CCInfoWindows/Views/MainView.xaml` contains `Toast.SessionVisibilityMigration` — FOUND
- `CCInfoWindows/CCInfoWindows/Views/MainView.xaml` contains `IsSessionVisibilityMigrationToastVisible` (×2) — FOUND
- `CCInfoWindows/CCInfoWindows/Views/MainView.xaml` contains `Closed="OnMigrationToastClosed"` — FOUND
- `CCInfoWindows/CCInfoWindows/Views/MainView.xaml.cs` contains `OnMigrationToastClosed` — FOUND
- `CCInfoWindows/CCInfoWindows/Views/MainView.xaml.cs` contains `DismissMigrationToastCommand` — FOUND
- de-DE Resources.resw contains `Toast.SessionVisibilityMigration.Title` — FOUND
- de-DE Resources.resw contains `Toast.SessionVisibilityMigration.Message` — FOUND
- en-US Resources.resw contains `Toast.SessionVisibilityMigration.Title` — FOUND
- en-US Resources.resw contains `Toast.SessionVisibilityMigration.Message` — FOUND
- Commit `ff924c2` (Task 1: resw + MainViewModel) — FOUND
- Commit `4cb64af` (Task 2: MainView.xaml + MainView.xaml.cs) — FOUND
- Build: 0 errors — PASS
- ResourceCoverageTests: 4 passed — PASS
- MessengerThreadingConventionTests: 2 passed — PASS
- JsonlServiceColdStartTests: 4 passed — PASS
- Full suite: 2 pre-existing failures only, no new failures — PASS

## Self-Check: PASSED
