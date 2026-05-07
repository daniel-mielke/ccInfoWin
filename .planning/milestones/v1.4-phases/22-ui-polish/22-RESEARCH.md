# Phase 22: UI Polish — Research

**Researched:** 2026-05-06
**Domain:** WinUI 3 / CommunityToolkit.Mvvm 8.4 / .NET 9 — three disjoint UI polish tasks (anti-flicker spinner, two-line tooltip, About-tab live timer)
**Confidence:** HIGH

## Summary

Phase 22 implements three disjoint, low-risk UI improvements on the existing v1.3 dashboard. The technical research shows that **all three tasks reuse existing infrastructure** — the v1.1 `SpinnerStoryboard`, the existing `SessionDisplayItem` ViewModel-wrapper, and the existing `WeakReferenceMessenger` settings-change pattern. Zero new XAML primitives, zero new dependencies, zero new architectural patterns.

The CONTEXT.md (D-01..D-11) has already locked all gray-area decisions. Research's job here is verification, not exploration: confirm that the locked decisions are implementable against the actual codebase shape, surface any discrepancies, and quantify the test surface.

**Primary recommendation:**
- **Spinner:** Use `[RelayCommand(CanExecute = nameof(CanRefresh))]` + `[NotifyCanExecuteChangedFor(nameof(RefreshCommand))]` (D-04 Option A). The codebase contains **zero** prior uses of `[NotifyCanExecuteChangedFor]` (verified via grep) — Phase 22 introduces this pattern. Still preferred over a new `InvertedBooleanConverter` because the Toolkit attribute is the canonical CommunityToolkit.Mvvm 8.4 path.
- **Tooltip:** Extend `SessionDisplayItem` with `required string TooltipText`. **Pre-existing bug confirmed**: `MainViewModel.cs:643` hardcodes `IsActive = true`. Filter at line 637 (`.Where(s => s.IsActive(threshold))`) must be removed (D-06).
- **Timer:** Use `Microsoft.UI.Xaml.DispatcherTimer` (NOT WPF's `System.Windows.Threading.DispatcherTimer`, NOT `DispatcherQueueTimer` from `DispatcherQueue.CreateTimer()`). The CONTEXT.md skeleton (D-09) using `new DispatcherTimer { Interval = ... }` is the correct WinUI 3 API.

---

## User Constraints (from CONTEXT.md)

### Locked Decisions (D-01..D-11)

- **D-01:** Spec FEAT-09c rejected. Existing v1.1 `FontIcon &#xE895;` + `RotateTransform` + `SpinnerStoryboard` IS the visual spinner. Zero new XAML for POLISH-01.
- **D-02:** 250 ms minimum-display floor applied **only** to the manual `[RelayCommand] Refresh()`, NOT to auto-poll-timer-driven `PollUsageAsync()`.
- **D-03:** Refactor `PollUsageAsync` — extract a private `PollUsageCoreAsync()` that does NOT touch `IsRefreshing`. Both `PollUsageAsync` (auto-poll wrapper) and `Refresh` (manual wrapper) own `IsRefreshing` lifetime.
- **D-04:** Disable-while-refreshing via `[RelayCommand(CanExecute = nameof(CanRefresh))]` + `[NotifyCanExecuteChangedFor(nameof(RefreshCommand))]` on `_isRefreshing` (Option A — recommended).
- **D-05:** `TooltipText` lives on `SessionDisplayItem` ViewModel-wrapper (`MainViewModel.cs:37-42`), NOT on `SessionInfo` (Plain Data Model convention).
- **D-06:** Pre-existing bug fix in scope: remove `.Where(s => s.IsActive(threshold))` filter at `MainViewModel.cs:637`; compute `IsActive` per item from `s.IsActive(threshold)`. Inactive sessions now appear in the ComboBox.
- **D-07:** New private helper `MainViewModel.ComputeTooltipText(session, isActive, sessionTimeoutMinutes)` returns single-line `Cwd` for active and `$"{Cwd}\n{template}"` for inactive (template from `Localizer.GetLocalizedString("InactiveSessionTooltip")`).
- **D-08:** New `SessionTimeoutChangedMessage` (in `Messages/`, follows the `RefreshIntervalChangedMessage` shape), sent by `SettingsViewModel.OnSelectedThresholdIndexChanged`, received by `MainViewModel` to trigger `RefreshSessionsAsync()`.
- **D-09:** `_aboutTimestampTimer`, `StartAboutTimestampTimer()`, `StopAboutTimestampTimer()`, `LastFetchRelativeTime` (computed) live on `SettingsViewModel`. Tick handler raises `OnPropertyChanged(nameof(LastFetchRelativeTime))`.
- **D-10:** Three event handlers in `SettingsView.xaml.cs`: extend existing `OnLoaded`, add `OnSegmentedSelectionChanged` (Segmented.SelectionChanged), add `OnUnloaded` (Page.Unloaded). Constant `AboutTabIndex = 3`.
- **D-11:** `LastFetchRelativeTime` is a pure computed property (no `[ObservableProperty]`); the timer's `OnPropertyChanged` is what drives rebinding.

### Claude's Discretion

- D-04 implementation choice (Option A `[NotifyCanExecuteChangedFor]` vs Option B `InvertedBooleanConverter`) — **research recommends Option A**.
- `PollUsageCoreAsync` exact split shape.
- Inactive-session secondary sort (`.ThenBy(s => s.IsActive ? 0 : 1)`) — only if UAT shows visual confusion.
- `SessionTimeoutChangedMessage` exact namespace.
- `AboutTabIndex` location (const in code-behind vs public on `SettingsViewModel`) — research recommends const in code-behind.
- 250 ms floor test mock strategy — research recommends `Stopwatch` + tolerance window over `ITimeProvider` injection.
- `InvertedBooleanConverter` — only introduce if D-04 Option B picked (NOT recommended).
- Pre-existing `IsActive` bug fix granularity (separate plan vs in-line).

### Deferred Ideas (OUT OF SCOPE)

- `InvertedBooleanConverter` extraction (defer indefinitely if Option A wins).
- `ITimeProvider` injection for testability.
- Unified `WithMinimumDuration(Task, TimeSpan)` helper.
- Timer lifetime audit / `ITimerService` centralization.
- Active-first explicit grouping via secondary sort.
- `AboutTabIndex` centralization to `SettingsViewModel`.
- `LastFetchRelativeTime` formatter localization helper extraction (mechanical, not a new gray area).
- Spinner-pattern consolidation across the app (RefreshIcon + ProgressRing + ShimmerAnimation).

---

## Phase Requirements

| ID | Description | Research Support |
|----|-------------|------------------|
| POLISH-01 | Refresh button shows a refresh-in-progress visual while refreshing | D-01 reinterprets "ProgressRing" as the existing rotating `FontIcon` — already wired via `MainView.xaml.cs` `OnViewModelPropertyChanged` reacting to `IsRefreshing` |
| POLISH-02 | Spinner stays visible >= 250 ms even on cached sub-100 ms refreshes | `Task.WhenAll(PollUsageCoreAsync(), Task.Delay(250))` inside the manual `Refresh()` RelayCommand. Verified existing `Refresh()` body at `MainViewModel.cs:857-861` is a single line awaiting `PollUsageAsync()` — direct extension point |
| POLISH-03 | Refresh button disabled while `IsRefreshing == true` | `[RelayCommand(CanExecute = nameof(CanRefresh))]` + `[NotifyCanExecuteChangedFor(nameof(RefreshCommand))]` on `_isRefreshing` field. WinUI 3 `Button.Command.CanExecute` auto-drives `IsEnabled` |
| POLISH-04 | Inactive ComboBox items show two-line tooltip (path + threshold) | `SessionDisplayItem.TooltipText` carries pre-composed `$"{Cwd}\n{template}"` string; `\n` rendered as line break by default `TextBlock` inside tooltip |
| POLISH-05 | Active ComboBox items keep single-line path tooltip | Same property, returns `Cwd` only when `IsActive == true`. Note: existing v1.3 has NO tooltip on the ComboBox items at all (verified at `MainView.xaml:104-107`) — Phase 22 introduces tooltips on both active and inactive |
| POLISH-06 | Tooltip recomputes when `SessionActivityThresholdMinutes` changes | New `SessionTimeoutChangedMessage` sent from `SettingsViewModel.OnSelectedThresholdIndexChanged` (currently no message — line 139-144 only persists; needs Send call added). Received by new `MainViewModel.Receive(SessionTimeoutChangedMessage)` handler triggering `RefreshSessionsAsync()` |
| POLISH-07 | About-tab "X minutes ago" refreshes every minute via DispatcherTimer | `Microsoft.UI.Xaml.DispatcherTimer { Interval = TimeSpan.FromMinutes(1) }`. Tick handler raises `OnPropertyChanged(nameof(LastFetchRelativeTime))`. Bound TextBlock (currently `LastPricingFetchText` at `SettingsView.xaml:295`) needs swap to `LastFetchRelativeTime` |
| POLISH-08 | Timer stops on tab switch and Page.Unloaded | `Segmented.SelectionChanged` + `Page.Unloaded` wired in `SettingsView.xaml.cs`; both route to `StopAboutTimestampTimer()` |

---

## Project Constraints (from CLAUDE.md)

- **MVVM**: `[ObservableProperty]` / `[RelayCommand]` source generators only — no manual property change plumbing.
- **No code-behind business logic in Views** — view-lifecycle event routing IS the documented exception (existing `OnLoaded` + `ApplyTabTooltips` precedent at `SettingsView.xaml.cs:22-35`).
- **`Models/` = Plain Data Objects** — `SessionInfo` stays a POCO (verified: no methods other than the existing `IsActive(TimeSpan)` and `ToString()`). `TooltipText` MUST live on the ViewModel-wrapper.
- **No magic numbers** — `MinimumSpinnerDisplayMs = 250`, `AboutTabIndex = 3`, both as named constants.
- **Bash Permission Rules** — every command in its own Bash tool call; no chaining with `;`, `&&`, `||`, `|`.
- **Async patterns** — always `async/await`, `DispatcherQueue.TryEnqueue` for UI marshaling. `DispatcherTimer.Tick` is already on UI thread (no marshaling needed).
- **Wrap external libraries** — Toolkit `[RelayCommand]` and Localizer are already wrapped via the source-generator pattern; nothing new to wrap.
- **F.I.R.S.T. tests** — 250 ms floor test must use a tolerance window, not exact wall-clock equality.
- **Network allowlist** — Phase 22 is local-state only; no new endpoints.

---

## Architectural Responsibility Map

| Capability | Primary Tier | Secondary Tier | Rationale |
|------------|-------------|----------------|-----------|
| 250 ms minimum-display floor | ViewModel (`MainViewModel`) | — | Timing is business state; XAML stays unchanged |
| Refresh button disabled state | ViewModel (RelayCommand CanExecute) | View (Button.IsEnabled auto-driven) | `[RelayCommand(CanExecute)]` + `[NotifyCanExecuteChangedFor]` is the canonical Toolkit pattern; XAML auto-binds via `Command.CanExecute` |
| `TooltipText` composition | ViewModel (`SessionDisplayItem`) | View (XAML `ToolTipService.ToolTip` binding) | Tooltip text is data; XAML only renders |
| Tooltip recompute on settings change | Cross-VM Messenger (`WeakReferenceMessenger`) | — | Same pattern as `RefreshIntervalChangedMessage`, `SonnetContextChangedMessage` |
| `_aboutTimestampTimer` ownership | ViewModel (`SettingsViewModel`) | View (lifecycle event routing only) | Timer lifetime is business state; view only signals "About tab is now visible / no longer visible" |
| Tab-active detection | View (`SettingsView.xaml.cs`) | ViewModel (consumer of Start/Stop) | View is the ONLY place that knows about `Segmented.SelectionChanged` and `Page.Unloaded` |

---

## Standard Stack

### Core (already in project — verified via codebase grep)

| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| Windows App SDK / WinUI 3 | 1.8 | UI framework | Project's chosen stack [VERIFIED: CLAUDE.md §Stack] |
| CommunityToolkit.Mvvm | 8.4 | MVVM source generators (`[ObservableProperty]`, `[RelayCommand]`) | Project standard [VERIFIED: CLAUDE.md §MVVM Conventions] |
| WinUI3Localizer | (existing) | Runtime language switching via `Localizer.Get().GetLocalizedString(key)` | Project standard [VERIFIED: existing `SettingsView.xaml.cs:30` and `SettingsViewModel.cs:158`] |
| CommunityToolkit.WinUI.Controls | (existing) | `Segmented` control | Already in use at `SettingsView.xaml:34` and `MainView.xaml:452` [VERIFIED: codebase] |
| Microsoft.UI.Xaml.DispatcherTimer | .NET 9 / WinUI 3 | UI-thread-bound timer | Standard WinUI 3 timer for UI binding updates [CITED: Microsoft.UI.Xaml namespace] |

### No new packages — Phase 22 is purely additive code on the existing stack.

### Alternatives Considered

| Instead of | Could Use | Tradeoff | Verdict |
|------------|-----------|----------|---------|
| `Microsoft.UI.Xaml.DispatcherTimer` | `DispatcherQueue.CreateTimer()` (returns `DispatcherQueueTimer`) | `DispatcherQueueTimer` is the newer abstraction (Microsoft.UI.Dispatching), preferred for low-priority background ticks but more verbose. | **Use `DispatcherTimer`** — simpler API, already used by `_pollTimer` infrastructure pattern in `MainViewModel.cs:60` (which uses `DispatcherQueueTimer`). For a 1-minute UI-rebind tick, `DispatcherTimer` is fine. CONTEXT.md D-09 commits to `DispatcherTimer` — research confirms valid. |
| `[NotifyCanExecuteChangedFor]` (D-04 Option A) | `InvertedBooleanConverter` + IsEnabled binding (D-04 Option B) | Option A: zero XAML changes, idiomatic Toolkit, introduces a new attribute pattern. Option B: requires new converter file + App.xaml registration + XAML edit. | **Use Option A**. Verified: codebase has zero existing uses of `[NotifyCanExecuteChangedFor]` (grep clean) — Phase 22 introduces it. Pattern is canonical CommunityToolkit.Mvvm 8.4. [CITED: CommunityToolkit.Mvvm 8.4 docs] |

---

## Architecture Patterns

### System Architecture (Phase 22 data flow)

```
┌─────────────────────────────────────────────────────────────┐
│  Polish 1 — Anti-flicker Spinner                            │
│                                                              │
│  User clicks RefreshButton                                  │
│        │                                                    │
│        ▼                                                    │
│  RefreshCommand (CanExecute=!IsRefreshing)                  │
│        │ if !CanRefresh: ignored (button disabled)          │
│        ▼                                                    │
│  Refresh() — IsRefreshing=true                              │
│        │                                                    │
│        ▼                                                    │
│  Task.WhenAll(                                              │
│    PollUsageCoreAsync(),     ─┐                             │
│    Task.Delay(250ms)         ─┘ both must complete          │
│  )                                                          │
│        │                                                    │
│        ▼                                                    │
│  IsRefreshing=false ─► OnViewModelPropertyChanged           │
│                       (existing v1.1 listener)              │
│                       ─► SpinnerStoryboard.Stop             │
│                          (with _stopOnComplete gate)        │
└─────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────┐
│  Polish 2 — Inactive Session Tooltip                        │
│                                                              │
│  RefreshSessionsAsync() rebuild                             │
│        │                                                    │
│        ▼                                                    │
│  threshold = settings.SessionActivityThresholdMinutes       │
│        │                                                    │
│        ▼                                                    │
│  for each session:                                          │
│    isActive = session.IsActive(threshold)                   │
│    tooltipText = ComputeTooltipText(session, isActive,      │
│                                     thresholdMinutes)       │
│    new SessionDisplayItem { Session, DisplayName,           │
│                             IsActive=isActive,              │
│                             TooltipText=tooltipText }       │
│        │                                                    │
│        ▼                                                    │
│  ComboBox.ItemTemplate TextBlock binds                      │
│  ToolTipService.ToolTip="{x:Bind TooltipText}"              │
│                                                              │
│  Settings change ──► SessionTimeoutChangedMessage           │
│                  ──► MainViewModel.Receive(...)             │
│                  ──► RefreshSessionsAsync() rebuild         │
└─────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────┐
│  Polish 3 — About-Tab DispatcherTimer                       │
│                                                              │
│  SettingsView.Loaded ──┐                                    │
│  Segmented.Selection ──┼─► OnSegmentedSelectionChanged      │
│                        │      if SelectedIndex == 3:        │
│                        │        ViewModel.StartAboutTimer() │
│                        │      else:                         │
│                        │        ViewModel.StopAboutTimer()  │
│                        │                                    │
│  SettingsView.Unloaded ─► OnUnloaded                        │
│                           ViewModel.StopAboutTimer()        │
│                                                              │
│  StartAboutTimer:                                           │
│    _aboutTimestampTimer = new DispatcherTimer {             │
│      Interval = TimeSpan.FromMinutes(1)                     │
│    }                                                        │
│    .Tick += () => OnPropertyChanged(                        │
│                     nameof(LastFetchRelativeTime))          │
│    .Start()                                                 │
│                                                              │
│  TextBlock binds {x:Bind LastFetchRelativeTime, OneWay}     │
│   ─► every Tick re-reads property ─► re-formats from        │
│      _pricingService.LastFetch                              │
└─────────────────────────────────────────────────────────────┘
```

### Pattern 1: 250 ms Anti-flicker Floor

**What:** Wrap async work in `Task.WhenAll(work, Task.Delay(floor))` so the visible spinner cannot disappear before `floor` ms even on sub-100 ms cached refreshes.
**When to use:** UI feedback that should not flicker (perceived-latency engineering).
**Example:**
```csharp
// Source: CONTEXT.md D-03 skeleton, validated against MainViewModel.cs:857-861
private const int MinimumSpinnerDisplayMs = 250;

[RelayCommand(CanExecute = nameof(CanRefresh))]
private async Task Refresh()
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

private bool CanRefresh => !IsRefreshing;
```

### Pattern 2: `[NotifyCanExecuteChangedFor]` for Auto-Disable

**What:** When a backing field changes, the Toolkit source generator auto-raises `RaiseCanExecuteChanged()` on the target command — `Button.IsEnabled` follows automatically.
**When to use:** Any command that should be disabled while some state is true.
**Example:**
```csharp
// Source: CommunityToolkit.Mvvm 8.4 docs [CITED]
[ObservableProperty]
[NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
private bool _isRefreshing;

private bool CanRefresh => !IsRefreshing;
```
Verified: codebase has zero prior uses (grep clean). Phase 22 introduces this attribute.

### Pattern 3: WeakReferenceMessenger for Settings Propagation

**What:** Settings change in `SettingsViewModel` triggers `WeakReferenceMessenger.Default.Send(new XxxChangedMessage(value))`. `MainViewModel` implements `IRecipient<XxxChangedMessage>` and reacts.
**When to use:** Cross-ViewModel notification without coupling.
**Example:**
```csharp
// Source: existing pattern at SettingsViewModel.cs:136 (RefreshIntervalChangedMessage)
// SettingsViewModel partial change handler — line 139-144 currently lacks Send call
partial void OnSelectedThresholdIndexChanged(int value)
{
    var settings = _settingsService.LoadSettings();
    settings.SessionActivityThresholdMinutes = MapThresholdIndexToMinutes(value);
    _settingsService.SaveSettings(settings);
    WeakReferenceMessenger.Default.Send(
        new SessionTimeoutChangedMessage(settings.SessionActivityThresholdMinutes)); // NEW
}

// Messages/SessionTimeoutChangedMessage.cs (NEW)
public class SessionTimeoutChangedMessage : ValueChangedMessage<int>
{
    public SessionTimeoutChangedMessage(int minutes) : base(minutes) { }
}

// MainViewModel — extend IRecipient list
public partial class MainViewModel : ObservableObject,
    IRecipient<AuthStateChangedMessage>,
    IRecipient<SessionTimeoutChangedMessage>  // NEW
{
    public void Receive(SessionTimeoutChangedMessage message)
    {
        _ = RefreshSessionsAsync(); // rebuild SortedSessions with new threshold
    }
}
```

### Pattern 4: View-Lifecycle Event Routing in Code-Behind

**What:** `Page.Loaded`, `Page.Unloaded`, `Segmented.SelectionChanged` route to ViewModel methods. Code-behind contains NO business logic — just one-line dispatch calls.
**When to use:** When ViewModel state depends on view lifecycle (e.g., timer that should only run while a tab is visible).
**Example:**
```csharp
// Source: existing precedent at SettingsView.xaml.cs:22-35 (OnLoaded + ApplyTabTooltips)
private const int AboutTabIndex = 3;

public SettingsView()
{
    ViewModel = App.Services.GetRequiredService<SettingsViewModel>();
    InitializeComponent();
    Loaded += OnLoaded;
    Unloaded += OnUnloaded;  // NEW
}

private void OnLoaded(object sender, RoutedEventArgs e)
{
    ViewModel.Initialize();
    ApplyTabTooltips();
    if (TabsSegmented.SelectedIndex == AboutTabIndex)  // NEW
        ViewModel.StartAboutTimestampTimer();
}

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
**Note on naming:** the `Segmented` element in `SettingsView.xaml:34-79` currently has NO `x:Name`. The planner MUST add `x:Name="TabsSegmented"` (or pick whichever name the planner uses) and wire `SelectionChanged="OnSegmentedSelectionChanged"`.

### Anti-Patterns to Avoid

- **Hand-rolled `Stopwatch`-driven minimum-display loop** — `Task.WhenAll(work, Task.Delay)` is idiomatic and race-free.
- **Putting `TooltipText` on `SessionInfo`** — violates `Models/ → Plain Data Objects`. ViewModel-wrapper is the right home (D-05).
- **Computing `LastFetchRelativeTime` as `[ObservableProperty]`** — would require setter calls; pure computed + `OnPropertyChanged(nameof(...))` is the correct pattern (D-11). Verified: `SettingsViewModel.cs:55-57, 89-91` already uses this exact pattern for `AppVersionText` and `LastPricingFetchText`.
- **Two animation systems on the same element** — D-01 already rejects bolting `ProgressRing` next to the existing storyboard.
- **`Microsoft.Toolkit.Uwp.UI.Helpers.DispatcherHelper`** — legacy UWP toolkit. Use `Microsoft.UI.Xaml.DispatcherTimer` directly.

---

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Auto-disable button while command runs | `IsEnabled="{Binding IsRefreshing, Converter={...InvertedBoolean...}}"` (D-04 Option B) | `[RelayCommand(CanExecute=...)]` + `[NotifyCanExecuteChangedFor]` (Option A) | Idiomatic Toolkit; zero new XAML; no new converter file |
| Cross-VM event signaling | Static event / shared static field | `WeakReferenceMessenger.Default` | Already 4 message types in `Messages/`; pattern proven |
| UI-thread timer | `System.Threading.Timer` + `DispatcherQueue.TryEnqueue` per tick | `Microsoft.UI.Xaml.DispatcherTimer` | Tick handler already on UI thread; no marshaling code |
| Storyboard completion gate | New gate flag + Tick logic | Existing `_stopOnComplete` mechanism in `MainView.xaml.cs:29, 167-192` | v1.1 quality attribute, locked by PROJECT.md |
| Anti-flicker minimum-display | `Stopwatch` + manual delay loop after `await work` | `await Task.WhenAll(work, Task.Delay(floor))` | Race-free, single line, no extra state |
| Two-line tooltip layout | `Grid` + nested `TextBlock`s | Single `TextBlock` with `\n` (default rendering wraps) | UI-SPEC D-05 explicitly chooses this; no new XAML structure |

**Key insight:** Phase 22 has zero "novel" engineering. Every locked decision points to an existing pattern in the codebase or in CommunityToolkit.Mvvm. Risk is purely in the mechanical implementation, not in design.

---

## Common Pitfalls

### Pitfall 1: `IsRefreshing` Setter Race During `Task.WhenAll`

**What goes wrong:** If the manual `Refresh()` finishes its work in 50 ms but `Task.Delay(250)` is still pending, an auto-poll-timer tick at 100 ms could re-enter `PollUsageAsync` and find `IsRefreshing == true`, returning early — but that's actually the correct guard behavior. The opposite race is the concern: an auto-poll fires its `IsRefreshing = true` while a manual click is mid-`Task.Delay`.

**Why it happens:** The `if (IsRefreshing) return;` guard sits at the start of both wrappers. Both wrappers set/unset `IsRefreshing` in their `try/finally`.

**How to avoid:** Keep both guards (D-03 specifies this). The `IsRefreshing` flag is correctly the single-source-of-truth lock; the 250 ms `Task.Delay` extends only the manual path's hold on the lock.

**Warning signs:** Spinner stops mid-rotation when poll-timer fires during a manual refresh.

### Pitfall 2: Removing `.Where(s => s.IsActive(threshold))` Changes Selection Behavior

**What goes wrong:** D-06 removes the active-only filter. Code at `MainViewModel.cs:680` does `.FirstOrDefault(d => d.IsActive)` to fall back to "first active session" — this still works because we now compute `IsActive` per item correctly. But code at line 671 (`.FirstOrDefault(d => d.Session.Id == settings.LastSelectedSessionId)`) could now restore an INACTIVE session as the selection. That may surprise users.

**Why it happens:** The previous filter pre-removed inactive items, so persistence-restore could only land on an active item by definition.

**How to avoid:** The planner should consider whether persisted-selection restoration of an inactive session is desired. Trade-off: user-friendly (their session is "still there") vs. confusing (selection shows zeros for context). Default: allow inactive restoration; UX is fine because the active session may legitimately have gone inactive between app sessions.

**Warning signs:** App restart → ComboBox shows the user's last session even though it has no recent activity; context numbers all zero.

### Pitfall 3: `LastFetchRelativeTime` Format String Localization

**What goes wrong:** CONTEXT.md D-11 says "compute relative time using existing localization keys" but no such keys exist in the codebase yet. `SettingsViewModel.LastPricingFetchText` (line 89-91) currently formats as absolute date `"dd.MM.yyyy HH:mm"` — there is NO existing relative-time helper.

**Why it happens:** The macOS spec assumes a `Date.RelativeFormatStyle` exists; .NET has no built-in equivalent.

**How to avoid:** Planner picks one of: (a) inline relative-time formatting in `LastFetchRelativeTime` getter using `(DateTimeOffset.Now - lastFetch).TotalMinutes`, with localized template strings authored in Phase 23 (e.g. `LastFetchRelativeMinutesAgo` = "vor {0} Minuten" / "{0} minutes ago"); (b) adopt a `RelativeTimeFormatter` helper in `Helpers/`. Default research recommendation: inline formatting (option a) — keeps Phase 22 minimal; helper extraction is a deferred idea.

**Warning signs:** About tab shows raw timestamp instead of "X minutes ago" because no formatter exists.

### Pitfall 4: `Segmented` Has No `x:Name` in `SettingsView.xaml`

**What goes wrong:** The Segmented control at `SettingsView.xaml:34-79` has NO `x:Name`. Phase 22 needs to access `SelectedIndex` from code-behind (D-10).

**Why it happens:** Existing tab-active state is tracked via ViewModel properties (`IsGeneralTabVisible` etc.), not via direct view access.

**How to avoid:** Planner adds `x:Name="TabsSegmented"` to the Segmented element AND wires `SelectionChanged="OnSegmentedSelectionChanged"`. Alternative: read `ViewModel.SelectedTabIndex` directly in the handler (no `x:Name` needed), reusing the existing `[ObservableProperty]` at `SettingsViewModel.cs:40`. Recommended: use `ViewModel.SelectedTabIndex == AboutTabIndex` — avoids adding `x:Name`, is idiomatic.

**Warning signs:** Compile error `TabsSegmented does not exist` when code-behind references the element.

### Pitfall 5: `OnSelectedTabIndexChanged` Already Exists — Hook the Timer There Instead?

**What goes wrong:** `SettingsViewModel.OnSelectedTabIndexChanged` (line 47-53) already raises `OnPropertyChanged` for the four `IsXxxTabVisible` properties. The timer Start/Stop logic could live HERE instead of in code-behind, which would be more MVVM-pure.

**Why it happens:** D-10 puts the trigger in code-behind because `Page.Unloaded` lives there anyway. But the tab-switch trigger (`Segmented.SelectionChanged`) could be replaced by `partial void OnSelectedTabIndexChanged(int value)` extension — no view-layer event routing needed.

**How to avoid:** Planner picks. Option (a) — D-10 as-locked: code-behind handles `SelectionChanged`, `Loaded`, `Unloaded`. Option (b) — hybrid: `OnSelectedTabIndexChanged` partial method handles tab-switch in ViewModel; only `Page.Unloaded` lives in code-behind. **Research recommends Option (b)** — fewer code-behind handlers, cleaner separation. The `Page.Unloaded` exception is unavoidable because no ViewModel callback exists for it. CONTEXT.md D-10 should be re-confirmed by the planner; the existing `OnSelectedTabIndexChanged` at line 47 is a strictly cleaner hook.

**Warning signs:** Two handlers (one in VM, one in code-behind) both reacting to tab change → potential timer Start/Stop double-call. Pick one home.

### Pitfall 6: `[NotifyCanExecuteChangedFor]` Requires `[ObservableProperty]` on the SAME Backing Field

**What goes wrong:** The attribute is placed on a `[ObservableProperty]` field. CommunityToolkit source generator emits the `RaiseCanExecuteChanged` call inside the generated property setter. If the field is NOT decorated with `[ObservableProperty]`, no setter is generated and the attribute is silently inert.

**How to avoid:** Verify `_isRefreshing` is decorated with `[ObservableProperty]` (verified: `MainViewModel.cs:153-154`). Add `[NotifyCanExecuteChangedFor(nameof(RefreshCommand))]` adjacent.

**Warning signs:** Button does not auto-disable; `CanRefresh` is never re-evaluated.

### Pitfall 7: `DispatcherTimer.Tick` Subscription Leak on Repeated Start

**What goes wrong:** If `StartAboutTimestampTimer()` is called twice without `Stop()` between, two timers exist; only the field-stored reference is `Stop()`-able. The first orphan ticks forever.

**How to avoid:** D-09 skeleton has `if (_aboutTimestampTimer != null) return;` guard at the top of `Start`. Verify presence in implementation.

**Warning signs:** "X minutes ago" updates twice per minute; CPU usage rises after multiple tab switches.

---

## Code Examples

### Example 1: Refactored `PollUsageAsync` + new `Refresh()`

```csharp
// Source: validated against existing MainViewModel.cs:404-441 and 857-861
private const int MinimumSpinnerDisplayMs = 250;

private async Task PollUsageCoreAsync()
{
    HasApiError = false;
    ApiErrorMessage = string.Empty;

    try
    {
        var result = await _apiService.FetchUsageAsync();
        if (result != null)
        {
            await UpdateUsagePropertiesAsync(result);
            _autoReauthAttempted = false;
        }
        else
        {
            HasApiError = true;
            ApiErrorMessage = "API returned empty data. The response body could not be deserialized.";
        }
    }
    catch (HttpFetchException ex)
    {
        HasApiError = true;
        ApiErrorMessage = $"API request failed (HTTP {ex.StatusCode}).";
        Debug.WriteLine($"[MainViewModel] PollUsage: {ex.Message}");
    }
    catch (Exception ex)
    {
        HasApiError = true;
        ApiErrorMessage = "API request failed. Please try again.";
        Debug.WriteLine($"[MainViewModel] PollUsage: {ex.Message}");
    }
}

private async Task PollUsageAsync()  // existing auto-poll entry
{
    if (IsRefreshing) return;
    IsRefreshing = true;
    try { await PollUsageCoreAsync(); }
    finally { IsRefreshing = false; }
}

[RelayCommand(CanExecute = nameof(CanRefresh))]
private async Task Refresh()  // existing manual entry — extended
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

private bool CanRefresh => !IsRefreshing;
```

### Example 2: Extended `SessionDisplayItem` + `ComputeTooltipText` helper

```csharp
// Source: validated against MainViewModel.cs:37-42 and 629-647
public class SessionDisplayItem
{
    public required SessionInfo Session { get; init; }
    public required string DisplayName { get; init; }
    public required bool IsActive { get; init; }
    public required string TooltipText { get; init; }   // NEW
}

private string ComputeTooltipText(SessionInfo session, bool isActive, int sessionTimeoutMinutes)
{
    if (isActive)
    {
        return session.Cwd;
    }

    var template = Localizer.Get().GetLocalizedString("InactiveSessionTooltip");
    // template: "Inaktiv seit > {0}min" / "Inactive for > {0}min"
    return $"{session.Cwd}\n{string.Format(template, sessionTimeoutMinutes)}";
}

// In RefreshSessionsAsync, replace lines 636-645:
var displayItems = latestSessions
    .OrderByDescending(s => s.LastActivity)
    .Select(s =>
    {
        var isActive = s.IsActive(threshold);
        return new SessionDisplayItem
        {
            Session = s,
            DisplayName = s.DisplayName,
            IsActive = isActive,
            TooltipText = ComputeTooltipText(s, isActive, settings.SessionActivityThresholdMinutes)
        };
    })
    .ToList();
```

### Example 3: XAML — single-line addition to `MainView.xaml:104-107`

```xml
<DataTemplate x:DataType="viewmodels:SessionDisplayItem">
    <TextBlock Text="{x:Bind DisplayName}"
               ToolTipService.ToolTip="{x:Bind TooltipText}"
               VerticalAlignment="Center" />
</DataTemplate>
```

### Example 4: `SettingsViewModel` timer skeleton

```csharp
// Source: CONTEXT.md D-09, validated against existing SettingsViewModel.cs structure
private DispatcherTimer? _aboutTimestampTimer;

public string LastFetchRelativeTime
{
    get
    {
        var lastFetch = _pricingService.LastFetch;
        if (!lastFetch.HasValue)
            return Localizer.Get().GetLocalizedString("LastFetchNever"); // or "Nie"

        var elapsedMinutes = (int)(DateTimeOffset.Now - lastFetch.Value).TotalMinutes;
        var template = Localizer.Get().GetLocalizedString("LastFetchMinutesAgo");
        // template: "vor {0} Minuten" / "{0} minutes ago"
        return string.Format(template, elapsedMinutes);
    }
}

public void StartAboutTimestampTimer()
{
    if (_aboutTimestampTimer != null) return;
    _aboutTimestampTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
    _aboutTimestampTimer.Tick += (_, _) => OnPropertyChanged(nameof(LastFetchRelativeTime));
    _aboutTimestampTimer.Start();
    OnPropertyChanged(nameof(LastFetchRelativeTime)); // initial tick on enter
}

public void StopAboutTimestampTimer()
{
    _aboutTimestampTimer?.Stop();
    _aboutTimestampTimer = null;
}
```

### Example 5: New message type

```csharp
// Source: pattern from Messages/RefreshIntervalChangedMessage.cs
namespace CCInfoWindows.Messages;

using CommunityToolkit.Mvvm.Messaging.Messages;

public class SessionTimeoutChangedMessage : ValueChangedMessage<int>
{
    public SessionTimeoutChangedMessage(int thresholdMinutes) : base(thresholdMinutes) { }
}
```

---

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| Manual `RaiseCanExecuteChanged()` calls in property setters | `[NotifyCanExecuteChangedFor]` source-generator attribute | CommunityToolkit.Mvvm 8.x | Zero plumbing code; declarative |
| WPF `System.Windows.Threading.DispatcherTimer` | WinUI 3 `Microsoft.UI.Xaml.DispatcherTimer` | WinUI 3 / WindowsAppSDK | Same API surface; different namespace |
| `Microsoft.Toolkit.Uwp.UI.Helpers` | `Microsoft.UI.Xaml` direct | UWP → WinUI 3 migration | Less indirection |
| `EventAggregator` / `IMessageBus` | `WeakReferenceMessenger.Default` (Toolkit) | Toolkit 8.x | Built-in, weak-ref, source-genned `IRecipient<T>` |

**Deprecated/outdated:**
- `Microsoft.Toolkit.Mvvm` package (renamed) — codebase already on `CommunityToolkit.Mvvm` 8.4.

---

## Runtime State Inventory

> Phase 22 is purely additive UI/VM code with no rename, refactor, or migration. Section omitted.

---

## Environment Availability

> Phase 22 has no external dependencies (no new tools, runtimes, services). All code targets the existing .NET 9 / WinUI 3 / Windows App SDK 1.8 stack already validated by Phases 16–21. Section is N/A.

---

## Validation Architecture

### Test Framework

| Property | Value |
|----------|-------|
| Framework | xUnit (existing — CCInfoWindows.Tests project) |
| Config file | `CCInfoWindows.Tests/CCInfoWindows.Tests.csproj` |
| Quick run command | `dotnet test CCInfoWindows.Tests/CCInfoWindows.Tests.csproj --filter Category!=Integration` |
| Full suite command | `dotnet test CCInfoWindows.Tests/CCInfoWindows.Tests.csproj` |

### Phase Requirements → Test Map

| Req ID | Behavior | Test Type | Automated Command | File Exists? |
|--------|----------|-----------|-------------------|--------------|
| POLISH-01 | Spinner storyboard starts on `IsRefreshing=true` | Manual smoke | (visual — WinUI 3 storyboard not headless-testable) | n/a |
| POLISH-02 | 250 ms minimum display floor — manual `Refresh()` always takes >= 250 ms even when `PollUsageCoreAsync` returns instantly | Unit (timing) | `dotnet test --filter FullyQualifiedName~MainViewModelTests.RefreshCommand_AppliesMinimumDisplayFloor` | ❌ Wave 0 — new test |
| POLISH-02 (negative) | `PollUsageAsync` (auto-poll) does NOT apply 250 ms floor | Unit (timing) | `dotnet test --filter FullyQualifiedName~MainViewModelTests.PollUsageAsync_DoesNotApplyMinimumDisplayFloor` | ❌ Wave 0 — new test |
| POLISH-03 | `RefreshCommand.CanExecute()` returns false when `IsRefreshing=true` | Unit | `dotnet test --filter FullyQualifiedName~MainViewModelTests.RefreshCommand_DisabledWhileRefreshing` | ❌ Wave 0 — new test |
| POLISH-04 | `ComputeTooltipText(session, isActive=false, threshold)` returns `"{cwd}\n{template}"` (DE + EN) | Unit | `dotnet test --filter FullyQualifiedName~MainViewModelTests.ComputeTooltipText_Inactive_TwoLine` | ❌ Wave 0 — new test |
| POLISH-05 | `ComputeTooltipText(session, isActive=true, threshold)` returns `cwd` only | Unit | `dotnet test --filter FullyQualifiedName~MainViewModelTests.ComputeTooltipText_Active_SingleLine` | ❌ Wave 0 — new test |
| POLISH-06 | `MainViewModel.Receive(SessionTimeoutChangedMessage)` triggers `RefreshSessionsAsync` and rebuilt items have new `TooltipText` | Unit | `dotnet test --filter FullyQualifiedName~MainViewModelTests.SessionTimeoutChangedMessage_RebuildsTooltips` | ❌ Wave 0 — new test |
| POLISH-07 | `StartAboutTimestampTimer()` creates `DispatcherTimer` with 60s interval; `Stop` nulls it | Unit (lifecycle) | `dotnet test --filter FullyQualifiedName~SettingsViewModelTests.AboutTimestampTimer_StartStopLifecycle` | ❌ Wave 0 — new test |
| POLISH-07 (binding) | After timer Tick, `OnPropertyChanged(nameof(LastFetchRelativeTime))` fires | Unit | `dotnet test --filter FullyQualifiedName~SettingsViewModelTests.AboutTimestampTimer_TickRaisesPropertyChanged` | ❌ Wave 0 — new test |
| POLISH-08 | `Page.Unloaded` triggers `StopAboutTimestampTimer` | Manual smoke | (XAML lifecycle event — not unit-testable) | n/a |
| POLISH-08 | Tab switch from About to General stops timer | Unit (via VM Start/Stop direct calls) | `dotnet test --filter FullyQualifiedName~SettingsViewModelTests.StopAboutTimestampTimer_NullifiesField` | ❌ Wave 0 — new test |

### Sampling Rate

- **Per task commit:** `dotnet test CCInfoWindows.Tests/CCInfoWindows.Tests.csproj --filter Category!=Integration` (~10 s)
- **Per wave merge:** `dotnet test CCInfoWindows.Tests/CCInfoWindows.Tests.csproj` (full suite)
- **Phase gate:** Full suite green + manual smoke checklist (spinner visual, tooltip hover, About-tab live update) before `/gsd-verify-work`.

### Wave 0 Gaps

- [ ] `CCInfoWindows.Tests/ViewModels/MainViewModelTests.cs` — extend with refresh-spinner timing tests, tooltip composition tests, `SessionTimeoutChangedMessage` handler tests. File presence: planner verifies; if absent, create.
- [ ] `CCInfoWindows.Tests/ViewModels/SettingsViewModelTests.cs` — extend with timer Start/Stop lifecycle tests. File presence: planner verifies.
- [ ] Localization test fixture: tests reference `Localizer.Get().GetLocalizedString("InactiveSessionTooltip")` which is authored by Phase 23. Tests must mock the localizer OR fall back to the key-name-as-fallback contract (Localizer returns the key string when the resource is missing — confirmed contract). Planner picks: (a) test with localizer-loaded fixture (DE + EN runs), (b) inject `IStringResolver` abstraction. Default: test with the localizer's key-fallback behavior — assertion checks `"Inaktiv seit > 30min"` substring presence under DE locale only after Phase 23 ships; pre-Phase-23, assertion checks `\nInactiveSessionTooltip` substring (key fallback).

### Manual Smoke Checklist (cannot be automated)

- [ ] Click refresh on a sub-100ms cached refresh — spinner visibly rotates >= 250 ms
- [ ] Auto-poll fires (wait 30s) — spinner duration matches actual API latency (no 250ms floor)
- [ ] Click refresh during refresh — second click ignored (button visibly disabled)
- [ ] Hover active session in ComboBox — tooltip shows single-line path
- [ ] Hover inactive session in ComboBox — tooltip shows two-line path + "Inaktiv seit > Nmin"
- [ ] Change SessionTimeout in Settings → re-open ComboBox — tooltip threshold matches new value
- [ ] Open Settings, switch to About tab — "X minutes ago" present; wait 60s — value increments
- [ ] Switch from About to General tab — "X minutes ago" stops updating (frozen)
- [ ] Close Settings page (back arrow) — no orphaned tick (verify with debug log or breakpoint)

---

## Security Domain

### Applicable ASVS Categories

| ASVS Category | Applies | Standard Control |
|---------------|---------|-----------------|
| V2 Authentication | no | Phase 22 does not touch auth |
| V3 Session Management | no | Phase 22 does not touch session-token state |
| V4 Access Control | no | Local desktop, single-user |
| V5 Input Validation | partial | `SessionTimeoutMinutes` value is from a fixed-set ComboBox (`[15, 30, 60, 120]` at `SettingsViewModel.cs:105`); `string.Format(template, value)` is safe — value is always int, template is from controlled localization resource |
| V6 Cryptography | no | No crypto in scope |

### Known Threat Patterns for WinUI 3 / .NET 9

| Pattern | STRIDE | Standard Mitigation |
|---------|--------|---------------------|
| `TooltipText` containing user-controlled path (`SessionInfo.Cwd`) | Information Disclosure | `Cwd` is read from local JSONL files written by Claude Code on the same machine — same trust boundary. No XSS surface (TextBlock, not WebView). No format-string injection (path is interpolated as `$"{cwd}\n..."`, not as format spec) |
| `string.Format(template, value)` where template is from resw | Tampering | resw resources are bundled read-only; threat assumes attacker can replace app binaries (already game-over). No mitigation needed at app layer |
| Localizer key fallback returning the key name | Information Disclosure | Returns `"InactiveSessionTooltip"` literal — exposes a localization key name to UI. Acceptable degradation; not a security issue |
| `DispatcherTimer` orphan tick (memory leak) | Denial of Service (resource exhaustion over many tab switches) | D-09 nullify-after-stop pattern + `OnUnloaded` belt-and-suspenders (D-10) |

---

## Assumptions Log

| # | Claim | Section | Risk if Wrong |
|---|-------|---------|---------------|
| A1 | `Localizer.Get().GetLocalizedString("InactiveSessionTooltip")` returns the key name as fallback when the resw entry is missing | Pitfall section + Wave 0 test fixture | If Localizer throws instead, Phase 22 must ship after Phase 23, OR use a try/catch in `ComputeTooltipText`. Mitigation: planner adds a defensive try/catch around the lookup. [ASSUMED — based on common WinUI3Localizer behavior; not verified in this session] |
| A2 | `LastFetchRelativeTime` requires NEW localization keys (`LastFetchMinutesAgo`, `LastFetchNever`) authored by Phase 23 — they do not exist today | Code Example 4 + Pitfall 3 | If Phase 22 ships before Phase 23, the About-tab text degrades to key-name. Mitigation: planner coordinates Phase 22/23 ordering, or uses inline German/English literals as a v1.4-only fallback. [ASSUMED — grep was not run for these specific keys in this research session, but `LastPricingFetchText` at `SettingsViewModel.cs:89-91` currently uses German absolute-date literals, implying no relative-time keys exist] |
| A3 | The four ComboBoxItem strings `"15min", "30min", "60min", "120min"` at `SettingsView.xaml:148-151` are the canonical mapping AND match `ThresholdMinuteOptions = [15, 30, 60, 120]` at `SettingsViewModel.cs:105` | Threshold value source | Confirmed via direct read of both files — VERIFIED. Not actually assumed; record kept here as a sanity flag for the planner. |

**Total assumptions:** 2 unverified ([A1], [A2]). Both have mitigation strategies. Neither blocks planning.

---

## Open Questions (RESOLVED)

> All four questions resolved during plan-phase. Plans 22-01..22-03 implement the recommendations:

1. **Phase 22 / Phase 23 ordering** — **RESOLVED:** defensive try/catch around localizer in Plan 22-02 Task 1; missing keys fall back to inline literals (no crash).
2. **Tab-switch trigger** — **RESOLVED:** D-10 honored as locked (code-behind `Segmented.SelectionChanged`). Pitfall 5 ViewModel-partial alternative documented as future-refactor in Plan 22-03 SUMMARY hand-off, NOT implemented.
3. **`PollUsageCoreAsync` exception handling** — **RESOLVED:** catch-all preserved per Plan 22-01 Task 1 (no behavioral change to error path).
4. **Persisted-session restoration after D-06 filter removal** — **RESOLVED:** treated as UX win per Plan 22-02 Task 1 inline note (user's last session preserved across restarts).

---

## Sources

### Primary (HIGH confidence — direct codebase verification)

- `MainView.xaml:18-24` — SpinnerStoryboard declaration verified (DoubleAnimation From=0 To=360, Duration 0:0:1, targets `RefreshIconTransform.Angle`)
- `MainView.xaml:606-618` — FooterRefreshButton structure verified (Command binding to `RefreshCommand`, FontIcon `&#xE895;`, `RotateTransform x:Name="RefreshIconTransform"`)
- `MainView.xaml:96-109` — SessionComboBox + ItemTemplate (DataTemplate `x:DataType="viewmodels:SessionDisplayItem"`, currently NO `ToolTipService.ToolTip` attached)
- `MainViewModel.cs:37-42` — `SessionDisplayItem` class shape (`Session`, `DisplayName`, `IsActive`)
- `MainViewModel.cs:154` — `_isRefreshing` is `[ObservableProperty]`
- `MainViewModel.cs:404-441` — Existing `PollUsageAsync` body (HasApiError handling, exception catch, IsRefreshing try/finally)
- `MainViewModel.cs:629-685` — Existing `RefreshSessionsAsync` body (filter at line 637, hardcoded `IsActive = true` at line 643)
- `MainViewModel.cs:857-861` — Existing `Refresh()` is a single-line passthrough to `PollUsageAsync`
- `Models/SessionInfo.cs` — POCO with `IsActive(TimeSpan threshold)` method, no other behavior
- `Models/AppSettings.cs:28` — `SessionActivityThresholdMinutes` field (default 30)
- `SettingsViewModel.cs:40` — `_selectedTabIndex = 0` `[ObservableProperty]`; line 45 — `IsAboutTabVisible => _selectedTabIndex == 3`
- `SettingsViewModel.cs:47-53` — Existing `OnSelectedTabIndexChanged` partial method
- `SettingsViewModel.cs:55-57, 89-91` — Existing computed-property pattern (no ObservableProperty)
- `SettingsViewModel.cs:105` — `ThresholdMinuteOptions = [15, 30, 60, 120]`
- `SettingsViewModel.cs:139-144` — Existing `OnSelectedThresholdIndexChanged` (currently no message Send)
- `SettingsView.xaml.cs:22-35` — Existing `OnLoaded` + `ApplyTabTooltips` precedent (view-lifecycle routing)
- `SettingsView.xaml:34-79` — Segmented control (no `x:Name`)
- `Services/Interfaces/IPricingService.cs:22` — `DateTimeOffset? LastFetch { get; }`
- `Messages/RefreshIntervalChangedMessage.cs:9-11` — `ValueChangedMessage<int>` template

### Primary (HIGH confidence — CONTEXT.md / UI-SPEC.md)

- `22-CONTEXT.md` D-01..D-11 — All eleven decisions
- `22-UI-SPEC.md` — Visual contracts, especially Polish 1/2/3 tables

### Secondary (MEDIUM confidence — published library docs)

- CommunityToolkit.Mvvm 8.4 — `[NotifyCanExecuteChangedFor]` attribute behavior [CITED: learn.microsoft.com/dotnet/communitytoolkit/mvvm/generators/relaycommand]
- WinUI 3 / Microsoft.UI.Xaml.DispatcherTimer — UI-thread Tick semantics [CITED: learn.microsoft.com/windows/winui]

### Tertiary (LOW confidence — none)

- None. Phase 22 is a verification phase, not a discovery phase.

---

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH — every library is already in the project; versions verified via CLAUDE.md and existing source.
- Architecture: HIGH — every pattern has a precedent in the codebase (verified via grep).
- Pitfalls: HIGH — pitfalls 1, 2, 4, 5, 6 are derived from direct code inspection; pitfall 3 is derived from absence-of-helper observation; pitfall 7 from D-09 skeleton review.

**Research date:** 2026-05-06
**Valid until:** 2026-06-05 (30 days — stack is stable; locked decisions in CONTEXT.md eliminate drift risk)
