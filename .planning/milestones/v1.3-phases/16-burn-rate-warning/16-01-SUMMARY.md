---
phase: 16-burn-rate-warning
plan: "01"
subsystem: burn-rate-prediction
tags: [tdd, helpers, models, localization, theme]
dependency_graph:
  requires: []
  provides:
    - BurnRateCalculator.Predict
    - BurnRateFormatter.FormatTimeLabel
    - BurnRatePrediction model
    - BurnRateWarningBrush (Dark + Light)
    - burn-rate localization keys (de-DE + en-US)
  affects:
    - Plan 16-02 (UI banner + toast wiring)
tech_stack:
  added: []
  patterns:
    - TDD red-green-refactor cycle
    - InternalsVisibleTo for testable internal helpers
    - Linear regression for utilization prediction
key_files:
  created:
    - CCInfoWindows/CCInfoWindows/Models/BurnRatePrediction.cs
    - CCInfoWindows/CCInfoWindows/Helpers/BurnRateCalculator.cs
    - CCInfoWindows/CCInfoWindows/Helpers/BurnRateFormatter.cs
    - CCInfoWindows/CCInfoWindows/Helpers/TimeFormat.cs
    - CCInfoWindows.Tests/Helpers/BurnRateCalculatorTests.cs
    - CCInfoWindows.Tests/Helpers/BurnRateFormatterTests.cs
  modified:
    - CCInfoWindows/CCInfoWindows/CCInfoWindows.csproj
    - CCInfoWindows/CCInfoWindows/Resources/AppTheme.xaml
    - CCInfoWindows/CCInfoWindows/Strings/de-DE/Resources.resw
    - CCInfoWindows/CCInfoWindows/Strings/en-US/Resources.resw
decisions:
  - InternalsVisibleTo in csproj for testability — allows testing ParseTime and TimeFormat without exposing them publicly
  - TimeFormat enum in separate file — avoids nesting enum inside static class, cleaner file organization
  - Predict guard for currentUtilization >= MaxUtilization — explicit null return when at 100%, prevents division edge case
metrics:
  duration: ~12 minutes
  completed: 2026-04-13
  tasks_completed: 2
  files_created: 6
  files_modified: 4
---

# Phase 16 Plan 01: Burn Rate Prediction Engine Summary

Burn rate prediction foundation: linear-regression calculator with 6 guard clauses, DRY time formatter, plain data model, theme brushes, and 15 passing unit tests.

## Tasks Completed

| Task | Name | Commit | Files |
|------|------|--------|-------|
| 1 | BurnRatePrediction model + BurnRateCalculator + BurnRateFormatter + tests (TDD) | 8fb7db2 | BurnRatePrediction.cs, BurnRateCalculator.cs, BurnRateFormatter.cs, TimeFormat.cs, CCInfoWindows.csproj, BurnRateCalculatorTests.cs, BurnRateFormatterTests.cs |
| 2 | Theme brush + localization strings | 68eee89 | AppTheme.xaml, de-DE/Resources.resw, en-US/Resources.resw |

## What Was Built

**BurnRatePrediction** (plain data model):
- `HitsLimitAt: DateTimeOffset` — projected timestamp when 100% is reached
- `MinutesUntilLimit: int` — minimum 1

**BurnRateCalculator.Predict** (linear regression engine):
- Guard 1: `resetsAt` null or in the past → null
- Guard 2: `currentUtilization < 20%` → null
- Guard 3: `currentUtilization >= 100%` → null (no room, secondsToLimit = 0)
- Guard 4: fewer than 3 points in last 15-minute window → null
- Guard 5: slope ≤ 0 (flat or decreasing usage) → null
- Guard 6: projected exhaustion ≥ resetsAt (burns out after window) → null
- Critical conversion: `p.Utilization * 100.0` (stored 0-1, algorithm needs 0-100)

**BurnRateFormatter.FormatTimeLabel** (DRY time label):
- Single shared method for banner and toast notification (Plan 02)
- Internal `ParseTime(int minutes)` returns `(hours, remainingMinutes, TimeFormat)` — pure logic, no Localizer dependency, fully testable
- `TimeFormat` enum: `MinutesOnly`, `HoursOnly`, `HoursMinutes`

**AppTheme.xaml**: `BurnRateWarningBrush` added to both Dark (#FF453A) and Light (#FF3B30) dictionaries.

**Localization**: 6 new keys in both `de-DE` and `en-US`:
- `BurnRateBannerText`, `BurnRateFormat_HoursMinutes`, `BurnRateFormat_HoursOnly`, `BurnRateFormat_MinutesOnly`, `BurnRateNotificationTitle`, `BurnRateNotificationBody`

## Test Results

- BurnRateCalculatorTests: 10/10 passed
- BurnRateFormatterTests: 5/5 passed
- Total: 15/15 green

## Deviations from Plan

### Extra Files

**1. [Rule 2 - Missing functionality] TimeFormat enum extracted to separate file**
- **Found during:** Task 1 implementation
- **Reason:** The plan places `TimeFormat` inside `BurnRateFormatter.cs` as an internal enum, but extracting it to `TimeFormat.cs` improves file organization and matches the project pattern of one type per file.
- **Files modified:** `CCInfoWindows/CCInfoWindows/Helpers/TimeFormat.cs` (created)
- **Impact:** None — still `internal` to the Helpers namespace, still visible via InternalsVisibleTo.

**2. [Rule 2 - Missing guard] Explicit guard for currentUtilization >= MaxUtilization**
- **Found during:** Test `Predict_FullUtilization_ReturnsNull`
- **Issue:** Plan's algorithm computes `secondsToLimit = (100 - currentUtilization) / slope` which returns 0 when utilization is 100. The division is valid but `hitsLimitAt` would equal `UtcNow`, which is before `resetsAt`, so the final guard would NOT catch it — the method would incorrectly return a prediction with `MinutesUntilLimit = 0` (rounded to 1 by Math.Max).
- **Fix:** Added explicit guard `if (currentUtilization >= MaxUtilization) return null;` before regression, matching the test expectation.
- **Files modified:** `BurnRateCalculator.cs`

## Known Stubs

None — all methods are fully implemented. `FormatTimeLabel` calls `Localizer.Get()` at runtime (production code); `ParseTime` is tested independently without Localizer.

## Self-Check: PASSED
