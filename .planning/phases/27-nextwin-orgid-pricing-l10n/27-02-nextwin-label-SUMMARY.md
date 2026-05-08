---
phase: 27-nextwin-orgid-pricing-l10n
plan: "02"
subsystem: ui/viewmodel
tags: [nextwin, l10n, mainview, resw, xaml, mvvm]
dependency_graph:
  requires:
    - 27-01 (resw files last modified; ResourceCoverageTests baseline)
  provides:
    - FiveHourNextWindowText + IsFiveHourNextWindowVisible ObservableProperty in MainViewModel
    - TextBlock below 5h-countdown in MainView.xaml bound to ViewModel properties
    - MainView.NextWindow.LabelDe + MainView.NextWindow.LabelEn in de-DE + en-US resw
  affects:
    - CCInfoWindows/CCInfoWindows/ViewModels/MainViewModel.cs
    - CCInfoWindows/CCInfoWindows/Views/MainView.xaml
    - CCInfoWindows/CCInfoWindows/Strings/en-US/Resources.resw
    - CCInfoWindows/CCInfoWindows/Strings/de-DE/Resources.resw
    - CCInfoWindows.Tests/Localization/ResourceCoverageTests.cs
tech_stack:
  added: []
  patterns:
    - CommunityToolkit.Mvvm partial void On*Changed() for IsSessionExpired cross-prop recompute
    - WinUI3Localizer.Get().GetLocalizedString() for culture-switched format keys
    - CultureInfo.CurrentUICulture.Name.StartsWith("de") locale selection
    - DateTimeOffset.LocalDateTime.ToString(format, culture) for locale-aware day names
key_files:
  created: []
  modified:
    - CCInfoWindows/CCInfoWindows/ViewModels/MainViewModel.cs
    - CCInfoWindows/CCInfoWindows/Views/MainView.xaml
    - CCInfoWindows/CCInfoWindows/Strings/en-US/Resources.resw
    - CCInfoWindows/CCInfoWindows/Strings/de-DE/Resources.resw
    - CCInfoWindows.Tests/Localization/ResourceCoverageTests.cs
decisions:
  - "D-NW-01: TextBlock placed directly below FiveHourCountdown in Grid.Column=1 vertical StackPanel"
  - "D-NW-02: IsFiveHourNextWindowVisible = false when _fiveHourResetsAt is null OR IsSessionExpired; OnIsSessionExpiredChanged partial wires this"
  - "D-NW-03: Two resw keys (LabelDe + LabelEn) with identical format-string values in both locales (structural parity per L10N-02)"
  - "D-NW-04: ObservableProperty _fiveHourNextWindowText + _isFiveHourNextWindowVisible; RecomputeNextWindowLabel() called from 4 sites"
metrics:
  duration: "~20 minutes"
  completed: "2026-05-08"
  tasks_completed: 3
  files_changed: 5
---

# Phase 27 Plan 02: Next-Window Label Summary

Added absolute 5h-window reset-time label below countdown in MainView via two new ObservableProperties in MainViewModel, culture-switched format string lookup, and a direct XAML TextBlock binding with BoolToVisibilityConverter.

## Tasks Completed

| Task | Name | Commit | Status |
|------|------|--------|--------|
| 1 | Add 2 NextWindow.* resw key pairs (DE + EN) + extend ResourceCoverageTests | 8b5f2c7 | Done |
| 2 | Add FiveHourNextWindowText + IsFiveHourNextWindowVisible to MainViewModel | 2dbf833 | Done |
| 3 | Add NextWindow TextBlock to MainView.xaml below FiveHourCountdown | 9d42119 | Done |

## Resw Keys Added

| Key | en-US | de-DE |
|-----|-------|-------|
| `MainView.NextWindow.LabelDe` | `ddd d.M. HH:mm` | `ddd d.M. HH:mm` |
| `MainView.NextWindow.LabelEn` | `ddd HH:mm` | `ddd HH:mm` |

Both keys added to both locale files (structural parity — same values because these are format patterns, not human-readable strings).

## MainViewModel Changes

