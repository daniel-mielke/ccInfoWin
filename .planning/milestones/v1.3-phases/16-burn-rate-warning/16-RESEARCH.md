# Phase 16: Burn Rate Warning - Research

**Researched:** 2026-04-13
**Domain:** Linear regression prediction engine, WinUI 3 banner UI, AppNotificationManager toast
**Confidence:** HIGH

---

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions

**Prediction Engine Design**
- Static class `Helpers/BurnRateCalculator.cs` — stateless pure math, no DI needed
- Minimum 3 data points within the last 15 minutes for regression to activate
- Minimum 20% utilization threshold — below this, no prediction calculated
- Return type: nullable `BurnRatePrediction` — null means no warning

**Warning Banner UI**
- Red `Border` element positioned below InfoBars, above 5-hour window section
- Flame icon via Segoe Fluent Icons glyph `\uE7C1` — no custom asset needed
- Text format: localized "Token-Limit erreicht in ~Xh YYmin" using `BurnRateFormat_*` resource strings
- Auto-dismiss: banner disappears when prediction becomes null (slope reversal, window reset, rate drop)

**Toast Notification**
- `AppNotificationManager` from Windows App SDK 1.8 — already bundled, no extra NuGet
- Fire-once via `_notifiedBurnRate` bool flag in service — reset when prediction becomes null
- DI-registered `IBurnRateNotificationService` + implementation — separation of concerns
- Toast tag `"usage-burnrate"` for OS-level deduplication

### Claude's Discretion

*(Not specified in CONTEXT.md — no discretion areas.)*

### Deferred Ideas (OUT OF SCOPE)

- FEAT-01d: Tray icon indicator for burn rate — deferred per spec recommendation, banner + toast sufficient for v1.3
</user_constraints>

---

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|------------------|
| BURN-01 | Red warning banner with flame icon appears when regression projects exhaustion before 5-hour window resets | BurnRateCalculator algorithm + XAML Border with `BurnRateWarningBrush`, `\uECAD` glyph |
| BURN-02 | Warning banner shows localized time-until-limit in compact format (~Xh YYmin / ~Xmin) | `BurnRatePrediction.FormattedTimeUntilLimit` built in calculator; localization keys in `.resw` |
| BURN-03 | Warning banner disappears when usage rate drops, window resets, or slope reverses | Null prediction → `IsBurnRateWarningVisible = false` → `BoolToVisibilityConverter` hides Border |
| BURN-04 | Windows toast notification fires exactly once when burn rate warning first triggers | `IBurnRateNotificationService` with `_notifiedBurnRate` flag; `AppNotificationManager.Default.Show()` |
| BURN-05 | Toast does not re-fire until warning clears and re-triggers in a new cycle | Flag reset in `CheckBurnRate()` when prediction becomes null |
| BURN-06 | Prediction uses linear regression over last 15 minutes, min 20% utilization, min 3 data points | Least-squares algorithm in `BurnRateCalculator.Predict()`; constants guard against noise |
| BURN-07 | All burn rate text (banner, toast, time format) is localized in German and English | 9 new `.resw` keys per language in both `de-DE/Resources.resw` and `en-US/Resources.resw` |
</phase_requirements>

---

## Summary

Phase 16 adds a burn rate prediction system to CCInfoWindows. The work breaks into three independent deliverables: a pure-math calculator (`BurnRateCalculator.cs`), a XAML banner in `MainView.xaml`, and a toast notification service (`BurnRateNotificationService.cs`).

The prediction engine uses least-squares linear regression over the last 15 minutes of `UsageHistoryPoint` records. The critical pitfall confirmed by code review: `UsageHistoryPoint.Utilization` is stored 0.0–1.0 (normalized), but the algorithm operates on 0–100 scale. Every history point must be multiplied by 100 before use in regression. The `currentUtilization` parameter comes from `UsageWindow.Utilization` which is already 0–100 from the API (see `UsageData.cs` — `NormalizedUtilization` divides by 100 for the stored value).

The toast notification uses `Microsoft.Windows.AppNotifications` (already in `Microsoft.WindowsAppSDK 1.8` referenced in `CCInfoWindows.csproj`). The app is **unpackaged** (`WindowsPackageType=None`) so no `Package.appxmanifest` changes are needed. However the app uses `dotnet publish --self-contained` for release builds, which triggers a Singleton-package dependency risk for `AppNotificationManager`. The `IsSupported` guard must be called before `Register()` to fail gracefully in self-contained builds where the Singleton package may not be present.

