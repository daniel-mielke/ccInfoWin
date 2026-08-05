# Stack Research

**Domain:** WinUI 3 Desktop App — v1.3 macOS v1.10.0 Feature Parity
**Researched:** 2026-04-13
**Confidence:** HIGH

## Executive Answer

**Zero new NuGet packages required.** All four v1.3 features are implementable with the existing dependency graph. The `.csproj` does not change. Every capability needed — `AppNotificationManager`, `CanvasLinearGradientBrush`, `CommunityToolkit.WinUI.Controls.Segmented`, and `FileSystemWatcher` — is already present.

---

## Existing Stack — No Changes Required

| Technology | Current Version | v1.3 Capability Used |
|------------|----------------|----------------------|
| Microsoft.WindowsAppSDK | 1.8.260209005 | `AppNotificationManager` (burn rate toast) — bundled, no extra NuGet |
| Microsoft.Graphics.Win2D | 1.3.2 | `CanvasLinearGradientBrush` + `CanvasGradientStop` (chart gradient) |
| CommunityToolkit.WinUI.Controls.Segmented | 8.2.251219 | Settings tab navigation (already installed) |
| System.IO (BCL) | .NET 9 | `FileSystemWatcher` `NotifyFilter` verification |
| CommunityToolkit.Mvvm | 8.4.0 | `[ObservableProperty]` for `BurnRatePrediction`, banner visibility |
| WinUI3Localizer | 2.3.0 | New resw keys for burn rate strings and settings tab labels |

---

## Feature-by-Feature Stack Analysis

### FEAT-01: Burn Rate Warning System

#### Prediction Engine (FEAT-01a)

Pure C# static class. Least-squares linear regression over `IReadOnlyList<UsageHistoryPoint>`.

| Capability | Source | Already Available? |
|-----------|--------|--------------------|
| LINQ for history filtering | `System.Linq` (BCL) | Yes |
| `DateTimeOffset` arithmetic | BCL | Yes |
| Math for regression (`Σxy`, slope) | `System.Math` (BCL) | Yes |
| `BurnRatePrediction` model class | New C# record | New file, no library |

**Utilization scale note:** `UsageHistoryPoint.Utilization` is stored as 0.0–1.0 in ccInfoWin. The algorithm spec operates on 0–100 scale. The calculator must multiply by 100 when reading from history points. `UsageWindow.Utilization` from the API is already 0–100 — no conversion there.

#### Toast Notification (FEAT-01c)

**Technology:** `Microsoft.Windows.AppNotifications.AppNotificationManager` + `AppNotificationBuilder`

**Already bundled** in `Microsoft.WindowsAppSDK 1.8.260209005`. Namespace: `Microsoft.Windows.AppNotifications`.

**Registration requirement for unpackaged apps:** ccInfoWin uses `WindowsPackageType=None` (unpackaged). This means `App.xaml.cs` must call `AppNotificationManager.Default.Register()` at startup. The order is:

1. Wire `AppNotificationManager.Default.NotificationInvoked` handler (even if empty — fire-and-forget only)
2. Call `AppNotificationManager.Default.Register()`
3. Call `AppNotificationManager.Default.Unregister()` on app exit

For ccInfoWin burn rate, the `NotificationInvoked` handler can be a no-op (no action needed when user clicks the toast). Registration must happen before the main window activates.

**Sending a toast:**

```csharp
// using Microsoft.Windows.AppNotifications;
// using Microsoft.Windows.AppNotifications.Builder;

var notification = new AppNotificationBuilder()
    .AddText("Burn rate warning")
    .AddText("At current pace, token limit reached in ~33min.")
    .BuildNotification();

notification.Tag = "usage-burnrate";  // deduplication key
AppNotificationManager.Default.Show(notification);
```

**Tag-based deduplication:** Setting `notification.Tag = "usage-burnrate"` means the Action Center replaces the previous notification with the same tag rather than stacking. Combined with the in-memory `_notifiedBurnRate` flag, this prevents both repeated toasts and Action Center spam.

#### Warning Banner (FEAT-01b)

