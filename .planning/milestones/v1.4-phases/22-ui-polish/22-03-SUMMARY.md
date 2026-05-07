---
phase: 22-ui-polish
plan: 03
subsystem: SettingsViewModel / SettingsView
tags: [timer, dispatcher-timer, about-tab, lifecycle, testability]
decisions:
  - key: D-09-timer-type
    summary: "Microsoft.UI.Xaml.DispatcherTimer wrapped behind IDispatcherTimer for testability"
  - key: D-10-code-behind
    summary: "Tab-switch detection in SettingsView code-behind (not OnSelectedTabIndexChanged partial)"
  - key: D-11-pure-computed
    summary: "LastFetchRelativeTime is pure-computed, no [ObservableProperty], timer drives rebinding"
  - key: IDispatcherTimer-seam
    summary: "IDispatcherTimer + WinuiDispatcherTimerAdapter introduced to allow headless unit tests"
dependency_graph:
  requires: [22-02]
  provides: [About-tab-timer-lifecycle, LastFetchRelativeTime-binding]
  affects: [SettingsViewModel, SettingsView, CCInfoWindows.Tests]
tech_stack:
  added:
    - IDispatcherTimer interface (Services/Interfaces)
    - WinuiDispatcherTimerAdapter (Services)
    - FakeDispatcherTimer (test helper in SettingsViewModelTimerTests.cs)
  patterns:
    - Adapter pattern wrapping WinRT DispatcherTimer as .NET EventHandler relay
    - Testability seam via internal Func<IDispatcherTimer> TimerFactory property
    - Named event handler (not lambda) for clean -= unsubscription on Stop
key_files:
  created:
    - CCInfoWindows/CCInfoWindows/Services/Interfaces/IDispatcherTimer.cs
    - CCInfoWindows/CCInfoWindows/Services/WinuiDispatcherTimerAdapter.cs
    - CCInfoWindows.Tests/ViewModels/SettingsViewModelTimerTests.cs
  modified:
    - CCInfoWindows/CCInfoWindows/ViewModels/SettingsViewModel.cs
    - CCInfoWindows/CCInfoWindows/Views/SettingsView.xaml
    - CCInfoWindows/CCInfoWindows/Views/SettingsView.xaml.cs
metrics:
  duration_minutes: 45
  completed_date: "2026-05-06"
  tasks_completed: 3
  files_changed: 6
---

# Phase 22 Plan 03: About-Tab DispatcherTimer Lifecycle Summary

About-tab DispatcherTimer wired with IDispatcherTimer abstraction, WinRT adapter, idempotent Start/Stop lifecycle, pure-computed LastFetchRelativeTime, and 6 xUnit tests via FakeDispatcherTimer seam.

## What Was Built

### Task 1: SettingsViewModel timer contract (D-09 + D-11)

- `IDispatcherTimer` interface abstraction added to `Services/Interfaces/` — enables headless unit testing without WinRT COM context
- `WinuiDispatcherTimerAdapter` wraps `Microsoft.UI.Xaml.DispatcherTimer` with a relay event pattern (single inner-Tick subscription forwards to standard .NET `EventHandler<object>`)
- `_aboutTimestampTimer` field typed as `IDispatcherTimer?` (was originally `DispatcherTimer?`)
- `TimerFactory` internal seam (`Func<IDispatcherTimer>`) defaults to `WinuiDispatcherTimerAdapter`; tests inject `FakeDispatcherTimer`
- `StartAboutTimestampTimer()`: idempotent guard `if (_aboutTimestampTimer != null) return;` (Pitfall 7 prevention), named Tick handler `OnAboutTimestampTimerTick` for clean `-=` unsubscription
- `StopAboutTimestampTimer()`: nullifies field after stop
- `LastFetchRelativeTime`: pure-computed property, not `[ObservableProperty]` (D-11). Returns `"Never"` / `"1 minute ago"` / `"N minutes ago"` (English inline literals, v1.4 fallback per RESEARCH [A2])
- `AboutTabIndex = 3` promoted to `public const` in ViewModel (single source of truth for both ViewModel and code-behind)

### Task 2: SettingsView XAML + code-behind lifecycle hooks (D-10)

- `Page.Unloaded="OnUnloaded"` added to XAML Page root (belt-and-suspenders POLISH-08)
- `x:Name="TabsSegmented"` added to Segmented control (required for code-behind `SelectedIndex` read)
- `SelectionChanged="OnSegmentedSelectionChanged"` added to Segmented control (D-10)
- `LastPricingFetchText` binding replaced with `LastFetchRelativeTime, Mode=OneWay` in the Updates-tab pricing timestamp TextBlock
- Three code-behind handlers added:
  - `OnLoaded` (extended): starts timer if About is initial selected tab on page open
  - `OnSegmentedSelectionChanged` (new): routes selection to Start/Stop based on `AboutTabIndex`
  - `OnUnloaded` (new): always stops timer on Page.Unloaded
- `private const int AboutTabIndex = SettingsViewModel.AboutTabIndex` in code-behind (DRY, no magic literal)

