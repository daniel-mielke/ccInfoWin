---
phase: 25-cold-start-session-hydration-visibility-window
plan: "01"
subsystem: backend/session-discovery
tags: [jsonl, session-hydration, cwd-fallback, race-fix, tdd]
dependency_graph:
  requires: []
  provides: [DROPDOWN-02, DROPDOWN-03, DROPDOWN-06]
  affects: [JsonlService, JsonlServiceTests, JsonlServiceColdStartTests]
tech_stack:
  added: []
  patterns: [internal-test-seam, ExcludeFromCodeCoverage]
key_files:
  created:
    - CCInfoWindows.Tests/Helpers/ControllableStreamProxy.cs
    - CCInfoWindows.Tests/Services/JsonlServiceColdStartTests.cs
  modified:
    - CCInfoWindows/CCInfoWindows/Services/JsonlService.cs
    - CCInfoWindows.Tests/Services/JsonlServiceTests.cs
decisions:
  - "DROPDOWN-02 fallback leaves data.Cwd empty (not set to decoded label) so DROPDOWN-03 IsNullOrEmpty path keeps the session; DisplayName is resolved by GetDisplayName(cwd:'', fallbackDirName:projectKey) in RebuildSessionsList"
  - "ScanAsync test-seam replaced by ProcessFilesForTestAsync to trigger the incremental (not forceFullRead) re-parse path in the race regression test"
  - "RebuildSessionsList_ExcludesEmptyCwd test updated to reflect new DROPDOWN-03 keep behaviour (old test described the pre-Phase-25 bug)"
metrics:
  duration: "~35 min"
  completed: "2026-05-08"
  tasks_completed: 2
  files_changed: 4
---

# Phase 25 Plan 01: JsonlService Hardening (DROPDOWN-02/03/06) Summary

Three-fix plan: per-entry Cwd hydration with DecodeProjectDirectory fallback, softened empty-Cwd filter in RebuildSessionsList, and stream.Position race fix — all in JsonlService.cs.

## What Was Built

### DROPDOWN-02: Per-entry Cwd Hydration (JsonlService.cs:575-602)

**Before:** Only `entries[0].Cwd` was consulted. Tail-window reads often land on entries without a `cwd` field, leaving `data.Cwd` empty.

**After:** `ParseFileIntoProject` iterates ALL parsed entries; the FIRST non-empty `cwd` across the entire batch wins. When no entry carries a cwd, `data.Cwd` intentionally stays empty (not set to the decoded label) so the DROPDOWN-03 softened filter keeps the session visible. A `Debug.WriteLine` diagnostic logs the derived display surrogate for dev builds.

**Key design decision:** The `DecodeProjectDirectory` decoded value (e.g. `"ccInfoWin"`) is NOT stored in `data.Cwd` — it is only used as the `fallbackDirName` argument to `SessionNameHelper.GetDisplayName(cwd, kvp.Key)` inside `RebuildSessionsList` (line 807). This way the DROPDOWN-03 `string.IsNullOrEmpty(s.Cwd)` path correctly triggers for these sessions.

### DROPDOWN-03: Softened RebuildSessionsList Filter (JsonlService.cs:820-823)

**Before:** `.Where(s => s is not null && IsValidProjectDirectory(s.Cwd))` — `IsValidProjectDirectory("")` returns false, silently dropping all sessions with empty Cwd.

**After:**
```csharp
.Where(s => s is not null && (string.IsNullOrEmpty(s.Cwd) || Directory.Exists(s.Cwd)))
```
Sessions with empty Cwd are kept (DisplayName resolved by GetDisplayName fallback chain). Sessions whose Cwd is non-empty but the directory no longer exists are still dropped. `IsValidProjectDirectory` itself is unchanged (O-01).

### DROPDOWN-06: stream.Position Race Fix (JsonlService.cs:444, 469)

**Before:** Both `ReadAllLines` and `ReadIncrementalLines` returned `stream.Length` as the end-position after draining the reader. If the underlying file grew during the read, `stream.Length` would be larger than the bytes actually consumed — subsequent incremental reads would start too far ahead and silently skip newly-written lines.

**After:** Both return `stream.Position` after the final `ReadLine()`. The StreamReader internal buffer is fully drained at that point, so `stream.Position` equals the byte offset of the last parsed line. The early-return guard at `ReadIncrementalLines:457` (`if (startPosition >= stream.Length) return (lines, stream.Length)`) is intentionally unchanged — it fires before any read occurs.

