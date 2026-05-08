---
phase: 26-persistent-session-renaming
plan: "01"
subsystem: services
tags: [session-rename, persistence, g2-pattern, semaphore, sanitizer]
dependency_graph:
  requires: []
  provides: [ISessionNameStore, SessionNameStore, SessionNameSanitizer, SessionNameChangedEventArgs]
  affects: [App.xaml.cs, CCInfoWindows.Tests]
tech_stack:
  added: [System.Collections.Concurrent.ConcurrentDictionary, System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping]
  patterns: [G-2 SemaphoreSlim write-guard, atomic-rename via File.Move, _lastSavedSnapshot cache, TDD RED/GREEN/REFACTOR]
key_files:
  created:
    - CCInfoWindows/CCInfoWindows/Helpers/SessionNameSanitizer.cs
    - CCInfoWindows/CCInfoWindows/Models/SessionNameChangedEventArgs.cs
    - CCInfoWindows/CCInfoWindows/Services/Interfaces/ISessionNameStore.cs
    - CCInfoWindows/CCInfoWindows/Services/SessionNameStore.cs
    - CCInfoWindows.Tests/Helpers/SessionNameSanitizerTests.cs
    - CCInfoWindows.Tests/Services/SessionNameStoreTests.cs
  modified:
    - CCInfoWindows/CCInfoWindows/App.xaml.cs
decisions:
  - "D-01 interface shape locked: GetCustomName, SetCustomName, ClearCustomName, Save, SaveAsync, NameChanged"
  - "D-02 storage: session-names.json in %LOCALAPPDATA%\\CCInfoWindows, Dictionary<string,string> keyed by encoded projectDirName"
  - "D-07 belt-and-suspenders: SessionNameSanitizer.Strip applied in SessionNameStore.SetCustomName even if UI pre-sanitizes"
  - "D-13 honored: ISessionNameStore.NameChanged is a standard .NET event (not WeakReferenceMessenger) for AddTransient-safe cross-VM delivery"
  - "A2-P1 atomic-rename delta from UsageHistoryService: File.Move(tmp, final, overwrite:true) instead of plain File.WriteAllText"
  - "A2-P2 UnsafeRelaxedJsonEscaping: emoji/CJK readable in JSON file without unicode escapes"
  - "O-02 Bidi-codepoints (U+202A..U+202E) intentionally NOT stripped — same scope as macOS reference"
metrics:
  duration: "~18 minutes"
  completed: "2026-05-08T16:47:16Z"
  tasks_completed: 3
  files_changed: 7
---

# Phase 26 Plan 01: Session Name Store Summary

Persistence backbone for Phase 26: `ISessionNameStore` singleton service + `SessionNameSanitizer` helper, fully tested, DI-wired.

## What Was Built

### Production Files (4 new + 1 modified)

**`SessionNameSanitizer.cs`** (`Helpers/`) — Pure static helper, strips C0 control characters (U+0000..U+001F) and DEL (U+007F). CVE-2021-42574 mitigation. Bidi codepoints intentionally out-of-scope (O-02). 24 chars of implementation.

**`SessionNameChangedEventArgs.cs`** (`Models/`) — Sealed `EventArgs` subclass carrying `required string SessionId` only (CD-04: consumers re-resolve via `GetCustomName` to avoid stale-data races).

**`ISessionNameStore.cs`** (`Services/Interfaces/`) — D-01 locked contract: `GetCustomName`, `SetCustomName`, `ClearCustomName`, `Save()`, `SaveAsync()`, `NameChanged` event. XML docs reference G-2 convention, D-13 .NET-event rationale, D-02 storage key.

**`SessionNameStore.cs`** (`Services/`) — G-2 implementation with one deliberate delta from `UsageHistoryService`: atomic-rename via `File.Move(tmp, final, overwrite:true)` per PITFALLS A2-P1. `ConcurrentDictionary<string,string>` for thread-safe reads outside the lock. `UnsafeRelaxedJsonEscaping` preserves emoji/CJK in the JSON file (A2-P2). `PeekLastSnapshot()` exposes `_lastSavedSnapshot` as `internal` for test introspection.

**`App.xaml.cs`** (modified) — `services.AddSingleton<ISessionNameStore, SessionNameStore>()` inserted immediately after `IUsageHistoryService` registration.

### Test Files (2 new)

**`SessionNameSanitizerTests.cs`** (`CCInfoWindows.Tests/Helpers/`) — 11 xUnit tests (Theory + Fact). Covers null, empty, normal text, all C0 chars, tab, newline, DEL, emoji+CJK, space boundary, Bidi non-stripping.

