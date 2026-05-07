# Phase 21: History Persistence Hardening - Context

**Gathered:** 2026-05-06
**Status:** Ready for planning

<domain>
## Phase Boundary

Robustness hardening of the usage-history persistence pipeline. Three bug-fix vectors against ccInfoWin v1.3:

1. **HIST-01**: History points appended after the last poll-cycle save are no longer lost when the user closes the main window via the X button — a synchronous save fires before process exit.
2. **HIST-02 / HIST-03**: The poll cycle moves from synchronous `File.WriteAllText` to async `File.WriteAllTextAsync` to eliminate UI-thread stutter on slow disks. The synchronous variant remains, used only by the termination hook. Both variants produce byte-identical JSON.
3. **HIST-04 / HIST-05**: When the API reports a new 5-hour-window `ResetsAt` greater than the previously observed one, `UsageHistory.Points` is cleared and persisted immediately — no vertical cliff from the retired window's high utilization down to 0% on the chart. The first poll after app start does not erase history (null-previous-`ResetsAt` guard).

**In scope:**
- `IUsageHistoryService` interface extension with `Task SaveHistoryAsync(UsageHistory)`
- `UsageHistoryService` implementation of `SaveHistoryAsync` via `File.WriteAllTextAsync`, plus a concurrency guard so sync and async writes do not interleave
- A live-snapshot cache inside `UsageHistoryService` (the singleton already injected everywhere) so the termination hook can write the current in-memory history without reaching into the transient `MainViewModel`
- `MainWindow.xaml.cs` — extend the existing `AppWindow.Closing += OnClosing` handler (already present at line 42) with a synchronous `SaveHistory` call that uses the live-snapshot cache
- `MainViewModel.PollUsageAsync` / `AppendHistoryPoint` — switch to `await SaveHistoryAsync(...)` for the poll path
- `MainViewModel.AppendHistoryPoint` — keep the existing `IsWindowReset` tolerance-based detection (it already covers HIST-04), but add an explicit null-previous-`ResetsAt` guard in line with HIST-05's wording
- `MainViewModel.Logout` — coordinate with the new live-snapshot cache so a logout that calls `ClearHistory()` does not race with a subsequent `Closing` save
- Unit tests for sync↔async equivalence (byte-identical JSON), concurrency safety, window-reset triggering, null-previous guard

**Out of scope:**
- Auth flow work (Phase 20 — already complete)
- Refresh spinner, inactive-session tooltip, About-tab live timestamp (Phase 22)
- The 6 new resw localization keys (Phase 23) — Phase 21 has no user-visible string changes
- Crash reporting / unhandled-exception persistence (PROJECT.md "Out of Scope")
- Migration to SQLite or any storage format change (PROJECT.md "Out of Scope")
- Refactoring `MainViewModel` lifetime from `Transient` to `Singleton` — D-04 below picks the live-snapshot cache approach instead

</domain>

<decisions>
## Implementation Decisions

### Termination-Save Architecture

- **D-01:** The synchronous termination save lives in `MainWindow.xaml.cs`'s existing `OnClosing(AppWindow, AppWindowClosingEventArgs)` handler at line 107, NOT in `App.xaml.cs` as the spec text suggests. The handler is already wired via `AppWindow.Closing += OnClosing` at line 42. Extend the existing handler with a `SaveHistory` call after the window-state save block. Rationale: the spec was authored against a generic Window template; the live codebase already uses `AppWindow.Closing` (the WinUI-3-correct event for AppWindow lifecycle), and bolting a second `Closed`-event handler in `App.xaml.cs` would split termination-save logic across two files for no benefit.

- **D-02:** Use `AppWindow.Closing` (the existing hook), NOT `Window.Closed`. `Closing` fires before the OS tears down the window and provides a synchronous extension point that finishes before process exit. `Closed` fires after the window is gone and offers no async-completion guarantee in WinUI 3 unpackaged apps — the awaited continuation can be aborted mid-write. Synchronous `File.WriteAllText` inside `Closing` is the safest write path at termination.

