# Architecture Patterns

**Domain:** WinUI 3 desktop app — macOS v1.10.0 feature parity integration (v1.3)
**Researched:** 2026-04-13
**Confidence:** HIGH (direct code inspection of every affected file)

---

## v1.3 Integration Analysis

v1.3 introduces four features across four implementation phases. Unlike v1.2, which only modified existing components, v1.3 adds two new services and one new helper class. The dependency graph is still linear but wider:

```
Phase 1: BurnRateCalculator (new Helper) + BurnRatePrediction (new Model)
    ↓ consumed by MainViewModel
Phase 1: BurnRateNotificationService (new Service, registered in DI)
    ↓ called from MainViewModel after prediction is computed
Phase 1: MainView.xaml (inline banner added to 5h section)
    ↓ binds to new ViewModel properties

Phase 2: ChartColors.cs + ChartRenderer.cs + ChartDrawing.cs (gradient replaces zones)
    ↓ ExportHelper.cs uses ChartDrawing — automatically inherits gradient
    (independent of Phase 1)

Phase 3: SettingsView.xaml (full XAML rewrite) + SettingsViewModel.cs (label text)
    (independent of Phases 1 and 2)

Phase 4: JsonlService.cs (watcher verification only — already correct per inspection)
    (independent of all other phases)
```

---

## Component Map: New vs Modified

### New Components

| Component | File | Purpose |
|-----------|------|---------|
| `BurnRatePrediction` | `Models/BurnRatePrediction.cs` | Data class: `HitsLimitAt`, `MinutesUntilLimit`, `FormattedTimeUntilLimit` |
| `BurnRateCalculator` | `Helpers/BurnRateCalculator.cs` | Static class: linear regression prediction engine |
| `IBurnRateNotificationService` | `Services/Interfaces/IBurnRateNotificationService.cs` | Service contract: `CheckBurnRate(prediction)`, `Reset()` |
| `BurnRateNotificationService` | `Services/BurnRateNotificationService.cs` | Windows App SDK `AppNotificationManager` toast, one-shot guard |

### Modified Components

| Component | File | What Changes |
|-----------|------|--------------|
| `MainViewModel` | `ViewModels/MainViewModel.cs` | Add `_burnRateNotificationService` field (ctor injection), add `[ObservableProperty] BurnRatePrediction?`, add `IsBurnRateWarningVisible` computed property, add `BurnRateWarningText` computed property, call `BurnRateCalculator.Predict()` + `CheckBurnRate()` at end of `UpdateUsageProperties()` |
| `MainView.xaml` | `Views/MainView.xaml` | Add `Border` burn rate banner immediately below the percentage/countdown row in the 5h section |
| `ChartColors.cs` | `Helpers/ChartColors.cs` | Add `BuildColorLookup(bool isDark)` returning `Color[101]` with linear RGB interpolation between 4 stops |
| `ChartRenderer.cs` | `Helpers/ChartRenderer.cs` | Add `BuildGradientStops(points, windowStart, plotWidth, colorLookup)` returning list of `(float position, Color color)`; keep `GetZoneSegments()` for glow indicator which still uses zone-based color |
| `ChartDrawing.cs` | `Helpers/ChartDrawing.cs` | Rewrite `DrawChartFills()` and `DrawChartTopLine()` to use `CanvasLinearGradientBrush` per gap-free span instead of per-zone segment iteration |
| `ExportHelper.cs` | `Helpers/ExportHelper.cs` | Verify — no changes expected; gradient is applied at ChartDrawing level and export already calls the same methods |
| `SettingsView.xaml` | `Views/SettingsView.xaml` | Full XAML rewrite: flat `StackPanel` → `Segmented` + 4 tab content panels |
| `SettingsViewModel.cs` | `ViewModels/SettingsViewModel.cs` | Change `RefreshOptions` labels to short notation (30s, 1min, etc.); timeout labels already inline in XAML (no VM change needed there); add version string property for About tab; add `SelectedTabIndex` observable property for `Segmented` control |
| `App.xaml.cs` | `App.xaml.cs` | Register `IBurnRateNotificationService` as singleton; register `AppNotificationManager` if required; inject into `MainViewModel` constructor |
| `App.xaml` | `App.xaml` | Add `BurnRateWarningBrush`, `BurnRateWarningTextBrush`, four `SettingsBadge*Brush` theme resources |
| `Resources.resw` (both locales) | `Strings/de-DE/` and `Strings/en-US/` | Add all burn rate string keys and settings tab label keys |
| `JsonlService.cs` | `Services/JsonlService.cs` | No changes required — watcher already has correct `NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size` and `IncludeSubdirectories = true` |