**`SessionNameStoreTests.cs`** (`CCInfoWindows.Tests/Services/`) — 13 xUnit tests. Uses `IDisposable` with per-test temp directory (F.I.R.S.T.). Covers: cold-start empty state, set/get, control-char stripping, empty-clears, NameChanged event, ClearCustomName event, atomic-rename proof (no .tmp after save), round-trip persistence, sync/async byte-identical output, 10 concurrent callers no corruption, orphan retention (RENAME-06), semaphore lock release after IOException, `_lastSavedSnapshot` null-before/set-after.

## Test Results

| Suite | Tests | Passed | Failed |
|-------|-------|--------|--------|
| SessionNameSanitizerTests | 11 | 11 | 0 |
| SessionNameStoreTests | 13 | 13 | 0 |
| MessengerThreadingConventionTests | 2 | 2 | 0 |
| Full suite | 314 | 312 | 2 (pre-existing ClaudeApiServiceTests) |

Pre-existing failures (`ClaudeApiServiceTests.FetchUsageAsync_On*NullResponse_*`) were already failing before Plan 26-01 — not caused by this plan.

## Key Invariants Locked for Wave 2/3

1. **Interface shape (D-01):** `ISessionNameStore` contract is frozen. Wave 2 (MainViewModel pencil dialog) and Wave 3 (SettingsViewModel Sessions tab) both consume it without modification.
2. **File path (D-02):** `%LOCALAPPDATA%\CCInfoWindows\session-names.json`. Wave 2/3 never read/write this directly — only via the store.
3. **Sanitization rule (D-07):** `SessionNameSanitizer.Strip` is belt-and-suspenders in `SetCustomName`. UI layer may pre-sanitize or not — result is always clean.
4. **Event contract (D-13 + CD-04):** `NameChanged` carries `SessionId` only. Handler in `MainViewModel` must do `_dispatcherQueue.TryEnqueue(RefreshSessionList)` per G-1.
5. **Orphan policy (D-08/RENAME-06):** Orphan entries survive app restarts. Wave 3 Settings tab renders them as greyed rows (or defers to v1.6+).
6. **G-2 lock invariant:** `_writeLock` is `SemaphoreSlim(1,1)`. No `lock` keyword anywhere. Sync `Save()` and async `SaveAsync()` share the same semaphore.

## Commits

| Task | Commit | Message |
|------|--------|---------|
| 1 — SessionNameSanitizer | `229cb0a` | feat(26-01): add SessionNameSanitizer helper + tests (RENAME-05) |
| 2 — ISessionNameStore interface | `963d318` | feat(26-01): add ISessionNameStore interface + SessionNameChangedEventArgs (RENAME-07) |
| 3 — SessionNameStore + DI + tests | `55b2f86` | feat(26-01): add SessionNameStore G-2 impl + DI registration + invariant tests (RENAME-03,05,06,07) |

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] xUnit1031 blocking task warning in SyncSave_ReleasesLockOnException**
- **Found during:** Task 3 test run
- **Issue:** Test used `saveTask.Wait(3000)` — xUnit1031 analyzer warns about blocking task operations in test methods (potential deadlock).
- **Fix:** Converted test method to `async Task`, replaced `.Wait()` with `await`.
- **Files modified:** `CCInfoWindows.Tests/Services/SessionNameStoreTests.cs`
- **Commit:** Included in `55b2f86` (same task commit — fix applied before commit)

No other deviations. Plan executed exactly as written for all three tasks.

## Known Stubs

None. No UI surface wired in this plan — by design. Wave 2 (`MainViewModel` + pencil dialog) and Wave 3 (`SettingsViewModel` Sessions tab) are the next consumers.

## Threat Flags

No new network endpoints, auth paths, or trust boundaries introduced. `session-names.json` is a new file path, but it is within the established `%LOCALAPPDATA%\CCInfoWindows\` trust boundary already used by `usage-history.json` and `settings.json`. Threats T-26-01 through T-26-06 were addressed as planned (accepted or mitigated — see plan threat model).

## Self-Check: PASSED

- `CCInfoWindows/CCInfoWindows/Helpers/SessionNameSanitizer.cs` — exists
- `CCInfoWindows/CCInfoWindows/Models/SessionNameChangedEventArgs.cs` — exists
- `CCInfoWindows/CCInfoWindows/Services/Interfaces/ISessionNameStore.cs` — exists
- `CCInfoWindows/CCInfoWindows/Services/SessionNameStore.cs` — exists
- `CCInfoWindows.Tests/Helpers/SessionNameSanitizerTests.cs` — exists
- `CCInfoWindows.Tests/Services/SessionNameStoreTests.cs` — exists
- Commits `229cb0a`, `963d318`, `55b2f86` — all present in git log
- Build: 0 errors
- SessionNameSanitizerTests: 11/11 passed
- SessionNameStoreTests: 13/13 passed
- MessengerThreadingConventionTests: 2/2 passed
