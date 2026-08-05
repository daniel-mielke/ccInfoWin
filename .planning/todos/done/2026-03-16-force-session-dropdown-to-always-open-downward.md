---
created: 2026-03-16T10:22:00Z
title: Force session dropdown to always open downward
area: ui
files:
  - CCInfoWindows/CCInfoWindows/Views/MainView.xaml
---

## Problem

The session/project ComboBox sometimes opens upward depending on available screen space. This
inconsistent behavior is disorienting — the dropdown should always open downward for a predictable
UX, especially since the ComboBox sits near the top of the window.

## Solution

- WinUI 3 `ComboBox` does not have a native `DropDownDirection` property like WinForms
- Options to force downward opening:
  1. Set `MaxDropDownHeight` to a value that fits within the window below the ComboBox
  2. Override the `ComboBox` control template and modify the `Popup` placement to `Bottom`
  3. Use a custom styled `ComboBox` with `Popup.Placement = PlacementMode.Bottom` (if available in WinUI 3)
- The template override approach is most reliable but verbose — evaluate tradeoffs