---

## Detailed Integration Points per Feature

### Feature 1: Burn Rate Warning

**Data flow through existing architecture:**

```
PollUsageAsync() [MainViewModel.cs, line 385]
  → _apiService.FetchUsageAsync() → UsageResponse
  → UpdateUsageProperties(result)
       [existing path: sets FiveHourUtilization, FiveHourPercentage, _fiveHourResetsAt]
       → AppendHistoryPoint() → UsageHistoryPoints updated
       [NEW path appended at end of UpdateUsageProperties()]:
       → BurnRateCalculator.Predict(
             UsageHistoryPoints,           // IReadOnlyList<UsageHistoryPoint>
             data.FiveHour.Utilization,    // double 0-100 (API scale, NOT normalized)
             data.FiveHour.ResetsAt        // DateTimeOffset?
           ) → BurnRatePrediction? prediction
       → BurnRatePrediction = prediction   // [ObservableProperty] → triggers banner
       → _burnRateNotificationService.CheckBurnRate(prediction)
```

**Critical data type contract:** `UsageHistoryPoint.Utilization` is stored as normalized `0.0–1.0` (confirmed in `UsageHistory.cs`). `BurnRateCalculator` must multiply by 100 internally before applying the `MinimumUtilization = 20.0` guard and regression math. The `currentUtilization` parameter from `data.FiveHour.Utilization` is already `0–100` (API scale), consistent with `FiveHourPercentage`. The calculator must handle both scales consistently — safest design: accept normalized `0.0–1.0` for history points and `double currentUtilization` in `0–100` scale for the current value (matching `FiveHourPercentage`), then convert history internally.

**MainViewModel observable properties to add:**

```csharp
[ObservableProperty]
private BurnRatePrediction? _burnRatePrediction;

public bool IsBurnRateWarningVisible => BurnRatePrediction is not null;
public string BurnRateWarningText => BurnRatePrediction is not null
    ? string.Format(ResourceLoader.GetForCurrentView().GetString("BurnRateBannerText"),
                    BurnRatePrediction.FormattedTimeUntilLimit)
    : string.Empty;
```

`OnBurnRatePredictionChanged` must notify `IsBurnRateWarningVisible` and `BurnRateWarningText` using `OnPropertyChanged()`.

**BurnRateNotificationService state machine:**

```
state: _notifiedBurnRate = false (initial, after Reset(), after logout)

CheckBurnRate(prediction):
  if prediction != null AND _notifiedBurnRate == false:
    → send toast via AppNotificationManager
    → _notifiedBurnRate = true
  if prediction == null AND _notifiedBurnRate == true:
    → _notifiedBurnRate = false  (ready for next warning cycle)

Reset():
  → _notifiedBurnRate = false
```

`Reset()` is called from `MainViewModel.Logout()` (existing logout path) to ensure the notification guard clears on session change.

**DI wiring in App.xaml.cs:**

```csharp
services.AddSingleton<IBurnRateNotificationService, BurnRateNotificationService>();
// MainViewModel constructor gains one more parameter:
services.AddTransient<MainViewModel>(sp => new MainViewModel(
    ...,
    sp.GetRequiredService<IBurnRateNotificationService>()));
```

