# Phase 21: History Persistence Hardening - Research

**Researched:** 2026-05-06
**Domain:** WinUI 3 / .NET 9 file persistence + AppWindow lifecycle
**Confidence:** HIGH (codebase fully verified by direct read; .NET 9 APIs are stable)

## Summary

Phase 21 is a small, surgical hardening pass against three discrete bugs in the existing `UsageHistoryService` / `MainViewModel.AppendHistoryPoint` pipeline. The CONTEXT.md already commits 14 implementation decisions (D-01..D-14) and locks the architecture: synchronous `SaveHistory` survives the termination hook (`AppWindow.Closing`), a new `SaveHistoryAsync` removes UI-thread blocking from the poll cycle, and a singleton-resident `_lastSavedSnapshot` cache lets the termination hook reach in-memory state without resurrecting a transient `MainViewModel`.

All required source files (`IUsageHistoryService`, `UsageHistoryService`, `MainViewModel`, `MainWindow.xaml.cs`, `App.xaml.cs`, the Phase-21 test class, the model `UsageHistory`) have been read in full. The CONTEXT.md decisions are consistent with the live code: line numbers cited in CONTEXT.md (`MainWindow.xaml.cs:42, 107`; `UsageHistoryService.cs:17-20`; `MainViewModel.cs:530, 562`; `App.xaml.cs:146`) are accurate. The existing 6 xUnit tests in `UsageHistoryServiceTests.cs` use a `Path.GetTempPath() + Guid` isolation pattern with `IDisposable` cleanup — Phase-21 tests follow this verbatim.

**Primary recommendation:** Implement exactly the 14 decisions in CONTEXT.md, no more. The phase has no design freedom remaining — only tactical choices (snapshot getter naming, exception-handling shape) which CONTEXT.md explicitly delegates to the planner.

## Architectural Responsibility Map

| Capability | Primary Tier | Secondary Tier | Rationale |
|------------|-------------|----------------|-----------|
| Termination-time disk write | Service (`UsageHistoryService.SaveHistory`) | Window lifecycle (`MainWindow.OnClosing`) | Service owns I/O; window only triggers it |
| Poll-cycle async disk write | Service (`UsageHistoryService.SaveHistoryAsync`) | ViewModel (`MainViewModel.AppendHistoryPoint`) | Service owns I/O; VM triggers per-poll |
| Live-snapshot cache | Service (`UsageHistoryService._lastSavedSnapshot`) | — | Singleton lifetime is the substrate (D-04) |
| Concurrency serialization | Service (`SemaphoreSlim _writeLock`) | — | Both write methods enter same gate (D-05) |
| 5-hour-window reset detection | ViewModel (`IsWindowReset` static) | — | Already correct, untouched (D-10) |
| Null-previous-`ResetsAt` guard | ViewModel (`IsWindowReset:566`) | — | Already implicit; phase adds explicit test (D-12) |
| Logout↔termination race avoidance | Service (`ClearHistory` nulls snapshot) | ViewModel (`Logout` calls `ClearHistory`) | Service contract bounds the race (D-13) |
| Window state persistence | Window (`MainWindow.OnClosing`) | Settings service | Pre-existing, unchanged |

## User Constraints (from CONTEXT.md)

### Locked Decisions

**Termination-Save Architecture**
- **D-01:** Termination save lives in `MainWindow.xaml.cs` `OnClosing` handler (line 107), NOT `App.xaml.cs`. The handler is already wired via `AppWindow.Closing += OnClosing` at line 42.
- **D-02:** Use `AppWindow.Closing` (synchronous extension point, fires before window teardown), NOT `Window.Closed` (no async-completion guarantee in WinUI 3 unpackaged apps). Synchronous `File.WriteAllText` only.
- **D-03:** Termination handler reaches `UsageHistory` via a live-snapshot cache inside the singleton `UsageHistoryService`, NOT via `App.Services.GetService<MainViewModel>()`. `MainViewModel` is `Transient` (App.xaml.cs:164) — resolving it from DI would construct a fresh empty instance.
- **D-04:** Cache shape: `private UsageHistory? _lastSavedSnapshot` updated AFTER each successful write; `ClearHistory` nulls it; new public method `PeekLastSnapshot()` returns it.
- **D-05:** Concurrency guard via `SemaphoreSlim _writeLock = new(1, 1)`. Sync uses `Wait()`, async uses `await WaitAsync()`. Both release in `finally`.

**Async Save API Surface**
- **D-06:** `IUsageHistoryService` adds exactly `Task SaveHistoryAsync(UsageHistory history)`. Existing methods unchanged.
- **D-07:** `SaveHistoryAsync` and `SaveHistory` MUST produce byte-identical JSON. Both share the existing static `JsonOptions` (UsageHistoryService.cs:17-20). Only the I/O call differs.
- **D-08:** `MainViewModel.PollUsageAsync` → `UpdateUsageProperties` → `AppendHistoryPoint` cascade becomes async. `AppendHistoryPoint` becomes `private async Task AppendHistoryPointAsync(...)`; `UpdateUsageProperties` becomes `async Task`.
- **D-09:** Termination handler always uses synchronous `SaveHistory` — never `SaveHistoryAsync`. Mixing async into `Closing` defeats HIST-01.

