# Pitfalls Research

**Domain:** WinUI 3 desktop app — adding burn rate prediction (linear regression), Win2D gradient rendering, Segmented Control settings redesign, AppNotificationManager toasts, FileSystemWatcher verification to an existing MVVM app (ccInfoWin v1.3)
**Researched:** 2026-04-13
**Confidence:** HIGH — all findings derived from direct codebase analysis + official documentation (AppNotificationManager quickstart, Win2D docs, CommunityToolkit Segmented docs)

---

## Critical Pitfalls

### Pitfall 1: Linear Regression Denominator Produces NaN — Passes Guards Silently

**What goes wrong:**
`BurnRateCalculator.Predict()` computes `slope = (n·Σxy − Σx·Σy) / (n·Σx² − (Σx)²)`. When all data points share the same timestamp — or when exactly three points all occur at startup in the same debounce window — the denominator `(n·Σx² − (Σx)²)` is exactly zero. C# `double` division returns `Double.NaN`. The spec's `slope > 0` guard silently returns `false` for NaN (correct outcome), but `(100 - currentUtilization) / NaN` also evaluates to NaN. If the NaN escapes into `MinutesUntilLimit`, the `Math.Max(1, ...)` clamp evaluates to `1`, and the banner shows a false "~1min" warning at app start.

**Why it happens:**
The minimum-data-points guard (≥ 3 points) does not prevent this. Three points are sufficient to pass the guard even if two share identical timestamps — common during cache replay at startup or rapid successive API responses. The algorithm assumes strictly increasing x-values.

**How to avoid:**
Add an explicit denominator guard immediately before the slope calculation, and assert finiteness before constructing the return value:

```csharp
var ssX = (n * sumX2) - (sumX * sumX);
if (ssX <= 0.0) return null;
var slope = ((n * sumXY) - (sumX * sumY)) / ssX;
if (!double.IsFinite(slope) || slope <= 0.0) return null;
var secondsToLimit = (MaxUtilization - currentUtilization) / slope;
if (!double.IsFinite(secondsToLimit) || secondsToLimit <= 0) return null;
```

**Warning signs:**
- Unit test for "flat usage / slope ≈ 0" crashes instead of returning null
- Banner shows "~1min" immediately after login at low utilization
- `MinutesUntilLimit` is 1 at startup even without any burn rate data

**Phase to address:** Phase 1 (Burn Rate Warning). Write all nine unit test cases from the spec before wiring to `MainViewModel`, including the degenerate case of three identical timestamps.

---

### Pitfall 2: Utilization Scale Mismatch — BurnRateCalculator Always Returns Null

**What goes wrong:**
`UsageHistoryPoint.Utilization` is stored as **0.0–1.0** (confirmed in `UsageHistory.cs` with comment "Utilization is stored as 0.0-1.0 (normalized), not the API's 0-100"). The spec's algorithm uses the **0–100 scale** throughout. If the calculator receives raw history points without converting, the minimum-utilization guard of `20.0` is never satisfied (actual value: `0.20`). The calculator always returns null. The burn rate banner never appears, and the feature appears to work (no crashes) but is completely non-functional.

**Why it happens:**
The spec explicitly calls this out under FEAT-01a: "The utilization in `UsageHistoryPoint` is stored normalized (0.0–1.0) in ccInfoWin. The calculator must convert to the 0–100 scale used by the algorithm." Developers porting from the macOS reference (which stores 0–100) miss this step because the algorithm's constants (20.0, 100.0) look plausible at face value.

**How to avoid:**
In `BurnRateCalculator.Predict()`, multiply each history point's utilization by 100.0 when building the y-values:

```csharp
var y = point.Utilization * 100.0; // stored 0.0-1.0 → algorithm 0-100 scale
```

The `currentUtilization` parameter comes from `UsageWindow.Utilization` which is already 0–100 from the API — do not convert it again.

**Warning signs:**
- All unit tests for "fast burn" scenarios return null when they should return a prediction
- Debug logging shows the MinimumUtilization guard firing at values like `0.55` instead of `55`
- Banner never appears even during a session that consumed tokens rapidly

**Phase to address:** Phase 1 (Burn Rate Warning). Write a unit test that directly asserts: given a point with `Utilization = 0.55`, the slope calculation uses `y = 55.0`, not `y = 0.55`.

---

### Pitfall 3: AppNotificationManager Ordering — NotificationInvoked Must Precede Register()