`AppNotificationManager` registration: call `AppNotificationManager.Default.Register()` in `App.OnLaunched()` before any notification is sent. No additional NuGet package required — already bundled in `Microsoft.WindowsAppSDK 1.8`.

---

### Feature 2: Chart Horizontal Gradient

**Existing rendering path (v1.2):**

```
MainView.xaml [CanvasAnimatedControl]
  → ChartInvalidateMessage → InvalidateChart()
  → Win2D Draw event
  → ChartDrawing.DrawAxesAndLabels()
  → ChartDrawing.DrawChartFills()     [zone segments → solid fill per zone]
  → ChartDrawing.DrawChartTopLine()   [zone segments → solid stroke per zone]
  → ChartDrawing.DrawGlowIndicator()  [zone color lookup — UNCHANGED]
```

**New rendering path (v1.3):**

```
ChartDrawing.DrawChartFills()
  → ChartColors.BuildColorLookup(isDark) → Color[101]
  → [for each continuous span in points (no gap markers in ccInfoWin — single span)]:
      → ChartRenderer.BuildGradientStops(points, windowStart, plotWidth, colorLookup)
           → for each point: x = ToX(point.Timestamp, ...) / plotWidth (normalized 0.0-1.0)
                             color = colorLookup[(int)(point.Utilization * 100)]
           → returns List<(float position, Color color)>
      → build closed path (area fill)
      → CanvasLinearGradientBrush with stops, spanning leftX to rightX of span
      → set brush alpha to 64 (25% opacity) on fill color stops
      → FillGeometry(closedPath, gradientBrush)

ChartDrawing.DrawChartTopLine()
  → same gradient stop calculation
  → CanvasLinearGradientBrush at 100% opacity
  → DrawGeometry(openPath, gradientBrush, lineWidth: 2.0f)
```

**Win2D `CanvasLinearGradientBrush` creation pattern:**

```csharp
var stops = gradientStops
    .Select(s => new CanvasGradientStop { Position = s.Position, Color = s.Color })
    .ToArray();
using var brush = new CanvasLinearGradientBrush(resourceCreator, stops)
{
    StartPoint = new System.Numerics.Vector2(leftX, 0),
    EndPoint   = new System.Numerics.Vector2(rightX, 0)
};
session.FillGeometry(geometry, brush);
```

`CanvasLinearGradientBrush` is `IDisposable` — wrap in `using`. The `resourceCreator` parameter matches the existing method signatures (already passed as `ICanvasResourceCreator`).

**`GetZoneSegments()` in `ChartRenderer` remains unchanged** — still used by `DrawGlowIndicator()` to determine the current zone color for the endpoint dot. Only `DrawChartFills()` and `DrawChartTopLine()` switch from zone segments to gradient stops.

**Export compatibility:** `ExportHelper.cs` calls `ChartDrawing.DrawChartFills()` and `DrawChartTopLine()` with the same method signatures (plus offset parameters). No changes needed in `ExportHelper` — gradient rendering propagates automatically. Export uses `lineWidth: 2.5f` vs live chart's `2.0f`; this is passed as a parameter at the call site in the export path.

---

### Feature 3: Settings View Redesign

**Current SettingsView.xaml structure:**

```
Grid [3 rows: header, content, logout]
  Row 0: StackPanel [back button + title]
  Row 1: StackPanel [flat list of all settings with dividers]
  Row 2: Button [logout, red]
```

**New structure:**

```
Grid [3 rows: header, segmented+content, footer]
  Row 0: StackPanel [back button + title]   [UNCHANGED]
  Row 1: Grid [2 rows: segmented control, content area]
    SubRow 0: controls:Segmented [4 items, colored icon badges]
    SubRow 1: Grid [content panels, one per tab, visibility-switched]
      Panel "General":  7 rows (autostart, refresh, timeout, dark mode, language, sonnet context, reset window)
      Panel "Updates":  version, pricing source, last fetch
      Panel "Account":  token status, logout button
      Panel "About":    app name, version, GitHub link, credits
  Row 2: [empty — logout moved into Account tab]
```

