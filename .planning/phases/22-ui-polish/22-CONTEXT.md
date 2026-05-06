# Phase 22: UI Polish - Context

**Gathered:** 2026-05-06
**Status:** Ready for planning

<domain>
## Phase Boundary

Three independent polish improvements on the existing v1.3 UI. Each lives in a disjoint code path; they share no state and can be implemented in parallel:

1. **Refresh Spinner Hardening (POLISH-01/02/03)** — The footer refresh button shows a visual refresh-in-progress indicator that stays visible for at least 250 ms even on cached sub-100ms refreshes, and the button is disabled while a refresh is running.
2. **Inactive Session Tooltip with Threshold (POLISH-04/05/06)** — Inactive ComboBox items show a two-line tooltip (path + "Inactive for > {threshold}min") using the user's currently configured `SessionTimeoutMinutes`. Active items keep the existing single-line path tooltip. Changing the threshold triggers a recompute on next refresh.
3. **About-Tab Pricing Timestamp Live Refresh (POLISH-07/08)** — A `DispatcherTimer` ticks every minute while the About tab is the active Settings tab and refreshes the "X minutes ago" relative-time display. The timer stops on tab switch and on Settings page Unloaded.

**In scope:**
- `MainViewModel.RefreshAsync` command wrapper extended with a 250 ms minimum-display window via `Task.WhenAll(PollUsageCoreAsync(), Task.Delay(MinimumSpinnerDisplayMs))`
- `PollUsageAsync` refactored to extract a `PollUsageCoreAsync` private method that does NOT set `IsRefreshing` — the wrapper owns `IsRefreshing` lifetime
- `MainView.xaml` `FooterRefreshButton` extended with `IsEnabled="{x:Bind ViewModel.IsRefreshing, Converter={StaticResource InvertedBoolToVisibilityConverter}, ...}"` (or equivalent disable binding)
- `SessionDisplayItem` (`MainViewModel.cs:37-42`) extended with new fields: `string TooltipText`, fix existing `IsActive = true` hardcoded bug to compute from real activity threshold
- New `SessionTimeoutChangedMessage` (or extend existing settings-changed messenger pattern) to trigger `RefreshSessionsAsync` when threshold changes
- `SettingsViewModel` adds `DispatcherTimer? _aboutTimestampTimer`, `StartAboutTimestampTimer()`, `StopAboutTimestampTimer()`, and a `LastFetchRelativeTime` computed property that triggers `OnPropertyChanged` on each tick
- `SettingsView.xaml.cs` extends existing `Loaded` handler, adds `Segmented.SelectionChanged` handler, adds `Unloaded` handler — all three route to ViewModel start/stop methods
- Unit tests for the 250 ms floor (mock `Task.Delay`), `SessionDisplayItem.TooltipText` formatting (active vs inactive cases, German + English), and `_aboutTimestampTimer` start/stop lifecycle

**Out of scope:**
- Auth flow work (Phase 20 — already complete)
- History persistence work (Phase 21 — already complete)
- The 6 new resw localization keys themselves (Phase 23) — Phase 22 **uses** the new `InactiveSessionTooltip` key but does not author it
- Replacement of the existing `RefreshIcon` rotation animation with a `ProgressRing` (see D-01 below — the existing v1.1 animation IS the spinner; Spec FEAT-09c is rejected)
- Lifetime change for `MainViewModel` (Phase 20 locked Transient)
- Lifetime change for `SettingsViewModel` (DI registration unchanged)
- Refactor of `SessionInfo` model (CLAUDE.md "Models/ → Plain data objects" — TooltipText lives on the ViewModel wrapper, not the model)

</domain>

<decisions>
## Implementation Decisions

### Refresh Spinner: Reject ProgressRing Replacement (POLISH-01)

