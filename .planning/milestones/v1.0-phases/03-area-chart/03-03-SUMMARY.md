---
phase: 03-area-chart
plan: "03"
subsystem: chart-rendering
tags: [win2d, chart, bug-fix, tdd, startup, history]
dependency_graph:
  requires: [03-02]
  provides: [visible-step-chart, right-edge-extension, stale-history-clearing]
  affects: [MainView.xaml.cs, ChartRenderer.cs, MainViewModel.cs]
tech_stack:
  added: []
  patterns: [GetRightEdgeAbsoluteX-helper, stale-window-check-on-startup]
key_files:
  created: []
  modified:
    - CCInfoWindows/CCInfoWindows/Helpers/ChartRenderer.cs
    - CCInfoWindows/CCInfoWindows/Views/MainView.xaml.cs
    - CCInfoWindows/CCInfoWindows/ViewModels/MainViewModel.cs
    - CCInfoWindows.Tests/Helpers/ChartRendererTests.cs
decisions:
  - GetRightEdgeAbsoluteX returns canvas-absolute X (includes LeftMargin) to match existing call-site pattern
  - DrawChartFills: start at baseline with BeginFigure(firstX, plotHeight) not (firstX, y) so path closes correctly
  - stale-check uses ResetsAt < UtcNow (expired window) rather than checking individual point timestamps
  - _fiveHourResetsAt set inside AppendHistoryPoint before ChartInvalidateCallback, duplicate removed from UpdateUsageProperties
metrics:
  duration: "~10 min"
  completed_date: "2026-03-11"
  tasks_completed: 2
  files_changed: 4
---

# Phase 03 Plan 03: Area Chart Bug Fixes Summary

Step-style area chart now renders visible fills and strokes for any segment size, extends the last data point to current time, and clears stale persisted history on startup.

## Tasks Completed

| Task | Name | Commit | Files |
|------|------|--------|-------|
| RED | Add failing tests for GetRightEdgeAbsoluteX | 7a4be23 | ChartRendererTests.cs |
| 1 | Fix DrawChartFills and DrawChartTopLine | 1d0272f | ChartRenderer.cs, MainView.xaml.cs |
| 2 | Clear stale history on startup | 6e93198 | MainViewModel.cs |

## What Was Built

**GetRightEdgeAbsoluteX helper (ChartRenderer.cs)**

New static method that returns the canvas-absolute right edge X for a zone segment:
- Mid-segment end: returns `LeftMargin + ToX(points[endIndex+1].Timestamp, ...)`
- Last segment with now within window: returns `LeftMargin + nowX`
- Last segment with now past window end: returns `LeftMargin + plotWidth` (clamped)

**DrawChartFills fix (MainView.xaml.cs)**

Previous path: `BeginFigure(firstX, plotHeight)` then drew a redundant vertical line down to plotHeight before the step. For single-point segments this produced a zero-width closed path (invisible). Fixed to:
1. `BeginFigure(firstX, plotHeight)` — baseline start
2. For first point: `AddLine(x, y)` — vertical rise only
3. For subsequent points: `AddLine(x, prevY)` then `AddLine(x, y)` — step transition
4. `AddLine(rightEdgeX, lastY)` — horizontal extension to right edge (uses `GetRightEdgeAbsoluteX`)
5. `AddLine(rightEdgeX, plotHeight)` — drop to baseline
6. `EndFigure(Closed)`

**DrawChartTopLine fix (MainView.xaml.cs)**

Added horizontal extension after the loop so the stroke line reaches the current time position. For single-point segments the loop body doesn't execute but the `AddLine(rightEdgeX, lastY)` after the loop produces the visible horizontal plateau.

**Startup stale history clearing (MainViewModel.cs)**

`InitializeAsync` now checks `history.ResetsAt < UtcNow` before displaying persisted history. Expired windows are cleared from disk and the chart starts empty — the first poll establishes a fresh window.

`_fiveHourResetsAt` is now assigned inside `AppendHistoryPoint` before `ChartInvalidateCallback` fires, ensuring `FiveHourWindowStart` is non-null when the Win2D draw handler runs. The duplicate assignment in `UpdateUsageProperties` was removed.

## Deviations from Plan

None - plan executed exactly as written.

## Test Results

- 3 new `GetRightEdgeAbsoluteX` tests added (mid-segment, last-in-window, last-clamped)
- All 57 tests pass (54 existing + 3 new)
- Build: 0 errors, 0 warnings

## Self-Check: PASSED

- ChartRenderer.cs: FOUND
- Commits 7a4be23, 1d0272f, 6e93198: FOUND
- GetRightEdgeAbsoluteX in ChartRenderer: FOUND
- ClearHistory in InitializeAsync: FOUND
