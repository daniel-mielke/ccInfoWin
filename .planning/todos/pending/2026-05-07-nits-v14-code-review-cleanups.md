---
created: 2026-05-07
source: v1.4 code review
severity: minor
area: multiple
related_phase: v1.4
---

# Nits — v1.4 Code Review Cleanups

Three minor cleanups identified during v1.4 code review. Bundle these into a single opportunistic cleanup commit, not a dedicated phase.

## N-1: Remove redundant null-guard in `OnSegmentedSelectionChanged`

**Location:** `Views/SettingsView.xaml.cs`

**Problem:** `if (ViewModel == null) return;` — `ViewModel` is assigned via `App.Services.GetRequiredService<SettingsViewModel>()` in the constructor and never set to null. The guard suggests it could be null, which it can't.

**Fix:** Delete the guard.

## N-2: Tighten bare `catch` on `Localizer.Get().GetLocalizedString()` in `ComputeTooltipText`

**Location:** `ViewModels/MainViewModel.cs` — `ComputeTooltipText` method

**Problem:** Bare `catch` was a defensive fallback because Phase 22 wired the consumer before Phase 23 authored the resw key. After Phase 23 the key is committed and verified by `ResourceCoverageTests` — the broad catch is no longer needed and now hides any genuine localizer failure.

**Fix:** Either remove the catch entirely or narrow it to `KeyNotFoundException` (or whatever WinUI3Localizer throws for missing keys — verify before narrowing).

## N-3: Remove duplicate `AboutTabIndex` constant in `SettingsView.xaml.cs`

**Location:** `Views/SettingsView.xaml.cs:13` re-declares `private const int AboutTabIndex = SettingsViewModel.AboutTabIndex`

**Problem:** The view-side constant is redundant. The `SettingsViewModel.AboutTabIndex` constant is already public (line 37 of `SettingsViewModel.cs`). The local re-declaration adds maintenance burden if the value ever changes.

**Fix:** Reference `SettingsViewModel.AboutTabIndex` directly in the view code-behind.

## Effort

XS total — three trivial edits.

## v1.5 Priority

Low. Bundle into any v1.5 commit that touches these files. Not worth a standalone PR.