**SettingsViewModel changes — minimal:**

1. `RefreshOptions` labels change from "30 Sekunden" → "30s", "1 Minute" → "1min", etc. No new properties.
2. Add `int SelectedTabIndex { get; set; }` as `[ObservableProperty]` to track which `Segmented` tab is selected. No persistence needed (tabs reset to General on each navigation).
3. Add `string AppVersion` property (computed, reads from `Package.Current.Id.Version` or `Assembly.GetEntryAssembly().GetName().Version`) for About tab display.
4. Logout command moves from the page footer `Button` to the Account tab content. The command itself (`LogoutCommand`) is unchanged.

**Segmented control binding pattern:**

```xml
<controls:Segmented SelectedIndex="{x:Bind ViewModel.SelectedTabIndex, Mode=TwoWay}"
                    HorizontalAlignment="Stretch">
    <controls:SegmentedItem>
        <controls:SegmentedItem.Icon>
            <IconSourceElement>
                <!-- colored badge DataTemplate here -->
            </IconSourceElement>
        </controls:SegmentedItem.Icon>
    </controls:SegmentedItem>
    ...
</controls:Segmented>
```

**Tab content visibility switching pattern:**

```xml
<Grid x:Name="GeneralPanel"
      Visibility="{x:Bind ViewModel.SelectedTabIndex, Mode=OneWay, Converter={StaticResource TabIndexToVisibilityConverter_0}}">
```

Alternative (simpler): use `x:Bind` with a converter that compares an integer to a constant, or use code-behind for tab switching since SettingsView.xaml.cs can legally manage visibility as a pure presentation concern with no business logic. The code-behind approach is simpler given WinUI 3's limited converter support for integer comparison. Either approach is valid.

**No new messenger messages.** All existing `SettingsViewModel` side effects (RefreshInterval, Threshold, Autostart, Language, SonnetContext, WindowSize, Logout) remain attached to the same observable property change handlers. The XAML restructuring does not affect the data flow.

---

### Feature 4: Session Watcher Fix

**Finding from direct code inspection:** `JsonlService.cs` at `StartWatching()` (line 812–818) already configures:

```csharp
NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
IncludeSubdirectories = true,
```

This is exactly the correct configuration for file-level change detection. The macOS FSEvents bug that triggered this feature (directory-level coalescing) does not apply to Windows `FileSystemWatcher`. **No code changes required in `JsonlService.cs`.**

The Phase 4 deliverable is a verified confirmation with test documentation, not a code change.

---

## Component Boundary Diagram (v1.3)

```
┌──────────────────────────────────────────────────────────────┐
│ Views                                                         │
│  MainView.xaml     [burn rate banner added to 5h section]    │
│  SettingsView.xaml [full XAML rewrite — Segmented Control]   │
└───────────────────────────┬──────────────────────────────────┘
                            │ x:Bind (compiled bindings)
┌───────────────────────────▼──────────────────────────────────┐
│ ViewModels                                                    │
│  MainViewModel   [+BurnRatePrediction, +IsBurnRateWarning-   │
│                   Visible, +BurnRateWarningText,             │
│                   +_burnRateNotificationService field]       │
│  SettingsViewModel [RefreshOptions labels, +SelectedTab-      │
│                     Index, +AppVersion]                      │
└────────────┬──────────────────────────┬──────────────────────┘
             │ service calls            │ static class calls
┌────────────▼────────────┐ ┌───────────▼──────────────────────┐
│ Services                │ │ Helpers                           │
│  BurnRateNotification-  │ │  BurnRateCalculator (NEW)         │
│  Service (NEW)          │ │    input: history, utilization,   │
│    AppNotificationMgr   │ │           resetsAt                │
│    one-shot guard       │ │    output: BurnRatePrediction?    │
│  [all existing services │ │  ChartColors (MODIFIED)           │
│   unchanged]            │ │    +BuildColorLookup(isDark)      │
└────────────┬────────────┘ │  ChartRenderer (MODIFIED)        │
             │              │    +BuildGradientStops(...)       │
┌────────────▼────────────┐ │  ChartDrawing (MODIFIED)         │
│ Models                  │ │    DrawChartFills → gradient      │
│  BurnRatePrediction     │ │    DrawChartTopLine → gradient    │
│  (NEW)                  │ │  ExportHelper (VERIFY ONLY)       │
│  [all existing models   │ └──────────────────────────────────┘
│   unchanged]            │
└────────────┬────────────┘
             │
┌────────────▼────────────┐
│ DI Container (App.xaml) │
│  +IBurnRateNotification │
│   Service singleton      │
│  MainViewModel ctor     │
│   gains 1 new param     │
└─────────────────────────┘
```

