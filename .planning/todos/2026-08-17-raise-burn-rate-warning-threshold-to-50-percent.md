---
created: 2026-08-17T19:05:00Z
title: Do not show the burn-rate warning below 50% of the 5-hour window
area: ux
files:
  - CCInfoWindows/CCInfoWindows/Helpers/BurnRateCalculator.cs
  - CCInfoWindows.Tests/Helpers/BurnRateCalculatorTests.cs
---

## Problem

The burn-rate warning fires from 20% utilization of the 5-hour window
(`BurnRateCalculator.MinimumUtilization = 20.0`). At that level the warning is noise: four fifths of
the window are still available, and a short burst of usage is enough to produce a steep enough slope
to trip the linear regression. Users see a red banner and get a Windows toast while there is nothing
to act on yet.

The warning should not appear below **50%** utilization of the 5-hour window — in the in-app banner
**and** in the Windows notification.

## Solution

Both channels are fed by the same single `Predict` return value, so one constant covers both — no
second gate needed:

- `MainViewModel.cs:837` → `IsBurnRateWarningVisible` / `BurnRateWarningText` (in-app red banner)
- `MainViewModel.cs:841` → `_usageNotificationService.CheckBurnRate(prediction)` (Windows toast)

The change:

- `BurnRateCalculator.cs:11` — `MinimumUtilization` from `20.0` to `50.0`, and update the doc comment
  so the number is not left unexplained.

No change needed in `UsageNotificationService.CheckBurnRate`: below 50% `Predict` returns null, which
takes the existing "prediction withdrawn" path and re-arms `BurnRateNotifiedWindowId`. The
once-per-window latch keeps working unchanged — the first toast then fires on the first genuine
warning at or above 50%.

## Tests

No existing test breaks (verified by reading every `Predict` call site in the test project — the
`currentUtilization` arguments are 15.0, 40.0, 50.0, 50.3, 60.0, 99.0, 100.0; the 50.0 cases stay
above the new bound because the guard is `<`). Two follow-ups:

- `BurnRateCalculatorTests.cs:68` (`Predict_TooFewPoints_ReturnsNull`, `currentUtilization: 40.0`)
  stays green but for the wrong reason — it would then be rejected by the utilization gate instead of
  the point-count gate, so it no longer tests what its name claims. Raise its value above 50.
- Add one boundary test pinning the new threshold (null just below, prediction at/above), otherwise
  the constant can be turned back down without a red test.