- **D-03:** The termination handler reaches the current in-memory `UsageHistory` snapshot via a **live-snapshot cache** inside the singleton `UsageHistoryService`, NOT via `App.Services.GetService<MainViewModel>()`. Rationale: `MainViewModel` is registered `Transient` in DI (`App.xaml.cs:164`) — calling `GetService<MainViewModel>()` from the termination hook would construct a brand-new instance with empty in-memory state and overwrite the persisted file with empty data. Refactoring `MainViewModel` to `Singleton` is rejected because (a) it changes semantics for every other consumer, (b) Phase 20 just locked in Transient as the foundation for `_autoReauthAttempted`'s cold-start invariant.

- **D-04:** Live-snapshot cache shape inside `UsageHistoryService`:
  ```csharp
  // New private field in UsageHistoryService
  private UsageHistory? _lastSavedSnapshot;

  // SaveHistory and SaveHistoryAsync both update _lastSavedSnapshot AFTER a successful write
  // ClearHistory sets _lastSavedSnapshot = null
  // New public method: UsageHistory? PeekLastSnapshot() — returns _lastSavedSnapshot
  ```
  The termination handler calls `PeekLastSnapshot()`; if non-null, it calls `SaveHistory(snapshot)` synchronously to flush it. Why "last saved" rather than "live in-memory MainViewModel": every `AppendHistoryPoint` already calls `SaveHistory` immediately after appending the point — so the gap between "live MainViewModel state" and "last-saved snapshot" is at most one `SaveHistoryAsync` race window during a poll cycle. The termination hook only needs to handle the case where the async save was in flight when the user clicked X. D-05 covers that race.

- **D-05:** Concurrency guard: `UsageHistoryService` adds a private `SemaphoreSlim _writeLock = new(1, 1)` field. Both `SaveHistory` and `SaveHistoryAsync` enter the semaphore before writing and release it in `finally`. `SaveHistory` uses `_writeLock.Wait()`; `SaveHistoryAsync` uses `await _writeLock.WaitAsync()`. This serializes any termination-time sync save against an in-flight poll-cycle async save. Best-effort failure semantics (existing `try/catch` swallowing exceptions to avoid app crash on disk full / read-only volume) are preserved.

### Async Save API Surface

- **D-06:** `IUsageHistoryService` adds exactly one new method:
  ```csharp
  Task SaveHistoryAsync(UsageHistory history);
  ```
  No other interface members change. The existing `LoadHistory` / `SaveHistory` / `ClearHistory` keep their signatures so the 6 existing tests in `UsageHistoryServiceTests` and all consumers compile unchanged.

- **D-07:** `SaveHistoryAsync` and `SaveHistory` MUST produce byte-identical JSON for the same input. Implementation guarantee: both methods share the same `JsonSerializerOptions JsonOptions` static (already defined in `UsageHistoryService.cs:17-20` with `WriteIndented = true`) and the same path resolution. Only the I/O call differs (`File.WriteAllText` vs `File.WriteAllTextAsync`). A new test `SaveSync_VS_SaveAsync_ProducesByteIdenticalJson` proves this with both methods writing to separate temp files and a `File.ReadAllBytes` comparison.

- **D-08:** `MainViewModel.PollUsageAsync` calls `await _historyService.SaveHistoryAsync(history)`. The chain is: `PollUsageAsync` → `UpdateUsageProperties` → `AppendHistoryPoint`. `AppendHistoryPoint` (currently sync at line 547) becomes `private async Task AppendHistoryPointAsync(...)` and the call site in `UpdateUsageProperties` is awaited. `UpdateUsageProperties` itself becomes `async Task` since it is invoked from the already-async `PollUsageAsync`. Cascade is minimal because the existing call chain is async-friendly.

- **D-09:** The termination handler's call site continues to use the **synchronous** `SaveHistory` — never `SaveHistoryAsync`. Rationale stated in D-02. Even though the public `SaveHistoryAsync` exists, mixing async into the `AppWindow.Closing` handler would defeat the entire HIST-01 acceptance criterion.

