---
phase: 13-sonnet-context-window-setting
plan: "01"
subsystem: settings-ui
tags: [settings, sonnet, context-window, localization, mvvm]
dependency_graph:
  requires: []
  provides: [AppSettings.SonnetContextSize, SonnetContextChangedMessage, SettingsViewModel.SelectedSonnetContextIndex, SettingsView-ComboBox]
  affects: [SettingsViewModel, SettingsView, AppSettings, Resources.resw]
tech_stack:
  added: []
  patterns: [ObservableProperty-backing-field-init, ValueChangedMessage, WeakReferenceMessenger]
key_files:
  created:
    - CCInfoWindows/CCInfoWindows/Messages/SonnetContextChangedMessage.cs
  modified:
    - CCInfoWindows/CCInfoWindows/Models/AppSettings.cs
    - CCInfoWindows/CCInfoWindows/ViewModels/SettingsViewModel.cs
    - CCInfoWindows/CCInfoWindows/Views/SettingsView.xaml
    - CCInfoWindows/CCInfoWindows/Strings/de-DE/Resources.resw
    - CCInfoWindows/CCInfoWindows/Strings/en-US/Resources.resw
decisions:
  - "SonnetContextSize stored as int in AppSettings (200_000 / 1_000_000) but carried as long in SonnetContextChangedMessage to match ModelContextLimits.GetMaxContextTokens return type"
  - "Backing field _selectedSonnetContextIndex set directly in Initialize() to prevent partial method from firing during load — matches exact LanguageIndex pattern"
metrics:
  duration: "~10 minutes"
  completed: "2026-04-12"
  tasks: 3
  files_modified: 6
---

# Phase 13 Plan 01: Sonnet Context Window Setting — UI Summary

Adds 200K/1M Sonnet context size picker to the Settings view: AppSettings property, messenger message type, ViewModel observable property with save-on-change and messenger send, XAML ComboBox after Language row, and German/English localized labels.

## Tasks Completed

| Task | Name | Commit | Files |
|------|------|--------|-------|
| 1 | Add AppSettings.SonnetContextSize and SonnetContextChangedMessage | 9b171ad | AppSettings.cs, SonnetContextChangedMessage.cs |
| 2 | Add SelectedSonnetContextIndex to SettingsViewModel | bf90bc3 | SettingsViewModel.cs |
| 3 | Add Sonnet context ComboBox to SettingsView.xaml and localization | f7b37b5 | SettingsView.xaml, de-DE/Resources.resw, en-US/Resources.resw |

## Verification Results

1. `dotnet build` — 0 errors (60 pre-existing warnings, none from new code)
2. AppSettings.cs contains `SonnetContextSize { get; set; } = 200_000` — PASS
3. SonnetContextChangedMessage.cs contains `ValueChangedMessage<long>` — PASS
4. SettingsViewModel.cs contains `_selectedSonnetContextIndex` and `SonnetContextSizes` — PASS
5. SettingsView.xaml contains `SelectedSonnetContextIndex` and `SonnetContextComboBox` — PASS
6. Both Resources.resw files contain `SettingsSonnetContextLabel.Text` — PASS

## Decisions Made

- `SonnetContextSize` in AppSettings is `int` (values fit in int range), but `SonnetContextChangedMessage` uses `long` to match `ModelContextLimits.GetMaxContextTokens` signature — no cast required at call site in Plan 02.
- Backing field `_selectedSonnetContextIndex` is set directly in `Initialize()` to avoid triggering the `OnSelectedSonnetContextIndexChanged` partial method during app startup — identical to the `_selectedLanguageIndex` pattern already established.

## Deviations from Plan

None — plan executed exactly as written.

## Known Stubs

None — all properties are wired. Plan 02 will consume `SonnetContextChangedMessage` in JsonlService/MainViewModel to apply the value to live context calculations.

## Self-Check: PASSED

- `CCInfoWindows/CCInfoWindows/Messages/SonnetContextChangedMessage.cs` — FOUND
- `CCInfoWindows/CCInfoWindows/Models/AppSettings.cs` — FOUND (SonnetContextSize property)
- `CCInfoWindows/CCInfoWindows/ViewModels/SettingsViewModel.cs` — FOUND (SelectedSonnetContextIndex)
- `CCInfoWindows/CCInfoWindows/Views/SettingsView.xaml` — FOUND (SonnetContextComboBox)
- Commits 9b171ad, bf90bc3, f7b37b5 — FOUND
