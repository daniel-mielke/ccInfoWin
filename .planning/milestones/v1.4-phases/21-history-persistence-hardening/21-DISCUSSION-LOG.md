# Phase 21: History Persistence Hardening - Discussion Log

> **Audit trail only.** Do not use as input to planning, research, or execution agents.
> Decisions are captured in CONTEXT.md — this log preserves the alternatives considered.

**Date:** 2026-05-06
**Phase:** 21-history-persistence-hardening
**Areas discussed:** Termination-Save Architecture, Async Save Semantics, 5-Hour Window Reset Detection, Logout vs. Termination-Save Coordination

---

## Gray Area Selection

**User selected (multi-select):** All four areas. The user instructed Claude to use the recommended option for each area and conclude the discussion: *"Deine Empfehlungen bei solchen Fragen waren bisher immer korrekt. Nutze deine Empfehlungen für diese Fragen und schließe die Diskussion für Phase 21 damit ab."*

This is effectively `--auto`-style resolution within an interactive session. All four recommendations were locked as Phase 21 decisions without further sub-questioning.

| Option | Description | Selected |
|--------|-------------|----------|
| Termination-Save Architecture | WHERE does sync SaveHistory() sit? Spec says App.xaml.cs OnLaunched + MainWindow.Closed; actual code has AppWindow.Closing in MainWindow.xaml.cs:42. How does the hook reach the current UsageHistory instance, given MainViewModel is Transient? | ✓ |
| Async Save Semantics | How do SaveHistory (sync) and SaveHistoryAsync (async) coexist? Race condition when both write simultaneously (poll mid-flight + window close)? | ✓ |
| 5-Hour Window Reset Detection | FEAT-14 is partially implemented (IsWindowReset with 2-min tolerance in MainViewModel.cs:557). HIST-04 says "newResetsAt > previousResetsAt". Which logic wins: existing tolerance-based detection or strict ">" from spec? | ✓ |
| Logout vs. Termination-Save Coordination | Logout currently calls ClearHistory() (deletes file). If user clicks Logout then closes window — should termination hook still write, or should Logout disable the hook? | ✓ |

**User's choice:** Accept all four recommended approaches as-locked.
**Notes:** The user explicitly delegated decision-making, signaling trust in Claude's analysis after the codebase scout surfaced three concrete spec drifts. No follow-up questions needed.

---

## Termination-Save Architecture

| Option (recommendation) | Description | Selected |
|--------|-------------|----------|
| **Live-snapshot cache in singleton service** | Add `_lastSavedSnapshot` field + `PeekLastSnapshot()` method to UsageHistoryService. Termination handler in MainWindow.xaml.cs reads the snapshot and writes synchronously. No DI changes. | ✓ |
| Refactor MainViewModel to Singleton | Spec's alternative path — would let App.xaml.cs reach a stable MainViewModel instance. Rejected: contradicts Phase 20's Transient lifetime invariant for `_autoReauthAttempted` cold-start reset. | |
| Move handler to App.xaml.cs `Window.Closed` | Spec text suggested this. Rejected: `AppWindow.Closing` already exists at MainWindow.xaml.cs:42, fires before teardown, and provides a synchronous extension point. `Closed` offers no async-completion guarantee. | |
| Snapshot via separate `IUsageHistoryStore` cache | Adds a new abstraction layer. Rejected as over-engineering — the singleton service already owns persistence. | |

**User's choice:** Live-snapshot cache (D-01 through D-05).
**Notes:** Spec drift documented in CONTEXT.md canonical_refs. The existing AppWindow.Closing handler is strictly better than the spec-suggested Window.Closed pattern.

---

## Async Save Semantics

| Option (recommendation) | Description | Selected |
|--------|-------------|----------|
| **Add `SaveHistoryAsync` to interface, share JsonOptions, SemaphoreSlim guard** | Single new interface method. Both sync and async methods serialize via `JsonOptions` (already a static singleton in the service). A `SemaphoreSlim(1,1)` serializes sync vs async writes. | ✓ |
| No concurrency guard | Spec did not call out concurrency. Rejected: the termination handler can fire while a poll-cycle async write is mid-flight, corrupting the JSON file. | |
| Atomic write via temp+move | Stronger isolation. Considered but heavier than needed — best-effort try/catch already swallows failures. | |
| Keep poll cycle synchronous | Defeats HIST-02. Rejected. | |