- `using System.Globalization` added
- `[ObservableProperty] private string _fiveHourNextWindowText = string.Empty;`
- `[ObservableProperty] private bool _isFiveHourNextWindowVisible;`
- `private void RecomputeNextWindowLabel()` — selects format key by `CultureInfo.CurrentUICulture`, calls `Localizer.Get().GetLocalizedString(formatKey)`, formats via `_fiveHourResetsAt.Value.LocalDateTime.ToString(format, culture)`
- 4 call-sites wired:
  1. `InitializeAsync` — after cold-start `_fiveHourResetsAt = history.ResetsAt` assignment
  2. `UpdateUsagePropertiesAsync` else-branch — after `_fiveHourResetsAt = null`
  3. `AppendHistoryPointAsync` — after `_fiveHourResetsAt = apiResetsAt`
  4. `UpdateCountdowns` — after `FiveHourCountdown = CountdownFormatter.FormatCountdown(_fiveHourResetsAt)`
- `partial void OnIsSessionExpiredChanged(bool value) => RecomputeNextWindowLabel()` — hides label when auth banner appears (D-NW-02)

## XAML Changes (MainView.xaml)

Grid.Column="1" in the 5h-window percentage/countdown row restructured:
- Outer: `StackPanel Orientation="Vertical" Spacing="2"`
- Inner (preserved): `StackPanel Orientation="Horizontal" Spacing="4"` with FontIcon + FiveHourCountdown TextBlock
- New: `TextBlock Text="{x:Bind ViewModel.FiveHourNextWindowText, Mode=OneWay}"` with `Visibility` bound to `IsFiveHourNextWindowVisible` via `BoolToVisibilityConverter`

## Test Coverage Delta

ResourceCoverageTests: +2 keys in RequiredKeys + ExpectedEnUs + ExpectedDeDe.
Test result: 4/4 passed.

Full suite baseline from Wave 1: 321 passed, 2 failed (pre-existing ClaudeApiServiceTests failures — unchanged).

## Deviations from Plan

None — plan executed exactly as written. All 4 call-sites, the partial method, and the XAML container restructure match the plan specification.

## Visual Smoke Deferred

Manual smoke-test (launch app, observe absolute time below countdown; trigger 401 to verify collapse) was not performed — no running app instance available in this execution context. The label's runtime behavior is structurally correct per:
- `RecomputeNextWindowLabel` logic reviewed
- `BoolToVisibilityConverter` already validated in prior phases
- Build passes without errors

Smoke-test can be performed at next app launch.

## Known Stubs

None — `FiveHourNextWindowText` is fully wired from `_fiveHourResetsAt` through `RecomputeNextWindowLabel`. No placeholder values.

## Threat Surface

No new threat surface beyond plan's threat model. `FiveHourNextWindowText` is author-controlled (format string from bundled resw) applied to a `DateTimeOffset?` from the existing trusted HTTPS source. No new network endpoints or auth paths introduced.

## Self-Check: PASSED

- `CCInfoWindows/CCInfoWindows/Strings/en-US/Resources.resw` — MainView.NextWindow.LabelDe + .LabelEn present (committed 8b5f2c7)
- `CCInfoWindows/CCInfoWindows/Strings/de-DE/Resources.resw` — MainView.NextWindow.LabelDe + .LabelEn present (committed 8b5f2c7)
- `CCInfoWindows.Tests/Localization/ResourceCoverageTests.cs` — both keys in RequiredKeys + ExpectedEnUs + ExpectedDeDe (committed 8b5f2c7)
- `CCInfoWindows/CCInfoWindows/ViewModels/MainViewModel.cs` — FiveHourNextWindowText, IsFiveHourNextWindowVisible, RecomputeNextWindowLabel, OnIsSessionExpiredChanged, 4 call-sites (committed 2dbf833)
- `CCInfoWindows/CCInfoWindows/Views/MainView.xaml` — TextBlock with FiveHourNextWindowText binding (committed 9d42119)
- Build: 0 Fehler (verified)
- ResourceCoverageTests: 4/4 passed (verified)