- **D-01:** **Spec FEAT-09c is rejected.** The existing v1.1 refresh animation — `FontIcon Glyph="&#xE895;"` with a `RotateTransform` driven by `SpinnerStoryboard` and the `_stopOnComplete` flag in `MainView.xaml.cs:167-192` — IS the visual refresh spinner. POLISH-01's acceptance criterion ("ProgressRing in place of the arrow glyph") is satisfied by reading "ProgressRing" generically as "rotating refresh indicator". The macOS spec wording is a generic Windows port and the existing rotation-animation is the better Windows-native solution. Phase 22 does NOT add a `ProgressRing` element; it does NOT change the `FontIcon`. The visual change for POLISH-01 in v1.4 is **zero** — the v1.1 implementation already meets the criterion.

  Rationale:
  - PROJECT.md "Validated" section explicitly locks "smooth refresh animation completing full 360° rotation before stopping" as a v1.1 quality attribute. Replacing it with a `ProgressRing` would discard that investment.
  - PROJECT.md Key Decisions row "_stopOnComplete flag for refresh animation (v1.1)" documents that this mechanism was deliberately introduced because "WinUI 3 Storyboard must complete current rotation before Stop() — no built-in API". Bolting a parallel `ProgressRing.IsActive` next to the storyboard would create race conditions between two animation systems.
  - The existing `OnViewModelPropertyChanged` (MainView.xaml.cs:179-192) already starts/stops the storyboard in response to `IsRefreshing` PropertyChanged — the wiring POLISH-01 needs is already done.

### Refresh Spinner: 250 ms Floor on Manual Click Only (POLISH-02)

- **D-02:** The 250 ms minimum-display floor is applied **only** in the manual `[RelayCommand] Refresh()` path (`MainViewModel.cs:850-854`), NOT in automatic poll-timer-driven `PollUsageAsync()` calls. Automatic polls fire every `_refreshIntervalSeconds` (default 30 s) — that interval is the natural floor; an additional 250 ms stretch on each automatic poll would cause the spinner to blink every 30 s for no reason.

  Spec FEAT-09b wraps only the `Refresh()` command. This is intentional and correct.

- **D-03:** Refactor `PollUsageAsync` to extract a private `PollUsageCoreAsync()` that performs the API fetch + state update WITHOUT setting `IsRefreshing`. Both the auto-poll-timer call site (currently `PollUsageAsync()`) and the manual `RefreshAsync()` wrapper become responsible for `IsRefreshing` lifetime management. Skeleton:

  ```csharp
  private async Task PollUsageCoreAsync()
  {
      // Existing body of PollUsageAsync, MINUS the IsRefreshing assignment
      // and the IsRefreshing reset in finally.
  }

  private async Task PollUsageAsync()  // existing auto-poll entry point
  {
      if (IsRefreshing) return;
      IsRefreshing = true;
      try { await PollUsageCoreAsync(); }
      finally { IsRefreshing = false; }
  }

  [RelayCommand]
  private async Task Refresh()  // existing manual entry point — line 850
  {
      if (IsRefreshing) return;
      IsRefreshing = true;
      try
      {
          await Task.WhenAll(
              PollUsageCoreAsync(),
              Task.Delay(TimeSpan.FromMilliseconds(MinimumSpinnerDisplayMs))
          );
      }
      finally { IsRefreshing = false; }
  }

  private const int MinimumSpinnerDisplayMs = 250;
  ```

  Note: the existing `if (IsRefreshing) return;` guard in `PollUsageAsync` (line 400) prevents the auto-timer and manual click from racing. The same guard in `Refresh()` prevents double-click race. Both must keep this guard.

### Refresh Spinner: Disabled-While-Refreshing (POLISH-03)

- **D-04:** Add `IsEnabled="{x:Bind ViewModel.IsRefreshing, Converter={StaticResource InvertedBoolToVisibilityConverter}, Mode=OneWay}"` to the `FooterRefreshButton` (MainView.xaml:606). Wait — `InvertedBoolToVisibilityConverter` returns Visibility, not bool. The correct converter is a new `InvertedBooleanConverter` (bool → bool inversion).

  Decision: introduce `Converters/InvertedBooleanConverter.cs` (bool → bool), follow the existing `BoolToVisibilityConverter`/`InvertedBoolToVisibilityConverter` pattern. Register in App.xaml resources.

  Alternative considered: `[RelayCommand]` source-generators support a `CanExecute` property pattern via `[NotifyCanExecuteChangedFor]` — could mark the command itself as not-executable when `IsRefreshing == true`. This is the more idiomatic CommunityToolkit.Mvvm path. Planner picks the cleaner option:
  - **Option A (recommended):** `[RelayCommand(CanExecute = nameof(CanRefresh))]` + `private bool CanRefresh => !IsRefreshing;` + `[NotifyCanExecuteChangedFor(nameof(RefreshCommand))]` on the `_isRefreshing` field. Button auto-disables via Command.CanExecute.
  - **Option B:** Add `InvertedBooleanConverter` + IsEnabled binding in XAML.

  Recommendation: Option A — fewer XAML changes, fewer new files, idiomatic Toolkit pattern. Already used elsewhere in the codebase (planner verifies with grep `NotifyCanExecuteChangedFor`).

