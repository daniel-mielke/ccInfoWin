# Phase 22: UI Polish - Discussion Log

> **Audit trail only.** Do not use as input to planning, research, or execution agents.
> Decisions are captured in CONTEXT.md — this log preserves the alternatives considered.

**Date:** 2026-05-06
**Phase:** 22-ui-polish
**Areas discussed:** Spinner-Pattern, TooltipText-Ownership, 250ms-Floor-Scope, DispatcherTimer-Lifecycle-Hook
**Discussion mode:** User-delegated — all four gray-area recommendations accepted as-is via the user instruction *"ich will, dass du für alle 4 Gray Areas deine Empfehlungen direkt verwendest und die Diskussion abschließ"*. No interactive sub-questions were asked.

---

## Spinner-Pattern: ProgressRing Replacement vs. Existing Storyboard Animation

| Option | Description | Selected |
|--------|-------------|----------|
| Replace `RefreshIcon` with `ProgressRing` per Spec FEAT-09c | Discard v1.1 rotating-FontIcon + `_stopOnComplete` mechanism, introduce `ProgressRing` element toggled by `IsRefreshing` Visibility binding | |
| Preserve existing v1.1 storyboard rotation as the visual spinner | Keep `RefreshIcon` + `RotateTransform` + `SpinnerStoryboard` + `_stopOnComplete` — they ARE the spinner; reject Spec FEAT-09c | ✓ |
| Hybrid: ProgressRing alongside FontIcon, both driven by `IsRefreshing` | Two animation systems in parallel — ProgressRing.IsActive + Storyboard | |

**User's choice:** Delegated — recommendation accepted.
**Notes:** PROJECT.md "Validated" section locks "smooth refresh animation completing full 360° rotation before stopping" as a v1.1 quality attribute. PROJECT.md Key Decisions row "_stopOnComplete flag for refresh animation (v1.1)" documents the deliberate introduction of the storyboard-completion gate because WinUI 3 has no built-in API for graceful storyboard stop. Replacing this with a ProgressRing would discard that investment. The spec FEAT-09c wording is a generic macOS→Windows port; the existing rotation-animation is the better Windows-native solution. The visual change for POLISH-01 in v1.4 is **zero**.

---

## TooltipText-Ownership: SessionDisplayItem ViewModel-Wrapper vs. SessionInfo Plain-Model-Method

| Option | Description | Selected |
|--------|-------------|----------|
| `GetTooltipText(int)` method on `Models/SessionInfo.cs` per Spec FEAT-10a wording | Method on the Plain Data Model — matches spec text but violates `CLAUDE.md` "Models/ → Plain data objects" convention | |
| `TooltipText` computed property on `ViewModels/SessionDisplayItem` wrapper | Honor existing wrapper-class architecture; `SessionInfo` stays immutable Plain Data Model; ComboBox already binds to `SessionDisplayItem` | ✓ |
| Helper static class `SessionInfoTooltips.For(SessionInfo, ...)` | Decouple tooltip computation from any class | |

**User's choice:** Delegated — recommendation accepted.
**Notes:** `SessionInfo` is `init`-only and conceptually immutable per `CLAUDE.md` Project Structure rule "Models/ → Plain data objects". The ComboBox already binds to `viewmodels:SessionDisplayItem` (`MainView.xaml:104`), and the wrapper class lives at `MainViewModel.cs:37-42`. Adding `TooltipText` there is mechanically trivial. Spec wording is treated as architectural-naive — Windows-port adapts to existing project layering.

**Sub-decision noted:** Pre-existing bug `IsActive = true` hardcoded on every `SessionDisplayItem` (MainViewModel.cs:636) must be fixed because POLISH-04 requires inactive sessions to be visible in the ComboBox. The `.Where(s => s.IsActive(threshold))` filter at line 630 is removed (or relaxed), and `IsActive` is computed per-item from `s.IsActive(threshold)`. This is an opportunistic fix bundled into Phase 22.

---

## 250ms-Floor-Scope: Manual Click Only vs. All Polls

| Option | Description | Selected |
|--------|-------------|----------|
| Floor on manual `Refresh()` command only | Wrap only the `[RelayCommand] Refresh()` with `Task.WhenAll(work, Task.Delay(250ms))`; auto-poll-timer calls `PollUsageAsync()` directly without floor | ✓ |
| Floor on every `PollUsageAsync` call (manual + auto) | Every poll, including the 30s-interval auto-polls, gets the 250ms-stretched spinner | |
| Floor with adaptive duration based on context | Different floors for different trigger sources | |

