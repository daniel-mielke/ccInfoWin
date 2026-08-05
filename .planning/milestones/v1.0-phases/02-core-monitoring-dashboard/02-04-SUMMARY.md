---
phase: 02-core-monitoring-dashboard
plan: 04
subsystem: ui
tags: [winui3, mvvm, settings, theme, refresh-interval]

requires:
  - phase: 02-core-monitoring-dashboard/02-01
    provides: ISettingsService, AppSettings, INavigationService, ICredentialService, ThemeChangedMessage, RefreshIntervalChangedMessage
provides:
  - SettingsView with refresh interval ComboBox and dark/light mode ToggleSwitch
  - SettingsViewModel with persisted settings and messenger integration
  - MainWindow ThemeChangedMessage handler for immediate theme application
affects: [03-area-chart]

tech-stack:
  added: []
  patterns:
    - WeakReferenceMessenger for cross-component theme and interval notifications
    - FrameworkElement.RequestedTheme for runtime theme switching (not Application.RequestedTheme)
    - ComboBox with record-based RefreshOption items

key-files:
  created:
    - CCInfoWindows/CCInfoWindows/ViewModels/SettingsViewModel.cs
    - CCInfoWindows/CCInfoWindows/Views/SettingsView.xaml
    - CCInfoWindows/CCInfoWindows/Views/SettingsView.xaml.cs
  modified:
    - CCInfoWindows/CCInfoWindows/MainWindow.xaml.cs
    - CCInfoWindows/CCInfoWindows/App.xaml.cs

self-check:
  build: PASSED
  tests: PASSED
  must-haves:
    - truth: "User can navigate to Settings page via Einstellungen footer icon"
      status: VERIFIED
    - truth: "User can select refresh interval from ComboBox (30s, 1min, 2min, 5min, 10min, Manual)"
      status: VERIFIED
    - truth: "User can toggle dark/light mode via ToggleSwitch with immediate visual effect"
      status: VERIFIED
    - truth: "Theme choice persists across restarts (default: dark)"
      status: VERIFIED
    - truth: "Refresh interval persists across restarts (default: 60s auto)"
      status: VERIFIED
    - truth: "Refresh interval change takes effect immediately on the running poll timer"
      status: VERIFIED
    - truth: "User can log out from Settings page"
      status: VERIFIED
    - truth: "User can navigate back to dashboard from Settings"
      status: VERIFIED
---

## Summary

Settings page with refresh interval configuration (6 options from 30s to Manual), dark/light mode toggle with immediate visual effect, and logout functionality. Theme changes apply instantly via `ThemeChangedMessage` handled by `MainWindow`, which sets `FrameworkElement.RequestedTheme`. Refresh interval changes update the running poll timer immediately via `RefreshIntervalChangedMessage`. All settings persist to `settings.json`.

## Deviations

None — implemented as planned. Human verification confirmed all features working.
