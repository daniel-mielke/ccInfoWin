---
phase: 18-settings-redesign
plan: "01"
subsystem: SettingsViewModel, AppTheme, Localization
tags: [viewmodel, settings, localization, theme, tdd]
dependency_graph:
  requires: []
  provides: [SettingsViewModel.SelectedTabIndex, SettingsViewModel.IsXxxTabVisible, SettingsViewModel.AppVersionText, SettingsViewModel.IsTokenValid, SettingsBadgeBrushes, SettingsLocalizationKeys]
  affects: [SettingsView.xaml (Plan 02 will consume all new bindings and resources)]
tech_stack:
  added: []
  patterns: [TDD red-green, ObservableProperty with partial method notification, computed visibility bool pattern]
key_files:
  created:
    - CCInfoWindows.Tests/ViewModels/SettingsViewModelTests.cs
  modified:
    - CCInfoWindows/CCInfoWindows/ViewModels/SettingsViewModel.cs
    - CCInfoWindows/CCInfoWindows/Resources/AppTheme.xaml
    - CCInfoWindows/CCInfoWindows/Strings/de-DE/Resources.resw
    - CCInfoWindows/CCInfoWindows/Strings/en-US/Resources.resw
decisions:
  - "RefreshOptions labels use universal short notation (30s/1min/etc) — not localized; Manuell is established label per spec FEAT-03d"
  - "IsXxxTabVisible computed from _selectedTabIndex field directly, not SelectedTabIndex property, to avoid source-generator conflict"
metrics:
  duration: "~8 minutes"
  completed: "2026-04-13"
  tasks: 2
  files: 5
---

# Phase 18 Plan 01: Settings ViewModel Data Layer Summary

SettingsViewModel extended with tab switching (4 visibility bools), short RefreshOption labels, AppVersionText from assembly reflection, and IsTokenValid from credential service; AppTheme.xaml gains 4 badge brushes in both themes; both .resw files gain 18 localization keys.

## Tasks Completed

| Task | Description | Commit | Files |
|------|-------------|--------|-------|
| 1 | SettingsViewModel tab switching, short labels, version, token (TDD) | c4e498d | SettingsViewModel.cs, SettingsViewModelTests.cs |
| 2 | AppTheme badge brushes + localization keys (de-DE + en-US) | 4b44dea | AppTheme.xaml, de-DE/Resources.resw, en-US/Resources.resw |

## Decisions Made

1. **Short RefreshOption labels are language-neutral** — "30s", "1min", "2min", "5min", "10min", "Manuell" are universal per spec FEAT-03d. "Manuell" is the established label already used in the current version.

2. **Computed bool properties read `_selectedTabIndex` field** — Using the backing field directly in computed properties avoids issues with [ObservableProperty] source generators. `OnSelectedTabIndexChanged` raises explicit `OnPropertyChanged` notifications for all 4 visibility bools.

## Deviations from Plan

None — plan executed exactly as written.

## Known Stubs

None — all new ViewModel properties are fully wired. AppVersionText reads real assembly version. IsTokenValid delegates to real ICredentialService. Badge brushes and localization keys are complete for Plan 02 consumption.

## Self-Check: PASSED

- CCInfoWindows/CCInfoWindows/ViewModels/SettingsViewModel.cs: FOUND
- CCInfoWindows.Tests/ViewModels/SettingsViewModelTests.cs: FOUND
- CCInfoWindows/CCInfoWindows/Resources/AppTheme.xaml: FOUND
- CCInfoWindows/CCInfoWindows/Strings/de-DE/Resources.resw: FOUND
- CCInfoWindows/CCInfoWindows/Strings/en-US/Resources.resw: FOUND
- Commit c4e498d: FOUND
- Commit 4b44dea: FOUND
- All 9 SettingsViewModelTests pass: CONFIRMED