**User's choice:** Delegated — recommendation accepted.
**Notes:** Acceptance Criterion POLISH-02 explicitly mentions "cached sub-100ms refreshes" — those occur on manual click when WebView2 caches the response. Auto-polls run every 30s already (`_refreshIntervalSeconds`), and adding a 250ms floor would cause the spinner to blink visibly every 30s for no reason. Spec FEAT-09b wraps only the `Refresh()` command — this is intentional. UX-wise: manual clicks are the only "refresh events" the user actively expects feedback for. Implementation requires extracting `PollUsageCoreAsync` from the existing `PollUsageAsync` body so `IsRefreshing` lifetime is owned by the wrappers (auto-poll wrapper retains current shape; manual-refresh wrapper adds the `Task.WhenAll` floor).

---

## DispatcherTimer-Lifecycle-Hook: SettingsView Code-Behind vs. SettingsViewModel Tab-Index Property

| Option | Description | Selected |
|--------|-------------|----------|
| Timer in SettingsViewModel; SettingsView Code-Behind triggers via Loaded + SegmentedSelectionChanged + Unloaded | View routes lifecycle events to ViewModel methods; ViewModel owns the timer state — matches Spec FEAT-15a/b structure | ✓ |
| Timer entirely in SettingsViewModel; observe a `SelectedTabIndex` ObservableProperty | Pure-MVVM, no Code-Behind — but SelectionChanged is a view-event, not a state change; round-tripping through a property is brittle | |
| Timer in SettingsView Code-Behind; ViewModel only exposes `LastFetchRelativeTime` getter | Pure View-side timer — but mixes business logic (interval management) into the view | |

**User's choice:** Delegated — recommendation accepted.
**Notes:** CLAUDE.md "No code-behind logic in Views" applies to **business** logic. View-lifecycle event routing IS view-layer concern — already practiced by the existing `SettingsView.xaml.cs.OnLoaded` + `ApplyTabTooltips` (lines 22-35). Three handlers needed:
1. Existing `OnLoaded` extended to start the timer if About is the initial active tab
2. New `OnSegmentedSelectionChanged` — start if `TabsSegmented.SelectedIndex == AboutTabIndex` (3), stop otherwise
3. New `OnUnloaded` — always stop (belt-and-suspenders against memory leak per POLISH-08)

ViewModel owns `_aboutTimestampTimer`, `Start/Stop` methods, and the `LastFetchRelativeTime` computed property. Timer.Tick raises `OnPropertyChanged(nameof(LastFetchRelativeTime))` — XAML rebinds without the underlying `IPricingService.LastFetch` source needing to change.

---

## Claude's Discretion (left to planner)

- Disable-while-refreshing implementation: `[RelayCommand(CanExecute = ...)]` + `[NotifyCanExecuteChangedFor]` (Option A, recommended) vs. `InvertedBooleanConverter` + IsEnabled binding (Option B). Planner verifies which is more idiomatic by grepping existing patterns.
- `PollUsageCoreAsync` extraction shape — exact name and split point.
- Inactive-session display ordering — implicit `LastActivity DESC` ordering puts inactive trailing; explicit `ThenBy(IsActive ? 0 : 1)` is optional.
- `SessionTimeoutChangedMessage` namespace and exact location.
- `AboutTabIndex` source — const in Code-Behind vs. centralized in ViewModel.
- Test mock strategy for the 250ms floor — `Stopwatch` with tolerance window (default) vs. `ITimeProvider` injection (heavy).
- `LastFetchRelativeTime` formatting helper location and shape.

## Deferred Ideas

- `InvertedBooleanConverter` extraction (only if D-04 Option B picked)
- `ITimeProvider` injection for cross-cutting time mocking
- Unified anti-flicker helper `WithMinimumDuration(Task, TimeSpan)`
- Timer lifetime audit / `ITimerService` centralization across the app
- Inactive-session display ordering tweak (`ThenBy(IsActive)`)
- `AboutTabIndex` centralization in `SettingsViewModel`
- `RelativeTimeFormatter.Format(DateTimeOffset)` helper extraction
- Spinner-Pattern consolidation into a single `LoadingIndicator` UserControl