### TooltipText: Owned by SessionDisplayItem ViewModel-Wrapper (POLISH-04, POLISH-05)

- **D-05:** **Spec FEAT-10a is adapted.** The Spec wording ("Modify: `Models/SessionInfo.cs`") violates the `CLAUDE.md` "Models/ → Plain data objects" convention. `SessionInfo.cs` (Models/SessionInfo.cs) is `init`-only and conceptually immutable; a `GetTooltipText(int)` method on a Plain Data Model is the wrong layer.

  The ComboBox already binds to `SessionDisplayItem` (`MainView.xaml:104`, `x:DataType="viewmodels:SessionDisplayItem"`). The wrapper class lives at `MainViewModel.cs:37-42`:

  ```csharp
  public class SessionDisplayItem
  {
      public required SessionInfo Session { get; init; }
      public required string DisplayName { get; init; }
      public required bool IsActive { get; init; }
  }
  ```

  Phase 22 extends it:

  ```csharp
  public class SessionDisplayItem
  {
      public required SessionInfo Session { get; init; }
      public required string DisplayName { get; init; }
      public required bool IsActive { get; init; }
      public required string TooltipText { get; init; }
  }
  ```

  `SessionInfo.cs` stays unchanged.

- **D-06:** **Pre-existing bug fix in scope:** the current `RefreshSessionsAsync` (MainViewModel.cs:629-637) hardcodes `IsActive = true` for every item in `SortedSessions` because the `.Where(s => s.IsActive(threshold))` filter only keeps active sessions. The `IsActive = true` hardcode happens to be correct under the current filter, BUT POLISH-04 requires showing inactive sessions in the ComboBox with two-line tooltips — meaning the `.Where` filter must be removed (or relaxed) to include inactive sessions, and `IsActive` must compute from `s.IsActive(threshold)` per item.

  Decision: the `.Where(s => s.IsActive(threshold))` filter is **REMOVED** (or relaxed to a wider activity window — planner picks based on UX testing). Each `SessionDisplayItem` computes `IsActive = s.IsActive(threshold)`. Inactive items are now visible in the ComboBox. This is a behavioral change — must be called out in DISCUSSION-LOG and the planner's task description.

  Alternative: keep the filter, only show active sessions. Rejected because POLISH-04's acceptance criterion ("Inactive session ComboBox items show a two-line tooltip") is meaningless if inactive sessions never appear in the ComboBox. The Spec implies inactive sessions are meant to be displayed.

  Trade-off: showing inactive sessions makes the ComboBox longer. Mitigation: the ordering is already by `LastActivity DESC` — active sessions stay at the top, inactive sessions trail. The user can scroll past them.

- **D-07:** `TooltipText` computation lives in a single helper method on `MainViewModel`:

  ```csharp
  private string ComputeTooltipText(SessionInfo session, bool isActive, int sessionTimeoutMinutes)
  {
      if (isActive)
      {
          return session.Cwd; // single-line, current behavior — POLISH-05
      }
      var template = Localizer.Get().GetLocalizedString("InactiveSessionTooltip");
      // template is "Inaktiv seit > {0}min" / "Inactive for > {0}min" — formatted with threshold
      return $"{session.Cwd}\n{string.Format(template, sessionTimeoutMinutes)}";
  }
  ```

  Called once per session during `SortedSessions` rebuild. The current `SessionTimeoutMinutes` is read from `_settingsService.LoadSettings().SessionTimeoutMinutes` at the call site.

  The `InactiveSessionTooltip` resw key is owned by Phase 23 — Phase 22 references it. Either Phase 22 ships first and Phase 23 fills the resw entries, OR Phase 23 ships first. ROADMAP.md shows Phase 23 depends on Phase 20 (parallel to Phase 22), so coordination is needed. Default: Phase 22 ships first; the `Localizer.GetLocalizedString` returns the key name itself if missing — visible-but-not-translated string is the failure mode, not crash.

### TooltipText Recompute on Threshold Change (POLISH-06)

