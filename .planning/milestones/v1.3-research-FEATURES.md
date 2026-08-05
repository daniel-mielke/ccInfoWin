# Feature Research

**Domain:** Desktop LLM usage monitoring — macOS v1.9.0–v1.10.0 feature parity delta for ccInfoWin v1.3
**Researched:** 2026-04-13
**Confidence:** HIGH (spec fully defined, reference implementation known, codebase inspected)

---

## Scope

This document covers ONLY the four new feature areas added in v1.3. The existing feature set
(authentication, charts with zone-based fills, weekly limits, context window, token stats,
cost, export, settings flat layout, auto-update, localization, session management) is already
implemented and NOT re-analyzed here.

---

## Feature Landscape

### Table Stakes (Users Expect These)

| Feature | Why Expected | Complexity | Existing Code Impact |
|---------|--------------|------------|----------------------|
| Proactive limit warning | Any monitoring tool must warn before hitting limits, not after. Silent exhaustion during an active coding session is the core failure mode the app exists to prevent. An app that shows 85% usage without projecting "you'll hit 100% in 18 min" misses its own value proposition. | HIGH | New: `BurnRateCalculator.cs`, `BurnRatePrediction.cs`, `BurnRateNotificationService.cs`. Modify: `MainViewModel.cs` (poll cycle), `MainView.xaml` (banner), `App.xaml.cs` (notification registration), resource files. |
| Session watcher reliability | If the session dropdown silently falls behind file system reality (stale names, missing updates), users distrust the data. Reliable file-level watching is expected baseline for any file-backed data tool. | LOW | Review `JsonlService.cs` watcher config. **Already configured correctly** — `NotifyFilters.LastWrite \| NotifyFilters.FileName \| NotifyFilters.Size` and `IncludeSubdirectories = true` are in place. Verification test only, likely no code change. |

### Differentiators (Competitive Advantage)

| Feature | Value Proposition | Complexity | Notes |
|---------|-------------------|------------|-------|
| Burn rate linear regression (not threshold alert) | Most monitoring tools use dumb threshold alerts ("you're at 80%"). Linear regression over the last 15 min extrapolates actual exhaustion time, accounting for usage velocity. "Limit in ~18min" is more actionable than "You're at 85%." | HIGH | Pure math class, no external dependencies. Input: `IReadOnlyList<UsageHistoryPoint>` (normalized 0–1), `double currentUtilization` (0–100 from API), `DateTimeOffset? resetsAt`. **Critical:** history points store utilization as 0–1; algorithm runs on 0–100 scale — conversion required in calculator. |
| Smooth chart gradient (green→yellow→orange→red) | Flat zone fills make the chart look like a status dashboard. A smooth horizontal gradient makes the velocity of change visually obvious — you see the color shift as usage grows, not just a color jump at a threshold. Visual differentiation from any competing tool. | HIGH | Win2D `CanvasLinearGradientBrush` per gap-free data span. Replaces `GetZoneSegments()` iteration in `DrawChartFills()` and `DrawChartTopLine()`. Requires new `BuildColorLookup(bool isDark) → Color[101]` in `ChartColors.cs` and `BuildGradientStops()` in `ChartRenderer.cs`. |
| Segmented Control settings UI | Flat StackPanel with all settings is functional but feels unpolished. Tabbed settings with colored icon badges (matching macOS pattern) signals attention to UX quality. `CommunityToolkit.WinUI.Controls.Segmented` is already installed — zero additional NuGet. | MEDIUM | Full rewrite of `SettingsView.xaml`. 4 tabs: General (all existing settings), Updates (version + pricing info), Account (logout + token status), About (version + credits). Uniform 40px row height, 360px width constraint. |
| Short time notation in dropdowns | "30 Sekunden", "2 Minuten" are too wide for right-aligned controls at 360px. Compact "30s", "2min" notation is language-independent (universal abbreviations) and improves layout density without losing clarity. | LOW | Modify `SettingsViewModel.cs` option lists only. No localization keys needed — s/min are universal. |