### 5-Hour Window Reset Detection (HIST-04, HIST-05)

- **D-10:** Keep the existing `IsWindowReset` tolerance-based logic (`MainViewModel.cs:557-563`, `WindowResetTolerance = TimeSpan.FromMinutes(2)`). Do NOT replace it with strict `newResetsAt > previousResetsAt` from the spec. Rationale: the strict `>` test fires on every micro-drift in the API's `ResetsAt` field (which can fluctuate by ±10 seconds between polls due to server clock variance), causing spurious history clears. The 2-minute tolerance is the project's deliberate hardening against that drift and was validated in v1.0–v1.3 production. The spec text was a generic recommendation — the existing implementation is strictly better.

- **D-11:** The HIST-04 wording "When ResetsAt is greater than the previously observed one" is satisfied by the existing `IsWindowReset` semantics PLUS the existing reset action: `history = new UsageHistory()` followed by `history.ResetsAt = apiResetsAt` and a `SaveHistory(history)` call. The chart then redraws with the empty Points list — no vertical cliff. Phase 21 adds NO new logic here; the existing implementation already meets HIST-04. The phase deliverable for HIST-04 is a verification test, not a code change.

- **D-12:** The HIST-05 null-previous-`ResetsAt` guard already exists implicitly: `IsWindowReset` returns `false` when `storedResetsAt` is null (line 559: `if (!storedResetsAt.HasValue || !apiResetsAt.HasValue) return false;`). On the first poll after app start, `history.ResetsAt` loaded from disk is either null (no prior history) or the persisted value (prior history). If null → no clear. If persisted value → tolerance check. Both cases are correct. Phase 21 adds an explicit unit test `FirstPoll_AfterAppStart_DoesNotEraseHistory` to lock this behavior and prevent regression.

### Logout vs. Termination-Save Coordination

- **D-13:** `MainViewModel.Logout` (line 869-877) keeps its current `_historyService.ClearHistory()` call. The live-snapshot cache (D-04) is invalidated as part of `ClearHistory`'s contract: `ClearHistory` sets `_lastSavedSnapshot = null` BEFORE deleting the file. After logout, if the user closes the window via X, the termination handler calls `PeekLastSnapshot()`, gets null, and skips the save — the file stays deleted. No race, no resurrection of cleared data.

- **D-14:** The `OnClosing` handler in `MainWindow.xaml.cs` performs a null check after `PeekLastSnapshot()`:
  ```csharp
  var snapshot = _historyService.PeekLastSnapshot();
  if (snapshot != null)
  {
      _historyService.SaveHistory(snapshot);
  }
  ```
  Either no history was ever saved (cold-start without any successful poll, or post-logout state), in which case there is nothing to persist; or a snapshot exists and gets flushed. Best-effort semantics: if `SaveHistory` itself throws, the existing internal try/catch swallows. The window close completes regardless.

### Claude's Discretion

- Whether to introduce a `IUsageHistorySnapshotProvider` interface alongside `IUsageHistoryService` for stricter SRP, or fold `PeekLastSnapshot` directly into `IUsageHistoryService`. Planner picks based on existing interface granularity in the project. Default: fold into `IUsageHistoryService` (matches existing project pattern of cohesive service interfaces).
- The exact name of the new interface method — `PeekLastSnapshot` vs `GetLastSavedSnapshot` vs `TryGetSnapshot(out UsageHistory)`. Planner picks the convention that matches existing service patterns.
- Whether to make `_lastSavedSnapshot` a deep clone or share the reference. Sharing the reference is safe because `UsageHistory.Points` is only mutated inside `MainViewModel.AppendHistoryPoint` AFTER the `SaveHistory` call returns — so post-save mutations affect the next poll's pre-load `LoadHistory` result, not the cached snapshot. Planner verifies this invariant during implementation; if mutation order is fragile, a defensive clone is fine.
- The semaphore release ordering on exception in `SaveHistory` (D-05): the `try { write } finally { release }` shape vs explicit `using var _ = await _writeLock.LockAsync()` extension. Planner picks based on existing project async patterns.
- Test mock strategy for the live-snapshot cache (D-04): pure unit test against the real `UsageHistoryService` with a temp directory follows the existing `UsageHistoryServiceTests` pattern at `CCInfoWindows.Tests/Services/UsageHistoryServiceTests.cs`. No mocking framework needed.

