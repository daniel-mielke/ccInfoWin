---
phase: 16-burn-rate-warning
verified: 2026-04-13T00:00:00Z
status: passed
score: 10/10 must-haves verified
re_verification: false
---

# Phase 16: Burn Rate Warning Verification Report

**Phase Goal:** Users are warned before their 5-hour token window runs out
**Verified:** 2026-04-13
**Status:** passed
**Re-verification:** No — initial verification

## Goal Achievement

### Observable Truths

| #  | Truth | Status | Evidence |
|----|-------|--------|----------|
| 1  | User sees a red banner with flame icon in the 5-hour window section when burn rate prediction is active | ✓ VERIFIED | MainView.xaml line 306–322: Border with BurnRateWarningBrush, flame glyph ECAD, bound to ViewModel.IsBurnRateWarningVisible |
| 2  | Banner shows localized time text like '~1h 33min' or '~33min' formatted via BurnRateFormatter.FormatTimeLabel (shared helper, no duplicate formatting logic) | ✓ VERIFIED | MainViewModel.cs FormatBurnRateText() delegates to BurnRateFormatter.FormatTimeLabel; no local hours/minutes branching found in MainViewModel or BurnRateNotificationService |
| 3  | Banner disappears automatically when prediction becomes null (slope reversal, window reset, rate drop) | ✓ VERIFIED | MainViewModel.cs lines 454–469: IsBurnRateWarningVisible = false in both the null-prediction path and the else branch (FiveHour null) |
| 4  | A Windows toast notification fires exactly once when the warning first triggers in a cycle | ✓ VERIFIED | BurnRateNotificationService.cs: _notifiedBurnRate flag prevents second fire; CheckBurnRate sets flag true before calling SendToast |
| 5  | Toast does not re-fire until the warning clears (prediction becomes null) and then re-triggers | ✓ VERIFIED | BurnRateNotificationService.cs line 27: _notifiedBurnRate = false when prediction == null resets the cycle gate |
| 6  | All banner and toast text uses localized strings from .resw files via Localizer.Get().GetLocalizedString() | ✓ VERIFIED | MainViewModel.cs uses GetLocalizedString("BurnRateBannerText"); BurnRateNotificationService.cs uses GetLocalizedString("BurnRateNotificationTitle") and ("BurnRateNotificationBody") |
| 7  | Neither BurnRateNotificationService nor MainViewModel contains its own hours/minutes formatting logic — both delegate to BurnRateFormatter | ✓ VERIFIED | Grep for FormatTimeUntilLimit and private FormatTime in both files returned no matches; both call BurnRateFormatter.FormatTimeLabel |
| 8  | BurnRateCalculator.Predict returns correct predictions for increasing usage and null for all 6 guard conditions | ✓ VERIFIED | 10 unit tests all passing (BurnRateCalculatorTests) |
| 9  | BurnRateFormatter.ParseTime correctly categorizes minutes into HoursOnly / HoursMinutes / MinutesOnly | ✓ VERIFIED | 5 unit tests all passing (BurnRateFormatterTests) |
| 10 | BurnRateWarningBrush exists in both theme dictionaries with correct hex colors | ✓ VERIFIED | AppTheme.xaml: Dark #FF453A line 27, Light #FF3B30 line 50 |

**Score:** 10/10 truths verified

---

### Required Artifacts