---

## Data Flow Diagrams

### Burn Rate Warning: Full Cycle

```
[Poll timer tick, every N seconds]
    ↓
PollUsageAsync() → FetchUsageAsync() → UsageResponse
    ↓
UpdateUsageProperties(data)
    ├── [existing] Update FiveHourUtilization, etc.
    ├── [existing] AppendHistoryPoint() → UsageHistoryPoints
    └── [NEW] BurnRateCalculator.Predict(
                 UsageHistoryPoints,          // history: normalized 0.0-1.0
                 data.FiveHour.Utilization,   // current: 0-100 API scale
                 data.FiveHour.ResetsAt)
             → BurnRatePrediction? prediction
                 ↓
             BurnRatePrediction = prediction
                 ↓ (property change notification)
             IsBurnRateWarningVisible + BurnRateWarningText updated
                 ↓ (x:Bind OneWay in MainView)
             Banner Border.Visibility toggled
                 ↓
             _burnRateNotificationService.CheckBurnRate(prediction)
                 ↓ (if first time in this warning cycle)
             AppNotificationManager.Default.Show(toast)
```

### Chart Gradient: Render Cycle

```
[ChartInvalidateMessage received or CanvasAnimatedControl tick]
    ↓
Win2D Draw event handler (MainView.xaml.cs)
    ↓
ChartDrawing.DrawChartFills(session, resourceCreator, points, ...)
    ├── ChartColors.BuildColorLookup(isDark) → Color[101]
    ├── ChartRenderer.BuildGradientStops(points, windowStart, plotWidth, colorLookup)
    │       → List<(float position, Color color)>
    ├── Build closed area path (identical geometry to v1.2, path construction unchanged)
    ├── CanvasLinearGradientBrush(resourceCreator, stops) { StartPoint=leftX, EndPoint=rightX }
    │   → set alpha=64 on all stop colors for fill
    └── session.FillGeometry(geometry, brush)  [REPLACES: session.FillGeometry(geometry, solidColor)]

ChartDrawing.DrawChartTopLine(session, resourceCreator, points, ...)
    ├── same BuildColorLookup + BuildGradientStops
    ├── Build open line path (identical geometry to v1.2)
    ├── CanvasLinearGradientBrush at alpha=255 (full opacity)
    └── session.DrawGeometry(geometry, brush, 2.0f)
```

### Settings Tab Switching: No New Messages

```
[User clicks Segmented Control tab]
    ↓
controls:Segmented.SelectedIndex changes
    ↓ (TwoWay binding)
SettingsViewModel.SelectedTabIndex [ObservableProperty]
    ↓ (OneWay binding in XAML)
Tab content panel Visibility toggled (Converter or code-behind)
    [NO service calls, NO messenger messages, NO persistence]
```

---

## Suggested Build Order

Dependency-driven. Each phase is an independent track; Phase 1 is highest value and must be first given spec priority.