**What goes wrong:**
For unpackaged apps, `AppNotificationManager.Default.Register()` registers the current process as the COM server for notification callbacks. If a notification is clicked while the app is running and `NotificationInvoked` has not been subscribed yet, Windows launches a **new process instance** to handle the activation instead of routing to the running instance. The user sees a second app window open.

**Why it happens:**
The official Microsoft documentation states: "To ensure all Notification handling happens in this process instance, register for `NotificationInvoked` before calling `Register()`." The natural order for a developer reading the spec is to call `Register()` first in `App.xaml.cs` startup. The spec for FEAT-01c says to register at startup but does not explicitly repeat this ordering constraint.

**How to avoid:**
Always subscribe before registering in `App.xaml.cs` or the notification service init method:

```csharp
AppNotificationManager.Default.NotificationInvoked += OnNotificationInvoked; // FIRST
AppNotificationManager.Default.Register();                                    // SECOND
```

Also call `AppNotificationManager.Default.Unregister()` in process exit. The existing `OnUnhandledException` handler in `App.xaml.cs` logs to crash.log and calls `e.Handled = true` — add a `ProcessExit` hook alongside it:

```csharp
AppDomain.CurrentDomain.ProcessExit += (_, _) => AppNotificationManager.Default.Unregister();
```

**Warning signs:**
- Clicking the toast opens a second app window instead of focusing the running instance
- No second window but notification activation is silently dropped

**Phase to address:** Phase 1 (Burn Rate Warning). Test: send toast, click it while app is running — confirm no second window appears.

---

### Pitfall 4: AppNotificationManager.Register() Throws E_FAIL on Double Registration

**What goes wrong:**
`AppNotificationManager.Default.Register()` throws `COMException` (E_FAIL, HRESULT 0x80004005) if called more than once per process lifetime. This happens if `BurnRateNotificationService` calls `Register()` in its constructor AND `App.xaml.cs` calls it at startup, or if the service is registered as `AddTransient` in DI and each resolution triggers a new `Register()` call.

**Why it happens:**
Registration is process-scoped and not idempotent. Calling it twice is a hard error, not a no-op. The existing `WebViewBridge` pattern in `App.xaml.cs` (registered as `AddSingleton`) avoids this problem — the notification service must follow the same pattern.

**How to avoid:**
- Register `BurnRateNotificationService` as `AddSingleton` in `ConfigureServices()`.
- Call `AppNotificationManager.Default.Register()` exactly once, from `App.xaml.cs` `OnLaunched`, not from the service constructor.
- The service's responsibility is to call `Show()` (send notifications). It does not own the `Register()`/`Unregister()` lifecycle.

**Warning signs:**
- App crashes on first launch after adding the notification service; crash.log shows `COMException`
- Works on the first run, fails on subsequent launches because the COM server slot was not cleaned up (missing `Unregister()`)

**Phase to address:** Phase 1 (Burn Rate Warning). Follow the `WebViewBridge` DI pattern exactly.

---

### Pitfall 5: CanvasLinearGradientBrush Created Without `using` — GPU Resource Leak

**What goes wrong:**
`CanvasLinearGradientBrush` implements `IDisposable` and holds an unmanaged GPU resource. If brushes are created inside `DrawChartFills()` or `DrawChartTopLine()` per span without `using`, each chart redraw leaks one or more GPU objects. At a 30-second poll interval with two spans (area fill + line stroke), this is 4 leaked objects per minute. Memory grows monotonically; after extended use the app can exhaust GPU resources or trigger a Win2D device-lost exception.

**Why it happens:**
Developers used to plain .NET objects forget that Win2D brushes are COM-backed resources. The existing `ChartDrawing.cs` already wraps `CanvasPathBuilder` and `CanvasGeometry` in `using` — this discipline must extend to brushes. The gradient implementation adds one `CanvasLinearGradientBrush` per gap-free span (area path) plus one for the line stroke, for a minimum of two per draw call.

**How to avoid:**
Strictly follow the `using` pattern already established in the same file:

```csharp
using var gradientBrush = new CanvasLinearGradientBrush(resourceCreator, gradientStops)
{
    StartPoint = new System.Numerics.Vector2(spanStartX, 0),
    EndPoint   = new System.Numerics.Vector2(spanEndX, 0)
};
session.FillGeometry(geometry, gradientBrush);
// brush disposed here — same as CanvasPathBuilder and CanvasGeometry
```

**Warning signs:**
- Memory usage grows visibly in Task Manager over 10+ minutes of active polling
- Win2D device-lost exception after extended run (especially on lower-end GPUs)
- Export path creates brushes in a loop without `using` — the export is directly testable

