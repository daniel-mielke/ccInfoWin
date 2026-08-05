---
phase: 10-visual-styles
plan: "02"
subsystem: chart-rendering
tags: [colors, chart, axis-labels, visual-consistency]
dependency_graph:
  requires: []
  provides: [AxisLabelBrush-SecondaryTextBrush-parity]
  affects: [MainView-chart-axis-labels, ExportHelper-chart-axis-labels]
tech_stack:
  added: []
  patterns: [ChartColors-lookup-table]
key_files:
  created: []
  modified:
    - CCInfoWindows/CCInfoWindows/Helpers/ChartColors.cs
decisions:
  - AxisLabelBrush now matches SecondaryTextBrush for visual consistency between axis labels and timer text
metrics:
  duration: "85s"
  completed: "2026-03-19"
requirements:
  - STYLE-05
---

# Phase 10 Plan 02: AxisLabelBrush Color Correction Summary

**One-liner:** Updated AxisLabelBrush colors in ChartColors.cs from TertiaryTextBrush (#636366) to SecondaryTextBrush values (#8E8E93 dark, #6E6E73 light) for visual consistency with timer text.

## Tasks Completed

| Task | Name | Commit | Files |
|------|------|--------|-------|
| 1 | Update AxisLabelBrush colors to match SecondaryTextBrush | 6ac444e | ChartColors.cs |

## Changes Made

**ChartColors.cs** — Updated two `AxisLabelBrush` entries in the `ColorTable` dictionary:

- `("AxisLabelBrush", true)`: `Color.FromArgb(255, 0x63, 0x63, 0x66)` → `Color.FromArgb(255, 0x8E, 0x8E, 0x93)`
- `("AxisLabelBrush", false)`: `Color.FromArgb(255, 0x63, 0x63, 0x66)` → `Color.FromArgb(255, 0x6E, 0x6E, 0x73)`

The previous values used the TertiaryTextBrush color (#636366) for both themes. The corrected values match `SecondaryTextBrush` from `AppTheme.xaml`, ensuring axis labels (0%, 50%, 100%, 0h-5h) render in the same color as the timer countdown text.

Consumers `MainView.xaml.cs DrawAxesAndLabels` and `ExportHelper.cs` call `ChartColors.GetColor("AxisLabelBrush", isDark)` and pick up the corrected values automatically — no changes needed there.

## Deviations from Plan

None - plan executed exactly as written.

## Verification

- Build: `dotnet build` succeeded with 0 errors (61 pre-existing warnings, unrelated to this change)
- AxisLabelBrush dark: confirmed `0x8E, 0x8E, 0x93` in ChartColors.cs
- AxisLabelBrush light: confirmed `0x6E, 0x6E, 0x73` in ChartColors.cs

## Self-Check: PASSED

- [x] `CCInfoWindows/CCInfoWindows/Helpers/ChartColors.cs` modified and committed
- [x] Commit `6ac444e` exists
- [x] `dotnet build` reports 0 errors