Pure XAML + ViewModel bindings. `Visibility="{x:Bind ViewModel.IsBurnRateWarningVisible, Mode=OneWay}"` on a `Border`. Flame icon via Segoe Fluent Icons glyph `\uECAD`. No new technology.

---

### FEAT-02: Chart Horizontal Gradient

**Technology:** `CanvasLinearGradientBrush` from `Microsoft.Graphics.Win2D 1.3.2`

Already used in the codebase via `Microsoft.Graphics.Canvas.Brushes` namespace (imported in `ChartDrawing.cs`).

**Key types:**

```csharp
// Already imported: using Microsoft.Graphics.Canvas.Brushes;
// CanvasGradientStop struct: two fields
//   float Position  — 0.0 to 1.0 normalized position along the gradient
//   Color  Color    — Windows.UI.Color at this stop

var stops = new CanvasGradientStop[]
{
    new() { Position = 0.0f, Color = Color.FromArgb(255, 0x30, 0xD1, 0x58) },  // green at data start
    new() { Position = 0.5f, Color = Color.FromArgb(255, 0xFF, 0xD6, 0x0A) },  // yellow at 50%
    new() { Position = 0.8f, Color = Color.FromArgb(255, 0xFF, 0x9F, 0x0A) },  // orange at 75%
    new() { Position = 1.0f, Color = Color.FromArgb(255, 0xFF, 0x45, 0x3A) },  // red at 90%+
};

// Constructor: CanvasLinearGradientBrush(ICanvasResourceCreator, CanvasGradientStop[])
using var brush = new CanvasLinearGradientBrush(resourceCreator, stops)
{
    StartPoint = new Vector2(startX, 0),
    EndPoint   = new Vector2(endX,   0)   // horizontal gradient — same Y
};
```

**Gradient stop positions in FEAT-02b** are computed from the actual data span (leftmost point X to rightmost point X), not the full chart width. The `CanvasLinearGradientBrush.StartPoint`/`EndPoint` define the brush's coordinate space — these must match the actual data span boundaries.

**Alpha for area fill:** `Color.FromArgb(64, r, g, b)` gives ~25% opacity. Apply to each stop's Color alpha channel when creating the fill brush. The line stroke brush uses full alpha (255).

**Existing architecture impact:** `ChartDrawing.DrawChartFills()` and `DrawChartTopLine()` currently iterate zone segments via `ChartRenderer.GetZoneSegments()`. For FEAT-02, replace zone-segment iteration with a single continuous path per gap-free span. `ChartRenderer.GetZoneSegments()` becomes obsolete for the main draw path (can be removed or kept for the glow indicator which still uses zone color logic via `ChartColors.GetZoneColor()`).

**IDisposable:** `CanvasLinearGradientBrush` implements `IDisposable`. Use `using var brush = ...` inside each draw call, same as the existing `CanvasPathBuilder` pattern in `ChartDrawing.cs`.

---

### FEAT-03: Settings View Redesign

**Technology:** `CommunityToolkit.WinUI.Controls.Segmented` v8.2.251219 — already installed.

No additional packages needed. The `Segmented` control is the only new UI component. Tab content switching via `Visibility` bindings on `StackPanel`/`Border` containers driven by the selected tab index.

**Colored icon badge pattern for `SegmentedItem`:** Inline `DataTemplate` with a colored `Border` (18×18px, CornerRadius=4) containing a white `FontIcon` (10px). This is pure XAML, no code.

**Short time notation** (`30s`, `1min`, `5min`) requires updating `SettingsViewModel` option lists — string changes only, no API or library changes.

**New theme brushes** (`BurnRateWarningBrush`, `SettingsBadge*Brush`) added to `App.xaml` resource dictionary — standard WinUI 3 `ResourceDictionary`, no new technology.

---

### FEAT-05: Session Dropdown Fix (FileSystemWatcher)

**Finding from direct codebase inspection:** `JsonlService.cs` `StartWatching()` already configures the `FileSystemWatcher` correctly:

```csharp
var watcher = new FileSystemWatcher(_projectsDirectory)
{
    Filter = JsonlFilePattern,          // "*.jsonl"
    IncludeSubdirectories = true,       // catches session files in project subdirs
    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
    InternalBufferSize = WatcherInternalBufferSize  // 64 KB
};
```

