---
phase: 04-local-data-pipeline
plan: 01
subsystem: data
tags: [jsonl, system.text.json, sessions, context-window, tokens, models]

# Dependency graph
requires:
  - phase: 01-foundation
    provides: AppSettings, project structure, DI setup
provides:
  - JsonlEntry record with full JSONL field deserialization and tolerant options
  - SessionInfo with IsActive(TimeSpan) method
  - ContextWindowData with Utilization computed property and Subagents list
  - TokenSummary and JsonlCache models for persistence
  - IJsonlService interface contract for Plan 02 implementation
  - TokenFormatter, ModelContextLimits, SessionNameHelper helpers
  - SessionSelectedMessage and JsonlDataUpdatedMessage for MVVM messaging
  - AppSettings extended with lastSelectedSessionId and sessionActivityThresholdMinutes
affects:
  - 04-02-local-data-pipeline (implements IJsonlService)
  - 04-03-local-data-pipeline (binds UI to SessionInfo, ContextWindowData)

# Tech tracking
tech-stack:
  added: []
  patterns:
    - JsonlEntry.DefaultOptions static field for reusable tolerant deserialization
    - ValueChangedMessage<T> pattern for all MVVM messenger messages
    - Static helper classes with const thresholds (no magic numbers)
    - ModelContextLimits dictionary lookup with OrdinalIgnoreCase

key-files:
  created:
    - CCInfoWindows/CCInfoWindows/Models/JsonlEntry.cs
    - CCInfoWindows/CCInfoWindows/Models/SessionInfo.cs
    - CCInfoWindows/CCInfoWindows/Models/ContextWindowData.cs
    - CCInfoWindows/CCInfoWindows/Models/TokenSummary.cs
    - CCInfoWindows/CCInfoWindows/Models/JsonlCache.cs
    - CCInfoWindows/CCInfoWindows/Services/Interfaces/IJsonlService.cs
    - CCInfoWindows/CCInfoWindows/Helpers/TokenFormatter.cs
    - CCInfoWindows/CCInfoWindows/Helpers/ModelContextLimits.cs
    - CCInfoWindows/CCInfoWindows/Helpers/SessionNameHelper.cs
    - CCInfoWindows/CCInfoWindows/Messages/SessionSelectedMessage.cs
    - CCInfoWindows/CCInfoWindows/Messages/JsonlDataUpdatedMessage.cs
    - CCInfoWindows.Tests/Helpers/TokenFormatterTests.cs
    - CCInfoWindows.Tests/Helpers/ModelContextLimitsTests.cs
    - CCInfoWindows.Tests/Helpers/SessionNameHelperTests.cs
    - CCInfoWindows.Tests/Helpers/ContextWindowTests.cs
    - CCInfoWindows.Tests/Models/SessionInfoTests.cs
  modified:
    - CCInfoWindows/CCInfoWindows/Models/AppSettings.cs

key-decisions:
  - "JsonlEntry.DefaultOptions uses UnmappedMemberHandling.Skip for tolerant deserialization of unknown JSONL fields"
  - "ShouldWarnAutocompact uses > LargeModelThresholdTokens (not >=) so exactly 100K models use 95% threshold"
  - "IsActive exact-threshold test uses 1-second buffer to avoid timing flakiness"
  - "ModelContextLimits uses StringComparer.OrdinalIgnoreCase on dictionary to handle mixed-case model names"

patterns-established:
  - "Static DefaultOptions field on deserialization records for reuse across service layer"
  - "TokenFormatter uses invariant culture for K/M decimal separator (dot), not German locale"

requirements-completed:
  - DATA-03
  - SESS-01
  - SESS-05
  - CTXW-01
  - CTXW-02
  - CTXW-04
  - TOKS-01
  - SETT-03

# Metrics
duration: 6min
completed: 2026-03-11
---

# Phase 4 Plan 01: Local Data Pipeline - Contracts and Helpers Summary

**Typed JSONL deserialization records, IJsonlService contract, session/context/token models, and pure helper classes (TokenFormatter, ModelContextLimits, SessionNameHelper) with 70 passing unit tests covering CTXW-01/02/04**

## Performance

- **Duration:** 6 min
- **Started:** 2026-03-11T17:16:06Z
- **Completed:** 2026-03-11T17:21:43Z
- **Tasks:** 3
- **Files modified:** 17

## Accomplishments
- Full data contract layer for Plan 02 implementation: 6 models, 1 interface, 3 helpers, 2 messages
- 70 unit tests green covering all pure logic (Token formatting, model display names, autocompact thresholds, session activity, JSONL deserialization)
- AppSettings extended with session persistence fields (lastSelectedSessionId, sessionActivityThresholdMinutes)

