# Phase 17: Chart Horizontal Gradient - Research

**Researched:** 2026-04-13
**Domain:** Win2D CanvasLinearGradientBrush, C# gradient rendering, area chart fill/stroke
**Confidence:** HIGH

---

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions

**Gradient Architecture**
- `CanvasLinearGradientBrush` wrapped in `using` per draw cycle — not cached across frames (Win2D disposal requirement from STATE.md pitfall)
- Gradient spans only the actual data range — no gradient bleed into empty chart space
- New `BuildGradientStops()` method in `ChartRenderer` (pure math, unit-testable) — calculates gradient stop positions from data points
- Area fill at 25% opacity (Alpha=64), line stroke at 100% opacity — two separate brush instances per draw cycle

**Color & Theme**
- Use existing `ChartColors.ColorTable` entries (Green→Yellow→Orange→Red) as gradient stops
- Pass `isDark` parameter through to gradient creation — same pattern as existing ChartDrawing methods
- Same draw code path for live rendering and PNG export — ExportHelper already calls ChartDrawing, gradient applies automatically
- Pre-multiplied alpha on gradient brush to prevent desaturation artifacts in both dark and light themes

**Gap Handling & Line Stroke**
- Separate gradient per contiguous data span — each gap gets its own gradient brush, no gradient bleed across gaps
- Line stroke width: 2.0px live, 2.5px export — per Success Criteria #2
- Gradient applied to both line and fill, same color stops, different opacity
- Replace `GetZoneSegments()` with `GetContiguousSpans()` — zone-based segmentation is obsoleted by the continuous gradient

### Claude's Discretion

None — discussion stayed within phase scope.

### Deferred Ideas (OUT OF SCOPE)

None — discussion stayed within phase scope.
</user_constraints>

---

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|------------------|
| CHRT-01 | User sees smooth horizontal color gradient (green→yellow→orange→red) instead of flat zone fills | BuildColorLookup + BuildGradientStops + CanvasLinearGradientBrush in DrawChartFills/DrawChartTopLine |
| CHRT-02 | Area fill 25% opacity, line stroke 100% opacity (2.0px live, 2.5px export) | Two separate brush instances per draw; line width passed as parameter or constant |
| CHRT-03 | Gradient spans only data range, correct gap handling | GetContiguousSpans returns index ranges; one brush per span |
| CHRT-04 | Exported PNG matches live chart gradient | ExportHelper already calls ChartDrawing.DrawChartFills + DrawChartTopLine — change flows automatically |
| CHRT-05 | No desaturation in dark or light themes | CanvasAlphaMode.Premultiplied on brush constructor; alpha is baked into stop Color.A, not brush Opacity |
</phase_requirements>

---

## Summary

Phase 17 replaces the zone-based flat color fills on the 5-hour area chart with a smooth horizontal linear gradient. The current code in `ChartDrawing.cs` iterates `GetZoneSegments()` tuples and fills/strokes each zone with a solid color. The new approach replaces this with a single continuous path per gap-free data span, filled and stroked with a `CanvasLinearGradientBrush` whose stops map data point X-positions to interpolated colors.