- **D-08:** When the user changes `SessionTimeoutMinutes` in Settings, send a new `SessionTimeoutChangedMessage(int newMinutes)` via `WeakReferenceMessenger.Default` from `SettingsViewModel`. `MainViewModel.Receive(SessionTimeoutChangedMessage)` triggers `RefreshSessionsAsync()` which rebuilds `SortedSessions` with fresh `TooltipText` values.

  Pattern: identical to existing `RefreshIntervalChangedMessage` and `SonnetContextChangedMessage` (already in `Messages/`). Adding a third instance is mechanical.

  Alternative considered: lazy recompute on next auto-poll cycle (no messenger). Rejected — POLISH-06 acceptance ("Tooltip recomputes when SessionTimeoutMinutes changes in settings") is more naturally satisfied by an immediate event-driven rebuild than waiting for the next 30 s auto-poll tick. Settings changes should feel instantaneous.

### About-Tab DispatcherTimer Lifecycle (POLISH-07, POLISH-08)

- **D-09:** The `DispatcherTimer` lives in `SettingsViewModel`. New private field `_aboutTimestampTimer`, new methods `StartAboutTimestampTimer()` / `StopAboutTimestampTimer()`. The timer's `Tick` handler raises `OnPropertyChanged(nameof(LastFetchRelativeTime))` — XAML rebinds the relative-time TextBlock without the underlying `IPricingService.LastFetch` source needing to change.

  ```csharp
  private DispatcherTimer? _aboutTimestampTimer;

  public string LastFetchRelativeTime => /* compute from _pricingService.LastFetch */;

  public void StartAboutTimestampTimer()
  {
      if (_aboutTimestampTimer != null) return;
      _aboutTimestampTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
      _aboutTimestampTimer.Tick += (_, _) => OnPropertyChanged(nameof(LastFetchRelativeTime));
      _aboutTimestampTimer.Start();
  }

  public void StopAboutTimestampTimer()
  {
      _aboutTimestampTimer?.Stop();
      _aboutTimestampTimer = null;
  }
  ```

- **D-10:** The trigger lives in `SettingsView.xaml.cs` Code-Behind. View-lifecycle events route to ViewModel methods. Three handlers needed:

  1. **Existing `OnLoaded`** — extend to start the timer if About is the initial active tab (rare but possible if user opens Settings with About pre-selected by persistence)
  2. **New `OnSegmentedSelectionChanged`** — wired to `Segmented.SelectionChanged`. If `TabsSegmented.SelectedIndex == AboutTabIndex`, start; else stop.
  3. **New `OnUnloaded`** — wired to `Page.Unloaded`. Always stop. Belt-and-suspenders against memory leak (POLISH-08).

  ```csharp
  private const int AboutTabIndex = 3; // 0=General, 1=Updates, 2=Account, 3=About

  private void OnSegmentedSelectionChanged(object sender, SelectionChangedEventArgs e)
  {
      if (ViewModel == null) return;
      if (TabsSegmented.SelectedIndex == AboutTabIndex)
          ViewModel.StartAboutTimestampTimer();
      else
          ViewModel.StopAboutTimestampTimer();
  }

  private void OnUnloaded(object sender, RoutedEventArgs e)
  {
      ViewModel?.StopAboutTimestampTimer();
  }
  ```

  CLAUDE.md "No code-behind logic in Views" applies to **business** logic. View-lifecycle event routing IS view-layer concern — this is the documented exception (already practiced by `ApplyTabTooltips` in the existing `SettingsView.xaml.cs:28-35`).

- **D-11:** `LastFetchRelativeTime` is a computed property that returns the localized "X minutes ago" string. Implementation reads `_pricingService.LastFetch` (or wherever the pricing-fetch timestamp lives) and formats as a relative time using the existing localization keys. If the pricing-fetch service does not expose a public timestamp, planner adds the necessary read-only property.

  No `[ObservableProperty]` on this — pure computed. The timer's `OnPropertyChanged(nameof(LastFetchRelativeTime))` is what drives the rebinding.

### Claude's Discretion