## Task Commits

Each task was committed atomically:

1. **Task 1: Create data models and IJsonlService interface** - `4182bf0` (feat)
2. **Task 2: Create helpers and extend AppSettings** - `9483f1c` (feat)
3. **Task 3: Write unit tests for all pure logic** - `acf2f98` (test)

## Files Created/Modified
- `CCInfoWindows/CCInfoWindows/Models/JsonlEntry.cs` - JSONL deserialization records with DefaultOptions, all usage fields
- `CCInfoWindows/CCInfoWindows/Models/SessionInfo.cs` - Session model with IsActive(TimeSpan) method
- `CCInfoWindows/CCInfoWindows/Models/ContextWindowData.cs` - Context window state with Utilization computed property and SubagentContextData list
- `CCInfoWindows/CCInfoWindows/Models/TokenSummary.cs` - Aggregated per-session token counts
- `CCInfoWindows/CCInfoWindows/Models/JsonlCache.cs` - Persistent cache with FilePositionMarker and CachedSessionData
- `CCInfoWindows/CCInfoWindows/Services/Interfaces/IJsonlService.cs` - Full service contract for Plan 02
- `CCInfoWindows/CCInfoWindows/Helpers/TokenFormatter.cs` - Compact K/M suffix formatting with invariant culture
- `CCInfoWindows/CCInfoWindows/Helpers/ModelContextLimits.cs` - Context limits, display name parsing, autocompact thresholds
- `CCInfoWindows/CCInfoWindows/Helpers/SessionNameHelper.cs` - Last-segment extraction from cwd and encoded directory names
- `CCInfoWindows/CCInfoWindows/Messages/SessionSelectedMessage.cs` - Session selection MVVM message
- `CCInfoWindows/CCInfoWindows/Messages/JsonlDataUpdatedMessage.cs` - Data update MVVM message
- `CCInfoWindows/CCInfoWindows/Models/AppSettings.cs` - Added lastSelectedSessionId and sessionActivityThresholdMinutes
- `CCInfoWindows.Tests/Helpers/TokenFormatterTests.cs` - 11 formatting cases
- `CCInfoWindows.Tests/Helpers/ModelContextLimitsTests.cs` - 12 cases for limits, display names, thresholds
- `CCInfoWindows.Tests/Helpers/SessionNameHelperTests.cs` - 8 cases for path and encoded name extraction
- `CCInfoWindows.Tests/Helpers/ContextWindowTests.cs` - 14 cases covering CTXW-01/02/04
- `CCInfoWindows.Tests/Models/SessionInfoTests.cs` - 6 cases including full JsonlEntry deserialization

## Decisions Made
- `JsonlEntry.DefaultOptions` uses `UnmappedMemberHandling.Skip` for tolerant deserialization — JSONL files may contain fields not in the schema
- `ShouldWarnAutocompact` uses `> LargeModelThresholdTokens` (not `>=`) so models with exactly 100K max tokens use the 95% threshold
- `IsActive` exact-threshold test uses a 1-second buffer to avoid CI timing flakiness
- `ModelContextLimits` dictionary uses `StringComparer.OrdinalIgnoreCase` to handle model name case variations

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Fixed ShouldWarnAutocompact boundary condition**
- **Found during:** Task 3 (unit tests)
- **Issue:** Using `>=` for LargeModelThresholdTokens meant 100K-context models were classified as large (90% threshold) instead of small (95% threshold)
- **Fix:** Changed `>=` to `>` in ModelContextLimits.cs
- **Files modified:** `CCInfoWindows/CCInfoWindows/Helpers/ModelContextLimits.cs`
- **Verification:** ContextWindowTests SmallModel tests pass
- **Committed in:** `acf2f98` (Task 3 commit)

---

**Total deviations:** 1 auto-fixed (1 bug in threshold boundary)
**Impact on plan:** Necessary for correct CTXW-04 behavior. No scope creep.

## Issues Encountered
- Duplicate InlineData in ModelContextLimitsTests caught by xUnit1025 analyzer — removed duplicate before committing

## User Setup Required
None - no external service configuration required.

## Next Phase Readiness
- IJsonlService contract is complete and ready for Plan 02 implementation
- All models, helpers and messages are in place — Plan 02 can implement JsonlService without further contract changes
- Plan 03 can bind to SessionInfo.DisplayName, ContextWindowData.Utilization, TokenSummary immediately

---
*Phase: 04-local-data-pipeline*
*Completed: 2026-03-11*