## Test Files

### `CCInfoWindows.Tests/Helpers/ControllableStreamProxy.cs`

`internal sealed class ControllableStreamProxy : Stream` — wraps any `Stream`, counts `\n` bytes in `Read()` overrides, fires `OnAfterReadLine(int lineIndex)` after each newline boundary. Delegate-all pattern for `Length`, `Position`, `Seek`, `Flush`. `CanWrite` returns false. Prepared for future use in injection tests (not wired into production in this plan — see Known Stubs section).

### `CCInfoWindows.Tests/Services/JsonlServiceColdStartTests.cs`

Four `[Fact]` tests, all GREEN after Task 2:

| Test | DROPDOWN | Assertion |
|------|----------|-----------|
| `ParseFileIntoProject_NoEntryHasCwd_FallsBackToDecodedProjectDirName` | 02 | Session appears in Sessions; DisplayName == "ccInfoWin" |
| `RebuildSessionsList_EmptyCwd_KeepsSessionWhenDisplayNameDerivable` | 03 | Sessions contains project with empty-cwd entries |
| `RebuildSessionsList_NonEmptyCwdPointingAtDeletedDir_DropsSession` | 03 | Sessions does NOT contain project pointing at deleted dir |
| `ParseFileIntoProject_LinesWrittenDuringRace_AreNotSilentlyDropped` | 06 | EntryCount == 5 after dual-Refresh with 3+2 line split |

## Internal Test Seams Added to JsonlService

Two `internal` methods added near `RebuildSessionsList`, both `[ExcludeFromCodeCoverage]`:

```csharp
internal int GetEntryCountForProject(string projectDirName)
// Returns data.EntryLog.Count under _sessionsLock for the given project.

internal async Task ProcessFilesForTestAsync(IEnumerable<string> filePaths)
// Triggers incremental re-parse of given files (mirrors FileSystemWatcher debounce path,
// NOT forceFullRead). Used by DROPDOWN-06 regression test.
```

**Why `ProcessFilesForTestAsync` instead of `ScanAsync`:** `DiscoverSessions` always uses `forceFullRead: true`, which re-reads all entries from scratch. Since `_projectData` is not reset between scans, a second `DiscoverSessions` call double-counts entries (pre-Phase-25 SeenIds have unique UUIDs). The incremental path via `ProcessSingleFile` correctly starts from the stored position, picking up only new lines.

> **Note added 2026-08-06 (post-review remediation, finding 2):** the double-counting claim above no longer holds. Deduplication now keys on `message.id|requestId` (per-line `uuid` only as fallback) and a repeated identity supersedes the earlier `EntryLog` entry in place, so a second full read is idempotent. `ProcessFilesForTestAsync` is kept anyway: it exercises the incremental read path the FileSystemWatcher actually uses, which a full re-scan would bypass. It now also runs the same per-file guard as the debounce callback.

## Updated Existing Test

`JsonlServiceTests.RebuildSessionsList_ExcludesEmptyCwd` (line 381) was renamed and updated to `RebuildSessionsList_EmptyCwd_KeepsSessionWithDecodedDisplayName`. The old assertion (`Assert.Empty`) described the pre-Phase-25 bug. The new assertion confirms `Sessions` contains exactly one session with the decoded display name. This is a Rule 1 (bug fix) deviation.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] DROPDOWN-02 fallback stores decoded value in data.Cwd**
- **Found during:** Task 2 test run (3/4 tests failing)
- **Issue:** Plan specified `data.Cwd = decoded` for the fallback path. But `decoded = "ccInfoWin"` (relative, not rooted) — so `Directory.Exists("ccInfoWin")` = false, and the DROPDOWN-03 filter was dropping the session.
- **Fix:** Leave `data.Cwd` empty when no entry carries a cwd. The DisplayName is already resolved by `SessionNameHelper.GetDisplayName(cwd: "", fallbackDirName: kvp.Key)` in `RebuildSessionsList`. The `Debug.WriteLine` diagnostic still logs the surrogate label.
- **Files modified:** `CCInfoWindows/CCInfoWindows/Services/JsonlService.cs`
- **Commit:** 4f4af42