### Step 1 — Burn Rate Warning end-to-end (Phase 1)

**New files:**
- `Models/BurnRatePrediction.cs`
- `Helpers/BurnRateCalculator.cs`
- `Services/Interfaces/IBurnRateNotificationService.cs`
- `Services/BurnRateNotificationService.cs`

**Modified files:**
- `ViewModels/MainViewModel.cs` — inject service, add observable properties, add prediction call
- `Views/MainView.xaml` — add burn rate banner Border in 5h section
- `App.xaml.cs` — register service, inject into MainViewModel, register AppNotificationManager
- `App.xaml` — add `BurnRateWarningBrush`, `BurnRateWarningTextBrush`
- `Strings/de-DE/Resources.resw` + `Strings/en-US/Resources.resw` — add burn rate string keys

Build order within Phase 1:
1. `BurnRatePrediction` model (no dependencies)
2. `BurnRateCalculator` static helper (depends only on `BurnRatePrediction` + `UsageHistoryPoint`)
3. `IBurnRateNotificationService` interface + `BurnRateNotificationService` implementation
4. `MainViewModel` integration (depends on all above)
5. `MainView.xaml` banner (depends on MainViewModel properties)
6. `App.xaml.cs` DI + `App.xaml` resources
7. Localization strings

### Step 2 — Chart Horizontal Gradient (Phase 2)

**Modified files only:**
- `Helpers/ChartColors.cs` — add `BuildColorLookup(bool isDark)`
- `Helpers/ChartRenderer.cs` — add `BuildGradientStops()`
- `Helpers/ChartDrawing.cs` — rewrite `DrawChartFills()` and `DrawChartTopLine()`
- `Helpers/ExportHelper.cs` — verify no changes needed (no expected modification)

Build order within Phase 2:
1. `ChartColors.BuildColorLookup()` (no dependencies on other changes)
2. `ChartRenderer.BuildGradientStops()` (depends on ColorLookup being callable)
3. `ChartDrawing` rewrite (depends on both above)
4. `ExportHelper` verification (no code change, just test export)

This phase is self-contained. Zero impact on any ViewModel, service, or model.

### Step 3 — Settings View Redesign (Phase 3)

**Modified files:**
- `Views/SettingsView.xaml` — full XAML rewrite
- `ViewModels/SettingsViewModel.cs` — add `SelectedTabIndex`, `AppVersion`, update label strings
- `Strings/de-DE/Resources.resw` + `Strings/en-US/Resources.resw` — add settings tab label keys

Build order within Phase 3:
1. `SettingsViewModel` additions first (so XAML bindings compile)
2. `SettingsView.xaml` rewrite
3. Localization strings (can be parallel with XAML work)

This phase has zero impact on `MainViewModel`, services, or chart helpers.

### Step 4 — Session Watcher Verification (Phase 4)

**No code changes.** Write a test script or manual test plan to verify session name updates when switching Claude Code projects. Document the confirmed-correct `NotifyFilter` configuration in the phase plan.

---

## Anti-Patterns to Avoid

### Anti-Pattern 1: Calling `BurnRateCalculator.Predict()` in `AppendHistoryPoint()` instead of `UpdateUsageProperties()`

**What goes wrong:** `AppendHistoryPoint()` runs inside the same method chain but after updating `_fiveHourResetsAt`. If called there, the prediction result is computed before the UI properties are fully updated. More critically, `AppendHistoryPoint()` is also called from the initial cache load path (`cached != null` branch in `InitializeAsync()`), which would fire a burn rate notification on app startup from stale cached data.

**Do this instead:** Call `BurnRateCalculator.Predict()` and `CheckBurnRate()` at the end of `UpdateUsageProperties()`, after `AppendHistoryPoint()` has completed and only in the live poll path.

### Anti-Pattern 2: Storing `BurnRatePrediction` history or persisting it to disk

