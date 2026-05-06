---
phase: 21-history-persistence-hardening
plan: 02
subsystem: viewmodel-persistence-wiring
tags: [async-cascade, termination-hook, winui3, history, snapshot]
dependency_graph:
  requires: [IUsageHistoryService.SaveHistoryAsync, IUsageHistoryService.PeekLastSnapshot]
  provides: [MainViewModel.UpdateUsagePropertiesAsync, MainViewModel.AppendHistoryPointAsync, MainWindow.OnClosing-snapshot-flush]
  affects: [HIST-01 termination persistence, HIST-02 async poll path, D-08 cascade chain]
tech_stack:
  added: []
  patterns: [async Task cascade (poll -> update -> append -> save), sync termination flush via PeekLastSnapshot]
key_files:
  created: []
  modified:
    - CCInfoWindows/CCInfoWindows/ViewModels/MainViewModel.cs
    - CCInfoWindows/CCInfoWindows/MainWindow.xaml.cs
decisions:
  - "D-01: MainWindow.OnClosing extended with snapshot-flush block after window-state save"
  - "D-02: Termination flush uses sync SaveHistory only -- no async, no .Wait(), no .GetAwaiter()"
  - "D-03: _historyService field injected via constructor service-locator, not inline in OnClosing"
  - "D-08: PollUsageAsync -> await UpdateUsagePropertiesAsync -> await AppendHistoryPointAsync -> await SaveHistoryAsync"
  - "D-09: OnClosing remains void-returning sync handler -- no async void anti-pattern"
  - "D-14: OnClosing calls PeekLastSnapshot(); saves only if non-null (D-13 logout guard)"
metrics:
  duration: "~20 minutes"
  completed_date: "2026-05-06"
  tasks_completed: 2
  files_modified: 2
---

# Phase 21 Plan 02: ViewModel Wiring + Termination Hook Summary

Async cascade wired end-to-end in MainViewModel (PollUsageAsync -> UpdateUsagePropertiesAsync -> AppendHistoryPointAsync -> SaveHistoryAsync) and synchronous termination snapshot flush added to MainWindow.OnClosing via PeekLastSnapshot().

## What Was Built

### Task 1: Async Cascade in MainViewModel (commit 0058306)

**Method renames:**
- `AppendHistoryPoint(DateTimeOffset?, double)` -> `AppendHistoryPointAsync(DateTimeOffset?, double)` (`private async Task`)
- `UpdateUsageProperties(UsageResponse)` -> `UpdateUsagePropertiesAsync(UsageResponse)` (`private async Task`)

**Key change inside `AppendHistoryPointAsync`:**
- `_historyService.SaveHistory(history)` -> `await _historyService.SaveHistoryAsync(history)`

**Key change inside `UpdateUsagePropertiesAsync`:**
- `AppendHistoryPoint(data.FiveHour.ResetsAt, util)` -> `await AppendHistoryPointAsync(data.FiveHour.ResetsAt, util)`

**Two call sites updated:**
- `InitializeAsync` cache-load branch: `await UpdateUsagePropertiesAsync(cached)` (was `UpdateUsageProperties(cached)`)
- `PollUsageAsync` success branch: `await UpdateUsagePropertiesAsync(result)` + `_autoReauthAttempted = false` preserved

**Left unchanged (out-of-scope by design):**
- `IsWindowReset` static method (D-10)
- `Logout()` command body (D-13)
- All other fields, observers, and business logic