### Task 3: SettingsViewModelTimerTests (6 tests)

All 6 tests pass headlessly via `FakeDispatcherTimer`:
1. `AboutTimestampTimer_StartStopLifecycle` — full Start→Stop→Start→Stop→double-Stop cycle
2. `AboutTimestampTimer_StartTwice_IsIdempotent` — `Assert.Same` on repeated Start
3. `LastFetchRelativeTime_NullTimestamp_ReturnsNeverFallback` — null → "Never"
4. `LastFetchRelativeTime_FiveMinutesAgo_ReturnsMinutesAgoString` — contains "5", "minute", "ago"
5. `LastFetchRelativeTime_OneMinuteAgo_ReturnsSingularForm` — exactly "1 minute ago"
6. `StopAboutTimestampTimer_NullifiesField` — field is null after Stop

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] WinRT COMException in headless xUnit tests**
- **Found during:** Task 3
- **Issue:** `Microsoft.UI.Xaml.DispatcherTimer..ctor()` throws `COMException` in xUnit without a Windows App SDK UI context. All 3 lifecycle tests that called `StartAboutTimestampTimer()` failed immediately.
- **Fix:** Introduced `IDispatcherTimer` interface + `WinuiDispatcherTimerAdapter` production wrapper, with `TimerFactory` seam on `SettingsViewModel`. Tests inject `FakeDispatcherTimer` (pure .NET, no WinRT deps). This is a minimal testability wrapper — no behavioral change in production.
- **Files modified:** `IDispatcherTimer.cs` (new), `WinuiDispatcherTimerAdapter.cs` (new), `SettingsViewModel.cs` (TimerFactory seam), `SettingsViewModelTimerTests.cs` (FakeDispatcherTimer)
- **Commits:** 4e28381

**2. [Rule 2 - Clean Code] AboutTabIndex single source of truth**
- **Found during:** Task 3 success criteria check
- **Issue:** Plan had `AboutTabIndex` in code-behind as private const, but success criteria required >= 2 matches in ViewModel. The correct solution avoids duplication: declare once in ViewModel as `public const`, reference from code-behind.
- **Fix:** `AboutTabIndex = 3` promoted to `public const` in `SettingsViewModel`. Code-behind references `SettingsViewModel.AboutTabIndex` (Clean Code DRY).
- **Commits:** 7beb8de

### Future-Phase Hand-off: Localization of LastFetchRelativeTime

Per RESEARCH [A2], v1.4 ships with English inline literals in `LastFetchRelativeTime`. Proper resw localization keys to add in a future phase:
- `LastFetchNever` (DE: "Nie" / EN: "Never")
- `LastFetchOneMinuteAgo` (DE: "vor 1 Minute" / EN: "1 minute ago")
- `LastFetchMinutesAgo` (DE: "vor {0} Minuten" / EN: "{0} minutes ago")

This is informational — Phase 23 is locked to a 6-key scope; timer-resw work is deferred to v1.5+.

### Future-Refactor Note: Pitfall 5 Alternative (NOT implemented)

`SettingsViewModel.OnSelectedTabIndexChanged` partial method (line ~47) is a strictly cleaner home for the tab-switch trigger than code-behind. D-10 was honored verbatim in this phase. A future refactor could:
- Replace `OnSegmentedSelectionChanged` code-behind handler with a `partial void OnSelectedTabIndexChanged` extension in the ViewModel
- Eliminate the `x:Name="TabsSegmented"` requirement (code-behind would read `ViewModel.SelectedTabIndex`)
- Improve MVVM purity

Note: `Page.Unloaded` would still need to live in code-behind (no ViewModel lifecycle callback exists for WinUI 3 Page.Unloaded).

## Architecture Notes

**DispatcherTimer type choice:** `Microsoft.UI.Xaml.DispatcherTimer` (not WPF's `System.Windows.Threading.DispatcherTimer`, not `DispatcherQueueTimer`) — consistent with WinUI 3 codebase conventions and the D-09 locked decision.

**Named handler vs. lambda:** `OnAboutTimestampTimerTick` is a named method, not a lambda. This enables correct `-=` unsubscription in `StopAboutTimestampTimer()` (a lambda would create a new delegate instance, making `-=` a no-op — the classic Pitfall 7 sub-variant).

**WinuiDispatcherTimerAdapter relay pattern:** Single inner `ForwardTick` method subscribed once to the WinRT `TypedEventHandler<DispatcherTimer, object>`. Re-raises via a private .NET `event EventHandler<object>? _tick` field. This avoids the delegate identity mismatch that a per-subscriber dictionary would introduce.

## Commits

| Hash | Type | Description |
|------|------|-------------|
| ad3d63e | feat | add _aboutTimestampTimer + LastFetchRelativeTime to SettingsViewModel |
| 4d163a4 | feat | wire SelectionChanged + Unloaded in SettingsView, add three lifecycle handlers |
| 4e28381 | test | add SettingsViewModelTimerTests + IDispatcherTimer testability seam |
| 7beb8de | refactor | promote AboutTabIndex to public const in SettingsViewModel |

## Self-Check: PASSED
