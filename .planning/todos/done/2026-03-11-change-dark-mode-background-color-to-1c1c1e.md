---
created: 2026-03-11T10:34:00Z
title: Change dark mode background color to #1c1c1e
area: ui
files:
  - CCInfoWindows/CCInfoWindows/Resources/AppTheme.xaml
  - CCInfoWindows/CCInfoWindows/Views/MainView.xaml
  - CCInfoWindows/CCInfoWindows/Views/LoginView.xaml
  - CCInfoWindows/CCInfoWindows/Views/SettingsView.xaml
---

## Problem

The app's dark mode background color needs to be changed from the current color (#0F172A per styleguide) to **#1C1C1E** — the iOS/macOS system dark background color. This gives the app a more native Apple-style dark appearance matching the original macOS ccInfo app.

Background color is likely defined centrally in `AppTheme.xaml` and referenced across all views (MainView, LoginView, SettingsView).

## Solution

- Update the background color resource in `AppTheme.xaml` from current value to `#1C1C1E`
- Verify all views reference the theme resource (not hardcoded colors)
- Check Win2D chart canvas clear color if it uses a separate background definition