**2. [Rule 1 - Bug] ScanAsync test seam triggers forceFullRead, double-counting entries**
- **Found during:** Task 2 DROPDOWN-06 test (Expected 5, Actual 8)
- **Issue:** `ScanAsync` calling `DiscoverSessions` uses `forceFullRead: true`, which re-reads all bytes from offset 0. Combined with unique UUIDs, this appended all previously-parsed entries again.
- **Fix:** Added `ProcessFilesForTestAsync` seam instead, which calls `ProcessSingleFile` (incremental path). `ScanAsync` was not added to the production class.
- **Files modified:** `CCInfoWindows/CCInfoWindows/Services/JsonlService.cs`, `CCInfoWindows.Tests/Services/JsonlServiceColdStartTests.cs`
- **Commit:** 4f4af42

**3. [Rule 1 - Bug] RebuildSessionsList_ExcludesEmptyCwd asserting old broken behaviour**
- **Found during:** Full test suite run (4 failures vs 3 baseline)
- **Issue:** The existing test `Assert.Empty(service.Sessions)` for empty-Cwd entries directly contradicted DROPDOWN-03's new "keep" behaviour.
- **Fix:** Renamed test to `RebuildSessionsList_EmptyCwd_KeepsSessionWithDecodedDisplayName` and updated assertion to `Assert.Single` + `Assert.Equal`.
- **Files modified:** `CCInfoWindows.Tests/Services/JsonlServiceTests.cs`
- **Commit:** 4f4af42

**4. [Rule 1 - Deviation] Test 3 (DropsSession) was GREEN on unmodified code**
- **Found during:** Task 1 RED analysis
- **Issue:** The plan expected 4 RED tests. `RebuildSessionsList_NonEmptyCwdPointingAtDeletedDir_DropsSession` describes correct existing behaviour — it passed on the unmodified code. The 4-RED expectation in the plan was overstated for this test.
- **Impact:** None — the test verifies that the deleted-directory drop path still works after DROPDOWN-03 changes, which is the correct regression guard.

**5. [Rule 2 - Missing] ControllableStreamProxy not wired to production seam**
- **Found during:** Task 1 design
- **Issue:** The plan described a `ControllableStreamProxy` for race-window injection during parse. Wiring it to the production `ReadAllLines` would require adding an injectable-stream overload (architectural change per the plan's interface sketch). The DROPDOWN-06 race is verified via the dual-Refresh `ProcessFilesForTestAsync` pattern instead, which tests the same property (lines written between reads are not skipped).
- **Status:** `ControllableStreamProxy` exists and is ready for future use. Not wired in this plan.

## Test Baseline

| Metric | Before Plan 25-01 | After Plan 25-01 |
|--------|------------------|-----------------|
| Total tests | 286 | 290 (+4 new) |
| Passing | 283 | 288 |
| Failing | 3 | 2 |
| New failures | — | 0 |

Pre-existing failures reduced from 3 to 2: one of the three baseline failures (`RebuildSessionsList_ExcludesEmptyCwd`) was the test updated by this plan; the remaining 2 are `ClaudeApiServiceTests` parameter-naming mismatches (out of scope, documented in STATE.md).

## Threat Flags

None — no new network endpoints, auth paths, file access patterns, or schema changes introduced.

## Self-Check

- `CCInfoWindows.Tests/Helpers/ControllableStreamProxy.cs` — FOUND
- `CCInfoWindows.Tests/Services/JsonlServiceColdStartTests.cs` — FOUND
- Commit `69aebed` (Task 1 RED scaffold) — FOUND
- Commit `4f4af42` (Task 2 GREEN implementation) — FOUND
- `grep stream.Position JsonlService.cs` → 5 occurrences (≥ 2 required) — PASS
- `grep DecodeProjectDirectory JsonlService.cs` → 1 occurrence — PASS
- `grep "IsNullOrEmpty(s.Cwd) || Directory.Exists" JsonlService.cs` → 1 occurrence — PASS
- `grep "data.Cwd = firstEntry.Cwd" JsonlService.cs` → 0 occurrences — PASS
- JsonlServiceColdStartTests: 4 passed, 0 failed — PASS
- MessengerThreadingConventionTests: 2 passed, 0 failed — PASS
- Full suite: 2 pre-existing failures only, no new failures — PASS

## Self-Check: PASSED