**Phase to address:** Phase 2 (Chart Gradient). Code review hard requirement: every `new CanvasLinearGradientBrush(...)` must have a `using` keyword. Verify in both `ChartDrawing.cs` and `ExportHelper.cs` call paths.

---

### Pitfall 6: Zero-Width Gradient Span Produces Invisible Stroke

**What goes wrong:**
`CanvasLinearGradientBrush` requires `StartPoint != EndPoint`. If a gap-free span contains only one data point, or if all points map to the same X coordinate (app just launched, one history entry), `spanStartX == spanEndX`. Win2D does not throw — it silently applies the first gradient stop color to all pixels, but with a zero-width linear gradient the result on the area fill is invisible (no extent to interpolate across). The chart appears blank on first launch after login.

**Why it happens:**
The existing zone-based code handles single-point segments via `GetRightEdgeAbsoluteX()` which synthesizes a right edge at the current-time X position. The new gradient path must replicate this protection. The spec says "add boundary stops at the start and end of the data range" — this does not guard against the StartPoint == EndPoint case.

**How to avoid:**
Before creating any `CanvasLinearGradientBrush`, check span width:

```csharp
if (Math.Abs(spanEndX - spanStartX) < 1.0f)
{
    // Fall back to solid color from first gradient stop
    var solidColor = gradientStops.First().Color;
    session.FillGeometry(geometry, solidColor);
    continue;
}
```

**Warning signs:**
- Chart renders blank area immediately after login (one history point from the first poll)
- Export PNG shows no fill on the first generated image after session start

**Phase to address:** Phase 2 (Chart Gradient). Test with zero history points (empty state) and one history point (first poll after login).

---

### Pitfall 7: Segmented Tab Content Switching with x:Bind Inline Expressions Fails to Compile

**What goes wrong:**
The spec uses `Visibility` bindings to show/hide tab content panels. If implemented as inline `x:Bind` equality expressions like `{x:Bind ViewModel.SelectedTabIndex == 0}`, the XAML compiler rejects the expression — WinUI 3 `x:Bind` does not support comparison operators in path expressions. The build fails with a cryptic error, not a runtime exception.

**Why it happens:**
Developers familiar with WPF `DataTrigger` or Uno Platform patterns try to write conditional visibility inline. WinUI 3 `x:Bind` only supports property paths, method calls with known signatures, and ternary-via-converter. The comparison operator is not supported.

**How to avoid:**
Use `SwitchPresenter` + `Case` from `CommunityToolkit.WinUI.Controls` (already a dependency). Tag each `SegmentedItem` and bind `SwitchPresenter.Value` to the selected item's tag — zero code-behind needed:

```xml
<controls:Segmented x:Name="Tabs" SelectedIndex="0">
    <controls:SegmentedItem Content="General" Tag="general" />
    <controls:SegmentedItem Content="Updates" Tag="updates" />
    ...
</controls:Segmented>
<controls:SwitchPresenter Value="{Binding SelectedItem.Tag, ElementName=Tabs}">
    <controls:Case Value="general"> ... </controls:Case>
    <controls:Case Value="updates"> ... </controls:Case>
</controls:SwitchPresenter>
```

Alternatively, add four `bool` `[ObservableProperty]` fields to `SettingsViewModel` and toggle them in a `SelectionChanged` handler — more code but no XAML compilation surprises.

**Warning signs:**
- XAML build error during Phase 3 mentioning `x:Bind` path syntax
- Designer shows all tab panels stacked (all `Visibility="Visible"`) instead of switching

**Phase to address:** Phase 3 (Settings Redesign). Decide the tab-switching strategy during planning before writing XAML — `SwitchPresenter` is the idiomatic CommunityToolkit approach.

---

### Pitfall 8: Segmented Control AutoSelection Fires SelectionChanged During XAML Init

**What goes wrong:**
`Segmented` control with `SelectionMode="Single"` (default) automatically selects the first item. If a `SelectionChanged` handler is wired in XAML or code-behind, it fires during the initial layout pass before `ViewModel` is fully initialized. Any ViewModel mutation in the handler (e.g., publishing a messenger message, saving a setting) runs prematurely with uninitialized state.

**Why it happens:**
The CommunityToolkit docs state: "When SelectionMode is set to Single the first item will be selected by default." This auto-selection fires the `SelectionChanged` event at XAML initialization time. The handler does not distinguish initial selection from user-driven selection.