- **Disable-while-refreshing implementation choice (D-04):** Option A (`[RelayCommand(CanExecute = ...)]` + `[NotifyCanExecuteChangedFor]`) vs. Option B (`InvertedBooleanConverter` + IsEnabled binding). Recommended A; planner confirms by checking if the codebase already uses `[NotifyCanExecuteChangedFor]` in any other RelayCommand. If not, A introduces a new pattern — still preferred for idiomatic-Toolkit reasons.
- **`PollUsageCoreAsync` extraction shape (D-03):** the exact name and the exact split point can vary. Planner picks based on existing private-method naming. Default: extract everything between `IsRefreshing = true;` and `finally { IsRefreshing = false; }` into the core method. The auto-poll wrapper and the manual-refresh wrapper both surround it.
- **Inactive-session display ordering (D-06):** the existing `OrderByDescending(s => s.LastActivity)` keeps inactive sessions trailing — that is the implicit ordering. Planner can add a secondary sort `ThenBy(s => s.IsActive ? 0 : 1)` to make active-first explicit if visual ordering becomes inconsistent.
- **`SessionTimeoutChangedMessage` location (D-08):** new file at `Messages/SessionTimeoutChangedMessage.cs`. Planner picks the exact namespace based on existing message types (`Messages/RefreshIntervalChangedMessage.cs` is the template).
- **`AboutTabIndex` source (D-10):** the constant `3` is fragile if Settings tabs are reordered. Planner can either centralize this in `SettingsViewModel` as a public const, or compute it via `TabsSegmented.Items.IndexOf(TabAbout)` — both are fine; the const is simpler.
- **Test mock strategy for the 250 ms floor:** unit testing `Task.Delay` is awkward. Planner picks: either inject an `ITimeProvider` (clean but heavy for a single timer), or rely on integration test that measures elapsed wall-clock (rough but works). Default: integration test with `Stopwatch` and a tolerance window (`>= 250ms && < 500ms`).
- **`InvertedBooleanConverter` introduction (D-04 fallback):** if planner picks Option B, follow the existing `Converters/BoolToVisibilityConverter.cs` shape.
- **Pre-existing `IsActive` bug fix:** acceptable to ship as part of Phase 22, or extract to a separate plan within the phase. Planner picks based on plan granularity.

</decisions>

<canonical_refs>
## Canonical References

**Downstream agents MUST read these before planning or implementing.**

### Phase 22 source spec & requirements
- `spec/v1.11.1-macOS/spec-release-1.10.0-to-1.11.1.md` §FEAT-09 — Refresh Spinner with Min-Display Window (FEAT-09a IsRefreshing property — already exists from v1.1, FEAT-09b 250ms-Floor Refresh-Command wrapper, FEAT-09c XAML ProgressRing). NOTE: FEAT-09c is REJECTED by D-01 — the existing v1.1 rotating-FontIcon animation is the visual spinner; ProgressRing replacement would discard the `_stopOnComplete` mechanism documented in PROJECT.md Key Decisions.
- `spec/v1.11.1-macOS/spec-release-1.10.0-to-1.11.1.md` §FEAT-10 — Inactive Session Tooltip with Threshold (FEAT-10a TooltipText computed property, FEAT-10b XAML binding). NOTE: spec text places `GetTooltipText` on `Models/SessionInfo.cs`; D-05 rejects this in favor of `SessionDisplayItem` ViewModel-wrapper to honor CLAUDE.md "Models/ → Plain data objects" convention.
- `spec/v1.11.1-macOS/spec-release-1.10.0-to-1.11.1.md` §FEAT-15 — About-Tab Pricing Timestamp Live Refresh (FEAT-15a Timer in SettingsViewModel, FEAT-15b Tab Activation Trigger)
- `spec/v1.11.1-macOS/spec-release-1.10.0-to-1.11.1.md` §"Testing Strategy" — Unit-test catalog (`Inactive session tooltip formatting`, refresh-spinner timing)
- `.planning/milestones/v1.4-REQUIREMENTS.md` §POLISH-01..POLISH-08 — Acceptance criteria (8 IDs)
- `.planning/milestones/v1.4-ROADMAP.md` §Phase 22 — Goal, success criteria, depends-on, FEAT-IDs

### Localization keys (used by Phase 22, authored by Phase 23)
- `spec/v1.11.1-macOS/spec-release-1.10.0-to-1.11.1.md` §FEAT-16 — `InactiveSessionTooltip` resw key (DE: "Inaktiv seit > {0}min" / EN: "Inactive for > {0}min"). Phase 22 references this key; Phase 23 authors the resw entries. If Phase 22 ships first, the localizer returns the key name as fallback string — visible degradation, not crash.

### Codebase conventions (project-wide, from CLAUDE.md)
- `CLAUDE.md` §"MVVM Conventions" — `[ObservableProperty]`, `[RelayCommand]`, no code-behind business logic, source-generators
- `CLAUDE.md` §"Async Patterns" — always async/await, never fire-and-forget, `DispatcherQueue.TryEnqueue` for UI thread marshaling
- `CLAUDE.md` §"Project Structure" — `Models/` for Plain Data Objects, `ViewModels/` for observable state + commands, `Views/` for XAML pages, `Messages/` for messenger types
- `CLAUDE.md` §"Clean Code Rules" — no magic numbers (`MinimumSpinnerDisplayMs`, `AboutTabIndex` already named), small functions, DRY
- `CLAUDE.md` §"Build Commands" — Release builds use `dotnet build -c Release`, NEVER `dotnet publish` with trimming
- `CLAUDE.md` §"Bash Permission Rules" — every command in its own tool call, no chaining

