# Phase 3: Area Chart - Context

**Gathered:** 2026-03-10
**Status:** Ready for planning

<domain>
## Phase Boundary

Interactive, color-coded area chart visualizing 5-hour usage history within the existing MainView dashboard. Chart replaces the current progress bar in the 5-STUNDEN-FENSTER section. Includes usage history persistence across app restarts and automatic reset detection when the 5-hour window rolls over. No hover/touch interactivity — purely visual. No chart export (Phase 6).

</domain>

<decisions>
## Implementation Decisions

### Chart Rendering Technology
- Win2D CanvasControl (Microsoft.Graphics.Canvas NuGet) — GPU-accelerated 2D rendering
- Custom draw logic via CanvasDrawingSession in the Draw event handler
- IDisposable pattern required for Win2D resources
- CanvasControl hosted inside the chart container Border in MainView.xaml

### Chart Visual Design
- **Step chart** pattern (horizontal lines between data points, matching macOS reference)
- **Horizontal gradient** along X-axis: line and fill color follows the Y-value at each position (green → yellow → orange → red as the value crosses zone thresholds)
- **Fill opacity**: 40% for the area fill, 100% for the top line (2px stroke width)
- **Glow indicator**: static (no animation), filled circle 8px diameter + radial glow 16px diameter at 30% opacity, positioned at the last (current) data point, color matches current zone
- **X-axis**: always shows full 0h-5h range regardless of data coverage; chart grows from left to right as data accumulates
- **Y-axis labels**: "0%", "50%", "100%" (10px, Regular, #636366)
- **X-axis labels**: "0h", "1h", "2h", "3h", "4h", "5h" (10px, Regular, #636366)
- **Threshold lines**: dashed at 50% and 100% (dash 4px, gap 4px, 1px stroke, #48484A)
- **Update animation**: new data point appended, no full redraw — glow indicator shifts to new position

### Chart Layout in MainView
- Chart REPLACES the existing ProgressBar in the 5-STUNDEN-FENSTER section
- Layout order: Section header → Chart container (120px) → Percentage + Countdown row
- Chart container: #2C2C2E (Dark) / #EBEBF0 (Light) background, 8px corner radius, 8px inner padding, 100% section width

### History Data Model
- Polling = sampling: each API poll creates one data point (timestamp + utilization percentage)
- At 60s polling interval over 5h = max ~300 data points per window
- Data format: array of `{timestamp, utilization}` objects

### History Persistence
- Separate JSON file: `%LOCALAPPDATA%\CCInfoWindows\usage-history.json`
- Written to disk after every API poll (~1 write/min, ~15KB file — negligible I/O)
- Loaded on app startup to restore chart from previous session
- Crash-safe: no data loss beyond the last poll interval

### Reset Detection
- Compare `resets_at` from API response against stored value
- When `resets_at` changes: clear history, start fresh data point, persist new `resets_at`
- No timestamp-based fallback — API is the source of truth

### Dark Mode Colors
- Use existing styleguide Dark/Light color pairs (Apple System Colors) — already optimized for dark backgrounds
- No additional desaturation processing needed
- Same ThemeResource brushes as progress bars (ProgressGreenBrush, ProgressYellowBrush, etc.)
- Chart reads theme colors at draw time, redraws on theme change

### Claude's Discretion
- Win2D CanvasControl initialization and device lost handling
- Exact gradient interpolation implementation (LinearGradientBrush segments vs per-point coloring)
- History service interface design and DI registration
- Chart data point model class design
- GaussianBlurEffect parameters for glow rendering
- How to extract Color values from ThemeResource brushes for Win2D drawing

</decisions>

<specifics>
## Specific Ideas

- "Schau dir das File 'spec/v1.7.1/flächenfüllung-chart-macOS.png' an" — the macOS reference shows a horizontal gradient that transitions green → yellow → orange → rot along the X-axis as the usage value increases over the 5-hour window
- The chart in the macOS app is a step chart (horizontal segments), not smooth curves
- The large percentage (94%) and "RESET IN 2min" countdown are displayed ABOVE the chart in the macOS reference, but our styleguide places them BELOW — follow our Phase 2 layout (percentage + countdown below)

</specifics>

<code_context>
## Existing Code Insights

### Reusable Assets
- `ColorThresholds.cs`: Maps utilization 0.0-1.0 to brush key names (ProgressGreenBrush etc.) — reuse for chart zone color selection
- `PercentageToColorConverter.cs`: Resolves brush keys to SolidColorBrush from ThemeResources — reference for extracting Color values
- `AppTheme.xaml`: Contains all progress bar color ThemeResources (Dark/Light variants) — chart will read same brushes
- `CountdownFormatter.cs`: Already formats "2h 14min" strings — no change needed
- `SettingsService` + `AppSettings`: JSON persistence in %LOCALAPPDATA% — pattern to follow for history file

### Established Patterns
- MVVM with CommunityToolkit.Mvvm source generators ([ObservableProperty], [RelayCommand])
- DispatcherQueue.TryEnqueue() for UI thread marshaling
- WeakReferenceMessenger for cross-ViewModel communication (ThemeChangedMessage)
- WebViewBridge for API calls (bypasses Cloudflare) — no change needed

### Integration Points
- `MainView.xaml` 5-STUNDEN-FENSTER section: replace ProgressBar with Win2D CanvasControl inside chart container Border
- `MainViewModel.cs`: add history collection, history service dependency, reset detection logic in existing poll cycle
- `App.xaml.cs` DI container: register new history service
- `AppTheme.xaml`: add chart container background ThemeResource (#2C2C2E / #EBEBF0)
- `CCInfoWindows.csproj`: add Microsoft.Graphics.Canvas NuGet reference

</code_context>

<deferred>
## Deferred Ideas

- Chart export as PNG — Phase 6 (EXPT-01, EXPT-02, EXPT-03)
- Hover tooltip showing timestamp + value at cursor position — potential v2 enhancement
- Smooth curve interpolation instead of step chart — v2 if users prefer

</deferred>

---

*Phase: 03-area-chart*
*Context gathered: 2026-03-10*