**How to avoid:**
Set `SelectedIndex="0"` in XAML (already enforces the default) and use the `AutoSelection` property set to `false` if you need to defer the first selection. In the `SelectionChanged` handler, guard against empty `AddedItems`:

```csharp
private void Tabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
{
    if (e.AddedItems.Count == 0) return; // ignore spurious init event
    // handle actual selection change
}
```

Or: use `SwitchPresenter` with `ElementName` binding (no code-behind needed, no initialization hazard).

**Warning signs:**
- A setting is written to disk during `SettingsView` page navigation (before user touches anything)
- `SelectionChanged` fires before `ViewModel.Initialize()` completes

**Phase to address:** Phase 3 (Settings Redesign). Use `SwitchPresenter` pattern to avoid the handler entirely.

---

### Pitfall 9: FileSystemWatcher Configuration Is Already Correct — Do Not "Fix" It

**What goes wrong:**
FEAT-05 was ported from macOS FSEvents, which had a directory-level coalescing bug. Treating this as a code-change requirement in ccInfoWin leads to replacing the working watcher configuration with a directory-only watcher, removing `NotifyFilters.FileName`, or changing `InternalBufferSize` — all of which regress working behavior.

**Why it happens:**
The macOS FSEvents bug was OS-specific. The ccInfoWin `FileSystemWatcher` at `JsonlService.cs` line 816 already has `NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size` with `IncludeSubdirectories = true` — this covers file-level changes precisely. The Windows `FileSystemWatcher` does not coalesce events the way FSEvents did.

**How to avoid:**
FEAT-05 is a **verification task, not an implementation task**. Read the watcher configuration, confirm `FileName | LastWrite` are both present, perform manual testing (switch Claude Code project while ccInfoWin runs, confirm session dropdown updates within 2 seconds), and document the finding. If the configuration is already correct, ship it with a code comment explaining why no change was needed.

**Warning signs:**
- A Phase 4 PR modifies `StartWatching()` without a reproducible regression that motivated the change
- The watcher `Filter` or `NotifyFilter` is changed from its current values

**Phase to address:** Phase 4 (Session Dropdown Fix). Acceptance criterion: do not change the watcher configuration unless a reproducible regression is first identified and documented.

---

## Technical Debt Patterns

| Shortcut | Immediate Benefit | Long-term Cost | When Acceptable |
|----------|-------------------|----------------|-----------------|
| `isDark: true` hardcoded in `ExportHelper.cs` line 245 | No behavior change | Export chart always uses dark palette regardless of user theme; gradient colors will look wrong in light-mode export | Acceptable as pre-existing v1.1 tech debt; flag for v1.4 if light export quality matters |
| Inline boolean tab visibility properties (4 booleans in SettingsViewModel) | Avoids `SwitchPresenter` dependency | 4 extra ViewModel properties that must stay in sync with tab count | Acceptable for v1.3 — `SwitchPresenter` is cleaner but both compile |
| Storing `BurnRatePrediction` as nullable `[ObservableProperty]` on `MainViewModel` | Simple, no extra abstraction | Banner state is coupled to VM; harder to unit test without full VM instantiation | Acceptable — prediction is derived state, not persistence |
| Skipping `Unregister()` on app close | One less shutdown call | COM server slot stays registered; can cause E_FAIL on the next `Register()` call | Never acceptable — always call `Unregister()` in process exit |

---

## Integration Gotchas

| Integration | Common Mistake | Correct Approach |
|-------------|----------------|------------------|
| `AppNotificationManager` + DI | Registering as `AddTransient` causes double `Register()` → E_FAIL on second resolution | Register as `AddSingleton`; call `Register()` once from `App.xaml.cs` `OnLaunched`, not from the service constructor |
| `AppNotificationManager` + event ordering | Calling `Register()` before `NotificationInvoked` subscription → new process launched on toast click | Subscribe to `NotificationInvoked` first; then call `Register()` |
| `CanvasLinearGradientBrush` in draw loop | Creating brush without `using` → GPU resource leak per poll cycle | Always `using var brush = new CanvasLinearGradientBrush(...)` — match the `CanvasPathBuilder` discipline already in `ChartDrawing.cs` |
| `SwitchPresenter` with `Segmented` | Binding `SwitchPresenter.Value` to `SelectedIndex` (int) when `Case` values are strings | Tag each `SegmentedItem` (string) and bind `Value` to `SelectedItem.Tag` via `ElementName` |
| `BurnRateCalculator` history input | Passing raw `UsageHistoryPoint.Utilization` (0.0–1.0) to algorithm expecting 0–100 | Convert `point.Utilization * 100.0` for y-values; `currentUtilization` from API is already 0–100 |
| `Segmented` init in SettingsView | `SelectionChanged` fires at XAML init time, mutating ViewModel before it is ready | Use `SwitchPresenter` pattern (no handler needed) or guard: `if (e.AddedItems.Count == 0) return` |

