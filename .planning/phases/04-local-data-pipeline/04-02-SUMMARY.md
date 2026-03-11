---
phase: 04-local-data-pipeline
plan: 02
subsystem: data
tags: [jsonl, filesystemwatcher, sessions, context-window, tokens, cache, incremental-read]

# Dependency graph
requires:
  - phase: 04-local-data-pipeline
    plan: 01
    provides: IJsonlService, JsonlEntry, SessionInfo, ContextWindowData, TokenSummary, JsonlCache, ModelContextLimits, SessionNameHelper

provides:
  - JsonlService implementing IJsonlService with full JSONL parsing pipeline
  - Tail read (last 1MB) with FileShare.ReadWrite to avoid locking conflicts
  - Tolerant JSONL parsing (skips malformed lines)
  - Session discovery from *.jsonl files grouped by sessionId
  - Context window from last non-sidechain assistant message only
  - Token aggregation with uuid+requestId deduplication
  - Subagent context from {sessionId}/subagents/agent-*.jsonl files
  - Cache persistence to jsonl-cache.json with FilePositionMarkers
  - Incremental reads from cached positions (only new bytes)
  - FileSystemWatcher with 300ms debounce for live updates
  - IJsonlService singleton registered in DI (App.xaml.cs)

affects:
  - 04-03-local-data-pipeline (binds UI to IJsonlService.Sessions, GetContextWindow, GetTokenSummary)

# Tech tracking
tech-stack:
  added: []
  patterns:
    - JsonlService accepts optional directory overrides in constructor for test isolation
    - Static public methods (ReadTailLines, ReadIncrementalLines, ParseJsonlEntries) for direct testability
    - System.Threading.Timer for debounce (not DispatcherQueueTimer — runs on background thread)
    - Lock object + Lock type for thread-safe session data access
    - ProcessFileChangeAsync wrapped in try/catch to prevent exceptions escaping timer callbacks
    - MaxWatcherRestarts guard prevents infinite restart loops on watcher errors

key-files:
  created:
    - CCInfoWindows/CCInfoWindows/Services/JsonlService.cs
    - CCInfoWindows.Tests/Services/JsonlServiceTests.cs
  modified:
    - CCInfoWindows/CCInfoWindows/App.xaml.cs

key-decisions:
  - "JsonlService constructor accepts directory overrides for test isolation (same pattern as UsageHistoryService)"
  - "ReadTailLines, ReadIncrementalLines, ParseJsonlEntries exposed as public static for direct unit testing without InitializeAsync"
  - "LastAssistantEntry replaced (not accumulated) on each new non-sidechain assistant message — context window is a snapshot, not a sum"
  - "MaxWatcherRestarts=5 prevents infinite restart loops on persistent watcher errors"
  - "FindSubagentFiles checks both {projectDir}/{sessionId}/subagents/ and {projectDir}/subagents/ layouts"

# Metrics
duration: 5min
completed: 2026-03-11
---

# Phase 4 Plan 02: JsonlService Implementation Summary

**JsonlService with FileSystemWatcher, incremental tail reads, tolerant JSONL parsing, session/context/token pipeline, and cache persistence — full IJsonlService implementation with 18 passing unit tests**

## Performance

- **Duration:** 5 min
- **Started:** 2026-03-11T17:24:49Z
- **Completed:** 2026-03-11T17:30:00Z
- **Tasks:** 2
- **Files modified:** 3

## Accomplishments

- Full `IJsonlService` implementation in `JsonlService.cs` (~330 lines)
- TDD workflow: 18 tests written first (RED), then implementation (GREEN)
- Tail read discards first partial line after seek to handle mid-line seeks correctly
- Context window uses only the LAST non-sidechain assistant entry (not cumulative sum)
- Token aggregation deduplicates by `uuid|requestId` composite key
- Subagent context discovery from `agent-*.jsonl` files in `subagents/` directories
- FileSystemWatcher with 64KB internal buffer, IncludeSubdirectories, *.jsonl filter
- 300ms debounce via `System.Threading.Timer` prevents burst processing on rapid writes
- Watcher error handler with max 5 restart guard
- Cache persistence: `FilePositionMarker` per file, incremental reads on subsequent runs
- DI singleton registration: `services.AddSingleton<IJsonlService, JsonlService>()`

## Task Commits

| Task | Description | Commit |
|------|-------------|--------|
| 1 | JsonlService core + 18 unit tests (TDD) | `1268df1` |
| 2 | FileSystemWatcher + DI registration | `611b64b` |

## Files Created/Modified

- `CCInfoWindows/CCInfoWindows/Services/JsonlService.cs` — Full IJsonlService implementation (330 lines)
- `CCInfoWindows.Tests/Services/JsonlServiceTests.cs` — 18 unit tests covering all must-have behaviors
- `CCInfoWindows/CCInfoWindows/App.xaml.cs` — Added `services.AddSingleton<IJsonlService, JsonlService>()`

## Decisions Made

- `ReadTailLines`, `ReadIncrementalLines`, `ParseJsonlEntries` are `public static` methods — allows direct unit testing without the full `InitializeAsync()` lifecycle
- `LastAssistantEntry` is replaced per entry (not accumulated) — context window is the current snapshot of the last message, not a running total
- `FindSubagentFiles` checks two directory layouts: `{projectDir}/{sessionId}/subagents/` and `{projectDir}/subagents/` to handle both nesting conventions
- `MaxWatcherRestarts = 5` hard limit prevents infinite error+restart loops from persistent filesystem issues

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Fixed large file test setup — prefix must exceed TailWindowBytes**
- **Found during:** Task 1 (TDD GREEN phase)
- **Issue:** Test used `TailWindowBytes - 50` prefix, making the file smaller than 1MB; seek resolved to position 0 so no partial line was discarded
- **Fix:** Changed prefix to `TailWindowBytes + 100` — file now exceeds 1MB boundary, seek lands inside the 'A'-line as intended
- **Files modified:** `CCInfoWindows.Tests/Services/JsonlServiceTests.cs`
- **Verification:** `ReadTailLines_LargeFile_DiscardsFirstPartialLine` passes

---

**Total deviations:** 1 auto-fixed (test setup bug in large-file boundary test)
**Impact on plan:** No scope creep. Correctness improvement to the test itself.

## Issues Encountered

- MVVMTK0045 warnings pre-exist in the codebase (not introduced here) — out of scope, logged as pre-existing
- Large file test required fix to boundary condition in test setup (off-by-100 bytes in prefix size)

## User Setup Required

None — no external service configuration required.

## Next Phase Readiness

- `IJsonlService` is fully implemented and DI-registered
- Plan 03 can bind UI to `Sessions`, `GetContextWindow()`, `GetTokenSummary()` directly
- Session discovery, context window, and token data are ready for display

---
*Phase: 04-local-data-pipeline*
*Completed: 2026-03-11*