### Anti-Features (Explicitly Out of Scope)

| Feature | Why Requested | Why Problematic | Alternative |
|---------|---------------|-----------------|-------------|
| System tray flame icon for burn rate | macOS shows a flame in the menu bar when burn rate is active — users want parity | Implementing a tray icon solely for burn rate requires full system tray infrastructure (H.NotifyIcon.WinUI, context menu, icon management) — significant effort for one indicator. No tray icon exists in ccInfoWin today. | Windows toast notification (FEAT-01c) delivers the same interrupt. Banner in main window delivers persistent state. Tray icon deferred to a dedicated V2 tray initiative. |
| ML-based usage prediction | "Smarter" prediction sounds better | Linear regression is the correct model for this domain — usage over a 15-min window is linear. ML adds cold-start problems, model training, and false confidence with small data sets. Explicitly out of scope per PROJECT.md. | Linear regression via least-squares on 15-min history window |
| Performance optimizations (incremental JSONL parsing, LRU cache) | macOS v1.9.0 added byte-offset tracking and a 200-file LRU cache for sessions | Premature optimization. ccInfoWin fetches pre-aggregated API data via WebView2 — it doesn't parse JSONL for usage numbers. JSONL reads are for token stats only. No profiling evidence of bottleneck. | Defer to future milestone if profiling shows CPU spike |
| Per-tab settings pages (frame navigation) | Separate pages per settings tab adds routing and back-stack | Overkill for a 360px window with 4 tabs and ~7 rows total. Visibility binding is sufficient and standard for this scale. | `Visibility` bindings switching content panels within a single `SettingsView.xaml` |
| Gradient desaturation for dark mode | Some apps desaturate colors in dark mode to reduce eye strain | Existing dark theme color variants in `ChartColors.cs` are already Apple system color dark variants — they are inherently appropriate for dark backgrounds. Extra desaturation adds complexity and diverges from the macOS reference rendering. | Use existing dark theme color stops as-is |

---

## Feature Dependencies

```
FEAT-01: Burn Rate Warning System
    ├── FEAT-01a: BurnRateCalculator (pure math, no UI deps)
    │       requires: UsageHistoryPoint list (already in MainViewModel._usageHistoryPoints)
    │       requires: FiveHour.Utilization from UsageData (already parsed)
    │       requires: FiveHour.ResetsAt from UsageData (already in _fiveHourResetsAt)
    ├── FEAT-01b: Warning Banner
    │       requires: FEAT-01a (BurnRatePrediction.MinutesUntilLimit for display text)
    │       requires: BurnRateWarningBrush in AppTheme.xaml (new theme resource)
    └── FEAT-01c: Toast Notification
            requires: FEAT-01a (prediction result)
            requires: AppNotificationManager registration in App.xaml.cs

FEAT-02: Chart Horizontal Gradient
    ├── FEAT-02a: BuildColorLookup (Color[101]) in ChartColors.cs
    │       independent, no deps
    ├── FEAT-02b: BuildGradientStops in ChartRenderer.cs
    │       requires: FEAT-02a (color lookup as input)
    └── FEAT-02c/d: Gradient rendering in ChartDrawing.cs
            requires: FEAT-02a + FEAT-02b
            replaces: GetZoneSegments() iteration (can delete method after migration)

FEAT-03: Settings Redesign
    └── all content tabs preserve existing ViewModel bindings
            no new ViewModel properties needed (General tab reuses all existing bindings)
            requires: SettingsBadge*Brush resources in AppTheme.xaml

FEAT-05: Session Watcher Fix
    └── independent verification — no code dependencies
```

### Dependency Notes