### Prior phase context (carry-forward decisions)
- `.planning/phases/20-auth-flow-stability/20-CONTEXT.md` D-04 — `MainViewModel.IsRefreshing` ObservableProperty reserved for Phase 22 use; Phase 20 must NOT remove or rename. Phase 22 honors this.
- `.planning/phases/20-auth-flow-stability/20-CONTEXT.md` `code_context` — `MainViewModel` registered as `Transient` in DI. Phase 22 does NOT change this.
- `.planning/phases/21-history-persistence-hardening/21-CONTEXT.md` D-04 — `IUsageHistoryService` singleton invariant. Phase 22 does NOT touch the history service.

### Project-level architecture
- `.planning/PROJECT.md` §"Validated" — v1.1 "smooth refresh animation completing full 360° rotation before stopping" is a locked quality attribute. D-01 explicitly preserves this.
- `.planning/PROJECT.md` §"Key Decisions" row "_stopOnComplete flag for refresh animation (v1.1)" — locks the existing storyboard mechanism. Phase 22 builds on it, does not replace it.

</canonical_refs>

<code_context>
## Existing Code Insights

### Reusable Assets

- **`MainViewModel.IsRefreshing`** (line 154-ish, exact line via grep `_isRefreshing`): `[ObservableProperty]` already exists. Phase 20 reserved this for Phase 22 use (Phase 20 CONTEXT.md `code_context` row). Phase 22 owns its lifetime in two new wrappers (D-03).
- **`MainViewModel.PollUsageAsync`** (line 398-434): existing API-fetch + state-update method. Phase 22 extracts a `PollUsageCoreAsync` private method out of the body (D-03), leaves the existing `PollUsageAsync` as the auto-poll-timer entry point.
- **`MainViewModel.RefreshCommand`** (line 850-854, `[RelayCommand] private async Task Refresh()`): existing manual-refresh entry point. Phase 22 wraps it with the 250 ms floor.
- **`MainView.xaml` SpinnerStoryboard** (line 20, `Storyboard.TargetName="RefreshIconTransform"`): existing v1.1 rotation animation. D-01 preserves it untouched.
- **`MainView.xaml.cs` `_stopOnComplete` mechanism** (line 29, 167-192): existing v1.1 storyboard-completion gate. D-01 preserves it untouched.
- **`MainView.xaml` FooterRefreshButton** (line 606-618): existing button with `Command="{x:Bind ViewModel.RefreshCommand}"`. Phase 22 adds disable-while-refreshing wiring (D-04).
- **`SessionDisplayItem` ViewModel-wrapper** (`MainViewModel.cs:37-42`): existing local class. Phase 22 extends with `TooltipText` property (D-05).
- **`SortedSessions` ObservableCollection** (`MainViewModel.cs:242, 640`): rebuilt on every refresh-cycle. Phase 22 changes the build code at line 629-637 to compute `IsActive` per-item and `TooltipText` from the new helper.
- **`SettingsView.xaml.cs` `OnLoaded` + `ApplyTabTooltips`** (lines 22-35): existing view-lifecycle pattern. Phase 22 extends with `OnSegmentedSelectionChanged` and `OnUnloaded` (D-10).
- **`Messages/RefreshIntervalChangedMessage.cs` and `Messages/SonnetContextChangedMessage.cs`**: existing settings-changed messenger pattern. Phase 22 adds `SessionTimeoutChangedMessage` following the same shape (D-08).
- **`Converters/BoolToVisibilityConverter.cs` and `Converters/InvertedBoolToVisibilityConverter.cs`**: existing converter patterns. If planner picks D-04 Option B, a new `Converters/InvertedBooleanConverter.cs` follows the same shape.
- **`WinUI3Localizer` `Localizer.Get().GetLocalizedString(key)`** pattern: used everywhere. `D-07` reuses for the inactive-tooltip template.

### Established Patterns