`NotifyFilters.LastWrite | NotifyFilters.FileName` already covers both file content changes and file creation/deletion. `IncludeSubdirectories = true` catches files in project subdirectories. **This feature is a verification task only.** No code changes are expected unless manual testing reveals a gap.

---

## What NOT to Add

| Avoid | Why | Evidence |
|-------|-----|---------|
| `Microsoft.Toolkit.Uwp.Notifications` / `ToastNotificationManager` | Legacy UWP notification API; incompatible with unpackaged WinUI 3 | Replaced by `AppNotificationManager` in Windows App SDK |
| `H.NotifyIcon.WinUI` | System tray icon library — deferred to future milestone (FEAT-01d deferred per spec) | Not needed for v1.3 burn rate warning |
| XAML `LinearGradientBrush` | XAML brush — incompatible with Win2D `CanvasDrawingSession`; different rendering pipeline | Use `CanvasLinearGradientBrush` (Win2D) for chart rendering |
| Additional `CanvasEdgeBehavior` / `CanvasAlphaMode` constructor overloads | Default constructor `(ICanvasResourceCreator, CanvasGradientStop[])` is sufficient | Gradient clamping at endpoints (default `Clamp`) is the correct behavior for a data chart |
| Reactive extensions (Rx.NET) | Overkill; burn rate prediction runs on the existing polling cycle, no new reactive streams needed | Existing `DispatcherQueue.TryEnqueue()` + `[ObservableProperty]` is sufficient |

---

## Integration Points Requiring Attention

| Point | Risk | Mitigation |
|-------|------|-----------|
| `AppNotificationManager.Default.Register()` in `App.xaml.cs` | Must be called before window activation; `NotificationInvoked` handler must be wired first | Add to `OnLaunched()` before `_window.Activate()`, unregister in `OnProcessExit` or `App` destructor |
| `CanvasLinearGradientBrush` must be disposed per draw call | Win2D requires `IDisposable` cleanup; brush lifetime is per-frame | Use `using var` inside `DrawChartFills()` and `DrawChartTopLine()` |
| `BurnRatePrediction` nullable observable property | Banner `Visibility` must be derived from null-check, not `bool` flag | Use `IsBurnRateWarningVisible => BurnRatePrediction is not null` computed property, or dedicated bool updated alongside the prediction |
| Utilization scale mismatch (0.0–1.0 stored vs 0–100 algorithm) | Wrong slope calculation if scale not converted | `BurnRateCalculator.Predict()` multiplies `point.Utilization * 100` for regression; `currentUtilization` parameter is already 0–100 from API |
| `GetZoneSegments()` in `ChartRenderer` | Still needed by `DrawGlowIndicator` (zone color lookup) — do not remove | Keep method; only `DrawChartFills` and `DrawChartTopLine` replace its usage |

---

## Sources

- Direct codebase inspection: `CCInfoWindows/CCInfoWindows/CCInfoWindows.csproj` — confirmed installed packages — HIGH
- Direct codebase inspection: `CCInfoWindows/CCInfoWindows/Helpers/ChartDrawing.cs` — confirmed `CanvasLinearGradientBrush` namespace already imported — HIGH
- Direct codebase inspection: `CCInfoWindows/CCInfoWindows/Services/JsonlService.cs` `StartWatching()` — FileSystemWatcher config confirmed correct — HIGH
- Official Win2D docs: https://microsoft.github.io/Win2D/WinUI3/html/T_Microsoft_Graphics_Canvas_Brushes_CanvasLinearGradientBrush.htm — `CanvasLinearGradientBrush` constructor signatures — HIGH
- Official Win2D docs: https://microsoft.github.io/Win2D/WinUI3/html/T_Microsoft_Graphics_Canvas_Brushes_CanvasGradientStop.htm — `CanvasGradientStop` fields (`Position`, `Color`) — HIGH
- Microsoft Learn: https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/notifications/app-notifications/app-notifications-quickstart — `AppNotificationManager` unpackaged registration pattern — HIGH

---
*Stack research for: ccInfoWin v1.3 macOS v1.10.0 feature parity*
*Researched: 2026-04-13*