**5-Hour Window Reset Detection**
- **D-10:** Keep existing `IsWindowReset` 2-minute tolerance (MainViewModel.cs:562-570). Do NOT replace with strict `>` from the spec — the tolerance hardens against API clock-drift (±10s).
- **D-11:** HIST-04 wording is satisfied by existing `IsWindowReset` + reset action (`history = new UsageHistory()` then `SaveHistory`). Phase 21 adds NO new logic for HIST-04 — only a verification test.
- **D-12:** HIST-05 null-previous guard already exists implicitly in `IsWindowReset:566` (`if (!storedResetsAt.HasValue || !apiResetsAt.HasValue) return false;`). Phase 21 adds an explicit lock-down test.

**Logout vs. Termination-Save Coordination**
- **D-13:** `Logout` keeps existing `_historyService.ClearHistory()` call. `ClearHistory` MUST set `_lastSavedSnapshot = null` BEFORE deleting the file. Termination handler then sees null and skips the save — no resurrection.
- **D-14:** `OnClosing` does null-check after `PeekLastSnapshot()`:
  ```csharp
  var snapshot = _historyService.PeekLastSnapshot();
  if (snapshot != null) _historyService.SaveHistory(snapshot);
  ```

### Claude's Discretion

- Whether to introduce `IUsageHistorySnapshotProvider` interface alongside `IUsageHistoryService` for SRP, or fold `PeekLastSnapshot` into `IUsageHistoryService`. **Default: fold in.**
- Exact name of new interface method: `PeekLastSnapshot` vs `GetLastSavedSnapshot` vs `TryGetSnapshot(out UsageHistory)`. Planner picks per existing conventions.
- Whether `_lastSavedSnapshot` is a deep clone or shared reference. Sharing is safe (mutation order is post-save). Defensive clone is fine if planner prefers.
- Semaphore release shape: `try/finally` vs `using var _ = await _writeLock.LockAsync()` extension. Planner picks per existing async patterns.
- Test mock strategy: pure unit test against real `UsageHistoryService` with temp dir, matching existing 6 tests. No mocking framework.

### Deferred Ideas (OUT OF SCOPE)

