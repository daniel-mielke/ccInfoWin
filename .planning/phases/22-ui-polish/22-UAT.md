---
status: partial
phase: 22-ui-polish
source: 22-01-SUMMARY.md, 22-02-SUMMARY.md, 22-03-SUMMARY.md
started: 2026-05-07T00:00:00+02:00
updated: 2026-05-07T12:35:00+02:00
---

## Current Test

[Phase 22 UAT complete — moving to Phase 23]

## Tests

### 1. Refresh button anti-flicker spinner
expected: |
  Click the refresh button on MainView footer. The arrow icon swaps to the
  spinning animation. The spinner stays visible for AT LEAST 250ms even if
  the data is cached (sub-100ms refresh). Button is disabled during refresh
  (clicking it again has no effect). Returns to refresh icon after
  (POLISH-01..03).
result: fixed
fixed_by: Plan 22-04 (gap closure)
fix_summary: |
  Three minimal anchor-based edits applied:
  1. MainViewModel.cs: added [NotifyPropertyChangedFor(nameof(CanRefresh))]
     to _isRefreshing field's existing attribute stack.
  2. MainViewModel.cs: changed `private bool CanRefresh =>` to
     `public bool CanRefresh =>` so x:Bind can resolve it.
  3. MainView.xaml: added IsEnabled="{x:Bind ViewModel.CanRefresh,
     Mode=OneWay}" to FooterRefreshButton — belt-and-suspenders override
     of the original [NotifyCanExecuteChangedFor] mechanism.

  D-04 reinforced (not replaced). Spinner animation + 250ms floor
  unchanged. 5/5 MainViewModelRefreshTests GREEN (4 existing + 1 new
  CanRefresh_RaisesPropertyChanged_WhenIsRefreshingFlips).

  Manual smoke verification still PENDING — re-run Test 1 after the next
  cold-start to visually confirm the button greys out during refresh.
original_severity: minor
notes: |
  Spinner animation and 250ms floor work correctly (user only reported the
  one issue). The CanExecute auto-disable does NOT visibly disable the
  button during refresh.

  Functional impact: minor. The Refresh() method has an explicit
  `if (IsRefreshing) return;` reentrancy guard at MainViewModel.cs:904, so
  rapid clicks during a refresh become no-ops. No crash, no double-fetch.
  But UX-wise the button looks clickable while spinning, which is the bug.

  Code review (no live debug):
  - MainView.xaml:609: `Command="{x:Bind ViewModel.RefreshCommand}"` — no
    hardcoded IsEnabled, no second binding override
  - MainViewModel.cs:161-163: `[NotifyCanExecuteChangedFor(nameof(
    RefreshCommand))] private bool _isRefreshing;`
  - MainViewModel.cs:901: `[RelayCommand(CanExecute = nameof(CanRefresh))]
    private async Task Refresh()`
  - MainViewModel.cs:916: `private bool CanRefresh => !IsRefreshing;`

  All static configuration appears correct. Suspected root cause:
  CommunityToolkit.Mvvm 8.4 source-generator behavior in WinUI 3 with
  classic `[ObservableProperty]` on a private field does not always wire
  CanExecuteChanged through to `x:Bind`-bound Buttons reliably. Build
  emits MVVMTK0045 warnings recommending C# 13 `partial property` syntax
  for full AOT/WinRT compatibility — the warnings may be load-bearing.

  Possible fixes (require investigation in a gap-closure phase):
    A) Migrate `_isRefreshing` to C# 13 `partial property` syntax per
       MVVMTK0045 recommendation. Risky if the codebase has other
       `[ObservableProperty]` fields that would need the same migration.
    B) Add an explicit `IsEnabled="{x:Bind ViewModel.CanRefresh, Mode=
       OneWay}"` to the XAML Button. Belt-and-suspenders — overrides
       whatever the Command's CanExecute does. Pragmatic, low-risk.
    C) Replace `x:Bind` with `Binding` for the Command to test if x:Bind
       is the culprit. Diagnostic only.

  Recommendation for fix: B (low-risk pragmatic override).

### 2. Inactive-session ComboBox shows two-line tooltip
expected: |
  Open the session ComboBox in MainView. If there's an inactive session
  (older than the current SessionTimeoutMinutes threshold), hover it.
  Tooltip shows TWO lines:
    line 1: <full session path>
    line 2: "Inactive for > <N>min" (where N = current threshold)
  Active sessions show only line 1 (path) — single-line tooltip
  (POLISH-04..05).