---

## Performance Traps

| Trap | Symptoms | Prevention | When It Breaks |
|------|----------|------------|----------------|
| `CanvasLinearGradientBrush` leak in draw loop | Memory grows 2–5 MB per minute during active polling; Win2D device-lost exception after extended use | `using` on every brush; verify in `ExportHelper.cs` call path which is directly testable | After ~30 minutes at 30-second poll interval |
| Linear regression over unbounded history | CPU spike on long sessions | Mitigated by spec's 15-minute lookback window — filter before calling the algorithm | Not a v1.3 concern; lookback window is explicit in the spec |
| `CanvasLinearGradientBrush` with 100+ gradient stops | GPU stall on gradient setup per draw | The 101-element lookup table is the source; emit stops only where color changes meaningfully; for typical 30s polling, ~30 stops per 15-minute window is fine | Only a concern if poll interval is reduced to 1s; not in-scope for v1.3 |

---

## Security Mistakes

| Mistake | Risk | Prevention |
|---------|------|------------|
| Toast notification body contains session path or token | Path/token visible in Windows Action Center (notifications persist) | Toast content uses only time labels (`~1h 33min`) and generic copy — never file paths, session IDs, or credentials |
| `NotificationInvoked` handler navigates based on unvalidated `Arguments` string | Crafted notification payload launches unintended views | For v1.3 burn rate toast: ignore notification activation entirely — awareness only, no deep-link action needed |

---

## UX Pitfalls

| Pitfall | User Impact | Better Approach |
|---------|-------------|-----------------|
| Burn rate toast fires every poll cycle when warning is active | Notification spam — potentially dozens of toasts per active coding session | One-shot per warning cycle: set `_notifiedBurnRate = true` on first toast; reset only when `BurnRatePrediction` becomes null |
| Banner appears at exactly 20% due to float precision loss | False positive at the threshold boundary | Use `currentUtilization >= MinimumUtilization` with a small tolerance; `20.0` as a `const double` avoids precision issues |
| Settings tab resets to General on every back-navigation | User must re-navigate to the tab they were on | Expected WinUI 3 Frame navigation behavior — `AddTransient` creates a new VM each time; this is acceptable for v1.3; note in user-visible behavior if questioned |
| Gradient chart shows solid color on first poll | Users may think chart broke | Zero-width span fallback (Pitfall 6) ensures a solid color renders instead of blank — visually equivalent to previous zone-based behavior for single-point history |

---

## "Looks Done But Isn't" Checklist

- [ ] **BurnRateCalculator:** Returns null for all edge cases — null `resetsAt`, utilization < 20, fewer than 3 points, `ssX == 0`, negative slope, projected time > `resetsAt`, utilization at exactly 100. Verify with unit tests before connecting to UI.
- [ ] **Utilization scale:** Unit test confirms a point with `Utilization = 0.55` produces `y = 55.0` in the regression, not `y = 0.55`.
- [ ] **Burn rate toast one-shot:** Send 5 poll cycles with active prediction — exactly 1 toast fires. `_notifiedBurnRate` resets when prediction becomes null, not on utilization drop alone.
- [ ] **AppNotificationManager:** `Unregister()` called in `ProcessExit` handler. No `COMException` in crash.log on any launch after the first.
- [ ] **Toast ordering:** `NotificationInvoked` subscribed before `Register()` call in `App.xaml.cs`. Clicking toast while app runs does not open a second window.
- [ ] **Gradient brush disposal:** Every `new CanvasLinearGradientBrush(...)` in `ChartDrawing.cs` and `ExportHelper.cs` has `using`. Zero exceptions.
- [ ] **Export chart gradient:** `ExportHelper.cs` hardcodes `isDark: true` at line 245. Gradient uses dark palette in export — confirm this is visually acceptable or flag the pre-existing tech debt explicitly.
- [ ] **Zero-width span:** Manual test with 0 and 1 history points — chart renders without Win2D exception.
- [ ] **Segmented tab switching:** All four tabs render content in both dark and light themes. Badge colors use `ThemeResource` references, not hardcoded hex values.
- [ ] **FEAT-05 watcher:** Manual test — switch Claude Code to a different project while ccInfoWin runs. Session dropdown updates within the 2-second debounce window. Watcher `NotifyFilter` is unchanged from `LastWrite | FileName | Size`.