**User's choice:** SaveHistoryAsync + SemaphoreSlim (D-06 through D-09).
**Notes:** Byte-identical JSON guarantee (D-07) is provided automatically by sharing the `JsonOptions` static. A new test `SaveSync_VS_SaveAsync_ProducesByteIdenticalJson` locks this property.

---

## 5-Hour Window Reset Detection

| Option (recommendation) | Description | Selected |
|--------|-------------|----------|
| **Keep existing `IsWindowReset` 2-min tolerance** | Already implemented in MainViewModel.cs:557. Hardened against API clock-drift. Phase 21 adds verification tests, no code change. | ✓ |
| Replace with strict `>` from spec | Spec text suggests strict comparison. Rejected: would fire on every micro-drift in API's ResetsAt (±10s server clock variance), causing spurious history clears. | |
| Hybrid: strict `>` with 30s cooldown | Discussed as middle ground. Rejected — adds complexity without addressing the root cause (API clock drift). | |

**User's choice:** Keep existing implementation, add verification tests (D-10 through D-12).
**Notes:** The 2-minute tolerance is project-validated production hardening. Spec authors did not have this context.

---

## Logout vs. Termination-Save Coordination

| Option (recommendation) | Description | Selected |
|--------|-------------|----------|
| **ClearHistory invalidates `_lastSavedSnapshot` to null; termination handler null-checks** | Logout's existing `ClearHistory()` call sets the cache to null. Termination handler does `if (snapshot != null) SaveHistory(snapshot)`. No race, no resurrection. | ✓ |
| Add explicit "logout flag" to disable termination hook | Adds state machine complexity. Rejected — the null-check on the snapshot is already sufficient. | |
| Move ClearHistory call to a separate handler | Splits responsibility unnecessarily. Rejected. | |
| Remove ClearHistory from Logout entirely | Would leak history to the next user on the same Windows account. Rejected — the existing logout-clears-history behavior is correct. | |

**User's choice:** Null-check pattern via cache invalidation in ClearHistory (D-13, D-14).
**Notes:** This is the simplest possible coordination — no new state, no new flag, just respect the cache's existing null-when-cleared semantics.

---

## Claude's Discretion

The following sub-decisions were left to the planner agent during plan-phase:

- Interface method name: `PeekLastSnapshot` vs `GetLastSavedSnapshot` vs `TryGetSnapshot(out UsageHistory)` — pick to match existing project conventions.
- Whether `_lastSavedSnapshot` should be a deep clone or a shared reference — defaults to shared reference; planner verifies the mutation-order invariant during implementation.
- Semaphore release pattern: `try/finally` vs `using` extension method — pick to match existing project async patterns.
- Whether to introduce a separate `IUsageHistorySnapshotProvider` interface for stricter SRP — default: fold `PeekLastSnapshot` into `IUsageHistoryService`.
- Test mocking strategy: real `UsageHistoryService` with temp directory follows the existing `UsageHistoryServiceTests` pattern; no mocking framework needed.

## Deferred Ideas

The following ideas surfaced during discussion or analysis and were deferred to future phases or backlog:

- Crash-recovery via async checkpointing — out of scope per v1.4-REQUIREMENTS.md "Crash reporting" exclusion
- SQLite migration — out of scope per PROJECT.md
- MainViewModel → Singleton refactor — rejected (D-03/D-04 chose live-snapshot cache instead)
- Strict `>` ResetsAt comparison — rejected (D-10/D-11 chose tolerance-based logic)
- Per-snapshot deep cloning — left to planner discretion; default is shared reference
- SaveHistoryAsync cancellation token — not needed for v1.4
- Long-tail point compaction — already handled by 5-hour cutoff in AppendHistoryPoint
- Separate IUsageHistorySnapshotProvider interface — left to planner discretion; default folds into IUsageHistoryService