- **FEAT-01a must precede FEAT-01b and FEAT-01c:** Both the banner and toast need a `BurnRatePrediction?` from the calculator. The MainViewModel integration point is the same for both — calculate once, publish to both consumers.
- **FEAT-01 toast requires AppNotificationManager registration:** Windows App SDK desktop apps must call `AppNotificationManager.Default.Register()` at startup. If not already registered, the toast silently fails. Must be in `App.xaml.cs` before any notification is sent.
- **FEAT-02 gradient replaces zone segments, not extends them:** `GetZoneSegments()` in `ChartRenderer.cs` becomes unused after FEAT-02 migration. Both `DrawChartFills()` and `DrawChartTopLine()` must be updated together — partial migration (fills use gradient, lines use zones) creates visual mismatch.
- **FEAT-03 is fully independent:** Settings redesign touches only XAML and ViewModel option-list formatting. No service layer changes. Cannot break any existing feature.
- **`UsageHistoryPoint.Utilization` is normalized 0–1:** This is the critical conversion point for FEAT-01a. The macOS algorithm runs on 0–100. The calculator must multiply history point utilization by 100 before regression, or the slope will be 100x too small and no warnings will ever fire.

---

## MVP Definition for This Milestone

### Ship Together (v1.3)

All four phases constitute the complete milestone. Each delivers a distinct user-visible improvement:

- [ ] Phase 1: Burn Rate Warning (FEAT-01a, 01b, 01c) — headline feature, highest user value
- [ ] Phase 2: Chart Horizontal Gradient (FEAT-02) — visual differentiator, replaces flat fills
- [ ] Phase 3: Settings Redesign (FEAT-03) — UX polish, organizes growing settings surface
- [ ] Phase 4: Session Watcher Verification (FEAT-05) — reliability fix, likely no code change

### Defer to Future Milestones

- FEAT-01d: Tray icon flame indicator — requires full tray icon infrastructure first
- FEAT-06: Performance optimizations (incremental JSONL, LRU cache) — no profiling evidence of need
- V2-01: System tray with quick status overview — listed in PROJECT.md future backlog

---

## Feature Prioritization Matrix

| Feature | User Value | Implementation Cost | Priority |
|---------|------------|---------------------|----------|
| Burn Rate Warning (FEAT-01) | HIGH — prevents silent quota exhaustion | HIGH — new calculator + toast service + banner | P1 |
| Chart Horizontal Gradient (FEAT-02) | MEDIUM — visual improvement, not functional | HIGH — Win2D gradient brush, path rework | P2 |
| Settings Redesign (FEAT-03) | MEDIUM — UX organization, not new function | MEDIUM — XAML rewrite, tab structure | P2 |
| Session Watcher Fix (FEAT-05) | LOW — already working, verification only | LOW — review + test, likely no change | P3 |

---

## Implementation Complexity Assessment

| Phase | Effort | Files Changed | Risk |
|-------|--------|---------------|------|
| Phase 1: Burn Rate Warning | HIGH | New: `BurnRateCalculator.cs`, `BurnRatePrediction.cs`, `BurnRateNotificationService.cs`, `IBurnRateNotificationService.cs`. Modify: `MainViewModel.cs`, `MainView.xaml`, `App.xaml.cs`, `AppTheme.xaml`, 2x `.resw` | MEDIUM — `AppNotificationManager` requires startup registration; toast behavior differs from macOS; utilization scale conversion is a subtle bug magnet |
| Phase 2: Chart Gradient | HIGH | `ChartColors.cs`, `ChartRenderer.cs`, `ChartDrawing.cs` (both methods), verify `ExportHelper.cs` | MEDIUM — Win2D `CanvasLinearGradientBrush` lifetime must be inside draw session scope; many gradient stops at low data densities; performance on lower-end hardware needs spot-check |
| Phase 3: Settings Redesign | MEDIUM | Rewrite `SettingsView.xaml`, modify `SettingsViewModel.cs` (option lists only), `AppTheme.xaml` (badge brushes), 2x `.resw` | LOW — pure XAML restructure, no service layer; `CommunityToolkit.WinUI.Controls.Segmented` already installed and in use |
| Phase 4: Watcher Verification | LOW | Verify `JsonlService.cs` only — `NotifyFilter` already correct | NEGLIGIBLE — already configured with `LastWrite \| FileName \| Size` and `IncludeSubdirectories = true` |

