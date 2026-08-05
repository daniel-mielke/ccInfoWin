# Phase 17: Chart Horizontal Gradient - Context

**Gathered:** 2026-04-13
**Status:** Ready for planning

<domain>
## Phase Boundary

Replace flat zone-based color fills on the 5-hour area chart with a smooth horizontal linear gradient (green→yellow→orange→red). The gradient transitions based on data point utilization values. Both the area fill (25% opacity) and line stroke (100% opacity) use the gradient. Export PNG must match the live chart exactly.

</domain>

<decisions>
## Implementation Decisions

### Gradient Architecture
- `CanvasLinearGradientBrush` wrapped in `using` per draw cycle — not cached across frames (Win2D disposal requirement from STATE.md pitfall)
- Gradient spans only the actual data range — no gradient bleed into empty chart space
- New `BuildGradientStops()` method in `ChartRenderer` (pure math, unit-testable) — calculates gradient stop positions from data points
- Area fill at 25% opacity (Alpha=64), line stroke at 100% opacity — two separate brush instances per draw cycle

### Color & Theme
- Use existing `ChartColors.ColorTable` entries (Green→Yellow→Orange→Red) as gradient stops
- Pass `isDark` parameter through to gradient creation — same pattern as existing ChartDrawing methods
- Same draw code path for live rendering and PNG export — ExportHelper already calls ChartDrawing, gradient applies automatically
- Pre-multiplied alpha on gradient brush to prevent desaturation artifacts in both dark and light themes

### Gap Handling & Line Stroke
- Separate gradient per contiguous data span — each gap gets its own gradient brush, no gradient bleed across gaps
- Line stroke width: 2.0px live, 2.5px export — per Success Criteria #2
- Gradient applied to both line and fill, same color stops, different opacity
- Replace `GetZoneSegments()` with `GetContiguousSpans()` — zone-based segmentation is obsoleted by the continuous gradient

</decisions>

<code_context>
## Existing Code Insights

### Reusable Assets
- `ChartRenderer` (Helpers/) — pure coordinate math: `ToX`, `ToY`, `GetRightEdgeAbsoluteX`, `GetZoneSegments`
- `ChartDrawing` (Helpers/) — Win2D rendering: `DrawFilledArea`, `DrawChartTopLine`, `DrawGlowIndicator`
- `ChartColors` (Helpers/) — Color table with zone keys, `GetColor(brushKey, isDark)`
- `ExportHelper` — calls `ChartDrawing.DrawFilledArea` + `DrawChartTopLine` with same parameters

### Established Patterns
- `ChartRenderer` = pure math (testable), `ChartDrawing` = Win2D side effects (not unit-testable)
- Zone segments: `(StartIndex, EndIndex, BrushKey)` tuples — currently drives fill and line colors per zone
- `ICanvasResourceCreator` passed to all Win2D drawing methods
- Offset parameters (`offsetX`, `offsetY`) for export vs live positioning

### Integration Points
- `ChartDrawing.DrawFilledArea()` — replace zone-based fill with gradient fill
- `ChartDrawing.DrawChartTopLine()` — replace zone-based line stroke with gradient stroke
- `ExportHelper.ExportChartAsPngAsync()` / `CopyChartToClipboardAsync()` — inherits changes via ChartDrawing
- `ChartRenderer.GetZoneSegments()` — replace with `GetContiguousSpans()`

### Critical Pitfall
- `CanvasLinearGradientBrush` must be wrapped in `using` per draw cycle — not cached across frames (from STATE.md v1.3 pitfalls)

</code_context>

<specifics>
## Specific Ideas

- macOS reference spec `spec/v1.10.0-macOS/spec-release-1.8.3-to-1.10.0.md` FEAT-02a/b/c/d has detailed gradient stops, color values, rendering steps
- Gradient stops map data point utilization to color: 0-25% green, 25-50% yellow, 50-75% orange, 75-100% red
- `CanvasLinearGradientBrush` start point = leftmost data X, end point = rightmost data X (horizontal only)

</specifics>

<deferred>
## Deferred Ideas

None — discussion stayed within phase scope.

</deferred>