**What goes wrong:** The prediction is a derived, ephemeral value from live data. Persisting it creates stale state that would show a false warning after app restart (e.g., a prediction from hours ago that no longer applies).

**Do this instead:** Compute prediction fresh on every poll cycle. The `BurnRatePrediction` observable property on MainViewModel is the only storage. It clears on the next poll cycle if the prediction no longer applies.

### Anti-Pattern 3: Creating `CanvasLinearGradientBrush` outside the Win2D draw session

**What goes wrong:** `CanvasLinearGradientBrush` is a Win2D graphics resource tied to the `ICanvasResourceCreator` (the `CanvasDrawingSession`). Creating it outside the draw handler and caching it as a field causes a `COMException` when the device is reset (e.g., on display mode change) or a resource leak if `Dispose()` is not called.

**Do this instead:** Create and dispose the `CanvasLinearGradientBrush` within each draw call using `using var brush = new CanvasLinearGradientBrush(...)`. Win2D resources created in the draw session are cheap — there is no meaningful performance benefit to caching them across frames.

### Anti-Pattern 4: Using `AppNotificationManager` before `Register()` is called

**What goes wrong:** On WinUI 3 unpackaged apps, calling `AppNotificationManager.Default.Show()` before `AppNotificationManager.Default.Register()` throws or silently fails. The registration must happen at app startup.

**Do this instead:** Call `AppNotificationManager.Default.Register()` in `App.OnLaunched()`, before routing to MainView or LoginView. No activation handler is needed for this use case (notifications are fire-and-forget, no action buttons that launch the app).

### Anti-Pattern 5: Putting business logic in SettingsView code-behind for tab switching

**What goes wrong:** Adding `OnSegmentedSelectionChanged()` in `SettingsView.xaml.cs` with tab-switching logic that depends on settings service calls or cross-component state. This embeds presentation logic that is hard to test and violates the no-code-behind-logic rule.

**Do this instead:** Tab content visibility is purely a presentation concern — it is acceptable to handle it in code-behind as a single `switch` on `SelectedTabIndex` that sets `Visibility` on four panels. Alternatively, use an integer-to-visibility converter in XAML. No service calls, no messenger messages, no state changes beyond the tab index.

### Anti-Pattern 6: Not handling the utilization scale mismatch in `BurnRateCalculator`

**What goes wrong:** `UsageHistoryPoint.Utilization` is `0.0–1.0` (normalized). `UsageWindow.Utilization` (from the API via `data.FiveHour.Utilization`) is `0–100`. Using them interchangeably without conversion produces a regression slope off by a factor of 100, resulting in either no predictions ever firing (slope too small, projects exhaustion far in future) or constant false alarms.

**Do this instead:** Define the calculator's internal scale explicitly. The spec algorithm uses 0–100 scale. Convert history point utilization to 0–100 by multiplying by 100 inside `BurnRateCalculator.Predict()` before computing regression. Document the expected scale in the method signature.

---

## Scalability Considerations

Single-user desktop app. No distributed concerns. Relevant performance notes:

| Concern | v1.3 Impact | Mitigation |
|---------|-------------|------------|
| `CanvasLinearGradientBrush` creation per draw frame | Created/disposed on every Win2D draw event | Acceptable — Win2D optimizes resource creation; profile only if frame rate drops below 30fps |
| `BuildColorLookup()` called on every draw | Returns `Color[101]`, all heap allocation | Cache per `isDark` value as a static field in `ChartColors`; only recompute on theme change |
| `BurnRateCalculator.Predict()` on every poll | O(n) over last 15 min of history points; ~15-30 points at 30s interval | Negligible — 30 iterations of arithmetic on the UI thread |
| `AppNotificationManager.Show()` | Async OS call | Already guarded by `_notifiedBurnRate` flag — fires at most once per warning cycle |

---

## Retained: Existing Architecture (v1.0–v1.2)

The stable architectural foundation that v1.3 builds on. No changes to these fundamentals.