- Crash-recovery via async checkpointing (separate crash-reporting initiative)
- Migration to SQLite (PROJECT.md "Out of Scope")
- `MainViewModel` lifetime refactor to Singleton (D-03/D-04 chose snapshot cache instead)
- Strict `>` comparison for `ResetsAt` (D-10/D-11 locked tolerance approach)
- Per-snapshot deep cloning (Claude's Discretion if needed)
- `SaveHistoryAsync` cancellation token support (no requirement for v1.4)
- Compaction of long-tail history points (5-hour cutoff at AppendHistoryPoint:546 already prunes)
- `IUsageHistorySnapshotProvider` separate interface (Claude's Discretion default: fold in)

## Phase Requirements

| ID | Description | Research Support |
|----|-------------|------------------|
| HIST-01 | `MainWindow.Closed` triggers synchronous `SaveHistory()` before process exit | D-01/D-02 wire termination save into existing `AppWindow.Closing` handler at MainWindow.xaml.cs:107. D-03/D-04 supply the snapshot via `PeekLastSnapshot()`. D-09 forces sync write only. |
| HIST-02 | Poll cycle uses `SaveHistoryAsync()` via `File.WriteAllTextAsync` | D-06 adds the interface method. D-07 guarantees byte-identical JSON. D-08 wires the await chain through `UpdateUsageProperties` → `AppendHistoryPointAsync`. |
| HIST-03 | `IUsageHistoryService` exposes both sync (termination) and async (poll) variants | D-06: interface gets exactly one new member `Task SaveHistoryAsync(UsageHistory)`. Existing sync `SaveHistory` retained for D-09. |
| HIST-04 | New `ResetsAt` > previous → clear `Points` and persist immediately | D-10/D-11: existing `IsWindowReset` tolerance check + reset action already correct. Phase delivers verification test only. |
| HIST-05 | First poll after app start does not erase history (null-previous guard) | D-12: `IsWindowReset:566` already returns false on null `storedResetsAt`. Phase delivers explicit regression test. |

## Standard Stack

### Core
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| .NET 9 BCL `System.IO.File` | shipped | `WriteAllText` + `WriteAllTextAsync` | Simplest sync/async pair with identical encoding contract [VERIFIED: existing UsageHistoryService.cs:58 uses sync; async sibling is the same shape] |
| `System.Text.Json` | shipped with .NET 9 | `JsonSerializer.Serialize` with `JsonSerializerOptions` | Already in use (UsageHistoryService.cs:17-20). No change. [VERIFIED: project already references] |
| `System.Threading.SemaphoreSlim` | shipped | Concurrency guard between sync and async writes | Has both `Wait()` and `WaitAsync()` — only primitive that natively supports both contexts [CITED: learn.microsoft.com SemaphoreSlim docs] |
| xUnit + `IDisposable` | already referenced in CCInfoWindows.Tests | Test framework | Existing pattern — see `UsageHistoryServiceTests.cs` [VERIFIED: read in full] |

### Supporting
| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| `Microsoft.UI.Windowing.AppWindow` | Windows App SDK 1.8 | `AppWindow.Closing` event | Already wired (MainWindow.xaml.cs:42). Phase extends existing handler. [VERIFIED: read] |

### Alternatives Considered
| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| `SemaphoreSlim` | `lock` (Monitor) | `lock` cannot be held across `await` — would defeat async write. Rejected. [VERIFIED: language spec] |
| `File.WriteAllTextAsync` | `FileStream` + `WriteAsync` + manual UTF-8 encoder | More control over encoding/buffering, but breaks D-07 byte-identical promise unless meticulously aligned. Rejected — `File.WriteAllText[Async]` are byte-pair compatible per BCL contract [CITED: learn.microsoft.com File.WriteAllText / File.WriteAllTextAsync — both default to UTF-8 without BOM]. |
| Snapshot in `MainViewModel` (singleton refactor) | Promote `MainViewModel` to Singleton | Breaks Phase-20 cold-start invariant for `_autoReauthAttempted`. Rejected per D-03. |
| Snapshot via `App.Services.GetService<MainViewModel>()` from termination | resolve VM from DI at close time | Transient lifetime → constructs empty instance → overwrites file with empty data. Rejected per D-03. |

**Installation:** No new packages required. All needed APIs are in the .NET 9 BCL and already-referenced Windows App SDK.

**Version verification:** `System.IO.File.WriteAllTextAsync` shipped in .NET Core 2.0 (2017), present and stable in .NET 9 [CITED: learn.microsoft.com/dotnet/api/system.io.file.writealltextasync]. Both `WriteAllText` and `WriteAllTextAsync` use UTF-8 without BOM by default and write the exact bytes of the input string [CITED: same source].

## Architecture Patterns

### System Architecture Diagram

```
┌─────────────────────────────────────────────────────────┐
│                      MainViewModel                       │
│                       (Transient)                        │
│                                                          │
│  PollUsageAsync ──> UpdateUsageProperties (async Task)  │
│                          │                               │
│                          ▼                               │
│                   AppendHistoryPointAsync                │
│                          │                               │
│                          │ await SaveHistoryAsync(h)    │
│                          ▼                               │
└──────────────────────────┼───────────────────────────────┘
                           │
                           ▼
┌─────────────────────────────────────────────────────────┐
│              UsageHistoryService (Singleton)             │
│                                                          │
│   _writeLock: SemaphoreSlim(1,1)                         │
│   _lastSavedSnapshot: UsageHistory?                      │
│                                                          │
│   ┌──────────────────┐   ┌──────────────────────────┐   │
│   │ SaveHistory      │   │ SaveHistoryAsync         │   │
│   │ Wait() ─┐        │   │ await WaitAsync() ─┐     │   │
│   │         ▼        │   │                    ▼     │   │
│   │  File.            │   │  File.                  │   │
│   │  WriteAllText     │   │  WriteAllTextAsync      │   │
│   │         │        │   │                    │     │   │
│   │  _lastSavedSnap  │   │  _lastSavedSnapshot │    │   │
│   │     = history    │   │     = history       │    │   │
│   │  Release()       │   │  Release()          │    │   │
│   └──────────────────┘   └──────────────────────────┘   │
│                                                          │
│   ClearHistory:  _lastSavedSnapshot = null; File.Delete │
│   PeekLastSnapshot: return _lastSavedSnapshot           │
└────────────────────┬────────────────────────────────────┘
                     ▲
                     │ snapshot = PeekLastSnapshot()
                     │ if (snapshot != null) SaveHistory(snapshot)
                     │
┌────────────────────┴────────────────────────────────────┐
│       MainWindow.OnClosing (AppWindowClosingEventArgs)  │
│       Existing line 107 — append snapshot block         │
└─────────────────────────────────────────────────────────┘
```

### Recommended Project Structure

No structure changes. Edits are in place:

```
CCInfoWindows/CCInfoWindows/
├── Services/
│   ├── Interfaces/IUsageHistoryService.cs   (+ 2 lines: new method, snapshot getter)
│   └── UsageHistoryService.cs               (+ ~40 lines: async, semaphore, snapshot)
├── ViewModels/MainViewModel.cs              (~15 lines edited: async cascade)
└── MainWindow.xaml.cs                       (+ ~5 lines in OnClosing)

CCInfoWindows.Tests/
└── Services/UsageHistoryServiceTests.cs     (+ ~5 new test methods)
```

### Pattern 1: Sync/Async Sibling with Shared Helper

**What:** Both `SaveHistory` and `SaveHistoryAsync` delegate string production to a single `Serialize(UsageHistory) -> string` helper, then differ only in the I/O call. Guarantees D-07 byte-identical output.

**When to use:** Any time a service offers both sync and async I/O paths.

**Example:**
```csharp
// Source: derived from existing UsageHistoryService.cs:52-64 + .NET BCL contract
private static string Serialize(UsageHistory history) =>
    JsonSerializer.Serialize(history, JsonOptions);

public void SaveHistory(UsageHistory history)
{
    _writeLock.Wait();
    try
    {
        Directory.CreateDirectory(_historyDirectory);
        File.WriteAllText(HistoryFilePath, Serialize(history));
        _lastSavedSnapshot = history;
    }
    catch
    {
        // Best-effort save — preserves existing semantics
    }
    finally
    {
        _writeLock.Release();
    }
}

public async Task SaveHistoryAsync(UsageHistory history)
{
    await _writeLock.WaitAsync();
    try
    {
        Directory.CreateDirectory(_historyDirectory);
        await File.WriteAllTextAsync(HistoryFilePath, Serialize(history));
        _lastSavedSnapshot = history;
    }
    catch
    {
        // Best-effort save
    }
    finally
    {
        _writeLock.Release();
    }
}
```

### Pattern 2: Synchronous Termination Hook in WinUI 3

**What:** `AppWindow.Closing` is the only WinUI-3 lifecycle event that fires synchronously *before* window teardown and provides a deterministic execution window for cleanup. `Window.Closed` returns `void` and offers no completion guarantee for awaited work.

**When to use:** Any state that MUST be flushed to disk before process exit.

**Example:**
```csharp
// Source: existing MainWindow.xaml.cs:107-116, with Phase-21 extension
private void OnClosing(AppWindow sender, AppWindowClosingEventArgs args)
{
    // existing window-state save (unchanged)
    var state = new WindowState(
        AppWindow.Position.X, AppWindow.Position.Y,
        AppWindow.Size.Width, AppWindow.Size.Height);
    _settingsService.SaveWindowState(state);

    // NEW: history-snapshot flush
    var historyService = App.Services.GetRequiredService<IUsageHistoryService>();
    var snapshot = historyService.PeekLastSnapshot();
    if (snapshot != null)
    {
        historyService.SaveHistory(snapshot);
    }
}
```

### Anti-Patterns to Avoid

- **`async void` in `OnClosing`:** WinUI 3 will not await it; the await continuation can be aborted by process exit. Always sync.
- **`SaveHistoryAsync(...).Wait()` or `.GetAwaiter().GetResult()` in `OnClosing`:** can deadlock on UI sync context, and even when it doesn't, defeats the purpose. Use sync `SaveHistory`.
- **`lock` keyword around the write:** cannot hold a `lock` across `await`. Use `SemaphoreSlim`.
- **Resolving `MainViewModel` from DI inside `OnClosing`:** transient → fresh instance → empty `UsageHistory` → overwrites file with empty data. Use the snapshot cache.
- **Two separate `JsonSerializerOptions` instances for sync vs async:** subtle drift (whitespace, encoding) breaks D-07. Use the single static.

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| UTF-8 file write | Manual `FileStream` + `Encoding.UTF8.GetBytes` | `File.WriteAllText[Async]` | BCL handles BOM, encoding, atomic flush; matches existing code |
| Sync/async mutex | Hand-rolled `volatile bool _writing` flag | `SemaphoreSlim(1,1)` | Race-free, supports both contexts, BCL-tested |
| JSON serialization | Manual `StringBuilder` JSON | `JsonSerializer.Serialize` with shared options | Already in use; mandatory for D-07 byte-identity |
| Snapshot deep-clone | Manual recursive copy | Either share reference (D-04 invariant) or `JsonSerializer.Deserialize(Serialize(x))` | Don't write a clone visitor for a 2-property model |
| Window-close hook | New `App.xaml.cs` `m_window.Closed` registration | Extend existing `AppWindow.Closing` handler | D-01 explicitly chose existing path |

**Key insight:** The phase has zero novel infrastructure. Every primitive (file write, JSON serializer, semaphore, AppWindow hook) is already in use somewhere in the codebase or BCL. Hand-rolling any of them creates drift.

## Runtime State Inventory

> Phase 21 modifies persistence behavior but does NOT rename or migrate any file/key/schema. Inventory included for completeness.

| Category | Items Found | Action Required |
|----------|-------------|-----------------|
| Stored data | `%LOCALAPPDATA%\CCInfoWindows\usage-history.json` — schema unchanged (`UsageHistory` model untouched). Phase 21 changes write *path* (sync vs async) but NOT bytes. | None — D-07 guarantees byte-identical JSON for both writers. |
| Live service config | None — no external services involved. | None. |
| OS-registered state | None — no Task Scheduler / pm2 / launchd entries. | None. |
| Secrets / env vars | None — `usage-history.json` contains no credentials. | None. |
| Build artifacts | `IUsageHistoryService` interface gets 1-2 new members → all consumers must recompile. Test project must rebuild. | Standard `dotnet build` — no extra steps. |

**Schema migration:** None. `UsageHistory` model has 2 properties (`ResetsAt`, `Points`); both are preserved across the sync↔async boundary by D-07. Existing on-disk files remain readable.

## Common Pitfalls

### Pitfall 1: SemaphoreSlim not released on exception

**What goes wrong:** Exception in `File.WriteAllTextAsync` returns control without releasing the semaphore — subsequent writes deadlock.

**Why it happens:** Forgetting `try/finally` around the await.

**How to avoid:** Wrap every `WaitAsync()` body in `try { ... } finally { _writeLock.Release(); }`. Existing best-effort `catch { }` shape MUST sit *inside* the `try` so the `finally` still fires.

**Warning signs:** Tests that hang after the first thrown exception. App freezes after a transient disk error.

### Pitfall 2: `_lastSavedSnapshot` updated *before* the write succeeds

**What goes wrong:** If write throws, snapshot reflects data that was never persisted. Termination hook then re-saves successfully, presenting a corrupted timeline (data exists in cache but never made it to disk during normal poll).

**Why it happens:** Setting `_lastSavedSnapshot = history` outside the `try` body before `WriteAllText` returns.

**How to avoid:** Always update `_lastSavedSnapshot` *after* the successful `File.WriteAllText[Async]` call, inside the same `try` block. Mirrors transactional commit ordering.

**Warning signs:** Test `WriteFails_DoesNotUpdateSnapshot` (recommended new test) catches this.

### Pitfall 3: `ClearHistory` nulls snapshot AFTER `File.Delete` instead of before

**What goes wrong:** If `File.Delete` succeeds but the post-delete instruction is preempted (or throws), termination handler later sees stale snapshot and re-creates the file the user just logged out of. D-13 explicitly orders snapshot-null *before* delete.

**Why it happens:** Default ordering instinct ("delete then clean up references") is backwards here.

**How to avoid:** Required order:
```csharp
public void ClearHistory()
{
    _writeLock.Wait();
    try
    {
        _lastSavedSnapshot = null;   // 1. Invalidate cache FIRST
        File.Delete(HistoryFilePath); // 2. Then delete on disk
    }
    catch { }
    finally { _writeLock.Release(); }
}
```

**Warning signs:** Test `Logout_ThenClose_DoesNotRecreateHistoryFile` catches this.

### Pitfall 4: Async cascade misses one caller

**What goes wrong:** `UpdateUsageProperties` becomes `async Task`, but a synchronous caller (e.g., `_ = UpdateUsageProperties(cached)` inside `InitializeAsync` at line 370) becomes fire-and-forget without intention.

**Why it happens:** Two call sites of `UpdateUsageProperties` exist:
- `MainViewModel.cs:370` — `IsUpdatingFromCache = true; UpdateUsageProperties(cached);` (cache load on init)
- `MainViewModel.cs:416` — inside `PollUsageAsync` (per-poll)

Both must `await`. The init path is already inside an `async Task InitializeAsync`, so just adding `await` works.

**How to avoid:** Grep for all call sites of `UpdateUsageProperties` and `AppendHistoryPoint` after the signature change and verify each is awaited.

**Warning signs:** Compiler warning CS4014 ("call is not awaited"). Treat as error during the phase.

### Pitfall 5: `IsWindowReset` tolerance regression in tests

**What goes wrong:** A naive HIST-04 test that uses `previousResetsAt = T`, `apiResetsAt = T + 30 seconds` will FAIL (expected reset, none triggered) because of the 2-min tolerance. Spec wording suggests strict `>`; reality is tolerance-based.

**Why it happens:** Test authored from spec text without reading `IsWindowReset:562-570`.

**How to avoid:** New HIST-04 test uses `apiResetsAt = previousResetsAt + WindowResetTolerance + 1 second` (i.e., > 2 min difference) to actually trigger reset. CONTEXT.md D-10/D-11 documents this.

**Warning signs:** Newly added reset test fails when implementation is correct.

### Pitfall 6: Termination handler grabs `IUsageHistoryService` from wrong scope

**What goes wrong:** `OnClosing` is on `MainWindow`, which already injects `ISettingsService` and `INavigationService` via constructor. Adding `IUsageHistoryService` to the constructor is one option; another is `App.Services.GetRequiredService<IUsageHistoryService>()` inline. Inconsistent choice creates a maintenance trap.

**Why it happens:** Two precedents in the codebase:
- Constructor injection: `MainWindow.cs:35-36` does `App.Services.GetRequiredService<ISettingsService>()` (so even existing code uses the static service-locator pattern, not constructor DI).

**How to avoid:** Match the existing pattern in `MainWindow.xaml.cs:35-36`:
```csharp
private readonly IUsageHistoryService _historyService;
// in constructor:
_historyService = App.Services.GetRequiredService<IUsageHistoryService>();
```

**Warning signs:** Two different DI patterns in the same constructor block.

## Code Examples

### Example 1: Full `IUsageHistoryService` after Phase 21

```csharp
// Source: derived from existing CCInfoWindows/Services/Interfaces/IUsageHistoryService.cs
namespace CCInfoWindows.Services.Interfaces;

public interface IUsageHistoryService
{
    UsageHistory LoadHistory();
    void SaveHistory(UsageHistory history);
    Task SaveHistoryAsync(UsageHistory history);    // NEW (D-06)
    void ClearHistory();
    UsageHistory? PeekLastSnapshot();                // NEW (D-04)
}
```

### Example 2: `AppendHistoryPoint` after async cascade

```csharp
// Source: derived from MainViewModel.cs:530-560 with D-08 changes
private async Task AppendHistoryPointAsync(DateTimeOffset? apiResetsAt, double utilization)
{
    var history = _historyService.LoadHistory();

    if (IsWindowReset(history.ResetsAt, apiResetsAt))
    {
        history = new UsageHistory();
    }

    history.ResetsAt = apiResetsAt;

    var now = DateTimeOffset.UtcNow;
    var cutoff = now - TimeSpan.FromHours(5);
    history.Points.RemoveAll(p => p.Timestamp < cutoff);

    history.Points.Add(new UsageHistoryPoint
    {
        Timestamp = now,
        Utilization = utilization
    });

    await _historyService.SaveHistoryAsync(history);   // CHANGED (D-08)

    _fiveHourResetsAt = apiResetsAt;
    UsageHistoryPoints = history.Points.AsReadOnly();
    InvalidateChart();
}
```

### Example 3: Test for byte-identical sync/async output (D-07)

```csharp
// Source: new test, mirrors UsageHistoryServiceTests pattern at line 41-65
[Fact]
public async Task SaveSync_VS_SaveAsync_ProducesByteIdenticalJson()
{
    var dirSync = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
    var dirAsync = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
    try
    {
        var sutSync = new UsageHistoryService(dirSync);
        var sutAsync = new UsageHistoryService(dirAsync);

        var history = new UsageHistory
        {
            ResetsAt = DateTimeOffset.Parse("2026-05-06T18:00:00Z"),
            Points =
            [
                new() { Timestamp = DateTimeOffset.Parse("2026-05-06T13:00:00Z"), Utilization = 0.42 },
                new() { Timestamp = DateTimeOffset.Parse("2026-05-06T13:05:00Z"), Utilization = 0.43 }
            ]
        };

        sutSync.SaveHistory(history);
        await sutAsync.SaveHistoryAsync(history);

        var bytesSync  = File.ReadAllBytes(Path.Combine(dirSync, "usage-history.json"));
        var bytesAsync = File.ReadAllBytes(Path.Combine(dirAsync, "usage-history.json"));

        Assert.Equal(bytesSync, bytesAsync);
    }
    finally
    {
        if (Directory.Exists(dirSync))  Directory.Delete(dirSync, recursive: true);
        if (Directory.Exists(dirAsync)) Directory.Delete(dirAsync, recursive: true);
    }
}
```

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| Synchronous `File.WriteAllText` in poll cycle | `await File.WriteAllTextAsync` in poll cycle, sync only at termination | Phase 21 | UI thread no longer blocks during poll-cycle disk write |
| No termination flush | `AppWindow.Closing` triggers sync `SaveHistory(snapshot)` | Phase 21 | History points appended after last poll survive hard close |
| Termination resolved `MainViewModel` from DI | Snapshot cache inside singleton `UsageHistoryService` | Phase 21 | Avoids transient-VM resurrection bug |

**Deprecated/outdated:**
- Spec text suggesting `App.xaml.cs` `m_window.Closed += ...` registration: superseded by D-01/D-02 in favor of existing `AppWindow.Closing`.
- Spec text suggesting strict `newResetsAt > previousResetsAt` comparison: superseded by D-10 in favor of existing 2-minute tolerance.

## Project Constraints (from CLAUDE.md)

- **MVVM:** No code-behind logic in Views — all logic in ViewModels. `MainWindow.xaml.cs` `OnClosing` is window-lifecycle plumbing, not view logic, so the snapshot-flush call there is acceptable per existing convention (the file already contains `_settingsService.SaveWindowState`).
- **Async patterns:** "Always async/await — never fire-and-forget." Phase-21 cascade (D-08) makes `UpdateUsageProperties` async — every call site must `await`. The existing `_pollTimer.Tick += async (s, e) => await PollUsageAsync()` lambda at MainViewModel.cs:376 is already async-friendly.
- **Naming:** PascalCase public, _camelCase private, I-prefix interfaces. New `_lastSavedSnapshot` and `_writeLock` follow the existing field convention.
- **Conventional Commits:** Plan commits use `feat:`, `fix:`, `chore:`, `test:` prefixes.
- **No magic numbers:** `WindowResetTolerance = TimeSpan.FromMinutes(2)` already extracted (MainViewModel.cs:562). No new constants needed.
- **Wrap external libraries:** `UsageHistoryService` already wraps `System.IO.File` and `System.Text.Json` — Phase 21 stays inside this wrapper.
- **Bash discipline:** Every command its own tool call. Applies to plan commits.
- **Release build:** `dotnet build -c Release -o ...` (NEVER `dotnet publish` with trimming). Phase 21 does not change build config.
- **Clean Code F.I.R.S.T. tests:** All new tests use temp-directory isolation (Independent), complete in milliseconds (Fast), zero shared state (Repeatable), Assert-based validation (Self-Validating), authored alongside code (Timely).

## Validation Architecture

### Test Framework
| Property | Value |
|----------|-------|
| Framework | xUnit (already in `CCInfoWindows.Tests.csproj`) |
| Config file | none — convention-based discovery |
| Quick run command | `dotnet test CCInfoWindows.Tests/CCInfoWindows.Tests.csproj --filter FullyQualifiedName~UsageHistoryServiceTests` |
| Full suite command | `dotnet test CCInfoWindows.Tests/CCInfoWindows.Tests.csproj` |

### Phase Requirements → Test Map

| Req ID | Behavior | Test Type | Automated Command | File Exists? |
|--------|----------|-----------|-------------------|-------------|
| HIST-01 | `OnClosing` saves snapshot when non-null | unit (snapshot contract) | `dotnet test --filter FullyQualifiedName~PeekLastSnapshot` | New tests in `UsageHistoryServiceTests.cs` |
| HIST-01 | End-to-end: kill `CCInfoWindows.exe`, verify last poll point present in `usage-history.json` | manual smoke | windows-mcp launch + `taskkill //F //IM CCInfoWindows.exe` + JSON inspection | Manual — Closing-event behavior cannot be triggered from xUnit (no AppWindow in unit-test process) |
| HIST-02 | Poll cycle calls `SaveHistoryAsync` (not `SaveHistory`) | unit (with `IUsageHistoryService` mock or test-double recording call) | New `MainViewModelHistoryTests.cs` — verify await chain | NEW — needs creation |
| HIST-03 | Both interface members exist and are wired correctly | unit | `dotnet test --filter FullyQualifiedName~SaveHistoryAsync_RoundTrip` | New tests in `UsageHistoryServiceTests.cs` |
| HIST-03 | Sync and async produce byte-identical JSON | unit | `dotnet test --filter SaveSync_VS_SaveAsync_ProducesByteIdentical` | NEW |
| HIST-04 | Window reset (apiResetsAt > storedResetsAt + tolerance) clears Points and persists | unit (against `MainViewModel.IsWindowReset` static — already covered? verify) | New test `WindowReset_ClearsPointsAndPersists` | NEW (verification only — D-11) |
| HIST-05 | First poll with null `storedResetsAt` does NOT clear | unit | `dotnet test --filter FirstPoll_AfterAppStart_DoesNotEraseHistory` | NEW |
| Concurrency | Sync and async writes do not interleave | unit (race) | New `ConcurrentSyncAndAsyncWrites_DoNotInterleave` | NEW |
| Snapshot lifecycle | After `ClearHistory`, `PeekLastSnapshot` returns null | unit | `dotnet test --filter PeekLastSnapshot_AfterClear` | NEW |
| Snapshot lifecycle | Before any save, `PeekLastSnapshot` returns null | unit | `dotnet test --filter PeekLastSnapshot_BeforeAnySave` | NEW |
| Snapshot lifecycle | After successful save, `PeekLastSnapshot` returns last-saved | unit | `dotnet test --filter PeekLastSnapshot_AfterSave` | NEW |
| Failure semantics | Write throws → snapshot NOT updated | unit (using read-only directory or invalid path) | `dotnet test --filter WriteFails_DoesNotUpdateSnapshot` | NEW (recommended) |

### Sampling Rate

- **Per task commit:** `dotnet test CCInfoWindows.Tests/CCInfoWindows.Tests.csproj --filter FullyQualifiedName~UsageHistory` (~5 sec)
- **Per wave merge:** `dotnet test CCInfoWindows.Tests/CCInfoWindows.Tests.csproj` (full suite)
- **Phase gate:** Full suite green + manual HIST-01 smoke (kill app + inspect JSON file) before `/gsd-verify-work`

### Manual Smoke Procedure (HIST-01)

The `AppWindow.Closing` event cannot be triggered from a headless xUnit process (no Microsoft.UI.Windowing host). Manual procedure required:

1. `dotnet run --project CCInfoWindows/CCInfoWindows/CCInfoWindows.csproj`
2. Wait for first successful poll (chart shows data point).
3. Note last point timestamp in `%LOCALAPPDATA%\CCInfoWindows\usage-history.json`.
4. Wait for second poll (new point appended in-memory).
5. Click X to close window (DO NOT use Task Manager kill — that bypasses `Closing`).
6. Re-read `usage-history.json` — second point MUST be present.

Optional automation via `mcp__windows-mcp__*` tools (per memory note): `window_management(action='find', title='ccInfoWin')` → `keyboard_control(action='press', key='F4', modifiers='alt')` to trigger Closing.

### Wave 0 Gaps

- [ ] No new test infrastructure needed — existing `UsageHistoryServiceTests` pattern is reused
- [ ] Optionally create `CCInfoWindows.Tests/ViewModels/MainViewModelHistoryTests.cs` mirroring existing `MainViewModelAuthFlowTests.cs` shape — for HIST-02 await-chain verification
- [ ] No framework install needed — xUnit already present

## Security Domain

Phase 21 is local-disk persistence of non-secret data (usage utilization percentages and reset timestamps). No credentials, no PII, no network. Security surface is minimal but non-zero.

### Applicable ASVS Categories

| ASVS Category | Applies | Standard Control |
|---------------|---------|-----------------|
| V2 Authentication | no | n/a — no auth in this phase |
| V3 Session Management | no | n/a |
| V4 Access Control | no | n/a — single-user desktop app |
| V5 Input Validation | yes | JSON deserialization already guarded by try/catch returning empty defaults (UsageHistoryService.cs:46-49) |
| V6 Cryptography | no | n/a — no secrets in `usage-history.json` |
| V7 Error Handling and Logging | yes | Existing best-effort try/catch must NOT log file paths or exception messages to UI |
| V12 File and Resources | yes | File path is constant `%LOCALAPPDATA%\CCInfoWindows\usage-history.json` — never user-supplied; no path injection possible |

### Known Threat Patterns for WinUI 3 desktop / .NET 9

| Pattern | STRIDE | Standard Mitigation |
|---------|--------|---------------------|
| Race-condition file corruption (concurrent sync + async write) | Tampering | `SemaphoreSlim _writeLock` (D-05) |
| Resurrected-data after logout | Information Disclosure | `ClearHistory` nulls snapshot BEFORE delete (D-13) |
| Disk-full / read-only volume swallowed silently | Denial of Service (best-effort, accepted) | Existing try/catch — explicit accepted risk per CONTEXT.md |
| Information leakage via exception text | Information Disclosure | Existing best-effort `catch { }` swallows entirely — no UI surface |

## Assumptions Log

| # | Claim | Section | Risk if Wrong |
|---|-------|---------|---------------|
| A1 | `File.WriteAllTextAsync` and `File.WriteAllText` produce byte-identical output for the same string input on .NET 9 | Standard Stack / D-07 | LOW — BCL contract is documented [CITED]. The D-07 byte-identical test catches any regression. |
| A2 | `AppWindow.Closing` fires synchronously and completes before process exit in unpackaged WinUI 3 apps | Pattern 2 / D-02 | LOW — confirmed by Microsoft.UI.Windowing docs and project's existing window-state save pattern relies on this guarantee already (MainWindow.xaml.cs:107-116) |
| A3 | The existing `UsageHistoryService` singleton is the only writer to `usage-history.json` (no other process or thread writes the file) | Concurrency / D-05 | LOW — single-app, single-writer; verified by grep for `usage-history.json` (only `UsageHistoryService` references it via `HistoryFilePath`) |
| A4 | The 2-minute `WindowResetTolerance` is sufficient to absorb API clock-drift in production | D-10 | LOW — CONTEXT.md notes it was "validated in v1.0–v1.3 production" |

**No `[ASSUMED]` claims about compliance, security baselines, retention policies, or external contracts.** All facts in this research are either verified against the codebase or cited from BCL/Windows App SDK documentation.

## Open Questions

1. **Should `PeekLastSnapshot` clone before returning, or share the reference?**
   - What we know: CONTEXT.md "Claude's Discretion" leaves this open. D-04 invariant says shared reference is safe because `AppendHistoryPoint` builds a new `history` object each cycle (MainViewModel.cs:532 `var history = _historyService.LoadHistory();`) and never mutates the shared snapshot in place after save.
   - What's unclear: Whether a future phase might mutate `UsageHistory.Points` post-save and trip a hidden aliasing bug.
   - Recommendation: Default to shared reference per CONTEXT.md. If planner wants belt-and-braces, return `new UsageHistory { ResetsAt = _lastSavedSnapshot.ResetsAt, Points = [.._lastSavedSnapshot.Points] }` — cheap because `Points` is small (≤ 5h × poll-rate of points).

2. **Should `IUsageHistoryService.PeekLastSnapshot` return `UsageHistory?` or a `bool TryGet(out UsageHistory)`?**
   - What we know: Both work; Claude's Discretion delegates the call.
   - What's unclear: Project-wide convention. The codebase has no precedent for `Try*` getters in service interfaces (the only `bool HasValidToken()` in `ICredentialService` returns `bool` separately from getter).
   - Recommendation: `UsageHistory? PeekLastSnapshot()` — matches the codebase's nullable-return-for-optional pattern.

3. **Is HIST-04 verification a unit test or end-to-end integration test?**
   - What we know: D-11 says "phase deliverable for HIST-04 is a verification test, not a code change." The existing `IsWindowReset` static is testable in isolation.
   - What's unclear: Whether the test should exercise the full `AppendHistoryPoint` flow (writes to disk, reads back) or just the static.
   - Recommendation: Test `IsWindowReset` directly (fast, F.I.R.S.T.) AND a single end-to-end test in `UsageHistoryServiceTests` that calls a refactored test helper exercising the clear-then-save sequence. Two complementary tests, both fast.

## Environment Availability

| Dependency | Required By | Available | Version | Fallback |
|------------|------------|-----------|---------|----------|
| .NET 9 SDK | Build + test | ✓ (presumed — v1.3 already builds with .NET 9 per CLAUDE.md) | 9.0 | — |
| Windows App SDK 1.8 | WinUI 3 / `AppWindow.Closing` | ✓ (already referenced) | 1.8 | — |
| xUnit | Test execution | ✓ (already in CCInfoWindows.Tests) | as referenced | — |
| `windows-mcp` MCP tools | Optional manual HIST-01 smoke automation | ✓ (per MEMORY.md) | n/a | Manual click of X button |

**Missing dependencies with no fallback:** None.
**Missing dependencies with fallback:** None.

## Sources

### Primary (HIGH confidence)
- `D:\myProjects\ccInfoWin\.planning\phases\21-history-persistence-hardening\21-CONTEXT.md` — 14 locked decisions, all referenced
- `D:\myProjects\ccInfoWin\CCInfoWindows\CCInfoWindows\Services\Interfaces\IUsageHistoryService.cs` — current interface (3 members)
- `D:\myProjects\ccInfoWin\CCInfoWindows\CCInfoWindows\Services\UsageHistoryService.cs` — current implementation (DefaultDirectory, JsonOptions, sync SaveHistory)
- `D:\myProjects\ccInfoWin\CCInfoWindows\CCInfoWindows\ViewModels\MainViewModel.cs` lines 404-441 (`PollUsageAsync`), 443-491 (`UpdateUsageProperties`), 530-560 (`AppendHistoryPoint`), 562-570 (`IsWindowReset`), 869-885 (`Logout`)
- `D:\myProjects\ccInfoWin\CCInfoWindows\CCInfoWindows\MainWindow.xaml.cs` lines 31-46 (constructor + Closing wire-up), 107-116 (existing OnClosing)
- `D:\myProjects\ccInfoWin\CCInfoWindows\CCInfoWindows\App.xaml.cs` lines 137-178 (DI registration — UsageHistoryService Singleton at 146, MainViewModel Transient at 164)
- `D:\myProjects\ccInfoWin\CCInfoWindows\CCInfoWindows\Models\UsageHistory.cs` — schema (2 properties, JSON-attributed)
- `D:\myProjects\ccInfoWin\CCInfoWindows.Tests\Services\UsageHistoryServiceTests.cs` — existing 6-test pattern
- `D:\myProjects\ccInfoWin\.planning\milestones\v1.4-REQUIREMENTS.md` HIST-01..HIST-05 verbatim
- `D:\myProjects\ccInfoWin\CLAUDE.md` — MVVM, async, naming, build, security rules

### Secondary (MEDIUM confidence — BCL contracts via Microsoft Learn docs, well-established but not Context7-verified this session)
- `learn.microsoft.com/dotnet/api/system.io.file.writeallasync` — UTF-8 no-BOM default
- `learn.microsoft.com/dotnet/api/system.threading.semaphoreslim` — `Wait()` and `WaitAsync()` semantics
- `learn.microsoft.com/windows/apps/api-reference/...AppWindow.Closing` — synchronous event before window destruction

### Tertiary (LOW confidence)
- None — all phase claims are either codebase-verified or BCL-contract-cited.

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH — all primitives already in use in codebase or shipped with .NET 9 BCL
- Architecture: HIGH — CONTEXT.md decisions match live code line-for-line; no drift detected
- Pitfalls: HIGH — derived from direct code reading + canonical .NET async patterns
- Validation architecture: HIGH for unit tests; MEDIUM for HIST-01 (manual smoke required — `AppWindow.Closing` is not testable headlessly)

**Research date:** 2026-05-06
**Valid until:** 2026-06-05 (30 days — stack is mature; no fast-moving dependencies)
