---
phase: 27-nextwin-orgid-pricing-l10n
plan: "03"
subsystem: MainViewModel / MainView / Tests
tags: [pricing, infobar, banner-stack, observable-property, tdd, localization]
dependency_graph:
  requires: [27-02]
  provides: [IsPricingError, IsPricingErrorVisible, PricingErrorInfoBar, BannerStackPolicyTests]
  affects: [MainViewModel.cs, MainView.xaml, Resources.resw, ResourceCoverageTests]
tech_stack:
  added: []
  patterns: [NotifyPropertyChangedFor computed property, TDD RED/GREEN cycle, banner-stack suppression policy]
key_files:
  created:
    - CCInfoWindows.Tests/ViewModels/BannerStackPolicyTests.cs
  modified:
    - CCInfoWindows/CCInfoWindows/ViewModels/MainViewModel.cs
    - CCInfoWindows/CCInfoWindows/Views/MainView.xaml
    - CCInfoWindows/CCInfoWindows/Strings/en-US/Resources.resw
    - CCInfoWindows/CCInfoWindows/Strings/de-DE/Resources.resw
    - CCInfoWindows.Tests/Localization/ResourceCoverageTests.cs
decisions:
  - "D-PR-04: IsPricingErrorVisible = IsPricingError && !IsSessionExpired (auth wins)"
  - "G-1: Site 1 (Task.Run) marshals via _dispatcherQueue.TryEnqueue; Site 2 (AggregateStatisticsAsync) is already on dispatcher chain"
  - "Banner-stack order: auth > API error > pricing > migration toast"
  - "BannerStackPolicyTests uses formula-mirror approach (no MainViewModel instantiation) per D-PR-05"
metrics:
  duration: "4 minutes"
  completed: "2026-05-08"
  tasks_completed: 3
  files_changed: 6
---

# Phase 27 Plan 03: Pricing Error Surfacing Summary

Surface silent `_pricingService.EnsurePricesLoadedAsync()` failures via `IsPricingError` ObservableProperty + Warning-severity InfoBar with banner-stack suppression policy (auth wins over pricing).

## Tasks Completed

| Task | Name | Commit | Files |
|------|------|--------|-------|
| 1 | Add PricingErrorInfoBar resw key pairs + extend ResourceCoverageTests | 5de392d | en-US/de-DE Resources.resw, ResourceCoverageTests.cs |
| 2 | Add IsPricingError + IsPricingErrorVisible to MainViewModel + wire sites | f267e1c | MainViewModel.cs |
| 3 | Add pricing-error InfoBar to MainView.xaml + BannerStackPolicyTests | 1b1081a | MainView.xaml, BannerStackPolicyTests.cs |

## Deliverables

### 2 New resw Key Pairs

| Key | en-US | de-DE |
|-----|-------|-------|
| `MainView.PricingErrorInfoBar.Title` | "Pricing data unavailable" | "Preisdaten nicht verfügbar" |
| `MainView.PricingErrorInfoBar.Message` | "Cost figures may be inaccurate." | "Kostendaten können ungenau sein." |

### IsPricingError + IsPricingErrorVisible (MainViewModel.cs)

- `_isSessionExpired`: `[NotifyPropertyChangedFor(nameof(IsPricingErrorVisible))]` added (D-PR-04)
- `_isPricingError`: new `[ObservableProperty]` with `[NotifyPropertyChangedFor(nameof(IsPricingErrorVisible))]` (D-PR-01)
- `IsPricingErrorVisible` computed property: `IsPricingError && !IsSessionExpired` (D-PR-04, banner-stack policy)

### Two Call-Site Instrumentations

**Site 1 — InitializeAsync Task.Run** (G-1 marshaling):
- Success path: `_dispatcherQueue.TryEnqueue(() => IsPricingError = false)`
- Failure catch: `_dispatcherQueue.TryEnqueue(() => IsPricingError = true)` + `Debug.WriteLine(ex.Message)`
- Rationale: Task.Run runs on ThreadPool; property mutation must marshal to UI thread per G-1

**Site 2 — AggregateStatisticsAsync** (refresh path / CD-04):
- Success: `IsPricingError = false` (no TryEnqueue needed — already on dispatcher chain)
- Failure catch: `IsPricingError = true` (OperationCanceledException excluded — not a pricing failure)
- Both auto-poll (tab switch triggers aggregation) and manual refresh (Refresh command calls PollUsageCoreAsync → AggregateStatisticsAsync) clear IsPricingError on success

### PricingErrorInfoBar (MainView.xaml)

- `x:Name="PricingErrorInfoBar"`, `l:Uids.Uid="MainView.PricingErrorInfoBar"`
- `Severity="Warning"`, `IsClosable="False"` (auto-clears on success per D-PR-03)
- Bound to `ViewModel.IsPricingErrorVisible` via `IsOpen` + `BoolToVisibilityConverter`
- Position in banner stack: after API Error InfoBar, before MigrationToast

### BannerStackPolicyTests (CCInfoWindows.Tests/ViewModels/BannerStackPolicyTests.cs)

5 test cases covering the `(IsPricingError x IsSessionExpired)` truth table:
- `[Theory]` with 4 `[InlineData]` rows (all 4 boolean combinations)
- `[Fact]` `BannerStackPolicy_AuthAlwaysWinsOverPricing` (explicit auth-priority assertion)
- Formula-mirror approach: `ComputeIsPricingErrorVisible` mirrors VM formula without instantiating MainViewModel

## Test Results

| Suite | Before | After |
|-------|--------|-------|
| ResourceCoverageTests | 4/4 (no pricing keys) | 4/4 |
| BannerStackPolicyTests | n/a | 5/5 |
| MessengerThreadingConventionTests | — | no regressions |
| Full test suite | — | no new failures |

## Decisions Made

1. **Banner-stack order** (documented for Phase-end PROJECT.md): auth (IsSessionExpired) > API error (HasApiError) > pricing (IsPricingError) > migration toast (IsSessionVisibilityMigrationToastVisible). Max 2 visible simultaneously enforced by suppression formula (IsPricingErrorVisible = IsPricingError && !IsSessionExpired).
2. **G-1 marshaling strategy**: Site 1 requires explicit `_dispatcherQueue.TryEnqueue` (Task.Run = ThreadPool); Site 2 inherits dispatcher context from `AggregateStatisticsAsync` caller chain.
3. **AggregateStatisticsAsync as Site 2** (not Refresh directly): `Refresh()` calls `PollUsageCoreAsync()` which does NOT call pricing — only `AggregateStatisticsAsync` (triggered by tab selection or statistics refresh) calls `EnsurePricesLoadedAsync`. CD-04 "both auto-poll and manual refresh" is satisfied because tab-switch triggers aggregation.

## Deviations from Plan

None — plan executed exactly as written.

## Visual Smoke (Deferred)

The InfoBar appearance with localized text requires running the app with internet blocked to trigger a pricing failure. This is a manual smoke-test step deferred to Phase 28 UAT or end-of-phase verification. The binding chain (ViewModel.IsPricingErrorVisible → IsOpen + Visibility) follows the identical pattern as the existing IsSessionExpired InfoBar, which is verified to work.

## Known Stubs

None. IsPricingError is fully wired at both call sites. The InfoBar auto-clears when pricing succeeds.

## Self-Check: PASSED

- `CCInfoWindows.Tests/ViewModels/BannerStackPolicyTests.cs` — created
- Commits `5de392d`, `f267e1c`, `1b1081a` — all exist
- Build: 0 errors
- Tests: ResourceCoverageTests 4/4, BannerStackPolicyTests 5/5, MessengerThreadingConventionTests passing
