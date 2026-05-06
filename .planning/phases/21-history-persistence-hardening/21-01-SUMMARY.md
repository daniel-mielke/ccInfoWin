---
phase: 21-history-persistence-hardening
plan: 01
subsystem: persistence
tags: [async, semaphore, json, unit-test, snapshot-cache, concurrency]
dependency_graph:
  requires: []
  provides: [IUsageHistoryService.SaveHistoryAsync, IUsageHistoryService.PeekLastSnapshot, UsageHistoryService._writeLock, UsageHistoryService._lastSavedSnapshot]
  affects: [Plan 21-02 termination hook, MainViewModel poll-cycle async cascade]
tech_stack:
  added: [SemaphoreSlim concurrency guard, File.WriteAllTextAsync]
  patterns: [SemaphoreSlim await/finally pattern (verbatim from LiteLLMPricingService), shared static JsonSerializerOptions]
key_files:
  created: []
  modified:
    - CCInfoWindows/CCInfoWindows/Services/Interfaces/IUsageHistoryService.cs
    - CCInfoWindows/CCInfoWindows/Services/UsageHistoryService.cs
    - CCInfoWindows.Tests/Services/UsageHistoryServiceTests.cs
    - CCInfoWindows/CCInfoWindows/ViewModels/MainViewModel.cs
decisions:
  - "D-04: live-snapshot cache via _lastSavedSnapshot field; updated AFTER successful write only; PeekLastSnapshot() returns shared reference (safe because AppendHistoryPoint mutates AFTER save)"
  - "D-05: SemaphoreSlim _writeLock = new(1,1); sync uses Wait(), async uses WaitAsync(); release in finally -- no lock keyword (cannot hold across await)"
  - "D-06: IUsageHistoryService adds Task SaveHistoryAsync(UsageHistory history) as async poll-cycle path"
  - "D-07: sync + async share the same static JsonOptions field -- byte-identical JSON proven by SaveSync_VS_SaveAsync_ProducesByteIdenticalJson test"
  - "D-10/D-11: IsWindowReset 2-minute tolerance kept unchanged; HIST-04 verified by WindowReset_ClearsPointsAndPersists test"
  - "D-12: null-previous-ResetsAt guard verified by FirstPoll_AfterAppStart_DoesNotEraseHistory test (IsWindowReset promoted to internal static)"
  - "D-13: ClearHistory sets _lastSavedSnapshot = null BEFORE File.Delete -- logout cannot resurrect cleared data"
metrics:
  duration: "~15 minutes"
  completed_date: "2026-05-06"
  tasks_completed: 3
  files_modified: 4
---

# Phase 21 Plan 01: Service-Layer Persistence Hardening Summary

Service-layer hardening of UsageHistoryService: async write path via File.WriteAllTextAsync guarded by SemaphoreSlim, live-snapshot cache for termination hook, and 9 new xUnit tests locking D-04..D-07/D-10..D-13 invariants.

## What Was Built

### Task 1: Interface Extension (commit 1dc52cc)
Added two new members to `IUsageHistoryService`:
- `Task SaveHistoryAsync(UsageHistory history)` — async poll-cycle write path (D-06)
- `UsageHistory? PeekLastSnapshot()` — live-snapshot accessor for termination hook (D-04)

Naming choice: `PeekLastSnapshot` over `GetLastSavedSnapshot` — nullable-return convention matches `IClaudeApiService.GetCachedUsage()` codebase pattern. No explicit `using System.Threading.Tasks;` needed (ImplicitUsings=enable in csproj).

### Task 2: Service Implementation (commit 62d2bf8)
Extended `UsageHistoryService` with:
- `private readonly SemaphoreSlim _writeLock = new(1, 1)` — serializes all writes (D-05)
- `private UsageHistory? _lastSavedSnapshot` — live-snapshot field (D-04)
- `SaveHistory`: wrapped in `_writeLock.Wait()/finally Release`; `_lastSavedSnapshot = history` AFTER `File.WriteAllText` (RESEARCH Pitfall 2)
- `SaveHistoryAsync`: mirrors sync sibling with `await _writeLock.WaitAsync()` and `await File.WriteAllTextAsync`; shares same static `JsonOptions` (D-07 byte-identity)
- `ClearHistory`: `_lastSavedSnapshot = null` BEFORE `File.Delete` (D-13 ordering)
- `PeekLastSnapshot()`: single-line expression body, no lock needed