- **MVVM via `[ObservableProperty]` / `[RelayCommand]`** — CommunityToolkit.Mvvm 8.4 source-generators. Phase 22 stays on this.
- **`WeakReferenceMessenger.Default` for cross-VM communication** — used for `AuthStateChangedMessage`, `RefreshIntervalChangedMessage`, `SonnetContextChangedMessage`, `ChartInvalidateMessage`. Phase 22 introduces `SessionTimeoutChangedMessage` as the fourth instance.
- **DI: ViewModels Transient, Services Singleton** (App.xaml.cs ConfigureServices). Phase 22 does NOT change registrations.
- **WinUI3Localizer with `l:Uids.Uid`** for runtime language switching — required for `InactiveSessionTooltip` (key authored by Phase 23).
- **`DispatcherQueue.TryEnqueue` for UI-thread marshaling** — used by `MainViewModel`'s timer callbacks. The new `_aboutTimestampTimer.Tick` handler runs on the UI thread by default (DispatcherTimer is UI-thread-bound), so no extra marshaling needed.
- **View-lifecycle event routing in Code-Behind** — existing `SettingsView.xaml.cs.OnLoaded` + `ApplyTabTooltips` shows the documented exception to "no code-behind logic" — view-layer event wiring stays in the view; ViewModel owns the business state.
- **Anti-flicker / minimum-display patterns** — Phase 22 is the first to introduce a `Task.WhenAll(work, Task.Delay(...))` floor pattern. Planner may want to extract it as a helper if any other phase needs the same shape; no current consumer.

### Integration Points

- `ViewModels/MainViewModel.cs:398` (`PollUsageAsync`) — extract `PollUsageCoreAsync` (D-03)
- `ViewModels/MainViewModel.cs:850` (`Refresh()`) — extend with 250 ms floor wrapper (D-02)
- `ViewModels/MainViewModel.cs:154-ish` (`_isRefreshing` field) — add `[NotifyCanExecuteChangedFor(nameof(RefreshCommand))]` if planner picks D-04 Option A
- `ViewModels/MainViewModel.cs:37-42` (`SessionDisplayItem`) — add `TooltipText` field (D-05)
- `ViewModels/MainViewModel.cs:629-637` (`SortedSessions` build) — remove `.Where(s => s.IsActive(threshold))`, compute `IsActive` per-item, compute `TooltipText` per-item (D-06, D-07)
- `ViewModels/MainViewModel.cs` — add `private string ComputeTooltipText(...)` helper (D-07)
- `ViewModels/MainViewModel.cs` — add `Receive(SessionTimeoutChangedMessage)` handler that triggers `RefreshSessionsAsync()` (D-08)
- `ViewModels/SettingsViewModel.cs` — add `_aboutTimestampTimer`, `StartAboutTimestampTimer()`, `StopAboutTimestampTimer()`, `LastFetchRelativeTime` computed property (D-09)
- `ViewModels/SettingsViewModel.cs` — add `SessionTimeoutChangedMessage` send call when settings change (D-08)
- `Views/SettingsView.xaml.cs` — extend `OnLoaded`, add `OnSegmentedSelectionChanged`, add `OnUnloaded` (D-10)
- `Views/SettingsView.xaml` — wire `SelectionChanged="OnSegmentedSelectionChanged"` on `TabsSegmented`, wire `Unloaded="OnUnloaded"` on the Page root
- `Views/MainView.xaml:606-618` — add disable-binding (D-04 Option B) OR no XAML change if Option A picked
- `Messages/SessionTimeoutChangedMessage.cs` (NEW) — single-line message type wrapping `int newMinutes` (D-08)
- `Converters/InvertedBooleanConverter.cs` (NEW, only if D-04 Option B picked) — bool → bool inversion
- `App.xaml` — register new converter in resources (only if D-04 Option B picked)
- `CCInfoWindows.Tests/ViewModels/MainViewModelTests.cs` (or new file) — test the 250 ms floor with `Stopwatch`, test `ComputeTooltipText` formatting (active vs inactive, EN+DE)
- `CCInfoWindows.Tests/ViewModels/SettingsViewModelTests.cs` (or new file) — test `_aboutTimestampTimer` start/stop lifecycle

### Architectural Constraints