---

## Recovery Strategies

| Pitfall | Recovery Cost | Recovery Steps |
|---------|---------------|----------------|
| NaN from regression denominator causes false banner | LOW | Add `ssX <= 0` guard + `double.IsFinite()` assertions; redeploy; no data loss |
| Utilization scale mismatch (feature silently non-functional) | LOW | Add `* 100.0` conversion for history points; verify with existing unit test suite |
| Brush leak identified post-ship | MEDIUM | Add `using` keyword to each leaked brush; ship patch release; no data loss |
| Double `Register()` E_FAIL on cold launch | LOW | Move `Register()` call to `App.xaml.cs` singleton pattern; crash.log confirms HRESULT; one-line fix |
| Toast spam reaches user before one-shot guard is in place | LOW | Add `_notifiedBurnRate` flag; existing toasts in Action Center cannot be recalled but future spam stops immediately |
| Segmented tab content invisible due to `x:Bind` compile error | LOW | Switch to `SwitchPresenter` pattern — build-time error is caught before shipping |
| Incorrect watcher change regresses session detection | LOW | Revert `StartWatching()` to the original `NotifyFilter = LastWrite | FileName | Size` configuration |

---

## Pitfall-to-Phase Mapping

| Pitfall | Prevention Phase | Verification |
|---------|------------------|--------------|
| Linear regression NaN / zero denominator | Phase 1: Burn Rate Warning | Unit test: three identical timestamps returns null, not `~1min` |
| Utilization scale mismatch (0-1 vs 0-100) | Phase 1: Burn Rate Warning | Unit test: `point.Utilization = 0.55` → slope uses `y = 55.0` |
| Burn rate toast double-fire spam | Phase 1: Burn Rate Warning | Integration test: 5 poll cycles with active warning fires exactly 1 toast |
| AppNotificationManager double Register() E_FAIL | Phase 1: Burn Rate Warning | App launches cleanly 3 times in a row; no COMException in crash.log |
| NotificationInvoked ordering → second window on click | Phase 1: Burn Rate Warning | Click toast while app running → no second window |
| Brush leak in draw loop | Phase 2: Chart Gradient | Code review: every `new CanvasLinearGradientBrush` has `using` |
| Zero-width gradient span → blank chart on cold start | Phase 2: Chart Gradient | Manual test: launch with empty history → chart renders solid color, no exception |
| Segmented `x:Bind` inline expression compile error | Phase 3: Settings Redesign | Build succeeds; tab switching works in dark and light mode |
| Segmented `SelectionChanged` fires during XAML init | Phase 3: Settings Redesign | No settings written to disk during SettingsView page load |
| Incorrect watcher change regresses session detection | Phase 4: Session Dropdown Fix | Watcher `NotifyFilter` unchanged; session names update on project switch within 2s |

---

## Sources

- Official AppNotificationManager quickstart (2025-07-25): https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/notifications/app-notifications/app-notifications-quickstart
- AppNotificationManager.Register E_FAIL issue: https://github.com/microsoft/WindowsAppSDK/issues/3540
- Win2D memory leak avoidance: https://microsoft.github.io/Win2D/WinUI3/html/RefCycles.htm
- Win2D CanvasLinearGradientBrush API: https://microsoft.github.io/Win2D/WinUI3/html/T_Microsoft_Graphics_Canvas_Brushes_CanvasLinearGradientBrush.htm
- CommunityToolkit Segmented control docs: https://learn.microsoft.com/en-us/dotnet/communitytoolkit/windows/segmented/
- Spec FEAT-01a utilization scale note: `spec/v1.10.0-macOS/spec-release-1.8.3-to-1.10.0.md` line 61
- Codebase: `UsageHistory.cs` (Utilization stored 0.0–1.0 with explicit comment), `ChartDrawing.cs` (existing `using` discipline on `CanvasPathBuilder` + `CanvasGeometry`), `JsonlService.cs` line 816 (watcher config already correct), `ExportHelper.cs` line 245 (hardcoded `isDark: true`)

---
*Pitfalls research for: ccInfoWin v1.3 — burn rate prediction, Win2D gradient, Segmented settings, AppNotificationManager, FileSystemWatcher*
*Researched: 2026-04-13*