---

## Pitfall Flags for Phase Implementation

| Phase | Key Pitfall |
|-------|-------------|
| Phase 1 | `UsageHistoryPoint.Utilization` is 0–1 (normalized). The burn rate algorithm runs on 0–100. Multiply history point utilization by 100 in `BurnRateCalculator.Predict()` before regression. Failing to do this makes the slope 100x too small — no warning ever fires. |
| Phase 1 | Toast notification requires `AppNotificationManager.Default.Register()` in `App.xaml.cs` before first use. Unpackaged apps (no MSIX) must also ensure the app's display name is registered — check Windows App SDK 1.8 docs for unpackaged toast registration requirements. |
| Phase 1 | One-shot notification per cycle: the `_notifiedBurnRate` flag must reset when the prediction clears (slope reverses, window resets, utilization drops below 20%). Otherwise the user gets no notification on the next warning cycle. |
| Phase 2 | `CanvasLinearGradientBrush` must be created and disposed within the draw session. It is an `IDisposable` — use `using`. Creating it outside the `Draw` event and caching it will cause `ObjectDisposedException` on the second draw. |
| Phase 2 | When all data points have zero utilization, skip gradient rendering entirely (same as current `DrawChartFills` behavior for empty data). A gradient brush from X=0 to X=0 is undefined behavior in Win2D. |
| Phase 3 | `CommunityToolkit.WinUI.Controls.Segmented` tab switching via `SelectedIndex` binding: ensure the content switcher uses `Visibility.Collapsed` (not `Visibility.Hidden`) for non-active tabs — WinUI 3 does not have `Hidden`. |
| Phase 3 | Colored icon badges in `SegmentedItem.Icon` require a `DataTemplate` wrapping a `Border` + `FontIcon`. The `SegmentedItem` does not accept direct content in its `Icon` property in all SDK versions — verify the correct property path for `CommunityToolkit.WinUI.Controls.Segmented` 8.x. |
| Phase 4 | The watcher is already configured correctly (`NotifyFilters.LastWrite \| NotifyFilters.FileName \| NotifyFilters.Size`, `IncludeSubdirectories = true`). The verification test is: rename a Claude Code project directory externally while ccInfoWin is open, then confirm the session dropdown updates. If it does, no code change is needed. |

---

## Sources

- `spec/v1.10.0-macOS/spec-release-1.8.3-to-1.10.0.md` — Primary specification (HIGH confidence)
- `.planning/PROJECT.md` — Project context, constraints, key decisions (HIGH confidence)
- `CCInfoWindows/CCInfoWindows/Helpers/ChartDrawing.cs` — Current zone-segment fill implementation (HIGH confidence)
- `CCInfoWindows/CCInfoWindows/Helpers/ChartColors.cs` — Existing color table, dark/light stops (HIGH confidence)
- `CCInfoWindows/CCInfoWindows/Helpers/ChartRenderer.cs` — `GetZoneSegments()`, coordinate math (HIGH confidence)
- `CCInfoWindows/CCInfoWindows/Models/UsageHistory.cs` — `UsageHistoryPoint.Utilization` is 0–1 normalized (HIGH confidence)
- `CCInfoWindows/CCInfoWindows/Services/JsonlService.cs` — FileSystemWatcher already configured correctly (HIGH confidence)
- `CCInfoWindows/CCInfoWindows/ViewModels/MainViewModel.cs` — `_usageHistoryPoints`, `_fiveHourResetsAt` already available (HIGH confidence)

---
*Feature research for: ccInfoWin v1.3 macOS v1.9.0–v1.10.0 parity delta*
*Researched: 2026-04-13*
