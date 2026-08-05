---
phase: 14-session-management-polish
plan: 01
subsystem: services
tags: [jsonl, session-filtering, directory-validation, subagent-sort, tdd]

# Dependency graph
requires:
  - phase: 13-sonnet-context-window-setting
    provides: JsonlService with sonnetContextSize parameter in BuildSubagentContext
provides:
  - IsValidProjectDirectory guard in JsonlService rejects empty, relative, UNC, and non-existent cwd paths
  - RebuildSessionsList filters out sessions for deleted project directories (SES-01/SES-02)
  - BuildSubagentContext returns subagents sorted alphabetically by AgentId with StringComparer.Ordinal (SES-03)
  - 4 new unit tests covering orphan filtering edge cases and sort stability
affects:
  - session-dropdown, subagent-context-bars, MainViewModel.RefreshSessionList

# Tech tracking
tech-stack:
  added: []
  patterns:
    - IsValidProjectDirectory static guard: short-circuit before filesystem call for invalid/UNC paths
    - UNC guard precedes Directory.Exists to avoid network hang on unreachable servers

key-files:
  created: []
  modified:
    - CCInfoWindows/CCInfoWindows/Services/JsonlService.cs
    - CCInfoWindows.Tests/Services/JsonlServiceTests.cs

key-decisions:
  - "IsValidProjectDirectory short-circuits UNC paths (\\\\server or //server) before Directory.Exists to prevent network hang on unreachable servers"
  - "SES-02 requires no ViewModel changes: when RebuildSessionsList filters out the active session, MainViewModel.RefreshSessionList fallback (lines 606-641) handles auto-reset to next valid session"
  - "Pre-existing DiscoverSessions tests fixed to use real temp directories as cwd (required by new validation)"

patterns-established:
  - "Directory validation guard: IsNullOrEmpty → IsPathRooted → UNC check → Directory.Exists (in that order)"

requirements-completed: [SES-01, SES-02, SES-03]

# Metrics
duration: 5min
completed: 2026-04-12
---

# Phase 14 Plan 01: Session Management Polish Summary

**IsValidProjectDirectory guard in JsonlService filters orphaned sessions and sorts subagent context bars alphabetically by AgentId**

## Performance

- **Duration:** ~5 min
- **Started:** 2026-04-12T17:30:04Z
- **Completed:** 2026-04-12T17:34:29Z
- **Tasks:** 2
- **Files modified:** 2

## Accomplishments

- Added `IsValidProjectDirectory` static guard that rejects empty cwd, relative paths, UNC paths (without calling Directory.Exists), and non-existent directories
- `RebuildSessionsList` now filters sessions via `IsValidProjectDirectory` — users no longer see sessions for deleted project directories
- `BuildSubagentContext` returns subagents sorted by `AgentId` using `StringComparer.Ordinal` — stable alphabetical order on every refresh
- 4 TDD tests added covering all filter cases and the sort contract

## Task Commits

Each task was committed atomically:

1. **Task 1: Add unit tests for session filtering and subagent sort** - `78d2a1e` (test)
2. **Task 2: Implement IsValidProjectDirectory filter and subagent sort** - `63faca2` (feat)

_Note: TDD flow — test commit (RED) followed by implementation commit (GREEN)_

## Files Created/Modified

- `CCInfoWindows/CCInfoWindows/Services/JsonlService.cs` - Added `IsValidProjectDirectory`, updated `RebuildSessionsList` filter, updated `BuildSubagentContext` sort
- `CCInfoWindows.Tests/Services/JsonlServiceTests.cs` - Added 4 new tests; fixed 3 pre-existing DiscoverSessions tests to use real temp dirs as cwd

## Decisions Made

- `IsValidProjectDirectory` short-circuits UNC paths before calling `Directory.Exists` to prevent the known Windows network hang when a UNC server is unreachable
- No ViewModel changes needed for SES-02: `MainViewModel.RefreshSessionList` already resets selection to the first active item when the previously-selected session is absent from the filtered list
- Pre-existing `DiscoverSessions` tests used fake Linux-style cwd paths (`/home/user/project-alpha`) that worked before the filter — updated to use real `_tempDir` subdirectories

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Fixed 3 pre-existing DiscoverSessions tests broken by new cwd validation**
- **Found during:** Task 2 (implement IsValidProjectDirectory)
- **Issue:** `Sessions_AfterInitialize_DiscoversSessions`, `Sessions_DisplayNameFromCwdField`, and `Sessions_SortedByLastActivityDescending` used `/home/user/...` paths as cwd — these don't exist on Windows, so the new filter correctly excluded them, causing the tests to fail
- **Fix:** Created real temp subdirectories under `_tempDir` for each test's cwd, matching the new validation contract
- **Files modified:** `CCInfoWindows.Tests/Services/JsonlServiceTests.cs`
- **Verification:** All 26 JsonlServiceTests pass after fix
- **Committed in:** `63faca2` (Task 2 commit)

---

**Total deviations:** 1 auto-fixed (Rule 1 - pre-existing tests broke under new validation)
**Impact on plan:** Required fix — tests were not testing against real directory state. Fix aligns tests with correct behavior.

## Issues Encountered

- 2 pre-existing `ClaudeApiServiceTests` failures exist in the test suite and are unrelated to this plan (confirmed by baseline check before changes). Not fixed — out of scope.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- SES-01, SES-02, SES-03 requirements satisfied
- Phase 14 complete — all session management polish requirements delivered
- v1.2 milestone complete: model-based context detection (Phase 12), Sonnet context setting (Phase 13), session filtering + subagent sort (Phase 14)

---
*Phase: 14-session-management-polish*
*Completed: 2026-04-12*