| Artifact | Expected | Status | Details |
|----------|----------|--------|---------|
| `CCInfoWindows/CCInfoWindows/Models/BurnRatePrediction.cs` | Plain data model | ✓ VERIFIED | class BurnRatePrediction with HitsLimitAt (DateTimeOffset) and MinutesUntilLimit (int); no FormattedTimeUntilLimit |
| `CCInfoWindows/CCInfoWindows/Helpers/BurnRateCalculator.cs` | Static prediction engine with linear regression | ✓ VERIFIED | public static class, Predict() method, all 4 named constants, p.Utilization * 100.0 conversion present |
| `CCInfoWindows/CCInfoWindows/Helpers/BurnRateFormatter.cs` | Shared time-label formatting (DRY) | ✓ VERIFIED | public static FormatTimeLabel(int), internal static ParseTime(int), uses BurnRateFormat_* keys |
| `CCInfoWindows/CCInfoWindows/Helpers/TimeFormat.cs` | Internal enum for time format type | ✓ VERIFIED | internal enum TimeFormat { MinutesOnly, HoursOnly, HoursMinutes } |
| `CCInfoWindows/CCInfoWindows/Services/Interfaces/IBurnRateNotificationService.cs` | Service contract | ✓ VERIFIED | interface IBurnRateNotificationService with void CheckBurnRate(BurnRatePrediction?) |
| `CCInfoWindows/CCInfoWindows/Services/BurnRateNotificationService.cs` | Toast implementation with one-shot flag | ✓ VERIFIED | _notifiedBurnRate flag, BurnRateFormatter.FormatTimeLabel used, AppNotificationManager.IsSupported() guard, notification.Tag = "usage-burnrate" |
| `CCInfoWindows/CCInfoWindows/ViewModels/MainViewModel.cs` | Observable properties + prediction hook | ✓ VERIFIED | [ObservableProperty] _isBurnRateWarningVisible and _burnRateWarningText, BurnRateCalculator.Predict called with data.FiveHour.Utilization (0-100), _burnRateNotificationService.CheckBurnRate called |
| `CCInfoWindows/CCInfoWindows/Views/MainView.xaml` | Red banner with flame icon | ✓ VERIFIED | Border with BurnRateWarningBrush, glyph ECAD, BoolToVisibilityConverter on IsBurnRateWarningVisible, AutomationProperties.Name, CornerRadius="6" |
| `CCInfoWindows/CCInfoWindows/App.xaml.cs` | DI registration + AppNotificationManager setup | ✓ VERIFIED | AddSingleton<IBurnRateNotificationService, BurnRateNotificationService>; IBurnRateNotificationService in MainViewModel transient registration; NotificationInvoked registered before Register() |
| `CCInfoWindows/CCInfoWindows/Resources/AppTheme.xaml` | BurnRateWarningBrush in Dark and Light dictionaries | ✓ VERIFIED | Dark #FF453A, Light #FF3B30 |
| `CCInfoWindows/CCInfoWindows/Strings/de-DE/Resources.resw` | 6 German burn rate keys | ✓ VERIFIED | BurnRateBannerText, BurnRateFormat_HoursMinutes, BurnRateFormat_HoursOnly, BurnRateFormat_MinutesOnly, BurnRateNotificationTitle, BurnRateNotificationBody all present |
| `CCInfoWindows/CCInfoWindows/Strings/en-US/Resources.resw` | 6 English burn rate keys | ✓ VERIFIED | All 6 keys present with English values |
| `CCInfoWindows.Tests/Helpers/BurnRateCalculatorTests.cs` | 10+ unit tests | ✓ VERIFIED | 10 [Fact] tests, all passing |
| `CCInfoWindows.Tests/Helpers/BurnRateFormatterTests.cs` | 5+ unit tests for ParseTime | ✓ VERIFIED | 5 [Fact] tests, all passing |

---

### Key Link Verification

| From | To | Via | Status | Details |
|------|----|-----|--------|---------|
| MainViewModel.cs | BurnRateCalculator.cs | BurnRateCalculator.Predict() call in UpdateUsageProperties | ✓ WIRED | Line 449: BurnRateCalculator.Predict(UsageHistoryPoints, data.FiveHour.Utilization, data.FiveHour.ResetsAt) |
| MainViewModel.cs | BurnRateFormatter.cs | FormatTimeLabel() call for banner text | ✓ WIRED | FormatBurnRateText() calls BurnRateFormatter.FormatTimeLabel(minutesUntilLimit) |
| MainViewModel.cs | IBurnRateNotificationService.cs | _burnRateNotificationService.CheckBurnRate() | ✓ WIRED | Line 458 and 469: both prediction paths call CheckBurnRate |
| BurnRateNotificationService.cs | BurnRateFormatter.cs | FormatTimeLabel() call for toast body | ✓ WIRED | Line 42: BurnRateFormatter.FormatTimeLabel(minutesUntilLimit) |
| MainView.xaml | MainViewModel.cs | x:Bind for IsBurnRateWarningVisible and BurnRateWarningText | ✓ WIRED | Lines 310, 312, 318: all three x:Bind expressions present with Mode=OneWay |
| BurnRateNotificationService.cs | Microsoft.Windows.AppNotifications | AppNotificationManager.Default.Show() | ✓ WIRED | Line 52: AppNotificationManager.Default.Show(notification) |
| App.xaml.cs | BurnRateNotificationService.cs | DI Singleton registration | ✓ WIRED | Line 160: AddSingleton<IBurnRateNotificationService, BurnRateNotificationService>() |
| BurnRateCalculator.cs | BurnRatePrediction.cs | Predict returns BurnRatePrediction? | ✓ WIRED | Return type is BurnRatePrediction?; returns new BurnRatePrediction { ... } |
| BurnRateCalculator.cs | UsageHistory.cs | IReadOnlyList<UsageHistoryPoint> parameter | ✓ WIRED | p.Utilization * 100.0 confirms UsageHistoryPoint consumed |
| BurnRateFormatter.cs | en-US/Resources.resw | Localizer.Get().GetLocalizedString for BurnRateFormat_* keys | ✓ WIRED | GetLocalizedString("BurnRateFormat_HoursOnly"), ("BurnRateFormat_HoursMinutes"), ("BurnRateFormat_MinutesOnly") |
| BurnRateCalculatorTests.cs | BurnRateCalculator.cs | xUnit tests calling Predict() | ✓ WIRED | 10 tests, all call BurnRateCalculator.Predict() |
| BurnRateFormatterTests.cs | BurnRateFormatter.cs | xUnit tests calling ParseTime() | ✓ WIRED | 5 tests, all call BurnRateFormatter.ParseTime() |

