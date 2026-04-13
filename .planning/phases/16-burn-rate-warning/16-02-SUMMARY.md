---
phase: 16-burn-rate-warning
plan: "02"
subsystem: burn-rate-warning-ui
tags: [services, viewmodel, xaml, notifications, di]
dependency_graph:
  requires:
    - BurnRateCalculator.Predict (Plan 16-01)
    - BurnRateFormatter.FormatTimeLabel (Plan 16-01)
    - BurnRatePrediction model (Plan 16-01)
    - BurnRateWarningBrush (Plan 16-01)
    - burn-rate localization keys (Plan 16-01)
  provides:
    - IBurnRateNotificationService (service contract)
    - BurnRateNotificationService (one-shot toast implementation)
    - IsBurnRateWarningVisible + BurnRateWarningText (ViewModel observable properties)
    - Burn rate warning banner (MainView XAML)
    - Full burn rate warning feature end-to-end
  affects:
    - MainViewModel (poll cycle now triggers prediction + banner + toast)
    - App.xaml.cs (AppNotificationManager setup + DI registration)
tech_stack:
  added:
    - Microsoft.Windows.AppNotifications (toast notifications, already in Windows App SDK)
    - Microsoft.Windows.AppNotifications.Builder (AppNotificationBuilder)
  patterns:
    - One-shot notification flag with cycle reset on prediction clear
    - DRY delegation to BurnRateFormatter.FormatTimeLabel (no duplicate formatting)
    - AppNotificationManager.NotificationInvoked registered before Register()
key_files:
  created:
    - CCInfoWindows/CCInfoWindows/Services/Interfaces/IBurnRateNotificationService.cs
    - CCInfoWindows/CCInfoWindows/Services/BurnRateNotificationService.cs
  modified:
    - CCInfoWindows/CCInfoWindows/ViewModels/MainViewModel.cs
    - CCInfoWindows/CCInfoWindows/Views/MainView.xaml
    - CCInfoWindows/CCInfoWindows/App.xaml.cs
decisions:
  - SendToast is static — BurnRateNotificationService holds only the _notifiedBurnRate cycle flag; no instance state needed for toast sending
  - AppNotificationManager.IsSupported() guard in both BurnRateNotificationService.SendToast and App.xaml.cs OnLaunched for defense-in-depth
  - FormatBurnRateText is static in MainViewModel — it is a pure string transformation with no side effects
metrics:
  duration: ~10 minutes
  completed: 2026-04-13
  tasks_completed: 2
  files_created: 2
  files_modified: 3
---

# Phase 16 Plan 02: Burn Rate Warning UI + Toast Summary

Complete burn rate warning feature: red banner with flame icon in 5-hour section, Windows toast notification firing once per warning cycle, all text via Localizer, DRY formatting delegation to BurnRateFormatter.

## Tasks Completed

| Task | Name | Commit | Files |
|------|------|--------|-------|
| 1 | Notification service + ViewModel integration + DI wiring | 1d70826 | IBurnRateNotificationService.cs, BurnRateNotificationService.cs, MainViewModel.cs, App.xaml.cs |
| 2 | Burn rate warning banner in MainView.xaml | 8c56cda | MainView.xaml |

## What Was Built

**IBurnRateNotificationService** (service contract):
- `void CheckBurnRate(BurnRatePrediction? prediction)` — single entry point from ViewModel

**BurnRateNotificationService** (implementation):
- `_notifiedBurnRate` flag: set true on first trigger, reset to false when prediction clears
- `SendToast(int minutesUntilLimit)`: guarded by `AppNotificationManager.IsSupported()`, delegates time formatting entirely to `BurnRateFormatter.FormatTimeLabel`, uses localized title+body via `Localizer.Get()`
- `notification.Tag = "usage-burnrate"` for deduplication in Action Center

**MainViewModel additions**:
- `[ObservableProperty] private bool _isBurnRateWarningVisible`
- `[ObservableProperty] private string _burnRateWarningText`
- `BurnRateCalculator.Predict(UsageHistoryPoints, data.FiveHour.Utilization, data.FiveHour.ResetsAt)` called in `UpdateUsageProperties` — uses Utilization (0-100), NOT NormalizedUtilization (0-1)
- `FormatBurnRateText(int)` delegates to `BurnRateFormatter.FormatTimeLabel` (DRY)
- Both the `if(FiveHour != null)` and `else` branches update banner visibility and call `CheckBurnRate`

**MainView.xaml burn rate banner**:
- `Border` with `BurnRateWarningBrush` background, `CornerRadius="6"`, `Padding="8,4"`, `Margin="0,8,0,0"`
- `FontIcon` with glyph `&#xECAD;` (flame) via `SymbolThemeFontFamily`, white foreground, 12pt
- `TextBlock` bound to `ViewModel.BurnRateWarningText`, white, 12pt
- `Visibility` via `BoolToVisibilityConverter` bound to `ViewModel.IsBurnRateWarningVisible`
- `AutomationProperties.Name` bound to `ViewModel.BurnRateWarningText` for accessibility
- Positioned inside 5-STUNDEN-FENSTER StackPanel, after percentage/countdown Grid

**App.xaml.cs changes**:
- `AppNotificationManager.NotificationInvoked += OnNotificationInvoked` registered BEFORE `Register()` (correct ordering per Windows App SDK requirement)
- `AppNotificationManager.IsSupported()` guard wraps the entire setup block
- `OnNotificationInvoked` handler is a no-op (toast click brings app to foreground automatically)
- `IBurnRateNotificationService` registered as singleton before ViewModels
- `MainViewModel` transient registration updated with `IBurnRateNotificationService` as last parameter

## Test Results

- BurnRateCalculatorTests: 10/10 passed (unchanged from Plan 01)
- BurnRateFormatterTests: 5/5 passed (unchanged from Plan 01)
- Total: 15/15 green

## Deviations from Plan

None — plan executed exactly as written.

## Known Stubs

None — all bindings are fully wired. Banner text comes from `BurnRateFormatter.FormatTimeLabel` + `Localizer.Get()`. Notification fires via `AppNotificationManager`. The feature is complete end-to-end pending human visual verification (Task 3 checkpoint).

## Self-Check: PASSED