The Win2D API is well-understood and stable. `CanvasLinearGradientBrush` takes a `CanvasGradientStop[]` array where each stop has a `Position` (0.0–1.0 normalized within the brush's own span) and a `Color`. The brush's `StartPoint` and `EndPoint` are set to the leftmost and rightmost X of the data span in canvas coordinates. Two brushes are created per span: fill at Alpha=64, line at Alpha=255. Both must be disposed via `using` at the end of each draw cycle — this is the critical Win2D pitfall already logged in STATE.md.

The export path is free: `ExportHelper.DrawChartArea` calls `ChartDrawing.DrawChartFills` and `ChartDrawing.DrawChartTopLine` with `isDark: true` and offset parameters. No changes to ExportHelper are required if the gradient logic is embedded in ChartDrawing. The only export-specific difference is the line width (2.5px vs 2.0px), which must be passed through as a parameter.

**Primary recommendation:** Implement in three focused units — (1) `ChartColors.BuildColorLookup()` for color interpolation (pure math, testable), (2) `ChartRenderer.BuildGradientStops()` for stop position calculation (pure math, testable), and (3) modify `ChartDrawing.DrawChartFills` / `DrawChartTopLine` to replace zone iteration with span iteration using gradient brushes.

---

## Standard Stack

### Core
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| Microsoft.Graphics.Canvas (Win2D) | 1.3.2 (already in project) | `CanvasLinearGradientBrush`, `CanvasGradientStop`, `FillGeometry`, `DrawGeometry` | Only Win2D-compatible gradient brush for WinUI 3 canvas drawing |
| Microsoft.Graphics.Canvas.Brushes | — (part of Win2D) | `CanvasLinearGradientBrush`, `CanvasAlphaMode` | Same package |
| Windows.UI | — (platform) | `Color.FromArgb` for gradient stops | Same Color type used throughout ChartColors.cs |

### No New Dependencies
All required APIs are already in the project. Win2D 1.3.2 is already referenced. No NuGet additions needed.

**Version verification:** Win2D 1.3.2 confirmed in project. The `CanvasLinearGradientBrush` constructor taking `CanvasGradientStop[], CanvasEdgeBehavior, CanvasAlphaMode` is available in this version.

---

## Architecture Patterns

### Current Code Structure (what gets changed)

```
ChartRenderer.cs  (pure math)
  GetZoneSegments()           ← REPLACED by GetContiguousSpans()
  ToX(), ToY()                ← REUSED unchanged
  GetRightEdgeAbsoluteX()     ← REUSED for last-span right edge

ChartColors.cs  (color lookup)
  ColorTable                  ← REUSED for gradient stop colors
  GetColor()                  ← REUSED
  GetZoneColor()              ← REUSED for glow indicator only
  BuildColorLookup()          ← NEW: Color[101] interpolation table

ChartDrawing.cs  (Win2D side effects)
  DrawChartFills()            ← MODIFIED: zone iteration → span + gradient
  DrawChartTopLine()          ← MODIFIED: zone iteration → span + gradient
  DrawGlowIndicator()         ← UNCHANGED (still uses GetZoneColor)

ExportHelper.cs
  DrawChartArea()             ← UNCHANGED if lineWidth passed as param
                              OR minimal change to pass lineWidth=2.5f
```

### Pattern 1: GetContiguousSpans (replaces GetZoneSegments)

**What:** Returns contiguous runs of non-gap data points. Unlike GetZoneSegments which splits at color zone boundaries, GetContiguousSpans keeps all points in one span as long as there is no data gap.

**When to use:** One span = one gradient brush. Each span gets its own `BuildGradientStops()` call, its own `CanvasLinearGradientBrush`, and its own path.

**Critical note on gap detection:** `UsageHistoryPoint` in ccInfoWin has NO `IsGap` field (from spec: "UsageHistoryPoint has no IsGap field — bug cannot occur in ccInfoWin"). Therefore, GetContiguousSpans can treat ALL existing points as one span. The method should still be written to accept a gap predicate for future-proofing, but currently there are no gaps to split on. The spec's "path construction rules" about skipping `isGap == true` are not applicable to the current data model.

**Example:**
```csharp
// In ChartRenderer.cs
public static List<(int StartIndex, int EndIndex)> GetContiguousSpans(
    IReadOnlyList<UsageHistoryPoint> points)
{
    // With no IsGap field, all points form one span.
    // Implementation can be expanded later for gap support.
    if (points.Count == 0) return [];
    return [(0, points.Count - 1)];
}
```

### Pattern 2: BuildColorLookup (new in ChartColors)

**What:** Pre-computes 101 interpolated colors (index 0–100) from the four gradient stops defined in the macOS spec. Pure function, no Win2D dependency.

**Gradient stops from spec (FEAT-02a):**

| Utilization % | Dark Color | Light Color |
|---------------|-----------|-------------|
| 0% | `#30D158` (Green) | `#34C759` (Green) |
| 50% | `#FFD60A` (Yellow) | `#FFCC00` (Yellow) |
| 75% | `#FF9F0A` (Orange) | `#FF9500` (Orange) |
| 90% | `#FF453A` (Red) | `#FF3B30` (Red) |

Note: the existing `ChartColors.ColorTable` already stores these exact hex values (confirmed by reading the file). `BuildColorLookup` extracts them from `ColorTable` via `GetColor()` calls to avoid duplication.

**Interpolation:** linear RGB between adjacent stops. Values above 90% clamp to red stop.

**Example:**
```csharp
// In ChartColors.cs
// Source: spec FEAT-02a + existing ColorTable values
public static Color[] BuildColorLookup(bool isDark)
{
    var lookup = new Color[101];
    var stops = new (double Position, Color Color)[]
    {
        (0.0,  GetColor("ProgressGreenBrush",  isDark)),
        (0.5,  GetColor("ProgressYellowBrush", isDark)),
        (0.75, GetColor("ProgressOrangeBrush", isDark)),
        (0.90, GetColor("ProgressRedBrush",    isDark)),
    };

    for (var i = 0; i <= 100; i++)
    {
        var t = i / 100.0;
        lookup[i] = InterpolateColor(stops, t);
    }
    return lookup;
}
```

### Pattern 3: BuildGradientStops (new in ChartRenderer)

**What:** Maps data point utilizations to `CanvasGradientStop` positions. Positions are normalized to [0.0, 1.0] within the span's X range. Pure function — no Win2D dependency (uses `Windows.UI.Color` which is in the platform layer).

**Input:** span points, windowStart, plotWidth, Color[] lookup (from BuildColorLookup)
**Output:** `CanvasGradientStop[]` — positions normalized to the span's own [0,1] range

**Key math:** position = (pointX - spanStartX) / (spanEndX - spanStartX)

**Important:** `CanvasGradientStop.Position` is normalized to [0,1] relative to the brush's own StartPoint→EndPoint span, not the full chart width. The brush's StartPoint and EndPoint are set to the actual canvas X coordinates of the span start/end.

**Example:**
```csharp
// In ChartRenderer.cs
// Source: Win2D CanvasGradientStop docs (Position = 0.0 to 1.0)
public static CanvasGradientStop[] BuildGradientStops(
    IReadOnlyList<UsageHistoryPoint> points,
    int startIndex,
    int endIndex,
    DateTimeOffset windowStart,
    float plotWidth,
    Color[] colorLookup)
{
    var stops = new List<CanvasGradientStop>();
    var spanStartX = ToX(points[startIndex].Timestamp, windowStart, plotWidth);
    var spanEndX   = ToX(points[endIndex].Timestamp,   windowStart, plotWidth);
    var spanWidth  = spanEndX - spanStartX;
    if (spanWidth <= 0f) spanWidth = 1f; // guard against single-point span

    for (var i = startIndex; i <= endIndex; i++)
    {
        var x = ToX(points[i].Timestamp, windowStart, plotWidth);
        var position = (x - spanStartX) / spanWidth;
        var utilIndex = (int)Math.Clamp(points[i].Utilization * 100.0, 0, 100);
        stops.Add(new CanvasGradientStop
        {
            Position = Math.Clamp(position, 0f, 1f),
            Color    = colorLookup[utilIndex]
        });
    }
    return stops.ToArray();
}
```

**Note:** `CanvasGradientStop` is a Win2D struct in `Microsoft.Graphics.Canvas.Brushes`. This makes `BuildGradientStops` technically not free of Win2D — it references the struct type. The planner must decide: either move the struct into pure C# tuples and convert at the ChartDrawing boundary, or accept the Win2D reference in ChartRenderer. The existing pattern keeps Win2D out of ChartRenderer — the cleanest approach is to return `(float Position, Color Color)[]` tuples from ChartRenderer and convert to `CanvasGradientStop[]` in ChartDrawing.

### Pattern 4: Gradient Brush Creation and Disposal (in ChartDrawing)

**What:** Creates two `CanvasLinearGradientBrush` instances per span — one for fill (Alpha=64), one for line stroke (Alpha=255). Both must be disposed immediately after the draw call.

**Alpha encoding:** Alpha is encoded in the `Color.A` field of each gradient stop, NOT via `brush.Opacity`. This is the correct Win2D approach to avoid the desaturation trap. The `CanvasAlphaMode.Premultiplied` mode ensures correct blending when compositing the semi-transparent fill onto the chart background.

**CanvasLinearGradientBrush constructor signature (from Win2D docs):**
```csharp
new CanvasLinearGradientBrush(
    ICanvasResourceCreator resourceCreator,
    CanvasGradientStop[] stops,
    CanvasEdgeBehavior edgeBehavior,
    CanvasAlphaMode alphaMode)
```
Use `CanvasEdgeBehavior.Clamp` (stops outside range get clamped to nearest stop color).
Use `CanvasAlphaMode.Premultiplied` (prevents desaturation during compositing).

**After construction, set StartPoint and EndPoint:**
```csharp
brush.StartPoint = new Vector2(spanStartAbsoluteX, 0f);
brush.EndPoint   = new Vector2(spanEndAbsoluteX,   0f);
```
Y coordinate for both is irrelevant for a horizontal gradient (can be 0).

**Full draw pattern for one span:**
```csharp
// Source: Win2D CanvasLinearGradientBrush docs, STATE.md disposal pitfall
var stops100 = BuildFillStops(gradientStops);   // stops with Alpha=255
var stops025 = BuildFillStops(gradientStops);   // same colors at Alpha=64

using var fillBrush = new CanvasLinearGradientBrush(
    resourceCreator, stops025, CanvasEdgeBehavior.Clamp, CanvasAlphaMode.Premultiplied);
fillBrush.StartPoint = new Vector2(spanStartX, 0f);
fillBrush.EndPoint   = new Vector2(spanEndX,   0f);

using var lineBrush = new CanvasLinearGradientBrush(
    resourceCreator, stops100, CanvasEdgeBehavior.Clamp, CanvasAlphaMode.Premultiplied);
lineBrush.StartPoint = new Vector2(spanStartX, 0f);
lineBrush.EndPoint   = new Vector2(spanEndX,   0f);

// Fill geometry
using var fillGeometry = CanvasGeometry.CreatePath(fillPathBuilder);
session.FillGeometry(fillGeometry, fillBrush);

// Stroke geometry
using var lineGeometry = CanvasGeometry.CreatePath(linePathBuilder);
session.DrawGeometry(lineGeometry, lineBrush, lineWidth);
```

**FillGeometry with ICanvasBrush:** `session.FillGeometry(geometry, ICanvasBrush)` — confirmed overload exists in Win2D.
**DrawGeometry with ICanvasBrush and stroke width:** `session.DrawGeometry(geometry, ICanvasBrush, strokeWidth)` — confirmed overload exists in Win2D.

### Pattern 5: Export Line Width Parameter

**Current state:** `DrawChartTopLine` hardcodes `2f` stroke width.
**Required change:** Add `float lineWidth = 2.0f` optional parameter to both `DrawChartFills` and `DrawChartTopLine`. ExportHelper passes `2.5f`, live chart uses default `2.0f`.

Current ExportHelper call:
```csharp
ChartDrawing.DrawChartFills(session, resourceCreator, points, windowStart.Value,
    plotWidth, plotHeight, isDark: true, chartLeft, plotOffsetY);
ChartDrawing.DrawChartTopLine(session, resourceCreator, points, windowStart.Value,
    plotWidth, plotHeight, isDark: true, chartLeft, plotOffsetY);
```

ExportHelper needs to pass `lineWidth: 2.5f` to `DrawChartTopLine`. The fill method does not draw a stroke so its lineWidth is irrelevant, but keeping the signature symmetric is clean.

### Anti-Patterns to Avoid

- **Caching CanvasLinearGradientBrush across frames:** Win2D brushes hold GPU resources; they must be recreated per draw cycle. This is the single most important pitfall for this phase.
- **Using brush.Opacity for fill transparency:** Setting `brush.Opacity = 0.25f` on a premultiplied brush causes desaturation. Encode alpha into the stop colors: `Color.FromArgb(64, r, g, b)`.
- **Setting brush.AlphaMode after construction:** `CanvasAlphaMode` must be set at construction time via the 4-parameter constructor overload.
- **Returning CanvasGradientStop from ChartRenderer:** Introducing a Win2D struct into ChartRenderer breaks the "pure math" contract. Return `(float Position, Color Color)[]` tuples instead, convert in ChartDrawing.
- **Gradient StartPoint/EndPoint in relative coordinates:** StartPoint and EndPoint are in canvas coordinate space (absolute pixels), not normalized [0,1]. They must include the `offsetX` applied to all other drawing calls.
- **Single global gradient spanning full chart width:** The brush start/end must be the actual data span start/end X, not the full plotWidth, otherwise colors will not correspond to actual utilization values.

---

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Color interpolation between 4 stops | Custom lerp algorithm with edge cases | Linear RGB lerp between adjacent stops (20 lines, testable) | Simple enough to implement correctly; no library needed |
| Gradient brush disposal tracking | Reference counting or pooling | `using` statement per draw cycle | Win2D design intent; anything else risks GPU resource leaks |
| Opacity via separate fill pass | Second fill with transparent solid brush | Alpha channel in CanvasGradientStop.Color | Correct approach for premultiplied compositing |

**Key insight:** Win2D gradient brushes are the standard for this exact use case. The complexity is in the stop position math and the alpha encoding, not the brush API itself.

---

## Common Pitfalls

### Pitfall 1: Cached CanvasLinearGradientBrush
**What goes wrong:** Brush is stored as a field and reused across Draw calls. Win2D throws on the second use or renders incorrectly because the brush's device context has been invalidated.
**Why it happens:** Looks like an optimization; the constructor call seems expensive.
**How to avoid:** Always create brush inside `using` within the draw method. Do not store as field.
**Warning signs:** `ObjectDisposedException` on second draw, or rendering stops after first frame.

### Pitfall 2: Alpha Desaturation on Semi-Transparent Fill
**What goes wrong:** Fill appears washed out/desaturated, especially on dark backgrounds.
**Why it happens:** Using `brush.Opacity = 0.25f` on a `CanvasAlphaMode.Premultiplied` brush causes the GPU to double-apply alpha during blending.
**How to avoid:** Encode alpha directly in each stop's `Color.A`. For fill stops: `Color.FromArgb(64, r, g, b)`. Keep `brush.Opacity` at its default (1.0).
**Warning signs:** Colors look pale/desaturated compared to the solid-color zone fills.

### Pitfall 3: Gradient Start/End Not Including offsetX
**What goes wrong:** Gradient appears shifted relative to the data, or is wrong in export (where offsetX is non-zero).
**Why it happens:** brush.StartPoint set to LeftMargin + spanRelativeX without adding offsetX.
**How to avoid:** `brush.StartPoint.X = offsetX + ChartRenderer.LeftMargin + spanRelativeStartX`
**Warning signs:** Gradient looks correct in live chart but shifted in PNG export.

### Pitfall 4: CanvasGradientStop Positions Not Normalized to Span
**What goes wrong:** All gradient stops compress into a small portion of the brush range, resulting in a color jump instead of a smooth gradient.
**Why it happens:** Using chart-width-relative positions (0.0–1.0 over the entire 5-hour window) instead of span-relative positions (0.0–1.0 over the span only).
**How to avoid:** Normalize: `position = (pointX - spanStartX) / spanWidth`.
**Warning signs:** Gradient has correct colors at edges but is mostly one solid color in the middle.

### Pitfall 5: Single-Point Span Division by Zero
**What goes wrong:** `BuildGradientStops` divides by `(spanEndX - spanStartX)` which is 0 when start == end.
**Why it happens:** Data may have a single point after session starts.
**How to avoid:** Guard: `if (spanWidth <= 0) spanWidth = 1f;` — use the start color as a solid fill.
**Warning signs:** `NaN` or `Infinity` in stop positions; Win2D may throw or render black.

### Pitfall 6: Missing CanvasGradientStop at Position 0.0 and 1.0
**What goes wrong:** Gradient brush has no stop at the exact start or end of the span, causing `CanvasEdgeBehavior.Clamp` to fill the missing portion with a default color.
**Why it happens:** The first/last data point X may not land exactly at 0.0/1.0 after normalization.
**How to avoid:** After building stops, ensure the first stop has Position=0.0f and the last has Position=1.0f. Clamp/add boundary stops if needed.
**Warning signs:** Fill or line has an unexpected flat-color region at one end.

---

## Code Examples

Verified patterns from official Win2D documentation and existing codebase:

### Creating a Gradient Brush (Win2D WinUI 3)
```csharp
// Source: https://microsoft.github.io/Win2D/WinUI3/html/T_Microsoft_Graphics_Canvas_Brushes_CanvasLinearGradientBrush.htm
using var brush = new CanvasLinearGradientBrush(
    resourceCreator,
    stops,                          // CanvasGradientStop[]
    CanvasEdgeBehavior.Clamp,
    CanvasAlphaMode.Premultiplied); // prevents desaturation
brush.StartPoint = new Vector2(spanStartAbsoluteX, 0f);
brush.EndPoint   = new Vector2(spanEndAbsoluteX,   0f);
```

### CanvasGradientStop Struct
```csharp
// Source: https://microsoft.github.io/Win2D/WinUI3/html/T_Microsoft_Graphics_Canvas_Brushes_CanvasGradientStop.htm
// Position: float 0.0 to 1.0 (normalized to brush StartPoint→EndPoint span)
// Color: Windows.UI.Color — alpha channel IS used
var stop = new CanvasGradientStop
{
    Position = 0.5f,
    Color    = Color.FromArgb(255, 0xFF, 0xD6, 0x0A) // Yellow at 100% opacity
};
```

### Fill with 25% Opacity Gradient
```csharp
// Alpha = 64 is approximately 25% opacity (64/255 ≈ 0.251)
// Encode alpha in Color.A, NOT via brush.Opacity
var fillStops = stops.Select(s => new CanvasGradientStop
{
    Position = s.Position,
    Color    = Color.FromArgb(64, s.Color.R, s.Color.G, s.Color.B)
}).ToArray();
```

### FillGeometry and DrawGeometry with ICanvasBrush
```csharp
// Source: https://microsoft.github.io/Win2D/WinUI3/html/M_Microsoft_Graphics_Canvas_CanvasDrawingSession_DrawGeometry.htm
// FillGeometry overload: (CanvasGeometry, ICanvasBrush)
session.FillGeometry(geometry, fillBrush);

// DrawGeometry overload: (CanvasGeometry, ICanvasBrush, Single strokeWidth)
session.DrawGeometry(geometry, lineBrush, lineWidth);
```

### Existing Path Construction (from ChartDrawing.cs)
```csharp
// Current fill path pattern — REUSE this structure with span-based iteration
using var pathBuilder = new CanvasPathBuilder(resourceCreator);
pathBuilder.BeginFigure(firstX, baselineY);
// ... AddLine calls ...
pathBuilder.AddLine(rightEdgeX, baselineY);
pathBuilder.EndFigure(CanvasFigureLoop.Closed);
using var geometry = CanvasGeometry.CreatePath(pathBuilder);
session.FillGeometry(geometry, fillBrush); // replace Color with ICanvasBrush overload
```

### Color Lookup Interpolation (pure math example)
```csharp
// 4 stops mapped to 0%, 50%, 75%, 90%
// For utilization t in [0.0, 1.0]:
private static Color InterpolateColor(
    (double Position, Color Color)[] stops, double t)
{
    t = Math.Clamp(t, 0.0, 1.0);
    // find the two surrounding stops
    for (var i = 0; i < stops.Length - 1; i++)
    {
        if (t <= stops[i + 1].Position)
        {
            var range = stops[i + 1].Position - stops[i].Position;
            var local = range > 0 ? (t - stops[i].Position) / range : 0;
            return LerpColor(stops[i].Color, stops[i + 1].Color, local);
        }
    }
    return stops[^1].Color; // clamp at red
}
```

---

## ExportHelper Integration

ExportHelper is clean — no structural changes needed to its pipeline. Confirmed by reading the file:

- `DrawChartArea` calls `ChartDrawing.DrawChartFills` and `ChartDrawing.DrawChartTopLine` with `isDark: true` (hardcoded — existing tech debt, not to be fixed here)
- `resourceCreator` passed as `ICanvasResourceCreator device` — valid for `CanvasLinearGradientBrush` constructor
- All offset parameters already correctly passed as `chartLeft, plotOffsetY`

The only required ExportHelper change is passing `lineWidth: 2.5f` to `DrawChartTopLine`. This requires adding an optional `float lineWidth = 2.0f` parameter to `DrawChartTopLine`.

Current signature:
```csharp
public static void DrawChartTopLine(
    CanvasDrawingSession session,
    ICanvasResourceCreator resourceCreator,
    IReadOnlyList<UsageHistoryPoint> points,
    DateTimeOffset windowStart,
    float plotWidth, float plotHeight,
    bool isDark,
    float offsetX = 0f, float offsetY = 0f)
```

New signature (adding `lineWidth`):
```csharp
public static void DrawChartTopLine(
    CanvasDrawingSession session,
    ICanvasResourceCreator resourceCreator,
    IReadOnlyList<UsageHistoryPoint> points,
    DateTimeOffset windowStart,
    float plotWidth, float plotHeight,
    bool isDark,
    float offsetX = 0f, float offsetY = 0f,
    float lineWidth = 2.0f)           // new — default preserves existing callers
```

ExportHelper passes `lineWidth: 2.5f`. Live chart callers (MainView) use default `2.0f`.

---

## Existing Code: What Changes vs What Stays

| File | Method | Change |
|------|--------|--------|
| `ChartColors.cs` | `BuildColorLookup(bool isDark)` | NEW — returns `Color[101]` interpolation table |
| `ChartRenderer.cs` | `GetContiguousSpans(points)` | NEW — returns `(int Start, int End)[]`, replaces zone concept |
| `ChartRenderer.cs` | `BuildGradientStops(...)` | NEW — returns `(float Position, Color Color)[]` tuples |
| `ChartRenderer.cs` | `GetZoneSegments()` | KEPT — still used by tests; mark as `[Obsolete]` or remove after verifying nothing else calls it |
| `ChartDrawing.cs` | `DrawChartFills()` | MODIFIED — span iteration, gradient brush, Alpha=64 stops |
| `ChartDrawing.cs` | `DrawChartTopLine()` | MODIFIED — span iteration, gradient brush, lineWidth param added |
| `ChartDrawing.cs` | `DrawGlowIndicator()` | UNCHANGED — still uses `ChartColors.GetZoneColor` for the dot color |
| `ExportHelper.cs` | `DrawChartArea()` | MINIMAL CHANGE — pass `lineWidth: 2.5f` to DrawChartTopLine |
| `ColorThresholds.cs` | — | UNCHANGED — still used by GetZoneSegments (if kept) |

---

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| Zone-based flat fills (per-segment solid color) | Continuous horizontal gradient per span | Phase 17 | Smoother visual, no segment borders |
| `GetZoneSegments()` → `(Start, End, BrushKey)` | `GetContiguousSpans()` → `(Start, End)` | Phase 17 | BrushKey no longer needed; color derived from utilization value |

---

## Environment Availability

Step 2.6: SKIPPED (no external dependencies — all required APIs are in Win2D which is already installed in the project at version 1.3.2).

---

## Validation Architecture

### Test Framework
| Property | Value |
|----------|-------|
| Framework | xunit 2.9.3 |
| Config file | none (implicit xunit discovery) |
| Quick run command | `dotnet test CCInfoWindows.Tests/CCInfoWindows.Tests.csproj -p:Platform=x64 --filter "FullyQualifiedName~ChartRenderer OR FullyQualifiedName~ChartColors" -x` |
| Full suite command | `dotnet test CCInfoWindows.Tests/CCInfoWindows.Tests.csproj -p:Platform=x64` |

### Phase Requirements → Test Map

| Req ID | Behavior | Test Type | Automated Command | File Exists? |
|--------|----------|-----------|-------------------|-------------|
| CHRT-01 | BuildColorLookup returns correct interpolated colors at 0%, 50%, 75%, 90%, 100% | unit | `dotnet test ... --filter "FullyQualifiedName~ChartColorsTests"` | ❌ Wave 0 |
| CHRT-01 | BuildGradientStops returns correct position/color for known data points | unit | `dotnet test ... --filter "FullyQualifiedName~ChartRendererTests"` | ✅ (file exists, new tests needed) |
| CHRT-01 | GetContiguousSpans returns single span for all-points input | unit | `dotnet test ... --filter "FullyQualifiedName~ChartRendererTests"` | ✅ (file exists, new tests needed) |
| CHRT-02 | Fill stops have Alpha=64; line stops have Alpha=255 | unit | `dotnet test ... --filter "FullyQualifiedName~ChartRendererTests"` | ✅ (file exists, new tests needed) |
| CHRT-03 | GetContiguousSpans returns empty list for empty input | unit | `dotnet test ... --filter "FullyQualifiedName~ChartRendererTests"` | ✅ (file exists, new tests needed) |
| CHRT-04 | Export path calls DrawChartTopLine with lineWidth=2.5f | manual/integration | Visual inspection of exported PNG | manual-only |
| CHRT-05 | Gradient colors match expected RGB values (no desaturation) | manual | Run app, observe live chart in dark + light themes | manual-only |

**Manual-only justification for CHRT-04/CHRT-05:** Win2D rendering requires a GPU device and WinUI runtime — not instantiable in unit tests. Visual correctness must be verified by running the app.

### Sampling Rate
- **Per task commit:** `dotnet test CCInfoWindows.Tests/CCInfoWindows.Tests.csproj -p:Platform=x64 --filter "FullyQualifiedName~ChartRenderer OR FullyQualifiedName~ChartColors" -x`
- **Per wave merge:** `dotnet test CCInfoWindows.Tests/CCInfoWindows.Tests.csproj -p:Platform=x64`
- **Phase gate:** Full suite green + visual chart inspection before `/gsd:verify-work`

### Wave 0 Gaps
- [ ] `CCInfoWindows.Tests/Helpers/ChartColorsTests.cs` — covers `BuildColorLookup` (new method, no test file exists for ChartColors)
- [ ] Add `GetContiguousSpans` and `BuildGradientStops` tests to existing `CCInfoWindows.Tests/Helpers/ChartRendererTests.cs`

*(Existing `ChartRendererTests.cs` for `ToX`/`ToY`/`GetZoneSegments`/`GetRightEdgeAbsoluteX` all pass and must remain green.)*

---

## Open Questions

1. **GetZoneSegments removal vs. retention**
   - What we know: GetZoneSegments is tested in 5 existing tests in ChartRendererTests.cs. Removing it breaks those tests.
   - What's unclear: Are those tests kept as regression coverage, or do they get deleted with the method?
   - Recommendation: Keep `GetZoneSegments` but mark `[Obsolete]`. Delete both method and tests only in a cleanup pass after phase is verified. Do not let the planner delete existing passing tests during this phase.

2. **ColorThresholds.cs retention**
   - What we know: Only used by GetZoneSegments and GetZoneColor (used by DrawGlowIndicator).
   - What's unclear: After GetZoneSegments is replaced, ColorThresholds is only used for the glow indicator.
   - Recommendation: Keep ColorThresholds unchanged. DrawGlowIndicator still needs GetZoneColor → GetThresholdKey.

3. **CanvasGradientStop in ChartRenderer**
   - What we know: The locked decision puts `BuildGradientStops` in ChartRenderer as "pure math, unit-testable". But `CanvasGradientStop` is a Win2D struct.
   - What's unclear: The decision says "unit-testable" which conflicts with a Win2D struct dependency.
   - Recommendation: Return `(float Position, Color Color)[]` tuples from ChartRenderer.BuildGradientStops. Convert to `CanvasGradientStop[]` at the boundary in ChartDrawing. This preserves pure-math testability. `Windows.UI.Color` is used throughout the model layer already and does not require Win2D — it is a platform type.

---

## Sources

### Primary (HIGH confidence)
- Win2D official docs — `CanvasLinearGradientBrush` class, constructor signatures, StartPoint/EndPoint properties: https://microsoft.github.io/Win2D/WinUI3/html/T_Microsoft_Graphics_Canvas_Brushes_CanvasLinearGradientBrush.htm
- Win2D official docs — `CanvasGradientStop` struct (Position 0.0–1.0, Color field): https://microsoft.github.io/Win2D/WinUI3/html/T_Microsoft_Graphics_Canvas_Brushes_CanvasGradientStop.htm
- Win2D official docs — `CanvasAlphaMode` enumeration (Premultiplied=0, Straight=1): https://microsoft.github.io/Win2D/WinUI3/html/T_Microsoft_Graphics_Canvas_CanvasAlphaMode.htm
- Win2D official docs — Pre-multiplied alpha explanation: https://microsoft.github.io/Win2D/WinUI3/html/PremultipliedAlpha.htm
- Win2D official docs — `DrawGeometry` with `ICanvasBrush` overload: https://microsoft.github.io/Win2D/WinUI3/html/M_Microsoft_Graphics_Canvas_CanvasDrawingSession_DrawGeometry.htm
- Codebase direct read — `ChartDrawing.cs`, `ChartRenderer.cs`, `ChartColors.cs`, `ExportHelper.cs`, `ChartRendererTests.cs`, `ColorThresholds.cs` — exact signatures, existing patterns
- macOS spec `spec/v1.10.0-macOS/spec-release-1.8.3-to-1.10.0.md` FEAT-02a/b/c/d — gradient stops, color values, opacity spec

### Secondary (MEDIUM confidence)
- Win2D docs — `FillGeometry(CanvasGeometry, ICanvasBrush)` overload confirmed: http://microsoft.github.io/Win2D/html/M_Microsoft_Graphics_Canvas_CanvasDrawingSession_FillGeometry_1.htm

---

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH — Win2D is already in project; APIs confirmed via official docs
- Architecture: HIGH — existing code fully read; change surface is small and well-defined
- Pitfalls: HIGH — disposal pitfall from STATE.md; alpha desaturation from Win2D docs; others from first-principles analysis of the API
- Test map: HIGH — test framework confirmed running (xunit 2.9.3, dotnet test works)

**Research date:** 2026-04-13
**Valid until:** 2026-07-13 (Win2D API is stable; 90-day window)