</decisions>

<canonical_refs>
## Canonical References

**Downstream agents MUST read these before planning or implementing.**

### Phase 21 source spec & requirements
- `spec/v1.11.1-macOS/spec-release-1.10.0-to-1.11.1.md` §FEAT-08 — Burn Chart Termination Save (FEAT-08a hook location, FEAT-08b history snapshot access). NOTE: spec assumes `App.xaml.cs` registration of `m_window.Closed`; actual code uses `AppWindow.Closing` in `MainWindow.xaml.cs:42`. D-01 / D-02 resolve this drift in favor of the existing handler.
- `spec/v1.11.1-macOS/spec-release-1.10.0-to-1.11.1.md` §FEAT-12 — Async History Save in Poll Cycle (FEAT-12a interface addition, FEAT-12b call-site swap)
- `spec/v1.11.1-macOS/spec-release-1.10.0-to-1.11.1.md` §FEAT-14 — Five-Hour Window Reset Clears Chart. NOTE: spec text uses strict `>` comparison; codebase uses 2-min-tolerance `IsWindowReset`. D-10 / D-11 lock the tolerance approach as superior.
- `spec/v1.11.1-macOS/spec-release-1.10.0-to-1.11.1.md` §"Resolved Design Decisions" #2 — Hybrid persistence locked: async in poll, sync at termination. `Window.Closed` provides no async-completion guarantee.
- `spec/v1.11.1-macOS/spec-release-1.10.0-to-1.11.1.md` §"Testing Strategy" — Unit-test catalog (sync/async JSON equivalence, reset detection, inactive-tooltip)
- `.planning/milestones/v1.4-REQUIREMENTS.md` §HIST-01..HIST-05 — Acceptance criteria
- `.planning/milestones/v1.4-ROADMAP.md` §Phase 21 — Goal, success criteria, depends-on, FEAT-IDs

### Codebase patterns (already established, must be respected)
- `CCInfoWindows/CCInfoWindows/Services/UsageHistoryService.cs` — Existing JSON I/O patterns (DefaultDirectory under LocalApplicationData, JsonOptions singleton, best-effort try/catch), constructor injection of directoryOverride for tests
- `CCInfoWindows.Tests/Services/UsageHistoryServiceTests.cs` — 6 existing xUnit tests with temp-directory pattern (`Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString())`), `IDisposable` cleanup. Phase 21 tests follow this exact shape.
- `CCInfoWindows/CCInfoWindows/MainWindow.xaml.cs:42-46, 107-116` — Existing `AppWindow.Closing` handler with `OnClosing(AppWindow, AppWindowClosingEventArgs)`. Phase 21 extends this handler.
- `CCInfoWindows/CCInfoWindows/ViewModels/MainViewModel.cs:520-563` — `AppendHistoryPoint` and `IsWindowReset` — Phase 21 converts `AppendHistoryPoint` to async and adds the explicit null-previous test, but does NOT touch `IsWindowReset` semantics.
- `CCInfoWindows/CCInfoWindows/App.xaml.cs:146` — DI registration `services.AddSingleton<IUsageHistoryService, UsageHistoryService>()`. The singleton lifetime is what makes the live-snapshot cache (D-04) work.

### Project-wide conventions (from CLAUDE.md)
- `CLAUDE.md` — MVVM conventions (`[ObservableProperty]`, `[RelayCommand]`), async patterns ("never fire-and-forget"), build commands (Release builds use `dotnet build -c Release`, NEVER `dotnet publish` with trimming), bash permission rules ("every command in its own tool call")
- `CLAUDE.md` §"Clean Code Rules" — No magic numbers (`MinimumSpinnerDisplayMs` precedent), small functions, DRY, F.I.R.S.T. tests
- `CLAUDE.md` §"Secure Coding Rules" — No sensitive data in errors/logs (the history JSON contains no secrets, but the service still must not log file paths verbatim if a future error path adds logging)