- **`MainViewModel` Transient lifetime** (Phase 20 lock) — Phase 22 uses NO singleton-only patterns
- **`IUsageHistoryService` Singleton invariant** (Phase 21 lock) — Phase 22 does NOT touch the history service
- **`IsRefreshing` ObservableProperty name** (Phase 20 reserved) — Phase 22 does NOT rename
- **No HttpClient for Claude API** (project-wide) — Phase 22 does not touch the API path
- **Network allowlist** — Phase 22 is local-state only; no new endpoints
- **CLAUDE.md "Models/ → Plain data objects"** — Phase 22 honors this; D-05 keeps `SessionInfo` clean and puts `TooltipText` on the ViewModel-wrapper
- **CLAUDE.md "No code-behind logic in Views"** applies to business logic; view-lifecycle event routing is the documented exception (D-10)
- **CLAUDE.md "No magic numbers"** — `MinimumSpinnerDisplayMs = 250` and `AboutTabIndex = 3` are named constants
- **CLAUDE.md "Bash Permission Rules"** — every command in its own tool call (applies to all commits)
- **CLAUDE.md "F.I.R.S.T. tests"** — fast, independent, repeatable, self-validating, timely. The 250 ms floor test must not depend on absolute wall-clock timing fragility (use a tolerance window)

</code_context>

<specifics>
## Specific Ideas

- The user delegated all four gray-area decisions ("ich will, dass du für alle 4 Gray Areas deine Empfehlungen direkt verwendest und die Diskussion abschließ"). Pattern matches Phase 21's same delegation — Phase 22 should NOT re-open these in planning.
- The v1.1 `RefreshIcon` rotation animation is a project-validated quality attribute; preserving it is non-negotiable. Spec FEAT-09c's ProgressRing replacement is rejected (D-01).
- The `SessionInfo` Plain Data Model convention is non-negotiable; `TooltipText` lives on `SessionDisplayItem` (D-05).
- Pre-existing bug `IsActive = true` hardcoded (MainViewModel.cs:636) gets fixed opportunistically as part of D-06 — POLISH-04 requires inactive sessions to appear in the ComboBox, which forces the filter relaxation.
- Phase 22 is the only v1.4 phase that touches three disjoint code paths (Refresh-Button, Session-ComboBox, About-Tab) with zero cross-coupling. Wave-parallelization is natural — three plans can execute in parallel during `/gsd-execute-phase`.

</specifics>

<deferred>
## Deferred Ideas

- **`InvertedBooleanConverter` extraction** — only needed if D-04 Option B is picked. If Option A wins, defer indefinitely; no consumer.
- **`ITimeProvider` injection for testability** — surfaced under D-Claude's-Discretion for the 250 ms floor test. Heavy abstraction for a single timer; deferred to a future phase that needs cross-cutting time mocking. Default test strategy uses `Stopwatch` with a tolerance window.
- **Unified anti-flicker helper (`async Task WithMinimumDuration(Task work, TimeSpan floor)`)** — could extract the `Task.WhenAll(work, Task.Delay(...))` pattern into a static helper. No current second consumer; defer until a second phase introduces a similar floor.
- **Timer lifetime audit across the app** — Phase 22 introduces `_aboutTimestampTimer`. PROJECT.md "Tech debt" already lists `BurnRateTimer` and other DispatcherTimers. A future phase could centralize timer lifecycle into a single `ITimerService`. Out of scope for Phase 22.
- **Inactive-session display ordering tweak** — D-06 keeps the existing `OrderByDescending(LastActivity)` sort, which puts inactive sessions trailing. If UAT reveals visual confusion (active mixed with inactive at the boundary), planner can add `ThenBy(IsActive ? 0 : 1)` for explicit grouping. Belongs in plan-level decisions, not phase-level.
- **`AboutTabIndex` centralization** — magic-number `3` in `SettingsView.xaml.cs`. If Settings tabs are reordered later, this breaks silently. Could centralize as `SettingsViewModel.AboutTabIndex` constant. Default: keep the const in Code-Behind for now; revisit if tab order changes.
- **`LastFetchRelativeTime` formatting localization** — depends on existing relative-time helpers in the codebase. If none exist, planner adds a small `RelativeTimeFormatter.Format(DateTimeOffset)` helper that returns localized "X minutes ago" / "vor X Minuten". Not a new gray area; mechanical implementation.
- **Spinner-Pattern consolidation** — D-01 keeps the v1.1 storyboard-based spinner. A future phase could unify all loading indicators (RefreshIcon rotation, ProgressRing in LoginView, ShimmerAnimation for aggregating) into a single `LoadingIndicator` UserControl. Out of scope for v1.4.

</deferred>

---

*Phase: 22-ui-polish*
*Context gathered: 2026-05-06*
