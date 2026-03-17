---
phase: 06-export-polish-and-distribution
plan: 03
subsystem: ui
tags: [winui3, mvvm, export, update-service, autostart, localization, accessibility, registry]

requires:
  - phase: 06-01
    provides: ExportHelper, IUpdateService, RegistryHelper, AppSettings.DismissedUpdateVersion, AppSettings.Language
  - phase: 06-02
    provides: WinUI3Localizer setup, Strings/de-DE/Resources.resw, Strings/en-US/Resources.resw

provides:
  - Export button with MenuFlyout (save as PNG, copy to clipboard) in 5-STUNDEN-FENSTER header
  - Update InfoBar with dismiss behavior and Download action button in MainView
  - IUpdateService wired into MainViewModel with periodic check and dismiss persistence
  - ANWENDUNG section in SettingsView with autostart ToggleSwitch and language ComboBox
  - Accessibility labels (x:Uid + .resw AutomationProperties.Name) on all interactive elements

affects:
  - future phases using MainViewModel or SettingsViewModel
  - any phase touching MainView.xaml or SettingsView.xaml

tech-stack:
  added: []
  patterns:
    - "Update dismiss: DismissUpdate() saves DismissedUpdateVersion to settings, sets IsUpdateAvailable=false"
    - "DispatcherQueue stored in _dispatcherQueue field during InitializeAsync for use in update callbacks"
    - "App.MainWindow static property exposes Window for AppWindow access in ViewModel commands"

key-files:
  created: []
  modified:
    - CCInfoWindows/CCInfoWindows/Views/MainView.xaml
    - CCInfoWindows/CCInfoWindows/Views/MainView.xaml.cs
    - CCInfoWindows/CCInfoWindows/ViewModels/MainViewModel.cs
    - CCInfoWindows/CCInfoWindows/Views/SettingsView.xaml
    - CCInfoWindows/CCInfoWindows/ViewModels/SettingsViewModel.cs
    - CCInfoWindows/CCInfoWindows/App.xaml.cs
    - CCInfoWindows/CCInfoWindows/Strings/de-DE/Resources.resw
    - CCInfoWindows/CCInfoWindows/Strings/en-US/Resources.resw

key-decisions:
  - "DispatcherQueue stored in field during InitializeAsync — update callbacks from background thread need it for TryEnqueue"
  - "App.MainWindow as public static property — ViewModel can't inject Window directly, static is the clean WinUI 3 pattern"
  - "SessionComboBox uid rename from SessionPlaceholder — consolidates PlaceholderText and AutomationProperties.Name under one uid"
  - "IUpdateService injected via explicit factory lambda in DI — ensures correct HttpClient instance reuse"

patterns-established:
  - "Background callback → UI dispatch pattern: store _dispatcherQueue in field, use TryEnqueue in event handlers"
  - "Accessibility via x:Uid + .resw AutomationProperties.Name entries — consistent pattern across all interactive controls"

requirements-completed: [UPDT-02, UPDT-03, SETT-07, UIPF-07]

duration: 25min
completed: 2026-03-17
---

# Phase 06 Plan 03: UI Wire-up Summary

**Export button with MenuFlyout, update InfoBar with dismiss, autostart toggle, language switcher, and accessibility labels wired into MainView and SettingsView**

## Performance

- **Duration:** ~25 min
- **Started:** 2026-03-17T11:30:00Z
- **Completed:** 2026-03-17T12:00:00Z
- **Tasks:** 3 of 3 (Task 3 = human verification checkpoint — approved)
- **Files modified:** 8

## Accomplishments
- Export button in 5-STUNDEN-FENSTER header opens MenuFlyout with save-as-PNG and copy-to-clipboard commands
- Update InfoBar appears when IUpdateService fires UpdateAvailable, dismisses via InfoBarClosing handler that persists DismissedUpdateVersion to settings
- ANWENDUNG section in Settings with autostart ToggleSwitch (writes HKCU Run key) and language ComboBox (calls Localizer.Get().SetLanguage() immediately)
- All interactive controls have x:Uid entries with AutomationProperties.Name for screen reader accessibility

## Task Commits

Each task was committed atomically:

1. **Task 1: Wire export button, update InfoBar, and commands into MainView + MainViewModel** - `9e7b8d5` (feat)
2. **Task 2: Settings additions (autostart, language) + accessibility labels** - `c2e3aa0` (feat)

3. **Task 3: Verification checkpoint** - approved by user
4. **Fix: x:Uid → l:Uids.Uid migration** - `16649b7` (fix) — WinUI3Localizer runtime language switching requires attached property, not x:Uid

## Files Created/Modified
- `CCInfoWindows/CCInfoWindows/Views/MainView.xaml` - Export button with MenuFlyout in 5-hour section, Update InfoBar, SessionComboBox uid rename
- `CCInfoWindows/CCInfoWindows/Views/MainView.xaml.cs` - OnUpdateInfoBarClosing handler
- `CCInfoWindows/CCInfoWindows/ViewModels/MainViewModel.cs` - IUpdateService injection, ExportChartAsPngCommand, CopyChartToClipboardCommand, OpenUpdateDownloadCommand, DismissUpdate(), OnUpdateAvailable()
- `CCInfoWindows/CCInfoWindows/Views/SettingsView.xaml` - ANWENDUNG section, DarkModeToggle/RefreshIntervalComboBox/SessionTimeoutComboBox uid
- `CCInfoWindows/CCInfoWindows/ViewModels/SettingsViewModel.cs` - IsAutostart, SelectedLanguageIndex properties with RegistryHelper and Localizer integration
- `CCInfoWindows/CCInfoWindows/App.xaml.cs` - App.MainWindow static property, IUpdateService DI registration, explicit MainViewModel factory
- `CCInfoWindows/CCInfoWindows/Strings/de-DE/Resources.resw` - SessionComboBox, DarkModeToggle, RefreshIntervalComboBox, SessionTimeoutComboBox, UpdateInfoBar accessibility entries
- `CCInfoWindows/CCInfoWindows/Strings/en-US/Resources.resw` - same accessibility entries in English

## Decisions Made
- Stored `_dispatcherQueue` as field in MainViewModel to support UI dispatch in the `OnUpdateAvailable` background callback
- Used `App.MainWindow` static property (not injected) — follows WinUI 3 convention where Window isn't available in DI without reflection
- Renamed SessionComboBox uid from SessionPlaceholder to SessionComboBox to consolidate PlaceholderText and AutomationProperties.Name under one uid
- IUpdateService registered with explicit factory lambda to ensure same HttpClient singleton is reused

## Deviations from Plan

- **x:Uid → l:Uids.Uid migration**: WinUI3Localizer requires its own `l:Uids.Uid` attached property for runtime language updates. Standard `x:Uid` only resolves at XAML load time. Fixed in commit 16649b7.

## Issues Encountered
None

## User Setup Required
None - no external service configuration required.

## Next Phase Readiness
- All Phase 6 UI features are functionally complete, verified by user, and build clean
- Language switching confirmed working after l:Uids.Uid migration

---
*Phase: 06-export-polish-and-distribution*
*Completed: 2026-03-17*