</canonical_refs>

<code_context>
## Existing Code Insights

### Reusable Assets
- **`UsageHistoryService` singleton** (`Services/UsageHistoryService.cs`) — already DI-registered, already exposes constructor-overridable directory for tests, already has best-effort try/catch shape. Phase 21 extends it in place.
- **`AppWindow.Closing` handler** (`MainWindow.xaml.cs:107-116`) — already wired and saves window state. Phase 21 adds two lines of history-save logic to the existing method.
- **`AppendHistoryPoint`** (`MainViewModel.cs:520-553`) — already encapsulates the load → mutate → save flow. Phase 21 changes the save call (sync → async) without restructuring the method.
- **`IsWindowReset` static helper** (`MainViewModel.cs:557-563`) — already implements tolerance-based detection. Phase 21 leaves it untouched and uses it as proof-of-correctness for HIST-04.
- **`UsageHistoryServiceTests` xUnit class** (`CCInfoWindows.Tests/Services/UsageHistoryServiceTests.cs`) — 6 existing tests with `IDisposable` temp-directory cleanup. Phase 21 adds new tests in the same file using the same pattern.
- **`JsonOptions` static field** (`UsageHistoryService.cs:17-20`) — already shared by Load and Save. Phase 21's `SaveHistoryAsync` uses the same instance, guaranteeing D-07's byte-identical-JSON property.

### Established Patterns
- **DI: Services Singleton, ViewModels Transient** (App.xaml.cs ConfigureServices) — `IUsageHistoryService` singleton lifetime is the foundation of the live-snapshot cache (D-04). Phase 21 does NOT change DI registration.
- **Best-effort persistence with try/catch** — UsageHistoryService already swallows write exceptions. Phase 21's new `SaveHistoryAsync` follows the same shape (`try { await write } catch { /* swallow */ }`).
- **xUnit tests with temp-directory isolation** — `UsageHistoryServiceTests` constructor creates a Guid-named subdir of `Path.GetTempPath()`, `Dispose` deletes it. Phase 21 tests reuse this pattern.
- **WinUI 3 AppWindow events** — the project uses `AppWindow.Closing` (not `Window.Closed`) for window-lifetime work. Phase 21 stays on this pattern.
- **`async Task` return for void-returning lifecycle methods** — `MainViewModel.PollUsageAsync` is async, the cascade up to `UpdateUsageProperties` and `AppendHistoryPoint` follows naturally.

