---
phase: 17-chart-horizontal-gradient
plan: "02"
subsystem: chart-rendering
tags: [chart, gradient, win2d, canvas-brush, export]
dependency_graph:
  requires:
    - ChartColors.BuildColorLookup (Phase 17 Plan 01)
    - ChartRenderer.GetContiguousSpans (Phase 17 Plan 01)
    - ChartRenderer.BuildGradientStops (Phase 17 Plan 01)
  provides:
    - ChartDrawing.DrawChartFills (gradient-based)
    - ChartDrawing.DrawChartTopLine (gradient-based, lineWidth param)
    - ExportHelper: 2.5px export line width
  affects:
    - CCInfoWindows/CCInfoWindows/Helpers/ChartDrawing.cs
    - CCInfoWindows/CCInfoWindows/Helpers/ExportHelper.cs
tech_stack:
  added: []
  patterns:
    - CanvasLinearGradientBrush per draw cycle (not cached) with CanvasAlphaMode.Premultiplied
    - Span-absolute X coordinates for brush StartPoint/EndPoint (includes offsetX + LeftMargin)
    - ConvertToFillStops (Alpha=64) / ConvertToLineStops (Alpha=255) private helpers
    - Optional lineWidth parameter with 2.0f default for live/export distinction
key_files:
  created: []
  modified:
    - CCInfoWindows/CCInfoWindows/Helpers/ChartDrawing.cs
    - CCInfoWindows/CCInfoWindows/Helpers/ExportHelper.cs
decisions:
  - CanvasAlphaMode.Premultiplied on CanvasLinearGradientBrush to prevent desaturation artifacts (CHRT-05)
  - FillAlpha=64 as named constant (no magic number) for 25% opacity fill
  - Private ConvertToFillStops/ConvertToLineStops helpers to separate alpha concerns from path building
  - lineWidth optional parameter with 2.0f default — all existing callers unchanged, export passes 2.5f
metrics:
  duration: "2 minutes"
  completed_date: "2026-04-13"
  tasks_completed: 1
  files_modified: 2
---

# Phase 17 Plan 02: Chart Gradient Rendering Summary

**One-liner:** Win2D CanvasLinearGradientBrush gradient fills replacing flat zone fills — 25% opacity area fill (Alpha=64) + 100% opacity line stroke with 2.5px export thickness, using Premultiplied alpha mode to prevent desaturation.

## Tasks Completed

| Task | Name | Commit | Files |
|------|------|--------|-------|
| 1 | Replace zone iteration with gradient brushes in DrawChartFills and DrawChartTopLine | f1fcbe7 | ChartDrawing.cs, ExportHelper.cs |

## What Was Built

### ChartDrawing.DrawChartFills (rewritten)

Zone-segment iteration (`GetZoneSegments`) replaced entirely with span-based gradient rendering:
1. `ChartColors.BuildColorLookup(isDark)` → `Color[101]`
2. `ChartRenderer.GetContiguousSpans(points)` → one span `(0, count-1)`
3. `ChartRenderer.BuildGradientStops(...)` → `(Position, Color)[]` tuples
4. `ConvertToFillStops()` → `CanvasGradientStop[]` with `Alpha=64` (25% opacity)
5. `CanvasLinearGradientBrush` in `using` statement with `CanvasAlphaMode.Premultiplied`
6. `StartPoint`/`EndPoint` at canvas-absolute X (includes `offsetX + LeftMargin + spanStartX`)
7. Staircase path geometry unchanged; `session.FillGeometry(geometry, fillBrush)` uses brush overload

### ChartDrawing.DrawChartTopLine (rewritten)

Same approach as fills, but:
- `ConvertToLineStops()` → `CanvasGradientStop[]` with `Alpha=255` (full opacity)
- New optional parameter `float lineWidth = 2.0f` (all existing callers unaffected)
- `session.DrawGeometry(geometry, lineBrush, lineWidth)` uses the parameter

### ExportHelper.DrawChartArea

Single line change: `DrawChartTopLine` call now passes `lineWidth: 2.5f` named argument for export.

## Acceptance Criteria Verification

| Criterion | Status |
|-----------|--------|
| `GetZoneSegments` absent from ChartDrawing.cs | PASS — grep returns no matches |
| `GetContiguousSpans` + `BuildGradientStops` called | PASS — both called in fills and line |
| `CanvasLinearGradientBrush` with `Premultiplied` | PASS — both brushes use it |
| Fill stops use `Alpha=64` | PASS — `FillAlpha = 64` constant |
| `float lineWidth = 2.0f` optional parameter | PASS — line 143 |
| Line stops use `Alpha=255` | PASS — `ConvertToLineStops` preserves original Alpha=255 |
| ExportHelper passes `lineWidth: 2.5f` | PASS — line 246 |
| All brushes in `using var` | PASS — `using var fillBrush` and `using var lineBrush` |
| `System.Numerics` using present | PASS — line 1 |
| Build succeeds: 0 errors | PASS — 62 pre-existing warnings only |

## Deviations from Plan

### Auto-fixed Issues

None — plan executed exactly as written.

### Minor Implementation Notes

- Introduced `FillAlpha = 64` named constant (CLAUDE.md: no magic numbers)
- Extracted `ConvertToFillStops` and `ConvertToLineStops` private helpers (CLAUDE.md: small functions SRP)
- The `spanEndAbsoluteX` variable is reused as `rightEdgeX` — `GetRightEdgeAbsoluteX` already includes LeftMargin, so `offsetX + GetRightEdgeAbsoluteX(...)` is the correct canvas-absolute value

## Known Stubs

None — gradient fully wired from infrastructure (Plan 01) to rendering (Plan 02).

## Checkpoint: Visual Verification Pending

Task 2 is a `checkpoint:human-verify`. The user must:
1. Run the app: `dotnet run --project CCInfoWindows/CCInfoWindows/CCInfoWindows.csproj`
2. Verify gradient fill (green→yellow→orange→red) with 25% opacity
3. Verify line stroke at 100% opacity matching gradient colors
4. Verify no color bleed into empty chart area
5. Export PNG and verify 2.5px thicker line + matching gradient

## Self-Check: PASSED
- `D:/myProjects/ccInfoWin/CCInfoWindows/CCInfoWindows/Helpers/ChartDrawing.cs` — FOUND (modified)
- `D:/myProjects/ccInfoWin/CCInfoWindows/CCInfoWindows/Helpers/ExportHelper.cs` — FOUND (modified)
- Commit `f1fcbe7` — FOUND