---

### Data-Flow Trace (Level 4)

| Artifact | Data Variable | Source | Produces Real Data | Status |
|----------|---------------|--------|--------------------|--------|
| MainView.xaml banner | ViewModel.IsBurnRateWarningVisible / ViewModel.BurnRateWarningText | BurnRateCalculator.Predict() in UpdateUsageProperties (called on every poll cycle) | Yes — derives from live API usage data (data.FiveHour.Utilization, UsageHistoryPoints) | ✓ FLOWING |
| BurnRateNotificationService toast | prediction.MinutesUntilLimit | Same poll cycle prediction passed via CheckBurnRate() | Yes — same BurnRatePrediction object from calculator | ✓ FLOWING |

---

### Behavioral Spot-Checks

| Behavior | Command | Result | Status |
|----------|---------|--------|--------|
| 10 BurnRateCalculator tests pass | dotnet test --filter BurnRateCalculator --no-build | 10/10 passed, 0 ms | ✓ PASS |
| 5 BurnRateFormatter tests pass | dotnet test --filter BurnRateFormatter --no-build | 5/5 passed, 0 ms | ✓ PASS |
| Visual banner + toast | Manual (human checkpoint) | Approved by user | ✓ PASS |

---

### Requirements Coverage

| Requirement | Source Plan | Description | Status | Evidence |
|-------------|------------|-------------|--------|----------|
| BURN-01 | 16-01, 16-02 | Red warning banner with flame icon when projected exhaustion before reset | ✓ SATISFIED | MainView.xaml: BurnRateWarningBrush border + ECAD glyph; IsBurnRateWarningVisible drives Visibility |
| BURN-02 | 16-01, 16-02 | Banner shows localized time-until-limit in compact format | ✓ SATISFIED | BurnRateFormatter.FormatTimeLabel with BurnRateFormat_* keys; FormatBurnRateText wraps in BurnRateBannerText template |
| BURN-03 | 16-01, 16-02 | Warning disappears when rate drops, window resets, or slope reverses | ✓ SATISFIED | IsBurnRateWarningVisible = false when prediction == null or FiveHour == null |
| BURN-04 | 16-02 | Windows toast notification (one-shot) when warning first triggers | ✓ SATISFIED | BurnRateNotificationService: AppNotificationManager.Default.Show, one-shot flag |
| BURN-05 | 16-02 | Toast does not re-fire until warning clears and re-triggers | ✓ SATISFIED | _notifiedBurnRate reset to false when prediction == null |
| BURN-06 | 16-01 | Linear regression over last 15 minutes, minimum 20% utilization and 3 data points | ✓ SATISFIED | BurnRateCalculator.cs: MinimumUtilization=20.0, LookbackWindowMinutes=15, MinimumDataPoints=3; linear regression implemented |
| BURN-07 | 16-01, 16-02 | All burn rate text localized in German and English | ✓ SATISFIED | 6 keys in both de-DE and en-US .resw; all text access via Localizer.Get().GetLocalizedString() |

All 7 requirement IDs (BURN-01 through BURN-07) satisfied. No orphaned requirements detected.

---

### Anti-Patterns Found

| File | Line | Pattern | Severity | Impact |
|------|------|---------|----------|--------|
| — | — | — | — | None found |

No TODO/FIXME/placeholder comments, no stub returns, no hardcoded empty data found in any phase-16 files.

---

### Human Verification Required

Human checkpoint completed (from 16-02-PLAN.md Task 3). User approved:
- Red banner visible in 5-hour section
- Localized text correct in DE and EN
- Toast notification fires in Windows Action Center
- Toast one-shot behavior confirmed

No additional human verification required.

---

## Summary

Phase 16 goal is fully achieved. All 7 requirement IDs are satisfied, all 14 artifacts exist with substantive implementations, all key links are wired, and data flows from the live poll cycle through to both the banner and toast. The 15/15 unit tests provide regression coverage for the prediction engine and time formatter. The DRY constraint (no duplicate hours/minutes formatting logic) is enforced: both MainViewModel and BurnRateNotificationService delegate to BurnRateFormatter.FormatTimeLabel exclusively.

---

_Verified: 2026-04-13_
_Verifier: Claude (gsd-verifier)_