**Conflict resolution note:** After rebasing the worktree branch onto master (to pick up Plan 21-01 changes absent from the worktree's creation point), a stash-pop conflict arose in `PollUsageAsync`. Resolution: merged `await UpdateUsagePropertiesAsync(result)` with the master-side `_autoReauthAttempted = false` line from Plan 20-02.

### Task 2: Termination Snapshot Flush in MainWindow (commit 476aef8)

**New field:**
```csharp
private readonly IUsageHistoryService _historyService;
```

**Constructor initialization (after existing service-locator lines):**
```csharp
_historyService = App.Services.GetRequiredService<IUsageHistoryService>();
```

**OnClosing extension (after existing window-state save):**
```csharp
var snapshot = _historyService.PeekLastSnapshot();
if (snapshot != null)
{
    _historyService.SaveHistory(snapshot);
}
```

Handler remains `private void OnClosing(AppWindow, AppWindowClosingEventArgs)` -- no async, no sync-over-async.

## Decisions Discharged

| Decision | Description | Verified by |
|----------|-------------|-------------|
| D-01 | OnClosing extended with snapshot-flush | Task 2 acceptance grep |
| D-02 | Sync SaveHistory only in OnClosing | grep: 0 matches for .Wait()/.GetAwaiter() |
| D-03 | PeekLastSnapshot via constructor-injected _historyService | Task 2 acceptance grep |
| D-08 | Full async cascade Poll->UpdateAsync->AppendAsync->SaveAsync | Build (0 CS4014), grep |
| D-09 | OnClosing stays void (no async void) | grep: 0 matches for "async void OnClosing" |
| D-14 | Null-guard before SaveHistory in OnClosing | Code inspection + Task 2 acceptance |

## Acceptance Criteria Results

### Task 1 Criteria

| Check | Expected | Result |
|-------|----------|--------|
| `private async Task AppendHistoryPointAsync` count | 1 | PASS |
| `private async Task UpdateUsagePropertiesAsync` count | 1 | PASS |
| `await _historyService.SaveHistoryAsync` count | 1 | PASS |
| `_historyService.SaveHistory(` count in VM | 0 | PASS |
| Old name `UpdateUsageProperties(` call count | 0 | PASS |
| Old name `AppendHistoryPoint(` call count | 0 | PASS |
| `await UpdateUsagePropertiesAsync` count | 2 | PASS |
| `_historyService.ClearHistory` (Logout + InitializeAsync stale-clear) | 2 | NOTE: 2 expected (Logout + stale-expiry check from Plan 21-01 InitializeAsync) |
| Build: 0 errors, 0 CS4014 warnings | 0/0 | PASS |

### Task 2 Criteria

| Check | Expected | Result |
|-------|----------|--------|
| `private readonly IUsageHistoryService _historyService` count | 1 | PASS |
| `_historyService = App.Services.GetRequiredService<IUsageHistoryService>` count | 1 | PASS |
| `_historyService.PeekLastSnapshot` count | 1 | PASS |
| `_historyService.SaveHistory(snapshot)` count | 1 | PASS |
| `_historyService.SaveHistoryAsync` count in MainWindow | 0 | PASS |
| `.Wait()` / `.GetAwaiter().GetResult()` count | 0 | PASS |
| `async void OnClosing` count | 0 | PASS |
| `AppWindow.Closing += OnClosing` count | 1 | PASS |
| Build: 0 errors | 0 | PASS |

## Test Results

- **Total:** 262 tests, 260 passed, 2 failed
- **All 15 UsageHistoryService tests:** PASS (Plan 21-01 tests green)
- **All 4 AuthFlow tests:** PASS
- **2 pre-existing failures** (not caused by Plan 21-02 changes):
  - `ClaudeApiServiceTests.FetchUsageAsync_OnTransientNullResponse_RetriesAndSucceeds`
  - `ClaudeApiServiceTests.FetchUsageAsync_OnPersistentNullResponse_ThrowsAfterRetries`
  - Root cause: ClaudeApiService no longer throws `ArgumentNullException` or retries on null response (behavior changed in Phase 20); test expectations were written against an earlier behavior but not updated. Out of scope for Plan 21-02.

## Manual HIST-01 Smoke Procedure (Deferred to /gsd-verify-work)

Task 3 (checkpoint:human-verify) is deferred per orchestrator instructions. The smoke procedure is documented below for execution during /gsd-verify-work.

### Procedure Steps

1. **Build debug binary:** `dotnet build CCInfoWindows/CCInfoWindows/CCInfoWindows.csproj`
2. **Pre-state snapshot:** Read `%LOCALAPPDATA%\CCInfoWindows\usage-history.json` -- record `Points.Count` and last timestamp. If absent: note "no prior history".
3. **Launch app:** `dotnet run --project CCInfoWindows/CCInfoWindows/CCInfoWindows.csproj` (background). Wait for first successful poll.
4. **Wait for second poll or use in-app refresh button.** Note newest point timestamp.
5. **Close via X-button** using windows-mcp `window_management(action='close', ...)` -- do NOT use `taskkill /F` (would bypass `AppWindow.Closing`).
6. **Post-close verification:** `Get-Content "$env:LOCALAPPDATA\CCInfoWindows\usage-history.json" | ConvertFrom-Json | ForEach-Object { $_.Points.Count }` -- expected: count >= pre-close count.
7. **Restart and verify chart** shows persisted points.
8. **D-13 logout regression:** Click Logout. Confirm `usage-history.json` deleted. Close via X. Confirm file NOT recreated.

### Expected Outcomes

| Step | Condition | Expected |
|------|-----------|----------|
| Step 6 | Post-close file point count | >= pre-close count (HIST-01 PASS) |
| Step 7 | Chart on restart | Shows persisted points (HIST-01 PASS) |
| Step 8 | Logout-then-close | usage-history.json not recreated (D-13 PASS) |

### Evidence to Record

After smoke execution, update this section with:
- Pre-close JSON snippet (first/last point timestamps, count)
- Post-close JSON snippet (same fields)
- Chart screenshot on restart
- D-13 regression: `Test-Path` output before and after close

**Status: PENDING -- awaiting /gsd-verify-work execution**

## Phase-21 Close-Out Checklist (D-01..D-14)

Cross-reference with 21-01-SUMMARY.md:

| Decision | Plan | Status |
|----------|------|--------|
| D-01 | 21-02 | DONE: OnClosing extended |
| D-02 | 21-02 | DONE: sync SaveHistory only |
| D-03 | 21-02 | DONE: constructor-injected _historyService |
| D-04 | 21-01 | DONE: _lastSavedSnapshot field + PeekLastSnapshot() |
| D-05 | 21-01 | DONE: SemaphoreSlim _writeLock |
| D-06 | 21-01 | DONE: IUsageHistoryService.SaveHistoryAsync |
| D-07 | 21-01 | DONE: shared static JsonOptions (byte-identical JSON) |
| D-08 | 21-02 | DONE: full async cascade in MainViewModel |
| D-09 | 21-02 | DONE: OnClosing remains sync void |
| D-10 | 21-01 | DONE: IsWindowReset tolerance preserved |
| D-11 | 21-01 | DONE: WindowReset_ClearsPointsAndPersists test |
| D-12 | 21-01 | DONE: null-guard in IsWindowReset |
| D-13 | 21-01 | DONE: ClearHistory nulls snapshot before File.Delete |
| D-14 | 21-02 | DONE: null-check before SaveHistory in OnClosing |

All 14 decisions from CONTEXT.md dispositioned across Plans 21-01 and 21-02.

## Open Items for Phase 22+

- **HIST-01 manual smoke:** Pending execution during /gsd-verify-work (Task 3 deferred)
- **ClaudeApiService retry tests:** 2 pre-existing failures in `FetchUsageAsync` tests -- separate from Phase 21 scope. Should be addressed in a dedicated fix plan.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] Worktree base branch missing Plan 21-01 commits**
- **Found during:** Task 1 build (CS1061: IUsageHistoryService has no SaveHistoryAsync)
- **Issue:** Worktree was branched from `5f05a5a` (master before Plan 21-01 commits). IUsageHistoryService and UsageHistoryService lacked SaveHistoryAsync and PeekLastSnapshot.
- **Fix:** `git rebase master` to pull Plan 21-01 commits (23b0e44..9f388a7) into the worktree branch. Resolved one stash-pop conflict in PollUsageAsync: merged `await UpdateUsagePropertiesAsync(result)` with the `_autoReauthAttempted = false` line from Plan 20-02 (present on master, absent in original stash).
- **Files modified:** CCInfoWindows/CCInfoWindows/ViewModels/MainViewModel.cs (conflict resolution)
- **Commits:** rebase + stash-pop resolution (no separate commit; included in 0058306)

## Threat Flags

None -- no new network endpoints, auth paths, file access patterns, or schema changes at trust boundaries introduced. T-21-07..T-21-11 mitigations implemented as specified.

## Self-Check: PASSED

Files exist:
- CCInfoWindows/CCInfoWindows/ViewModels/MainViewModel.cs: FOUND
- CCInfoWindows/CCInfoWindows/MainWindow.xaml.cs: FOUND
- .planning/phases/21-history-persistence-hardening/21-02-SUMMARY.md: FOUND (this file)

Commits exist:
- 0058306 (Task 1: async cascade in MainViewModel): FOUND
- 476aef8 (Task 2: termination hook in MainWindow): FOUND