result: blocked
blocked_by: cold-start-session-scanning-bug
reason: |
  User reports: "es werden niemals inaktive sessions angezeigt. es werden
  immer nur aktive sessions angezeigt. dort wird ein tooltip mit
  pfadangabe angezeigt".

  Code review confirms Phase 22 D-06 is correctly implemented:
  - MainViewModel.cs:671-684 explicitly removes the .Where(IsActive)
    filter and sets IsActive per-item; comment at 671-672 documents the
    D-06 intent.
  - JsonlService.RebuildSessionsList (line 779) also has no IsActive
    filter.

  But the inactive-session tooltip cannot be observed because
  `_jsonlService._projectData` itself doesn't contain inactive sessions
  on cold start — separate issue tracked at
  `backlog_session_dropdown_recent_sessions.md` (memory). The
  ComboBox population pipeline is downstream of this scan-window gap.

  Phase 22's tooltip composition (ComputeTooltipText) is verified
  correctly by `SessionDisplayTooltipTests` (Plan 22-02 Task 3, 5/5 GREEN
  per build verification). Visual smoke is just not exercisable until
  the cold-start scan is fixed.

  Phase 22 single-line tooltip on active sessions IS observed working
  (user confirmed: "dort wird ein tooltip mit pfadangabe angezeigt" —
  POLISH-05 active-session path-tooltip is implicitly verified).

### 3. Threshold change updates inactive tooltip live
expected: |
  Open Settings → Sessions tab. Change "SessionTimeoutMinutes" to a different
  value. Return to MainView. Open the ComboBox again — hover the same
  inactive session. Tooltip's second line shows the NEW threshold value
  (POLISH-06 / SessionTimeoutChangedMessage).
result: blocked
blocked_by: cold-start-session-scanning-bug
reason: |
  Same root cause as Test 2 — no inactive sessions are visible in the
  ComboBox to observe the threshold-driven tooltip text update. The
  SessionTimeoutChangedMessage wiring itself IS verified by
  `SessionDisplayTooltipTests` (Plan 22-02 Task 3) — message round-trip
  passes the unit test. Visual smoke deferred until cold-start scan fix.

### 4. About-tab pricing timestamp ticks every minute
expected: |
  Open Settings → About tab (or Updates tab). Note the "Last fetched: X
  minutes ago" (or similar) text. Wait 60 seconds without leaving the tab.
  The text updates to the next minute increment (POLISH-07 /
  DispatcherTimer).
result: skipped
reason: |
  User reports: "noch nie aktualisiert wurde --> wert ist 'never'".
  Pricing has never been fetched — timestamp shows "Never" / "Nie"
  instead of "X minutes ago". DispatcherTimer firing cannot be observed
  because the displayed text doesn't change between "Never" and "Never".

  Cross-issue: `_pricingService.EnsurePricesLoadedAsync()` is started
  fire-and-forget at MainViewModel.cs:366-370 with a catch-all for
  failures. The fact that pricing is "Never" suggests either the load
  is failing silently or it has been disabled. This is orthogonal to
  Phase 22 — the DispatcherTimer is not observably broken, just
  unexercisable in the current state.

  POLISH-07 timer wiring is otherwise verified by
  `SettingsViewModelTimerTests` (Plan 22-03 Task 3, 6/6 GREEN).

### 5. Tab switch / unload stops the DispatcherTimer
expected: |
  From the About tab (timer running), switch to another Settings tab. Wait
  60 seconds. Switch back to About — the timestamp text reflects the wall
  clock (i.e., timer was correctly stopped and restarted, no double-firing).
  Alternatively: navigate away from Settings entirely. No background ticking
  / memory leak (POLISH-08).
result: pass
notes: User confirmed no lag, no freeze on tab switch or settings exit. Timer lifecycle clean. SettingsViewModelTimerTests (6/6 GREEN) provides unit-level coverage of the lifecycle code paths.

## Summary

total: 5
passed: 1
fixed: 1
pending: 0
skipped: 1
blocked: 2

## Gaps

- truth: "Refresh button is visually disabled while a refresh is in progress (POLISH-01..03 / D-04 NotifyCanExecuteChangedFor)"
  status: failed
  reason: "User reported: 'Button bleibt klickbar während Refresh'"
  severity: minor
  test: 1
  root_cause: "CommunityToolkit.Mvvm 8.4 [ObservableProperty] on classic private field _isRefreshing in WinUI 3 + x:Bind context does not reliably propagate CanExecuteChanged to the Button. Build emits MVVMTK0045 warnings recommending C# 13 partial property syntax for full WinRT compatibility."
  artifacts:
    - path: "CCInfoWindows/CCInfoWindows/Views/MainView.xaml"
      lines: "609"
      issue: "Button has no explicit IsEnabled binding to fall back on when Command.CanExecute notification fails"
    - path: "CCInfoWindows/CCInfoWindows/ViewModels/MainViewModel.cs"
      lines: "161-163"
      issue: "[ObservableProperty] on private field _isRefreshing — should be partial property per MVVMTK0045"
  missing:
    - "Pragmatic fix: add IsEnabled=\"{x:Bind ViewModel.CanRefresh, Mode=OneWay}\" to MainView.xaml refresh button as a belt-and-suspenders override"
    - "Cleaner fix: migrate _isRefreshing to C# 13 partial property syntax (and consistently the rest of [ObservableProperty] fields)"
  debug_session: ""
