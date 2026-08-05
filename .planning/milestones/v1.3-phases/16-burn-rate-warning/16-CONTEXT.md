# Phase 16: Burn Rate Warning - Context

**Gathered:** 2026-04-13
**Status:** Ready for planning

<domain>
## Phase Boundary

Users are warned before their 5-hour token window runs out. A prediction engine uses linear regression over recent usage data to project exhaustion time. Three delivery channels: inline red banner in MainView, localized time-until-limit text, and a one-shot Windows toast notification.

</domain>

<decisions>
## Implementation Decisions

### Prediction Engine Design
- Static class `Helpers/BurnRateCalculator.cs` — stateless pure math, no DI needed
- Minimum 3 data points within the last 15 minutes for regression to activate
- Minimum 20% utilization threshold — below this, no prediction calculated
- Return type: nullable `BurnRatePrediction` — null means no warning

### Warning Banner UI
- Red `Border` element positioned inside 5h window section, after percentage/countdown row (per macOS spec, confirmed by user)
- Flame icon via Segoe Fluent Icons glyph `\uECAD` (Calories/Flame) — per macOS spec resolved decision, confirmed by user
- Text format: localized "Token limit reached in ~Xh YYmin" using `BurnRateFormat_*` resource strings
- Auto-dismiss: banner disappears when prediction becomes null (slope reversal, window reset, rate drop)

### Toast Notification
- `AppNotificationManager` from Windows App SDK 1.8 — already bundled, no extra NuGet
- Fire-once via `_notifiedBurnRate` bool flag in service — reset when prediction becomes null
- DI-registered `IBurnRateNotificationService` + implementation — separation of concerns
- Toast tag `"usage-burnrate"` for OS-level deduplication

</decisions>

<code_context>
## Existing Code Insights

### Reusable Assets
- `UsageHistoryPoint` model (`Models/UsageHistory.cs`) — has `Timestamp` and `Utilization` (stored 0-1, multiply by 100 for regression)
- `UsageHistory.Points` — already accumulated per poll cycle, persisted via `IUsageHistoryService`
- `_fiveHourResetsAt` in `MainViewModel` — window reset timestamp needed for prediction cutoff
- `CountdownFormatter` helper — existing time formatting patterns to reference
- InfoBar pattern in `MainView.xaml` — existing banner UI pattern (update, session expired, API error)

### Established Patterns
- `[ObservableProperty]` + `[RelayCommand]` source generators for ViewModel bindable state
- `l:Uids.Uid` for runtime localization (DE/EN switch without restart)
- `BoolToVisibilityConverter` for conditional UI element display
- `DispatcherQueue.TryEnqueue()` for UI thread marshaling
- DI registration in `App.xaml.cs` for services

### Integration Points
- `MainViewModel.RefreshDataAsync()` — poll cycle where prediction calculation hooks in
- `MainView.xaml` Row 0 area — InfoBar stack where banner goes (after existing InfoBars)
- `App.xaml.cs` DI container — register `IBurnRateNotificationService`
- `AppTheme.xaml` — add `BurnRateWarningBrush` and `BurnRateWarningTextBrush` theme resources

### Critical Pitfall
- `UsageHistoryPoint.Utilization` is stored 0-1; BurnRateCalculator needs 0-100 — must multiply by 100 before regression

</code_context>

<specifics>
## Specific Ideas

- macOS reference spec `spec/v1.10.0-macOS/spec-release-1.8.3-to-1.10.0.md` FEAT-01a/b/c has detailed algorithm, XAML layout, localization strings, and test cases
- BurnRatePrediction model: `HitsLimitAt` (DateTimeOffset), `MinutesUntilLimit` (double), `FormattedTimeUntilLimit` (string)
- Theme colors from spec: Dark `#FF453A`, Light `#FF3B30` for banner background; white text both themes
- AppNotificationManager: subscribe `NotificationInvoked` BEFORE `Register()`, and only once (not on every refresh)

</specifics>

<deferred>
## Deferred Ideas

- FEAT-01d: Tray icon indicator for burn rate — deferred per spec recommendation, banner + toast sufficient for v1.3

</deferred>