Anti-patterns deliberately avoided:
- No `lock` keyword (cannot hold across `await`)
- No per-call `new JsonSerializerOptions()` (anti-pattern from ClaudeApiService.SaveCacheAsync)
- Snapshot not updated before write completes (RESEARCH Pitfall 2)

### Task 3: 9 New Tests + IsWindowReset Promotion (commit f7fe6e0)
`MainViewModel.IsWindowReset` promoted from `private` to `internal static` (behavior unchanged; `InternalsVisibleTo CCInfoWindows.Tests` already in csproj).

9 new `[Fact]` tests added to `UsageHistoryServiceTests`:

| Test | Coverage |
|------|----------|
| `SaveHistoryAsync_RoundTrip_PreservesAllFields` | HIST-02/HIST-03 async round-trip |
| `SaveSync_VS_SaveAsync_ProducesByteIdenticalJson` | HIST-03/D-07 byte-identity |
| `PeekLastSnapshot_BeforeAnySave_ReturnsNull` | D-04 initial state |
| `PeekLastSnapshot_AfterSave_ReturnsLastSavedHistory` | D-04 post-write shared reference |
| `PeekLastSnapshot_AfterClear_ReturnsNull` | D-13 logout invalidation |
| `ConcurrentSyncAndAsyncWrites_DoNotInterleave` | D-05/T-21-01 SemaphoreSlim guard |
| `WriteFails_DoesNotUpdateSnapshot` | D-04/RESEARCH Pitfall 2 write-failure invariant |
| `WindowReset_ClearsPointsAndPersists` | HIST-04/D-11 reset semantics |
| `FirstPoll_AfterAppStart_DoesNotEraseHistory` | HIST-05/D-12 null-guard |

Test count: 6 existing → 15 total. All 15 pass, 0 failures.

## Decisions Discharged

D-04, D-05, D-06, D-07, D-10, D-11, D-12, D-13 — all implemented and verified by automated tests.

## Open Items Handed to Plan 21-02

| Decision | Work Remaining |
|----------|----------------|
| D-01 | Extend `MainWindow.xaml.cs` OnClosing handler with snapshot-save block |
| D-02 | Confirm AppWindow.Closing (not Window.Closed) is the termination hook |
| D-03 | Wire `PeekLastSnapshot()` in the termination handler |
| D-08 | Convert `AppendHistoryPoint` to async; cascade `UpdateUsageProperties` → `PollUsageAsync` |
| D-09 | Termination handler uses synchronous `SaveHistory` (not async) |
| D-14 | Null-check after `PeekLastSnapshot()` before calling `SaveHistory` in OnClosing |

Manual smoke test (HIST-01 end-to-end: history survives window-close) deferred to Plan 21-02 SUMMARY.

## Deviations from Plan

None — plan executed exactly as written. The `new JsonSerializerOptions` acceptance criterion uses target-typed `new()` syntax in the static field initializer — `grep -c "new JsonSerializerOptions"` returns 0 because the actual C# is `= new() { WriteIndented = true }`. The singleton invariant is maintained: exactly one JsonSerializerOptions instance exists in the file.

## Threat Flags

None — no new network endpoints, auth paths, file access patterns, or schema changes at trust boundaries introduced. Threats T-21-01 through T-21-03 are now mitigated by implementation + tests.

## Self-Check: PASSED

Files exist:
- CCInfoWindows/CCInfoWindows/Services/Interfaces/IUsageHistoryService.cs: FOUND
- CCInfoWindows/CCInfoWindows/Services/UsageHistoryService.cs: FOUND
- CCInfoWindows.Tests/Services/UsageHistoryServiceTests.cs: FOUND
- CCInfoWindows/CCInfoWindows/ViewModels/MainViewModel.cs: FOUND

Commits exist:
- 1dc52cc (Task 1: interface extension): FOUND
- 62d2bf8 (Task 2: service implementation): FOUND
- f7fe6e0 (Task 3: 9 new tests): FOUND
