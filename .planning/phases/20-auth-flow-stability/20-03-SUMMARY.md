---
phase: 20-auth-flow-stability
plan: "03"
subsystem: auth-flow
tags: [login, webview2, reload-button, visibility-gate, d-08, auth-07]
dependency_graph:
  requires: [20-auth-flow-stability-01]
  provides: [reload-button-overlay, webview2-visibility-gate, navigation-completed-extension]
  affects: [LoginView.xaml, LoginView.xaml.cs, LoginViewModel.cs]
tech_stack:
  added: []
  patterns: [InvertedBoolToVisibilityConverter, WinUI3Localizer-uid-binding, double-null-guard]
key_files:
  created: []
  modified:
    - CCInfoWindows/CCInfoWindows/Views/LoginView.xaml
    - CCInfoWindows/CCInfoWindows/Views/LoginView.xaml.cs
    - CCInfoWindows/CCInfoWindows/ViewModels/LoginViewModel.cs
decisions:
  - "D-04: Reload button overlay at top-right (HorizontalAlignment=Right, VerticalAlignment=Top, Margin=8)"
  - "D-05: Visual style mirrors MainView footer — Glyph=&#xE72C;, Padding=8, CornerRadius=6, FontSize=16, SecondaryTextBrush"
  - "D-06: Click handler is LoginWebView?.CoreWebView2?.Reload() — double null guard, no try/catch"
  - "D-07: LoginWebView stays Collapsed until NavigationCompleted fires with IsSuccess+login URL"
  - "D-08: IsLoading is SINGLE source of truth — no second visibility flag; InvertedBoolToVisibilityConverter from App.xaml:14 is the inverse-binding helper"
metrics:
  duration: "~12 minutes"
  completed: "2026-05-06"
  tasks_completed: 3
  files_modified: 3
---

# Phase 20 Plan 03: LoginView Reload Button and WebView2 Visibility Gate Summary

Three surgical edits implementing AUTH-06 (manual reload affordance) and AUTH-07 (no flash of previous chat URL after logout) via the D-08 single-flag approach using the pre-existing `InvertedBoolToVisibilityConverter`.

## Tasks Completed

| Task | Name | Commit | Files |
|------|------|--------|-------|
| 1 | Add WinUI3Localizer xmlns, reload button overlay, inverse-IsLoading binding | 34d588a | LoginView.xaml |
| 2 | Add OnReloadLoginClicked handler to code-behind | 0188db7 | LoginView.xaml.cs |
| 3 | Extend HandleNavigationCompleted; remove premature IsLoading=false | 159bb6a | LoginViewModel.cs |

## Changes by File

### CCInfoWindows/CCInfoWindows/Views/LoginView.xaml
Lines added: +27, removed: -3 (net +24)

- Added `xmlns:l="using:WinUI3Localizer"` namespace import
- Added `Visibility="{x:Bind ViewModel.IsLoading, Mode=OneWay, Converter={StaticResource InvertedBoolToVisibilityConverter}}"` on `LoginWebView` (D-08 single source of truth)
- Added reload button overlay as last Grid child (Z-order top): `l:Uids.Uid="LoginReloadButton"`, `Click="OnReloadLoginClicked"`, `Glyph="&#xE72C;"`, `FontSize=16`, `Padding=8`, `CornerRadius=6`, `Background=Transparent`, `BorderThickness=0`, `Foreground=SecondaryTextBrush`, `HorizontalAlignment=Right`, `VerticalAlignment=Top`, `Margin=8`

### CCInfoWindows/CCInfoWindows/Views/LoginView.xaml.cs
Lines added: +10, removed: 0 (net +10)

- Added `OnReloadLoginClicked(object sender, RoutedEventArgs e)` as sibling of `OnLoaded`
- Body: `LoginWebView?.CoreWebView2?.Reload()` — double `?.` null guard per D-06
- No try/catch, no retry, no new using directives

### CCInfoWindows/CCInfoWindows/ViewModels/LoginViewModel.cs
Lines added: +20, removed: -2 (net +18)

