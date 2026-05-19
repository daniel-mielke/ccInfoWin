---
phase: 29
fixed_at: 2026-05-19T11:03:00Z
review_path: .planning/phases/29-fix-subagent-activity-detection-switch-from-assistant-timest/29-REVIEW.md
iteration: 1
findings_in_scope: 2
fixed: 2
skipped: 0
status: all_fixed
---

# Phase 29: Code Review Fix Report

**Fixed at:** 2026-05-19T11:03:00Z
**Source review:** `.planning/phases/29-fix-subagent-activity-detection-switch-from-assistant-timest/29-REVIEW.md`
**Iteration:** 1

**Summary:**
- Findings in scope (Critical + Warning): 2
- Fixed: 2
- Skipped: 0
- Info findings deferred: 4 (per `--fix` default, no `--all` flag)

## Fixed Issues

### WR-01: Test koppelt sich an einen real existierenden Pfad auf der Maintainer-Maschine

**Files modified:** `CCInfoWindows.Tests/Services/JsonlServiceSubagentTests.cs`
**Commit:** `3ea8a10`
**Applied fix:** Replaced hardcoded `ProjectDirName = "D--myProjects-ccInfoWin"` with synthetic `"X--phase29-subagent-fixture"`. Verified `SessionNameHelper.DecodeProjectDirectory` is a pure string operation (no filesystem touch), so the synthetic name decodes cleanly to `"fixture"` regardless of machine layout. Tests remain hermetic and future-proof against the Phase-25-backlog scenario of an `IsValidProjectDirectory` existence-check creeping into `GetContextWindow`.

**Verification:**
- Grep `D--myProjects-ccInfoWin` in `JsonlServiceSubagentTests.cs`: 0 hits.
- Test run after fix: `Fehler: 0, erfolgreich: 3` (591 ms).

### WR-02: `svc.Stop()` läuft NICHT, wenn eine Assertion vorher wirft

**Files modified:** `CCInfoWindows.Tests/Services/JsonlServiceSubagentTests.cs`
**Commit:** `f916b93`
**Applied fix:** Replaced `var svc = new JsonlService(...)` with `using var svc = new JsonlService(...)` in all 3 tests, removed the explicit `svc.Stop()` at the end of each. Verified `JsonlService` implements `IDisposable` (`public void Dispose() => Stop();` at `JsonlService.cs:256`), so RAII via `using` is equivalent to `Stop()` but exception-safe. Also eliminates 3 duplicated `Stop()` calls (CLAUDE.md DRY rule).

**Verification:**
- Grep `svc\.Stop\(\)` in `JsonlServiceSubagentTests.cs`: 0 hits.
- Grep `using var svc = new JsonlService` in `JsonlServiceSubagentTests.cs`: 3 hits (one per test).
- Test run after fix: `Fehler: 0, erfolgreich: 3` (256 ms).

## Skipped Issues

None — all in-scope findings were fixed cleanly.

The 4 Info findings (IN-01 through IN-04) are intentionally deferred per the `--fix` default scope (no `--all` flag):
- IN-01: `async Task` test signature consistency (no change recommended in REVIEW.md itself).
- IN-02: Magic-number `TimeSpan.FromMinutes(-5)` duplicated 3×.
- IN-03: Redundant `Subagent file isSidechain` comment in `JsonlService.cs:715-716`.
- IN-04: `AssertMtimeWasSet` tolerance value (1s vs 3s for FAT32/NTFS coverage).

---

_Fixed: 2026-05-19T11:03:00Z_
_Fixer: Claude (gsd-code-fixer)_
_Iteration: 1_
