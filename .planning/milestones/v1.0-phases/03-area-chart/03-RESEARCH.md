# Phase 3: Area Chart - Research

**Researched:** 2026-03-11
**Domain:** Win2D GPU-accelerated 2D rendering, step chart drawing, usage history persistence
**Confidence:** HIGH

---

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions
- **Chart rendering:** Win2D CanvasControl (Microsoft.Graphics.Canvas NuGet) — GPU-accelerated 2D rendering
- **Draw logic:** Custom via CanvasDrawingSession in the Draw event handler; IDisposable pattern required
- **Chart pattern:** Step chart (horizontal lines between data points, matching macOS reference)
- **Gradient:** Horizontal gradient along X-axis; line and fill color follows Y-value at each position (green → yellow → orange → red as value crosses zone thresholds)
- **Fill opacity:** 40% for the area fill, 100% for the top line (2px stroke width)
- **Glow indicator:** Static (no animation), filled circle 8px diameter + radial glow 16px diameter at 30% opacity, positioned at last (current) data point, color matches current zone
- **X-axis:** Always shows full 0h-5h range; chart grows left-to-right as data accumulates
- **Y-axis labels:** "0%", "50%", "100%" — 10px Regular #636366
- **X-axis labels:** "0h", "1h", "2h", "3h", "4h", "5h" — 10px Regular #636366
- **Threshold lines:** Dashed at 50% and 100% (dash 4px, gap 4px, 1px stroke, #48484A)
- **Chart replaces:** Existing ProgressBar in 5-STUNDEN-FENSTER section
- **Chart layout:** Section header → Chart container (120px height, 8px corner radius, 8px padding, full section width) → Percentage + Countdown row
- **Chart container background:** #2C2C2E (Dark) / #EBEBF0 (Light) — already in AppTheme.xaml as ChartBackgroundBrush
- **Sampling:** Each API poll creates one data point (timestamp + utilization percentage)
- **Data format:** Array of `{timestamp, utilization}` objects
- **Persistence:** Separate JSON file `%LOCALAPPDATA%\CCInfoWindows\usage-history.json`; written after every poll; loaded on startup
- **Reset detection:** Compare `resets_at` from API response against stored value; when changed → clear history, start fresh

### Claude's Discretion
- Win2D CanvasControl initialization and device lost handling
- Exact gradient interpolation implementation (LinearGradientBrush segments vs per-point coloring)
- History service interface design and DI registration
- Chart data point model class design
- GaussianBlurEffect parameters for glow rendering
- How to extract Color values from ThemeResource brushes for Win2D drawing

### Deferred Ideas (OUT OF SCOPE)
- Chart export as PNG — Phase 6 (EXPT-01, EXPT-02, EXPT-03)
- Hover tooltip showing timestamp + value at cursor position — potential v2
- Smooth curve interpolation instead of step chart — v2
</user_constraints>

---

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|-----------------|
| 5HUR-03 | Interactive area chart visualizes usage over the full 5-hour window | Win2D CanvasControl with CanvasPathBuilder step chart geometry |
| 5HUR-04 | Chart fill and line color interpolates by zone (green 0-50%, yellow 50-75%, orange 75-90%, red 90-100%) | CanvasLinearGradientBrush with per-segment zone color stops; reuse ColorThresholds.cs |
| 5HUR-05 | Glowing position indicator at current time point in chart | GaussianBlurEffect applied to CanvasCommandList circle; drawn after main chart fill |
| 5HUR-06 | Chart shows Y-axis labels and X-axis labels with dashed threshold lines | CanvasDrawingSession.DrawText with CanvasTextFormat; DrawLine with CanvasStrokeStyle.CustomDashStyle |
| 5HUR-07 | Usage history is persisted locally and survives app restart | IUsageHistoryService with JSON serialization to %LOCALAPPDATA%\CCInfoWindows\usage-history.json; mirrors SettingsService pattern |
| 5HUR-08 | Automatic reset detection clears history when 5-hour window resets | Compare stored `resets_at` DateTimeOffset vs API response in MainViewModel.UpdateUsageProperties |
| 5HUR-09 | Chart colors are slightly desaturated in dark mode | Decision locked: use existing ThemeResource brushes as-is (already Apple System Colors optimized for dark); no additional processing |
</phase_requirements>

---

## Summary

Phase 3 adds a Win2D GPU-rendered step chart to the existing 5-STUNDEN-FENSTER section of MainView, replacing the ProgressBar. The chart visualizes up to ~300 data points accumulated across the 5-hour window, with zone-colored fill and line, dashed threshold gridlines, axis labels, and a glow indicator at the current position.

The core technical work breaks into three independent tracks: (1) Win2D chart rendering with CanvasControl, (2) history data persistence via a new IUsageHistoryService, and (3) reset detection logic in MainViewModel. All three are wired together through the existing poll cycle.

Win2D is confirmed supported in WinUI 3 unpackaged apps via the `Microsoft.Graphics.Win2D` NuGet package. CanvasControl handles device lost automatically when using the CreateResources event. The gradient coloring strategy must draw the area as per-segment colored polygons (one polygon per same-zone data segment) rather than a single LinearGradientBrush, because the color transitions must track the Y-value (utilization), not a fixed X-position.

**Primary recommendation:** Use `CanvasPathBuilder` to build the step chart polygon per zone segment, fill each with a `CanvasSolidColorBrush` at 40% alpha; draw the top line separately at 100% alpha. Call `_chartControl.Invalidate()` after each poll to trigger redraw. Use `GaussianBlurEffect` on a `CanvasCommandList` circle for the glow indicator.

---

## Standard Stack

### Core
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| Microsoft.Graphics.Win2D | Latest stable on NuGet | Win2D CanvasControl for GPU-accelerated 2D rendering | Official Microsoft library; only Win2D package for WinUI 3; replaces old UWP `Win2D.uwp` |
| System.Text.Json | Built-in (.NET 9) | History JSON serialization/deserialization | Already used in SettingsService; zero new dependencies |

### Supporting
| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| Microsoft.Graphics.Canvas.Geometry | Part of Win2D | CanvasPathBuilder, CanvasGeometry, CanvasStrokeStyle | Drawing step chart polygon and dashed lines |
| Microsoft.Graphics.Canvas.Effects | Part of Win2D | GaussianBlurEffect | Glow circle behind position indicator |
| Microsoft.Graphics.Canvas.Text | Part of Win2D | CanvasTextFormat | Axis labels with font size control |
| Microsoft.Graphics.Canvas.Brushes | Part of Win2D | CanvasSolidColorBrush, CanvasLinearGradientBrush | Fill and line coloring |

**Installation:**
```bash
dotnet add CCInfoWindows/CCInfoWindows/CCInfoWindows.csproj package Microsoft.Graphics.Win2D
```

**IMPORTANT:** Win2D requires a specific CPU architecture target. The project already has `<Platforms>x64;ARM64</Platforms>` and `Directory.Build.props` sets `<Platform>x64</Platform>` as default — this satisfies Win2D's requirement. Do NOT set to `Any CPU`.

---

## Architecture Patterns

### Recommended Project Structure
```
CCInfoWindows/CCInfoWindows/
├── Models/
│   └── UsageHistoryPoint.cs     # {Timestamp, Utilization} data point model
├── Services/
│   ├── Interfaces/
│   │   └── IUsageHistoryService.cs   # Load/Save/Append/Clear contract
│   └── UsageHistoryService.cs        # JSON persistence in %LOCALAPPDATA%
├── Views/
│   └── MainView.xaml            # Replace ProgressBar with CanvasControl + XAML chart container
├── ViewModels/
│   └── MainViewModel.cs         # Add history collection, reset detection, Invalidate() call
└── (no new Helpers/Converters needed — reuse ColorThresholds.cs)
```

### Pattern 1: CanvasControl Setup in XAML

**What:** Declare Win2D namespace, add CanvasControl inside chart container Border, handle Draw and Unloaded events.

**When to use:** All Win2D rendering — CanvasControl raises Draw when `Invalidate()` is called.

```xml
<!-- In MainView.xaml -->
xmlns:canvas="using:Microsoft.Graphics.Canvas.UI.Xaml"

<!-- Replace existing ProgressBar with: -->
<Border Background="{ThemeResource ChartBackgroundBrush}"
        CornerRadius="8"
        Padding="8"
        Height="120">
    <canvas:CanvasControl x:Name="UsageChart"
                          Draw="UsageChart_Draw"
                          ClearColor="Transparent" />
</Border>
```

**Unloaded cleanup (mandatory — prevents memory leak reference count cycles):**
```csharp
// In MainView.xaml.cs code-behind
private void Page_Unloaded(object sender, RoutedEventArgs e)
{
    UsageChart.RemoveFromVisualTree();
    // UsageChart = null; // only if declared as field
}
```

Source: [Microsoft Learn Win2D Quick Start](https://learn.microsoft.com/en-us/windows/apps/develop/win2d/quick-start)

### Pattern 2: Step Chart Drawing Logic

**What:** Build the area fill as separate per-zone closed polygons using CanvasPathBuilder. The step chart pattern uses horizontal then vertical lines.

**When to use:** Any time data coverage changes (new data point added) — triggered by `_usageChart.Invalidate()` from UI thread.

```csharp
// Source: Win2D official docs — CanvasPathBuilder pattern
private void DrawStepChartArea(CanvasDrawingSession ds, IReadOnlyList<UsageHistoryPoint> points, float chartWidth, float chartHeight)
{
    // Map utilization 0.0-1.0 to Y pixels (inverted: 0% = bottom, 100% = top)
    // Map timestamp within 5-hour window to X pixels

    // Group consecutive points by zone color, draw one polygon per zone segment
    // Each segment: top line follows step pattern, bottom line is always Y=chartHeight
}
```

**Step chart coordinate calculation:**
```csharp
// Source: derived from Win2D geometry patterns
private float ToX(DateTimeOffset timestamp, DateTimeOffset windowStart, float chartWidth)
{
    const double WindowSeconds = 5 * 60 * 60;
    var elapsed = (timestamp - windowStart).TotalSeconds;
    return (float)(elapsed / WindowSeconds * chartWidth);
}

private float ToY(double utilization, float chartHeight)
{
    return (float)((1.0 - Math.Min(utilization, 1.0)) * chartHeight);
}
```

### Pattern 3: Zone Color Extraction from ThemeResources

**What:** Win2D needs `Windows.UI.Color`, not `SolidColorBrush`. Access ThemeResource colors by searching through theme dictionaries at draw time.

**Critical pitfall:** `Application.Current.Resources["ProgressGreenBrush"]` only returns the theme value current at the time of first access — it does NOT update when the theme changes. Must search ThemeDictionaries for the active theme key.

```csharp
// Source: WinUI 3 ThemeResource access pattern (community-verified)
private Windows.UI.Color GetThemeColor(string brushKey, string theme)
{
    foreach (var dict in Application.Current.Resources.MergedDictionaries)
    {
        if (dict.ThemeDictionaries.TryGetValue(theme, out var themeDict)
            && themeDict is ResourceDictionary rd
            && rd.TryGetValue(brushKey, out var resource)
            && resource is SolidColorBrush brush)
        {
            return brush.Color;
        }
    }
    return Colors.Gray; // fallback
}
```

**Simplification for this project:** Since all chart colors are hard-coded hex values in AppTheme.xaml (known at compile time), an alternative is to maintain a static lookup table of `Color` values keyed by `(brushKey, theme)` — avoids runtime dictionary traversal on every Draw event.

### Pattern 4: Glow Indicator with GaussianBlurEffect

**What:** Render a blurred circle (the glow halo) by drawing to a CanvasCommandList, applying GaussianBlurEffect, then drawing the solid circle on top.

```csharp
// Source: Win2D official docs — GaussianBlurEffect via CanvasCommandList
private void DrawGlowIndicator(CanvasDrawingSession ds, float x, float y, Windows.UI.Color zoneColor, ICanvasResourceCreator device)
{
    // Draw glow halo
    using var glowList = new CanvasCommandList(device);
    using (var glowDs = glowList.CreateDrawingSession())
    {
        var glowColor = Windows.UI.Color.FromArgb(77, zoneColor.R, zoneColor.G, zoneColor.B); // 30% = 77/255
        glowDs.FillCircle(x, y, GlowRadius, glowColor);
    }
    var blur = new GaussianBlurEffect { Source = glowList, BlurAmount = 4.0f };
    ds.DrawImage(blur);

    // Draw solid indicator dot on top
    ds.FillCircle(x, y, IndicatorRadius, zoneColor);
}

private const float IndicatorRadius = 4f;  // 8px diameter
private const float GlowRadius = 8f;       // 16px diameter
```

Source: [Win2D GaussianBlurEffect docs](https://microsoft.github.io/Win2D/WinUI3/html/T_Microsoft_Graphics_Canvas_Effects_GaussianBlurEffect.htm)

### Pattern 5: Dashed Threshold Lines

**What:** Draw horizontal dashed lines at 50% and 100% Y positions.

```csharp
// Source: Win2D CanvasStrokeStyle docs
private static readonly CanvasStrokeStyle DashedStyle = new()
{
    // CustomDashStyle values are multiples of strokeWidth
    // strokeWidth = 1px; [4, 4] = 4px dash, 4px gap
    CustomDashStyle = [4f, 4f]
};

private void DrawThresholdLines(CanvasDrawingSession ds, float chartWidth, float chartHeight)
{
    var thresholdColor = Windows.UI.Color.FromArgb(255, 0x48, 0x48, 0x4A);
    float y50 = ToY(0.50, chartHeight);
    float y100 = ToY(1.00, chartHeight);

    ds.DrawLine(0, y50, chartWidth, y50, thresholdColor, 1f, DashedStyle);
    ds.DrawLine(0, y100, chartWidth, y100, thresholdColor, 1f, DashedStyle);
}
```

Source: [CanvasStrokeStyle docs](https://microsoft.github.io/Win2D/WinUI3/html/T_Microsoft_Graphics_Canvas_Geometry_CanvasStrokeStyle.htm)

### Pattern 6: Axis Labels

**What:** Draw axis labels using CanvasTextFormat for font size control.

```csharp
// Source: Win2D CanvasDrawingSession.DrawText docs
private static readonly CanvasTextFormat AxisLabelFormat = new()
{
    FontFamily = "Segoe UI Variable",
    FontSize = 10f,
    HorizontalAlignment = CanvasHorizontalAlignment.Left,
    VerticalAlignment = CanvasVerticalAlignment.Center
};

// Y-axis labels: "0%", "50%", "100%" drawn at left margin
// X-axis labels: "0h"..."5h" drawn at bottom margin
```

**Layout note:** The CanvasControl occupies the full 120px container interior (after 8px padding on the Border). Reserve ~20px left margin for Y-labels and ~16px bottom margin for X-labels. The chart plotting area is thus `(width-20) x (height-16)`.

### Pattern 7: Device Lost Handling

**What:** CanvasControl automatically handles device lost by re-raising CreateResources. For this chart (no cached GPU resources — redraws from data each time), no CreateResources handler is needed beyond the automatic behavior.

**When to add CreateResources:** Only needed if pre-baking `CanvasCachedGeometry` or other GPU resources. The step chart redraws from data each Draw event, so no CreateResources handler is required.

Source: [Win2D Device Lost docs](https://learn.microsoft.com/en-us/windows/apps/develop/win2d/handling-device-lost)

### Pattern 8: UsageHistoryService — JSON Persistence

**What:** Mirrors SettingsService.cs pattern exactly. Write after every successful API poll. Load on startup in MainViewModel.InitializeAsync().

```csharp
// Source: mirrors existing SettingsService.cs pattern in this project
public class UsageHistoryService : IUsageHistoryService
{
    private static readonly string HistoryFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CCInfoWindows", "usage-history.json");

    public UsageHistory LoadHistory() { /* try/catch, return defaults on corrupt */ }
    public void SaveHistory(UsageHistory history) { /* best-effort, never crash */ }
}
```

**UsageHistory model:**
```csharp
public class UsageHistory
{
    public DateTimeOffset? ResetsAt { get; set; }
    public List<UsageHistoryPoint> Points { get; set; } = [];
}

public class UsageHistoryPoint
{
    public DateTimeOffset Timestamp { get; set; }
    public double Utilization { get; set; }  // 0.0 - 1.0
}
```

### Anti-Patterns to Avoid

- **Drawing in a loop without sector grouping:** Drawing 300 separate polygons each with FillGeometry is expensive. Group consecutive points of the same zone into a single polygon.
- **Storing Windows.UI.Color in CanvasControl fields at construction time:** Theme can change after construction. Resolve colors at draw time (in the Draw handler).
- **Forgetting RemoveFromVisualTree() on Unloaded:** Causes reference count cycle memory leaks — Win2D explicitly documents this as mandatory.
- **Using CanvasAnimatedControl instead of CanvasControl:** AnimatedControl fires Draw at 60fps. This chart updates only on poll — CanvasControl + Invalidate() is correct.
- **Calling Invalidate() from a non-UI thread:** Must call via `DispatcherQueue.TryEnqueue()` from background threads (existing pattern in this codebase).

---

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| GPU-accelerated 2D drawing | Custom Direct2D P/Invoke | Win2D CanvasControl | Win2D is the official Direct2D wrapper for WinUI 3; handles device creation, DPI scaling, XAML integration |
| Gaussian blur glow | Manual pixel blending | GaussianBlurEffect + CanvasCommandList | GPU-accelerated, correct blur falloff, handles DPI automatically |
| Dashed line rendering | Manual line segment math | CanvasStrokeStyle.CustomDashStyle | Direct2D handles dash geometry; custom math gets DPI scaling wrong |
| JSON serialization | Custom text parsing | System.Text.Json | Already in project, handles DateTimeOffset, nullable types, indented output |

**Key insight:** Win2D abstracts the entire Direct2D complexity. Every chart primitive (fill, stroke, text, blur) has a one-line Win2D API call.

---

## Common Pitfalls

### Pitfall 1: CanvasControl Memory Leak
**What goes wrong:** App slowly leaks GPU resources; may crash on navigation or theme toggle.
**Why it happens:** Win2D C++/WinRT objects create reference count cycles with .NET GC.
**How to avoid:** Always call `canvas.RemoveFromVisualTree()` in the Page.Unloaded event handler. Add handler to MainView.xaml.
**Warning signs:** Memory climbing during normal usage; crash on Settings navigation.

### Pitfall 2: CustomDashStyle Values Are Stroke-Width-Multiplied
**What goes wrong:** Dashes appear too long/short.
**Why it happens:** CustomDashStyle values are multiplied by strokeWidth. With strokeWidth=1, `[4f, 4f]` gives 4px dash / 4px gap. With strokeWidth=2, same array gives 8px/8px.
**How to avoid:** Use strokeWidth=1f for threshold lines. Values in array directly equal pixel lengths.

### Pitfall 3: ThemeResource Color Access Staleness
**What goes wrong:** Chart keeps using light mode colors after switching to dark mode.
**Why it happens:** `Application.Current.Resources["key"]` is evaluated once and captures the initial theme value.
**How to avoid:** Either resolve colors at draw time by searching ThemeDictionaries, OR maintain a hard-coded `Dictionary<(string brushKey, bool isDark), Windows.UI.Color>` from the known values in AppTheme.xaml, and update on ThemeChangedMessage receipt.
**Recommended approach:** Hard-coded lookup table — simpler and faster for a chart that redraws every poll.

### Pitfall 4: 5-Hour Window Start Time
**What goes wrong:** Data points render at incorrect X positions.
**Why it happens:** X position is calculated relative to the window start time, not absolute time. The window start = `resets_at - 5 hours`.
**How to avoid:** Compute `windowStart = resetsAt - TimeSpan.FromHours(5)` and use it as the zero X reference.

### Pitfall 5: Empty History at First Launch
**What goes wrong:** Chart panics with empty collection or renders nothing.
**Why it happens:** No history file on first run, or file was just cleared after a reset.
**How to avoid:** Chart Draw handler must gracefully render an empty state (just axes and threshold lines, no data polygon).

### Pitfall 6: Architecture Not Set to x64/ARM64
**What goes wrong:** Build fails with "Win2D requires a specific CPU architecture."
**Why it happens:** Win2D is implemented in C++ and cannot target Any CPU.
**How to avoid:** Already resolved — project targets `x64;ARM64` and Directory.Build.props defaults to x64. No action needed, but be aware if adding ARM64 build pipelines.

---

## Code Examples

### Full CanvasControl Draw Handler Skeleton

```csharp
// Source: Win2D official docs + project patterns
private void UsageChart_Draw(CanvasControl sender, CanvasDrawEventArgs args)
{
    var ds = args.DrawingSession;
    float width = (float)sender.Size.Width;
    float height = (float)sender.Size.Height;

    const float LeftMargin = 22f;    // space for Y-axis labels
    const float BottomMargin = 16f;  // space for X-axis labels
    float plotWidth = width - LeftMargin;
    float plotHeight = height - BottomMargin;

    DrawThresholdLines(ds, LeftMargin, plotWidth, plotHeight);
    DrawAxisLabels(ds, LeftMargin, plotWidth, plotHeight, height);

    var points = ViewModel.UsageHistory;
    if (points.Count == 0) return;

    DrawStepChartArea(ds, points, LeftMargin, plotWidth, plotHeight, sender);
    DrawTopLine(ds, points, LeftMargin, plotWidth, plotHeight);
    DrawGlowIndicator(ds, points[^1], LeftMargin, plotWidth, plotHeight, sender);
}
```

### Triggering Redraw After Poll

```csharp
// In MainViewModel.cs — add after UpdateUsageProperties(result) call
// Must be on UI thread — PollUsageAsync is already dispatched via DispatcherQueueTimer
_chartInvalidateCallback?.Invoke();

// In MainView.xaml.cs — register callback in Loaded event
ViewModel.ChartInvalidateCallback = () => UsageChart.Invalidate();
```

Alternative: expose the history collection as an `ObservableCollection` and observe changes in code-behind to call `Invalidate()`.

### NuGet Package Reference in .csproj

```xml
<PackageReference Include="Microsoft.Graphics.Win2D" Version="1.3.2" />
```

Verify current version on https://www.nuget.org/packages/Microsoft.Graphics.Win2D before pinning.

---

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| `Win2D.uwp` NuGet (UWP) | `Microsoft.Graphics.Win2D` (WinUI 3) | 2022 | Different package name; API is identical |
| Device lost: manual catch/retry | CanvasControl auto-handles via CreateResources(NewDevice) | Original Win2D design | No manual device lost code needed in most apps |
| `CanvasAnimatedControl` for all charts | `CanvasControl` + `Invalidate()` for data-driven charts | Always the case | Use animated only when you need 60fps continuous redraw |

**Deprecated/outdated:**
- `Win2D.uwp` NuGet: UWP-only, replaced by `Microsoft.Graphics.Win2D` for WinUI 3
- `SunburstApps/Win2D.WinUI` (GitHub): Community fork, replaced by official `Microsoft.Graphics.Win2D`

---

## Open Questions

1. **GaussianBlurEffect BlurAmount for 8px/16px glow feel**
   - What we know: BlurAmount is in device-independent pixels; 4.0f produces a moderate blur
   - What's unclear: The exact sigma that produces a visually correct 16px-diameter glow at 1x DPI
   - Recommendation: Use BlurAmount = 3.0f as starting point; tune visually during implementation

2. **Per-segment vs. LinearGradientBrush for zone coloring**
   - What we know: LinearGradientBrush maps stops to fixed X positions; zone boundaries depend on data values (Y), not X positions
   - What's unclear: If a segment stays entirely in one zone, LinearGradientBrush across that segment with same color at both ends is equivalent to a solid fill
   - Recommendation: Use per-segment solid-color polygons (one per zone run). This correctly handles non-monotone data (e.g., usage drops and rises again through zones).

3. **CanvasControl inside a Border with CornerRadius**
   - What we know: CanvasControl is an immediate-mode control; it does not clip to its parent's CornerRadius
   - What's unclear: Whether WinUI 3 compositing clips CanvasControl content to the parent Border's corner clip
   - Recommendation: Set `CanvasControl.ClearColor = Transparent`; the parent Border's rounded corners will clip visually via compositor. If clipping fails, add `Canvas.Clip` or use `CanvasControl` within a `Viewbox` with corner clipping.

---

## Validation Architecture

### Test Framework
| Property | Value |
|----------|-------|
| Framework | xunit 2.9.3 + Moq 4.20.72 |
| Config file | none — discovery via xunit runner |
| Quick run command | `dotnet test CCInfoWindows.Tests/CCInfoWindows.Tests.csproj -c Debug --no-build` |
| Full suite command | `dotnet test CCInfoWindows.Tests/CCInfoWindows.Tests.csproj -c Debug` |

### Phase Requirements → Test Map
| Req ID | Behavior | Test Type | Automated Command | File Exists? |
|--------|----------|-----------|-------------------|-------------|
| 5HUR-03 | Step chart draws correct X/Y for given data points | unit | `dotnet test ... --filter "FullyQualifiedName~UsageChartRendererTests"` | ❌ Wave 0 |
| 5HUR-04 | Zone color selection at threshold boundaries (0.50, 0.75, 0.90) | unit | `dotnet test ... --filter "FullyQualifiedName~ColorThresholdsTests"` | ✅ exists (Helpers/ColorThresholdsTests.cs) |
| 5HUR-05 | Glow indicator position = last data point coordinates | unit | `dotnet test ... --filter "FullyQualifiedName~UsageChartRendererTests"` | ❌ Wave 0 |
| 5HUR-06 | Axis labels and threshold line Y-positions computed correctly | unit | `dotnet test ... --filter "FullyQualifiedName~UsageChartRendererTests"` | ❌ Wave 0 |
| 5HUR-07 | LoadHistory returns persisted data; SaveHistory writes correct JSON | unit | `dotnet test ... --filter "FullyQualifiedName~UsageHistoryServiceTests"` | ❌ Wave 0 |
| 5HUR-08 | Reset detection: when resets_at changes, history cleared | unit | `dotnet test ... --filter "FullyQualifiedName~UsageHistoryServiceTests"` | ❌ Wave 0 |
| 5HUR-09 | Dark mode color values used in dark theme (visual) | manual-only | N/A — ThemeResource color extraction is a runtime UI concern; no unit test surface | N/A |

**Note on 5HUR-03/05/06:** Win2D CanvasControl itself cannot be unit-tested (requires GPU). Test the pure coordinate-calculation helper methods extracted from the Draw handler (ToX, ToY, BuildStepPolygon, etc.) as plain static functions. This is why a `UsageChartRenderer` helper class (non-Win2D) is recommended for coordinate math.

### Sampling Rate
- **Per task commit:** `dotnet test CCInfoWindows.Tests/CCInfoWindows.Tests.csproj -c Debug`
- **Per wave merge:** `dotnet test CCInfoWindows.Tests/CCInfoWindows.Tests.csproj -c Debug`
- **Phase gate:** Full suite green before `/gsd:verify-work`

### Wave 0 Gaps
- [ ] `CCInfoWindows.Tests/Services/UsageHistoryServiceTests.cs` — covers 5HUR-07, 5HUR-08
- [ ] `CCInfoWindows.Tests/Helpers/UsageChartRendererTests.cs` — covers 5HUR-03, 5HUR-05, 5HUR-06 (pure coordinate math)

*(5HUR-04 already covered by existing `ColorThresholdsTests.cs`)*

---

## Sources

### Primary (HIGH confidence)
- [Microsoft.Graphics.Win2D NuGet](https://www.nuget.org/packages/Microsoft.Graphics.Win2D) — confirmed package name for WinUI 3
- [Win2D Quick Start — Microsoft Learn](https://learn.microsoft.com/en-us/windows/apps/develop/win2d/quick-start) — CanvasControl setup, RemoveFromVisualTree pattern, GaussianBlurEffect + CanvasCommandList
- [Win2D Device Lost — Microsoft Learn](https://learn.microsoft.com/en-us/windows/apps/develop/win2d/handling-device-lost) — automatic handling via CreateResources(NewDevice)
- [Win2D WinUI3 CanvasControl reference](https://microsoft.github.io/Win2D/WinUI3/html/T_Microsoft_Graphics_Canvas_UI_Xaml_CanvasControl.htm) — ClearColor, Invalidate, ReadyToDraw
- [CanvasLinearGradientBrush](https://microsoft.github.io/Win2D/WinUI3/html/T_Microsoft_Graphics_Canvas_Brushes_CanvasLinearGradientBrush.htm) — gradient stops, StartPoint/EndPoint
- [CanvasStrokeStyle](https://microsoft.github.io/Win2D/WinUI3/html/T_Microsoft_Graphics_Canvas_Geometry_CanvasStrokeStyle.htm) — CustomDashStyle array
- [GaussianBlurEffect](https://microsoft.github.io/Win2D/WinUI3/html/T_Microsoft_Graphics_Canvas_Effects_GaussianBlurEffect.htm) — BlurAmount, Source from CanvasCommandList
- [CanvasPathBuilder](https://microsoft.github.io/Win2D/WinUI3/html/T_Microsoft_Graphics_Canvas_Geometry_CanvasPathBuilder.htm) — BeginFigure/AddLine/EndFigure pattern
- Project codebase: `SettingsService.cs`, `ColorThresholds.cs`, `PercentageToColorConverter.cs`, `AppTheme.xaml` — all examined directly

### Secondary (MEDIUM confidence)
- [WinUI 3 ThemeResource access from code — GitHub Discussion #7410](https://github.com/microsoft/microsoft-ui-xaml/discussions/7410) — ThemeDictionaries traversal pattern; verified by multiple community responders
- macOS reference screenshot `spec/v1.7.1/flächenfüllung-chart-macOS.png` — confirms step chart (not smooth), color zone transitions, glow dot, axis label positions

### Tertiary (LOW confidence)
- None — all major claims verified via official docs or existing codebase

---

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH — official NuGet package, Microsoft Learn docs verified 2025/2026
- Architecture: HIGH — Win2D patterns from official quickstart; history service mirrors existing SettingsService
- Pitfalls: HIGH — memory leak and ThemeResource staleness documented in official Win2D and WinUI3 issues
- Chart coordinate math: MEDIUM — step chart algorithm is straightforward but untested; recommended to extract to testable helper

**Research date:** 2026-03-11
**Valid until:** 2026-06-11 (Win2D API is stable; WinUI 3 1.8 already locked in project)