- Replaced `HandleNavigationCompleted` body: now flips `IsLoading = false` ONLY when `args.IsSuccess == true` AND `source.StartsWith("https://claude.ai/login", StringComparison.OrdinalIgnoreCase)` (D-07/D-08)
- Preserved `_loginHandled` guard and `TryExtractSessionCookieAsync` call
- Removed premature `IsLoading = false;` at `InitializeWebViewAsync` tail (after `Navigate(...)`)
- Added D-08 comment block explaining the deferred-flip contract
- Final `IsLoading = false;` count in file: **2** (init-failure catch at line 79 + HandleNavigationCompleted gate at line 158)

## Decision Provenance

| Decision | Where implemented |
|----------|-------------------|
| D-04: Top-right overlay placement | LoginView.xaml reload Button HorizontalAlignment/VerticalAlignment/Margin |
| D-05: Locked visual style (Padding=8, CornerRadius=6, FontSize=16, Glyph=&#xE72C;) | LoginView.xaml reload Button + FontIcon attributes |
| D-06: Double null guard, one-shot, no error UI | LoginView.xaml.cs OnReloadLoginClicked body |
| D-07: No flash of previous chat URL | LoginViewModel.cs HandleNavigationCompleted — `StartsWith("https://claude.ai/login")` gate |
| D-08: Single source of truth via IsLoading extension | LoginView.xaml InvertedBoolToVisibilityConverter on LoginWebView; LoginViewModel.cs removed premature false-flip |

## D-08 Compliance Confirmation

- `IsLoading` is the single source of truth for both the loading overlay and the WebView2 visibility gate.
- `LoginWebView.Visibility` is bound to the inverse of `IsLoading` via the pre-existing global `InvertedBoolToVisibilityConverter` (registered in `App.xaml` line 14, `x:Key="InvertedBoolToVisibilityConverter"`).
- No `IsWebViewVisible`, `IsWebViewReady`, or any other second visibility flag was introduced anywhere in the project.
- Verified: `grep -c 'IsWebViewVisible' CCInfoWindows/CCInfoWindows/Views/LoginView.xaml` == 0
- Verified: `grep -c 'IsWebViewVisible' CCInfoWindows/CCInfoWindows/ViewModels/LoginViewModel.cs` == 0

## Deviations from Plan

None — plan executed exactly as written.

## Known Stubs

None — all bindings are wired to live ViewModel properties. The `LoginReloadButton.*` resw keys were authored in Plan 01 Task 2 (dependency satisfied).

## Open Question for Plan 04

From Plan 03 output spec: smoke test should verify that `Window.Activate()` actually unminimizes the app window when triggered from tray (Open Question #2 / Assumption A4 from 20-CONTEXT.md).

## Pre-existing Test Failures (out of scope)

3 tests were failing before and after this plan's changes — they are in unrelated files:
- `BurnRateCalculatorTests.Predict_FlatUsage_ReturnsNull`
- `ClaudeApiServiceTests.FetchUsageAsync_OnPersistentNullResponse_ThrowsAfterRetries`
- `ClaudeApiServiceTests.FetchUsageAsync_OnTransientNullResponse_RetriesAndSucceeds`

These pre-exist in the codebase and are not caused by any change in this plan.

## Self-Check: PASSED

- [x] LoginView.xaml modified and committed (34d588a)
- [x] LoginView.xaml.cs modified and committed (0188db7)
- [x] LoginViewModel.cs modified and committed (159bb6a)
- [x] `dotnet build` exits 0 (67 warnings, 0 errors)
- [x] `InvertedBoolToVisibilityConverter` present in LoginView.xaml (1 match)
- [x] `IsWebViewVisible` absent from LoginView.xaml (0 matches)
- [x] `IsWebViewVisible` absent from LoginViewModel.cs (0 matches)
- [x] `OnReloadLoginClicked` present in LoginView.xaml.cs (1 match)
- [x] `IsLoading = false;` appears exactly twice in LoginViewModel.cs (lines 79 + 158)
- [x] `args.IsSuccess` gate present in HandleNavigationCompleted
- [x] `StringComparison.OrdinalIgnoreCase` used in login URL check