**Primary recommendation:** Implement the three deliverables in dependency order — calculator first (pure C#, immediately testable), then ViewModel integration, then banner XAML, then notification service + DI.

---

## Standard Stack

### Core
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| `Microsoft.Windows.AppNotifications` | Bundled in WindowsAppSDK 1.8.260209005 | Toast notifications | Already in csproj, no new NuGet needed |
| `CommunityToolkit.Mvvm` | 8.4.0 | `[ObservableProperty]` for banner visibility | Already used throughout the project |
| `WinUI3Localizer` | 2.3.0 | `l:Uids.Uid` runtime language switch | Already used in all Views |

### Supporting
| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| xUnit | 2.9.3 | Unit tests for `BurnRateCalculator` | Test project already uses this |
| Moq | 4.20.72 | Mock `IBurnRateNotificationService` in ViewModel tests | Test project already has it |

**Installation:** No new NuGet packages required. All dependencies are already present.

---

## Architecture Patterns

### Recommended Project Structure (new files only)

```
CCInfoWindows/CCInfoWindows/
  Models/
    BurnRatePrediction.cs         -- new: HitsLimitAt, MinutesUntilLimit, FormattedTimeUntilLimit
  Helpers/
    BurnRateCalculator.cs         -- new: static, pure math, no DI
  Services/
    Interfaces/
      IBurnRateNotificationService.cs  -- new: CheckBurnRate(BurnRatePrediction?)
    BurnRateNotificationService.cs     -- new: implements IBurnRateNotificationService
  ViewModels/
    MainViewModel.cs              -- modified: add prediction fields + hook in RefreshDataAsync
  Views/
    MainView.xaml                 -- modified: add burn rate banner Border
  Resources/
    AppTheme.xaml                 -- modified: add BurnRateWarningBrush (Dark + Light)
  Strings/
    de-DE/Resources.resw          -- modified: 9 new burn rate keys
    en-US/Resources.resw          -- modified: 9 new burn rate keys
CCInfoWindows.Tests/
  Helpers/
    BurnRateCalculatorTests.cs    -- new: 9+ unit test cases
```

### Pattern 1: BurnRateCalculator (Pure Static Helper)

**What:** A static class with a single `Predict()` method. No instance state, no DI, no side effects.
**When to use:** For deterministic math that depends only on its inputs — regression, formatting.

```csharp
// Source: spec/v1.10.0-macOS/spec-release-1.8.3-to-1.10.0.md FEAT-01a
public static class BurnRateCalculator
{
    private const double MinimumUtilization = 20.0;
    private const int LookbackWindowMinutes = 15;
    private const int MinimumDataPoints = 3;
    private const double MaxUtilization = 100.0;

    public static BurnRatePrediction? Predict(
        IReadOnlyList<UsageHistoryPoint> history,
        double currentUtilization,   // 0-100, from UsageWindow.Utilization
        DateTimeOffset? resetsAt)
    {
        // Guard 1: window must be active
        if (!resetsAt.HasValue || resetsAt.Value <= DateTimeOffset.UtcNow) return null;

        // Guard 2: minimum utilization
        if (currentUtilization < MinimumUtilization) return null;

        // Collect last 15 min of points — IMPORTANT: multiply Utilization by 100 (stored 0-1)
        var cutoff = DateTimeOffset.UtcNow - TimeSpan.FromMinutes(LookbackWindowMinutes);
        var recentPoints = history.Where(p => p.Timestamp >= cutoff).ToList();

        // Guard 3: minimum data points
        if (recentPoints.Count < MinimumDataPoints) return null;

        // Linear regression: x = seconds since first point, y = utilization (0-100)
        var refTime = recentPoints[0].Timestamp;
        double n = recentPoints.Count;
        double sumX = 0, sumY = 0, sumXY = 0, sumX2 = 0;

        foreach (var p in recentPoints)
        {
            double x = (p.Timestamp - refTime).TotalSeconds;
            double y = p.Utilization * 100.0;  // CRITICAL: convert 0-1 → 0-100
            sumX += x; sumY += y; sumXY += x * y; sumX2 += x * x;
        }

        double denominator = n * sumX2 - sumX * sumX;
        if (Math.Abs(denominator) < double.Epsilon) return null;

        double slope = (n * sumXY - sumX * sumY) / denominator;

        // Guard 4: only warn when usage is increasing
        if (slope <= 0) return null;

        // Project exhaustion
        double secondsToLimit = (MaxUtilization - currentUtilization) / slope;
        var hitsLimitAt = DateTimeOffset.UtcNow.AddSeconds(secondsToLimit);

        // Guard 5: only warn if projected before window reset
        if (hitsLimitAt >= resetsAt.Value) return null;

        int minutesUntilLimit = Math.Max(1, (int)Math.Floor(secondsToLimit / 60));
        return new BurnRatePrediction
        {
            HitsLimitAt = hitsLimitAt,
            MinutesUntilLimit = minutesUntilLimit,
            FormattedTimeUntilLimit = FormatTimeUntilLimit(minutesUntilLimit)
        };
    }

    private static string FormatTimeUntilLimit(int minutes)
    {
        if (minutes >= 60)
        {
            int h = minutes / 60;
            int m = minutes % 60;
            return m == 0
                ? string.Format(GetLocalizedString("BurnRateFormat_HoursOnly"), h)
                : string.Format(GetLocalizedString("BurnRateFormat_HoursMinutes"), h, m);
        }
        return string.Format(GetLocalizedString("BurnRateFormat_MinutesOnly"), minutes);
    }
}
```

> NOTE: The `FormattedTimeUntilLimit` localization approach needs a decision. Two options: (a) build it in BurnRateCalculator by calling `Localizer.Get().GetLocalizedString(key)`, or (b) expose raw `MinutesUntilLimit` int and format in the ViewModel using the current locale. Option (b) keeps the calculator free of localization dependencies. The spec shows `FormattedTimeUntilLimit: string` on the model — but if the calculator is static and called on a background thread, it needs the Localizer available. **Recommended:** format in ViewModel instead, keep model as plain data.

### Pattern 2: MainViewModel Integration Hook

**What:** After `UpdateUsageProperties()` completes in the poll cycle, calculate prediction and update observable properties.
**When to use:** Every poll cycle where FiveHour data is present.

```csharp
// Source: spec/v1.10.0-macOS/spec-release-1.8.3-to-1.10.0.md — Data Flow section
private void UpdateUsageProperties(UsageResponse data)
{
    // ... existing FiveHour, weekly, sonnet logic unchanged ...

    // Burn rate prediction — hook after AppendHistoryPoint
    if (data.FiveHour != null)
    {
        var prediction = BurnRateCalculator.Predict(
            UsageHistoryPoints,
            data.FiveHour.Utilization,     // 0-100 from API — NOT NormalizedUtilization
            data.FiveHour.ResetsAt);

        IsBurnRateWarningVisible = prediction != null;
        BurnRateWarningText = prediction != null
            ? FormatBurnRateText(prediction.MinutesUntilLimit)
            : string.Empty;

        _burnRateNotificationService.CheckBurnRate(prediction);
    }
    else
    {
        IsBurnRateWarningVisible = false;
        BurnRateWarningText = string.Empty;
        _burnRateNotificationService.CheckBurnRate(null);
    }
}
```

**New observable properties needed in MainViewModel:**

```csharp
[ObservableProperty]
private bool _isBurnRateWarningVisible;

[ObservableProperty]
private string _burnRateWarningText = string.Empty;
```

### Pattern 3: AppNotificationManager Registration (Unpackaged App)

**What:** Register before calling `Show()`, subscribe `NotificationInvoked` BEFORE `Register()`, unregister on app exit.
**When to use:** Called once at app startup in `App.xaml.cs` `OnLaunched`.

```csharp
// Source: https://learn.microsoft.com/en-us/windows/apps/develop/notifications/app-notifications/app-notifications-quickstart
// In App.xaml.cs OnLaunched, BEFORE RouteOnStartupAsync:
if (AppNotificationManager.IsSupported())
{
    var notificationManager = AppNotificationManager.Default;
    notificationManager.NotificationInvoked += OnNotificationInvoked;  // Subscribe FIRST
    notificationManager.Register();                                       // Then Register
}
```

**Critical ordering:** `NotificationInvoked` subscription MUST precede `Register()`. If Register is called first, notifications fired while the app is not running may launch a new process instead of routing to the existing one.

**Unregister on exit:**
```csharp
// In App.xaml.cs, wire to application exit or window close:
AppNotificationManager.Default.Unregister();
```

### Pattern 4: IBurnRateNotificationService

**What:** DI-injectable service that owns the `_notifiedBurnRate` bool flag. Decouples ViewModel from AppNotificationManager.

```csharp
// Source: CONTEXT.md locked decisions
public interface IBurnRateNotificationService
{
    void CheckBurnRate(BurnRatePrediction? prediction);
}

public class BurnRateNotificationService : IBurnRateNotificationService
{
    private bool _notifiedBurnRate;

    public void CheckBurnRate(BurnRatePrediction? prediction)
    {
        if (prediction == null)
        {
            _notifiedBurnRate = false;  // Reset cycle on warning clear
            return;
        }

        if (_notifiedBurnRate) return;  // Already notified this cycle

        _notifiedBurnRate = true;
        SendToast(prediction.FormattedTimeUntilLimit);
    }

    private static void SendToast(string timeLabel)
    {
        if (!AppNotificationManager.IsSupported()) return;

        var title = Localizer.Get().GetLocalizedString("BurnRateNotificationTitle");
        var body = string.Format(
            Localizer.Get().GetLocalizedString("BurnRateNotificationBody"),
            timeLabel);

        var notification = new AppNotificationBuilder()
            .AddText(title)
            .AddText(body)
            .BuildNotification();

        notification.Tag = "usage-burnrate";  // OS deduplication

        AppNotificationManager.Default.Show(notification);
    }
}
```

### Pattern 5: Burn Rate Banner XAML

**What:** Red `Border` inside the 5-STUNDEN-FENSTER `StackPanel`, immediately after the percentage/countdown row.
**When to use:** `IsBurnRateWarningVisible` is true.

```xml
<!-- Source: spec/v1.10.0-macOS/spec-release-1.8.3-to-1.10.0.md FEAT-01b -->
<!-- Insert AFTER the percentage+countdown Grid row in the 5-STUNDEN-FENSTER StackPanel -->
<Border Background="{ThemeResource BurnRateWarningBrush}"
        CornerRadius="6"
        Padding="8,4"
        Margin="0,8,0,0"
        Visibility="{x:Bind ViewModel.IsBurnRateWarningVisible, Mode=OneWay,
                     Converter={StaticResource BoolToVisibilityConverter}}"
        AutomationProperties.Name="{x:Bind ViewModel.BurnRateWarningText, Mode=OneWay}">
    <StackPanel Orientation="Horizontal" Spacing="5">
        <FontIcon Glyph="&#xECAD;" FontSize="12" Foreground="White" />
        <TextBlock Text="{x:Bind ViewModel.BurnRateWarningText, Mode=OneWay}"
                   Foreground="White" FontSize="12" />
    </StackPanel>
</Border>
```

**Flame icon glyph:** The spec (CONTEXT.md) mentions `\uE7C1` but the macOS spec (FEAT-01b and FEAT-01c decision table) resolves to `\uECAD` (Segoe Fluent Icons "Calories/Flame"). CONTEXT.md has `\uE7C1` — this is a conflict. The macOS spec's resolved decision table explicitly says `\uECAD`. Recommend using `\uECAD` as it was the final resolved answer in the spec. Planner should confirm with user.

### Pattern 6: AppTheme.xaml — Adding BurnRateWarningBrush

**What:** Two new `SolidColorBrush` entries in the `Dark` and `Light` theme dictionaries.
**Location:** `CCInfoWindows/CCInfoWindows/Resources/AppTheme.xaml`

```xml
<!-- In Dark ResourceDictionary -->
<SolidColorBrush x:Key="BurnRateWarningBrush" Color="#FF453A" />

<!-- In Light ResourceDictionary -->
<SolidColorBrush x:Key="BurnRateWarningBrush" Color="#FF3B30" />
```

Note: `BurnRateWarningTextBrush` is not needed since text color is hardcoded `White` (same in both themes).

### Pattern 7: Localization Strings (.resw)

**Format of existing entries:**
```xml
<data name="AutocompactWarning.Text" xml:space="preserve">
    <value>⚠ Autocompact bald</value>
</data>
```

**New keys required (9 per language file) — plain string values, not Uid-targeted:**
These are not element Uid strings — they are code-behind resource strings accessed via `Localizer.Get().GetLocalizedString(key)`.

Check how `WinUI3Localizer` exposes non-Uid strings. The existing pattern uses `.Text`, `.Content`, `.PlaceholderText` suffixes for Uid binding. For code-behind access, verify the API — likely `Localizer.Get().GetLocalizedString("BurnRateBannerText")` or via `ResourceLoader`.

**German (de-DE):**
```xml
<data name="BurnRateBannerText" xml:space="preserve">
    <value>Token-Limit erreicht in {0}</value>
</data>
<data name="BurnRateFormat_HoursMinutes" xml:space="preserve">
    <value>~{0}h {1}min</value>
</data>
<data name="BurnRateFormat_HoursOnly" xml:space="preserve">
    <value>~{0}h</value>
</data>
<data name="BurnRateFormat_MinutesOnly" xml:space="preserve">
    <value>~{0}min</value>
</data>
<data name="BurnRateNotificationTitle" xml:space="preserve">
    <value>Burn-Rate-Warnung</value>
</data>
<data name="BurnRateNotificationBody" xml:space="preserve">
    <value>Bei aktuellem Tempo wird das Token-Limit in {0} erreicht.</value>
</data>
```

**English (en-US):**
```xml
<data name="BurnRateBannerText" xml:space="preserve">
    <value>Token limit reached in {0}</value>
</data>
<data name="BurnRateNotificationTitle" xml:space="preserve">
    <value>Burn rate warning</value>
</data>
<data name="BurnRateNotificationBody" xml:space="preserve">
    <value>At current pace, token limit reached in {0}.</value>
</data>
```
(Format keys are language-neutral: `~{0}h {1}min` is the same in both files.)

### Anti-Patterns to Avoid

- **Calling `UsageWindow.NormalizedUtilization` in the calculator** — this is 0.0–1.0 and will make the minimum utilization guard (20.0) never trigger.
- **Subscribing `NotificationInvoked` after `Register()`** — confirmed in official docs as incorrect order. The event handler must be wired first.
- **Calling `Register()` on every refresh cycle** — registration is a one-time app startup operation. Calling it repeatedly throws `COMException`.
- **Using `data.FiveHour.NormalizedUtilization` as the calculator's `currentUtilization` param** — must use `data.FiveHour.Utilization` (0-100), not the normalized 0-1 value.
- **Not guarding with `IsSupported()`** — in self-contained release builds, `AppNotificationManager` has a Singleton package dependency that may not be present. Calling `Register()` without the guard will throw a `COMException`.

---

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Toast notification UI | Custom overlay/popup | `AppNotificationManager` + `AppNotificationBuilder` | OS handles placement, dismissal, Action Center, accessibility |
| Localized time format | Manual string concat | `.resw` keys via `WinUI3Localizer` | Consistent with rest of app; hot-swap without restart |
| Observable property change notification | Manual `PropertyChanged` | `[ObservableProperty]` source generator | Already the project pattern; avoids boilerplate |
| Visibility from bool | x:Bind ternary | `BoolToVisibilityConverter` (already registered) | Consistent, already in `App.xaml` resources |

---

## Common Pitfalls

### Pitfall 1: Utilization Scale Mismatch (CRITICAL)
**What goes wrong:** The regression produces near-zero slope (or the 20% threshold is never met) because `UsageHistoryPoint.Utilization` is 0.0–0.2 when usage is at 20% — not 20.
**Why it happens:** The model stores normalized 0-1 values (confirmed in `UsageHistory.cs` docstring and `AppendHistoryPoint` which saves `util = data.FiveHour.NormalizedUtilization`). The algorithm expectes 0-100.
**How to avoid:** In `BurnRateCalculator.Predict()`, always apply `p.Utilization * 100.0` when computing `y` for regression. The `currentUtilization` parameter receives `data.FiveHour.Utilization` directly (0-100 from API) — do not normalize this parameter before passing.
**Warning signs:** Banner never appears even with high utilization; regression slope is ~100x too small.

### Pitfall 2: NotificationInvoked Order
**What goes wrong:** A `COMException` or toast click activates a second app process instead of routing to the existing window.
**Why it happens:** `Register()` sets up the COM activation endpoint. If `NotificationInvoked` is not subscribed before `Register()`, the existing handler is not in place when activation events arrive.
**How to avoid:** In `App.xaml.cs OnLaunched`: subscribe event → then call `Register()`. Never reverse.
**Warning signs:** Clicking a toast opens a second app instance.

### Pitfall 3: Register() Called Multiple Times
**What goes wrong:** `COMException (0x800703FD)` — "Cannot create a stable subkey under a volatile parent key".
**Why it happens:** `Register()` must only be called once per process lifetime. If the DI service is transient or if `InitializeAsync` is called multiple times, duplicate registrations occur.
**How to avoid:** Keep `AppNotificationManager.Default.Register()` in `App.xaml.cs OnLaunched`, not in any service constructor or refresh cycle. Mark it with `_isRegistered` flag.
**Warning signs:** App crashes on startup with `COMException` after the second navigation to MainView.

### Pitfall 4: Self-Contained Build Notification Failure
**What goes wrong:** `AppNotificationManager.Default.Show()` silently fails (returns `Id == 0`) or throws in self-contained `dotnet publish` builds.
**Why it happens:** `AppNotificationManager` depends on the Windows App SDK Singleton MSIX package. Self-contained builds don't include it by default.
**How to avoid:** Wrap all `AppNotificationManager` calls with `AppNotificationManager.IsSupported()` check. If not supported, skip notification silently — banner still shows.
**Warning signs:** Notifications work in debug but not in published release build.

### Pitfall 5: Banner Position Conflict
**What goes wrong:** Banner appears in wrong section (e.g., above context window) or is clipped by ScrollViewer.
**Why it happens:** The 5-STUNDEN-FENSTER section is a `StackPanel Spacing="8"`. Adding a child adds it at whatever position in the XML it appears. The banner must go AFTER the countdown/percentage Grid row (line 279–303 in current `MainView.xaml`).
**How to avoid:** Insert the burn rate banner `Border` as the last child of the 5-hour `StackPanel`, after the percentage+countdown `Grid` (lines 280–303 of current `MainView.xaml`).
**Warning signs:** Banner visually appears above or below the wrong section.

### Pitfall 6: WinUI3Localizer Code-Behind String Access
**What goes wrong:** `Localizer.Get().GetLocalizedString("BurnRateBannerText")` returns empty string or throws.
**Why it happens:** WinUI3Localizer has two APIs — the Uid binding path (`.Text`, `.Content` etc. in `.resw`) and direct code access. Non-Uid keys accessed from code require a specific resw naming scheme.
**How to avoid:** Verify the correct WinUI3Localizer API for code-behind string lookup. The safest pattern is building the text in the ViewModel using `Microsoft.Windows.ApplicationModel.Resources.ResourceLoader` as fallback, or verifying the WinUI3Localizer source for `GetLocalizedString`.
**Warning signs:** Toast body shows empty text; banner text shows key name instead of value.

---

## Code Examples

### Linear Regression Test Cases

```csharp
// Source: spec/v1.10.0-macOS/spec-release-1.8.3-to-1.10.0.md Testing Strategy
[Fact]
public void Predict_NoResetsAt_ReturnsNull() { /* resetsAt = null */ }

[Fact]
public void Predict_LowUtilization_ReturnsNull() { /* currentUtilization = 15% */ }

[Fact]
public void Predict_TooFewPoints_ReturnsNull() { /* 2 points in last 15 min */ }

[Fact]
public void Predict_NegativeSlope_ReturnsNull() { /* decreasing usage */ }

[Fact]
public void Predict_FastBurn_ReturnsPrediction()
{
    // 20% → 60% in 10 min, resets in 3h
    // Expected: prediction with ~25 min until limit
}

[Fact]
public void Predict_SlowBurn_ExceedsReset_ReturnsNull()
{
    // 80% → 85% in 15 min, resets in 10 min
    // Projected exhaustion > resetsAt → null
}
```

### AppNotificationBuilder Toast Example

```csharp
// Source: https://learn.microsoft.com/en-us/windows/apps/develop/notifications/app-notifications/app-notifications-quickstart
var notification = new AppNotificationBuilder()
    .AddText("Burn rate warning")
    .AddText("At current pace, token limit reached in ~33min.")
    .BuildNotification();

notification.Tag = "usage-burnrate";  // deduplication: OS replaces previous if same tag shown
AppNotificationManager.Default.Show(notification);
```

---

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| WinRT `ToastNotificationManager` (UWP-era) | `AppNotificationManager` (Windows App SDK) | Windows App SDK 1.0 (2021) | Single API for packaged and unpackaged apps |

**Deprecated/outdated:**
- `Windows.UI.Notifications.ToastNotificationManager`: UWP API, still works in packaged apps, but `AppNotificationManager` is the standard for Windows App SDK apps.

---

## Open Questions

1. **Flame glyph: `\uE7C1` vs `\uECAD`**
   - What we know: CONTEXT.md says `\uE7C1`; the macOS spec FEAT-01b resolved decision table says `\uECAD` (Calories/Flame)
   - What's unclear: Which was the final decision; `\uE7C1` is the "Weather" glyph in some font versions
   - Recommendation: Planner should use `\uECAD` (the spec's resolved answer) and note the discrepancy. If visual review shows wrong icon, switch to `\uE7C1`.

2. **`FormattedTimeUntilLimit` in model vs ViewModel**
   - What we know: Spec defines it on the `BurnRatePrediction` model; building it there requires calling `WinUI3Localizer` from a static helper
   - What's unclear: Whether `WinUI3Localizer` is safe to call from a static background context
   - Recommendation: Keep `BurnRatePrediction` as a plain data record (`HitsLimitAt`, `MinutesUntilLimit` only); format time string in ViewModel where `Localizer.Get()` is safe to call on the UI thread.

3. **`AppNotificationManager` in non-self-contained debug builds**
   - What we know: Debug builds are framework-dependent, not self-contained. The Singleton package is typically available on a dev machine with Windows App SDK installed.
   - What's unclear: Whether `Register()` has been called successfully in any prior phase
   - Recommendation: Always gate with `AppNotificationManager.IsSupported()`. No existing code in the app uses `AppNotificationManager` — this is the first use.

---

## Environment Availability

| Dependency | Required By | Available | Version | Fallback |
|------------|------------|-----------|---------|----------|
| `Microsoft.Windows.AppNotifications` | Toast notifications | ✓ | Bundled in WindowsAppSDK 1.8.260209005 | `IsSupported()` check silences failure |
| WinUI3Localizer | Localized strings | ✓ | 2.3.0 (in csproj) | — |
| xUnit | BurnRateCalculator tests | ✓ | 2.9.3 (in test csproj) | — |
| Segoe Fluent Icons font | Flame glyph `\uECAD` | ✓ | Windows 11 built-in | Fallback: text "🔥" emoji |

---

## Validation Architecture

### Test Framework
| Property | Value |
|----------|-------|
| Framework | xUnit 2.9.3 |
| Config file | CCInfoWindows.Tests/CCInfoWindows.Tests.csproj |
| Quick run command | `dotnet test CCInfoWindows.Tests/CCInfoWindows.Tests.csproj -x --filter "FullyQualifiedName~BurnRate"` |
| Full suite command | `dotnet test CCInfoWindows.Tests/CCInfoWindows.Tests.csproj` |

### Phase Requirements → Test Map
| Req ID | Behavior | Test Type | Automated Command | File Exists? |
|--------|----------|-----------|-------------------|-------------|
| BURN-06 | Regression returns null for < 3 points | unit | `dotnet test ... --filter "BurnRate"` | ❌ Wave 0 |
| BURN-06 | Regression returns null for utilization < 20% | unit | `dotnet test ... --filter "BurnRate"` | ❌ Wave 0 |
| BURN-06 | Regression returns null for negative slope | unit | `dotnet test ... --filter "BurnRate"` | ❌ Wave 0 |
| BURN-06 | Regression returns null if projected exhaustion after resetsAt | unit | `dotnet test ... --filter "BurnRate"` | ❌ Wave 0 |
| BURN-06 | Regression returns prediction for fast-burn scenario | unit | `dotnet test ... --filter "BurnRate"` | ❌ Wave 0 |
| BURN-01 | `IsBurnRateWarningVisible` true when prediction non-null | unit | `dotnet test ... --filter "BurnRate"` | ❌ Wave 0 |
| BURN-03 | `IsBurnRateWarningVisible` false when prediction null | unit | `dotnet test ... --filter "BurnRate"` | ❌ Wave 0 |
| BURN-04/05 | Toast fires once per cycle, resets on null | unit | `dotnet test ... --filter "BurnRate"` | ❌ Wave 0 |
| BURN-02 | Time format ~Xh YYmin / ~Xmin | unit | `dotnet test ... --filter "BurnRate"` | ❌ Wave 0 |
| BURN-07 | Localization keys present in both .resw files | manual | open files + verify keys | ❌ Wave 0 |

### Sampling Rate
- **Per task commit:** `dotnet test CCInfoWindows.Tests/CCInfoWindows.Tests.csproj -x --filter "FullyQualifiedName~BurnRate"`
- **Per wave merge:** `dotnet test CCInfoWindows.Tests/CCInfoWindows.Tests.csproj`
- **Phase gate:** Full suite green before `/gsd:verify-work`

### Wave 0 Gaps
- [ ] `CCInfoWindows.Tests/Helpers/BurnRateCalculatorTests.cs` — covers BURN-06 (regression algorithm)
- [ ] `CCInfoWindows.Tests/Services/BurnRateNotificationServiceTests.cs` — covers BURN-04, BURN-05
- [ ] `CCInfoWindows.Tests/Models/BurnRatePredictionTests.cs` — covers BURN-02 (time formatting, if done in ViewModel helper)

---

## Project Constraints (from CLAUDE.md)

| Directive | Impact on Phase 16 |
|-----------|-------------------|
| No code-behind logic in Views — all logic in ViewModels | Banner visibility and text must be ViewModel `[ObservableProperty]` fields, not code-behind |
| `[ObservableProperty]` for bindable properties | `IsBurnRateWarningVisible` and `BurnRateWarningText` use source generators |
| `[RelayCommand]` for commands | No new commands in this phase |
| `async/await` always — no fire-and-forget | `AppNotificationManager.Default.Show()` is synchronous — no async needed |
| `DispatcherQueue.TryEnqueue()` for UI thread marshaling | Prediction runs inside `UpdateUsageProperties()` which is already on UI thread; no marshaling needed |
| No magic numbers — named constants | `MinimumUtilization = 20.0`, `LookbackWindowMinutes = 15`, `MinimumDataPoints = 3` as `const` fields |
| DRY — no duplicate logic | `FormatTimeUntilLimit()` is one method, called from both banner and toast |
| Wrap external libraries | `IBurnRateNotificationService` wraps `AppNotificationManager` — correct |
| No secrets in source | No credentials involved |
| Fail securely | `IsSupported()` guard before any notification API call |
| No sensitive data in error messages | Notification body contains only time-until-limit, no tokens or system details |
| `using` statements for IDisposable | `AppNotificationBuilder` — verify if IDisposable; if so, wrap in `using` |

---

## Sources

### Primary (HIGH confidence)
- Code review of `MainViewModel.cs` — `UpdateUsageProperties()`, `AppendHistoryPoint()`, `_fiveHourResetsAt` field
- Code review of `UsageHistory.cs` — `UsageHistoryPoint.Utilization` docstring confirms 0.0–1.0 storage
- Code review of `UsageData.cs` — `UsageWindow.Utilization` confirmed as 0-100 raw API value
- Code review of `MainView.xaml` — 5-STUNDEN-FENSTER section at lines 229–305; InfoBar stack at lines 38–83
- Code review of `AppTheme.xaml` — existing `ProgressRedBrush` color values: dark `#FF453A`, light `#FF3B30`
- Code review of `App.xaml.cs` — `ConfigureServices()` DI registration pattern
- Code review of `Resources.resw` (both languages) — Uid-based localization key format
- Code review of `CCInfoWindows.csproj` — confirms `Microsoft.WindowsAppSDK 1.8.260209005`, `WindowsPackageType=None`
- `spec/v1.10.0-macOS/spec-release-1.8.3-to-1.10.0.md` FEAT-01a/b/c — algorithm, XAML structure, localization keys

### Secondary (MEDIUM confidence)
- [Microsoft Learn — App Notifications Quickstart](https://learn.microsoft.com/en-us/windows/apps/develop/notifications/app-notifications/app-notifications-quickstart) — confirmed: (1) unpackaged apps skip manifest changes, (2) `NotificationInvoked` before `Register()` is mandatory, (3) `AppNotificationBuilder` + `.Tag` property API

### Tertiary (LOW confidence)
- [Self-contained app deployment guide](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/self-contained-deploy/deploy-self-contained-apps) — `AppNotificationManager.IsSupported()` required for self-contained builds; Singleton package dependency confirmed

---

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH — all dependencies verified in csproj; no new NuGet
- Architecture: HIGH — all integration points verified by direct code reading
- Pitfalls: HIGH — utilization scale pitfall confirmed by `UsageHistory.cs` docstring and `AppendHistoryPoint` code; notification order confirmed by official docs
- Toast API: MEDIUM — confirmed via official quickstart docs; `IsSupported()` guard confirmed via deployment guide

**Research date:** 2026-04-13
**Valid until:** 2026-07-13 (stable Windows App SDK APIs, 90-day estimate)
