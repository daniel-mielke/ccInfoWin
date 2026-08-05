---
created: 2026-03-11T10:30:00Z
title: Match font from original macOS ccInfo app
area: ui
files:
  - spec/v1.7.1/ccinfo-styleguide.md:67-71
  - CCInfoWindows/CCInfoWindows/Views/MainView.xaml
  - CCInfoWindows/CCInfoWindows/Views/MainView.xaml.cs
---

## Problem

The current font in the Windows app doesn't match the look of the original macOS ccInfo app. The user wants to replicate the original font feel.

Per the styleguide (spec/v1.7.1/ccinfo-styleguide.md):
- **macOS original**: SF Pro (Apple system font)
- **Windows target**: Segoe UI Variable (Windows 11 system font)

The styleguide already specifies Segoe UI Variable as the Windows equivalent, but the current implementation may not be using it consistently, or the font weights/sizes may not match the original's visual appearance.

## Solution

1. Research the exact font family, weights, and sizes used in the original macOS ccInfo v1.7.1 (check Tauri source at `D:\myProjects\ccInfoWindows\`)
2. Compare with current XAML font settings
3. Apply matching font family (Segoe UI Variable or consider bundling a cross-platform font like Inter if closer match desired)
4. Match font weights and sizes to the original's visual proportions