### Integration Points
- `Services/Interfaces/IUsageHistoryService.cs` — add `Task SaveHistoryAsync(UsageHistory history)` and `UsageHistory? PeekLastSnapshot()` (or chosen synonym, see Claude's Discretion)
- `Services/UsageHistoryService.cs` — implement `SaveHistoryAsync`, add `_lastSavedSnapshot` field, add `_writeLock` semaphore, update `SaveHistory`/`SaveHistoryAsync`/`ClearHistory` to maintain the snapshot
- `MainWindow.xaml.cs:107` (OnClosing) — append the snapshot-save block after the existing window-state save
- `ViewModels/MainViewModel.cs:436` (`UpdateUsageProperties` signature: `void` → `async Task`) and `:447` (call site of `AppendHistoryPoint`)
- `ViewModels/MainViewModel.cs:520` (`AppendHistoryPoint` signature: sync → async) and `:547` (the `SaveHistory` call site)
- `ViewModels/MainViewModel.cs:398` (`PollUsageAsync`) — already async, only the awaited subtree changes
- `CCInfoWindows.Tests/Services/UsageHistoryServiceTests.cs` — add tests:
  - `SaveHistoryAsync_RoundTrip_PreservesAllFields`
  - `SaveSync_VS_SaveAsync_ProducesByteIdenticalJson`
  - `ConcurrentSyncAndAsyncWrites_DoNotInterleave` (or similar — content TBD by planner)
  - `PeekLastSnapshot_AfterSave_ReturnsLastSavedHistory`
  - `PeekLastSnapshot_AfterClear_ReturnsNull`
  - `PeekLastSnapshot_BeforeAnySave_ReturnsNull`
- New unit-test or behavioral test for the AppendHistoryPoint flow (location: planner picks — likely a new `MainViewModelHistoryTests.cs` file mirroring the existing test layout)

### Architectural Constraints
- **Singleton invariance for `IUsageHistoryService`** — D-04's live-snapshot cache REQUIRES the singleton lifetime. If a future phase changes this to Transient, Phase 21's HIST-01 guarantee breaks silently. A code review should call this out as a constraint to preserve.
- **Transient lifetime for `MainViewModel`** — Phase 20 locked this for cold-start `_autoReauthAttempted` reset. Phase 21 must NOT refactor to Singleton. The live-snapshot cache exists specifically to avoid that refactor.
- **No SQLite migration** — PROJECT.md explicitly excludes SQLite. JSON file persistence is the contract.
- **Network allowlist unchanged** — Phase 21 is local-disk only. No new network calls.
- **Bash discipline** (CLAUDE.md) — every command in its own tool call. Applies to all phase commits and to the planner agent.

</code_context>

<specifics>
## Specific Ideas

- The user authorized accepting all four recommendations as-is, signaling high confidence in the analysis surface (spec drift, transient lifetime, tolerance-based reset detection, logout-coordination). Phase 21 should NOT introduce additional gray-area discovery during planning — the four recommendation areas above are the complete decision set.
- Spec FEAT-08's wording was authored against a generic Window template; the live codebase's `AppWindow.Closing` hook is strictly better (synchronous extension point, fires before window teardown). Future phases that quote the spec should preserve this resolution.
- The existing `IsWindowReset` 2-minute tolerance is project-validated hardening against API clock-drift. Spec authors did not have this context — the tolerance stays.
- The `_lastSavedSnapshot` cache is intentionally minimal: just a reference + null after clear. No deep cloning, no observation pattern, no events. The simplest thing that makes HIST-01 work, kept inside the service that already owns persistence — no new abstraction layer.

</specifics>

<deferred>
## Deferred Ideas

- **Crash-recovery via async checkpointing** — surfaced by spec's mention of "unexpected termination" but already explicitly out of scope per v1.4-REQUIREMENTS.md "Crash reporting" entry. Belongs in a future crash-reporting initiative.
- **Migration to SQLite for history** — surfaced as alternative storage backend; rejected per PROJECT.md "Out of Scope" (data volumes too small to justify the dependency). JSON persistence is the locked contract.
- **`MainViewModel` lifetime refactor to Singleton** — discussed and rejected (D-03 / D-04 picked the live-snapshot cache instead). Documented here in case a future phase has a separate reason to make the lifetime change.
- **Strict `>` comparison for ResetsAt** — discussed and rejected (D-10 / D-11). The 2-minute tolerance is the validated approach; a future phase that wants strict `>` would need to also propose a guard against API micro-drift.
- **Per-snapshot deep cloning in the live-snapshot cache** — discussed in Claude's Discretion; planner can add if a mutation-order audit reveals fragility, otherwise the shared reference is fine.
- **`SaveHistoryAsync` cancellation token support** — not surfaced as a requirement; not needed for v1.4. Could be added later if a future phase introduces user-facing "abort save" semantics.
- **Compaction of long-tail history points** — orthogonal to v1.4; the existing 5-hour cutoff in `AppendHistoryPoint:537-539` already prunes points older than the window. No additional compaction needed.
- **`IUsageHistorySnapshotProvider` separate interface for SRP** — Claude's Discretion left the call to planner; default fold into `IUsageHistoryService`. Documented here as the alternative shape if a future phase needs to inject the snapshot provider without the full service surface.

</deferred>

---

*Phase: 21-history-persistence-hardening*
*Context gathered: 2026-05-06*