### Component Responsibilities

| Component | Responsibility | Communicates With |
|-----------|----------------|-------------------|
| **MainView + MainViewModel** | Primary dashboard: 5h chart, weekly usage, context window, session picker, token stats | ClaudeApiService, JsonlService, PricingService, NavigationService, BurnRateNotificationService (new) |
| **LoginView + LoginViewModel** | WebView2 login flow, cookie extraction | CredentialService, NavigationService |
| **SettingsView + SettingsViewModel** | App preferences (refresh interval, theme, language, autostart, sonnet context) | SettingsService, NavigationService |
| **ClaudeApiService** | HTTP polling via WebView2 bridge, bypasses Cloudflare | CredentialService |
| **JsonlService** | JSONL parsing, session index, FileSystemWatcher | PricingService, ISettingsService |
| **BurnRateNotificationService** | Toast notification one-shot guard | AppNotificationManager (Windows App SDK) |
| **PricingService** | LiteLLM pricing fetch, cache, tiered calculation | Local cache, GitHub |
| **CredentialService** | Win32 CredRead/CredWrite for session token | Windows Credential Manager |
| **SettingsService** | Read/write settings.json in %LOCALAPPDATA% | Local filesystem |
| **UpdateService** | GitHub Releases version check | GitHub API |
| **NavigationService** | Frame-based page navigation | All Views |

### Key Architectural Patterns

**DI-Based MVVM:** All services registered as singletons. Source generators eliminate boilerplate. `BurnRateNotificationService` follows the same singleton registration pattern as all other services.

**WeakReferenceMessenger:** Cross-ViewModel communication via typed messages. v1.3 adds no new messages — burn rate state is owned exclusively by `MainViewModel` with no cross-ViewModel sharing needed.

**Timer-Driven Polling:** Burn rate prediction piggybacks on the existing poll cycle — no new timer, no new async background work.

**DispatcherQueue.TryEnqueue():** Still mandatory for any property updates from background threads. The `BurnRateNotificationService` call originates from the poll timer tick, which runs on the `DispatcherQueue` (UI thread already). No additional marshaling needed for the banner update. Toast notification call is a fire-and-forget OS API call, safe from any thread.

---

## Sources

- `spec/v1.10.0-macOS/spec-release-1.8.3-to-1.10.0.md` — Authoritative implementation spec (HIGH confidence, authored for this milestone)
- `CCInfoWindows/CCInfoWindows/ViewModels/MainViewModel.cs` — `PollUsageAsync()`, `UpdateUsageProperties()`, `AppendHistoryPoint()`, full field inventory — direct code read
- `CCInfoWindows/CCInfoWindows/Helpers/ChartDrawing.cs` — `DrawChartFills()`, `DrawChartTopLine()`, `DrawGlowIndicator()` — direct code read
- `CCInfoWindows/CCInfoWindows/Helpers/ChartRenderer.cs` — `GetZoneSegments()`, `ToX()`, `ToY()`, `GetRightEdgeAbsoluteX()` — direct code read
- `CCInfoWindows/CCInfoWindows/Helpers/ChartColors.cs` — color table structure, `GetZoneColor()` — direct code read
- `CCInfoWindows/CCInfoWindows/Models/UsageHistory.cs` — `UsageHistoryPoint.Utilization` scale (0.0–1.0 normalized) — direct code read
- `CCInfoWindows/CCInfoWindows/ViewModels/SettingsViewModel.cs` — current `RefreshOptions` labels, `Initialize()` pattern — direct code read
- `CCInfoWindows/CCInfoWindows/Views/SettingsView.xaml` — current flat StackPanel structure — direct code read
- `CCInfoWindows/CCInfoWindows/Services/JsonlService.cs` — `StartWatching()` NotifyFilter config — direct code read (lines 812–818)
- `CCInfoWindows/CCInfoWindows/App.xaml.cs` — DI registration pattern — direct code read
