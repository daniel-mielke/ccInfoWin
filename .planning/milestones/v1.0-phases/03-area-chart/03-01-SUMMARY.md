---
phase: 03-area-chart
plan: 01
subsystem: database
tags: [win2d, json-persistence, usage-history, csharp, xunit, di]

# Dependency graph
requires:
  - phase: 02-core-monitoring
    provides: UsageData models (UsageWindow, UsageResponse) and ClaudeApiService for fetching live usage
provides:
  - UsageHistory and UsageHistoryPoint models with JSON snake_case serialization
  - IUsageHistoryService contract (LoadHistory, SaveHistory, ClearHistory)
  - UsageHistoryService JSON persistence to %LOCALAPPDATA%\CCInfoWindows\usage-history.json
  - Win2D 1.3.2 NuGet package installed for chart rendering
  - ChartBackgroundBrush theme resource in both Dark/Light themes
  - DI registration of IUsageHistoryService as singleton
affects: [03-area-chart, 03-02]

# Tech tracking
tech-stack:
  added: [Microsoft.Graphics.Win2D 1.3.2]
  patterns: [directoryOverride constructor parameter for test isolation, try/catch defensive file I/O matching SettingsService pattern]

key-files:
  created:
    - CCInfoWindows/CCInfoWindows/Models/UsageHistory.cs
    - CCInfoWindows/CCInfoWindows/Services/Interfaces/IUsageHistoryService.cs
    - CCInfoWindows/CCInfoWindows/Services/UsageHistoryService.cs
    - CCInfoWindows.Tests/Services/UsageHistoryServiceTests.cs
  modified:
    - CCInfoWindows/CCInfoWindows/CCInfoWindows.csproj
    - CCInfoWindows/CCInfoWindows/App.xaml.cs
    - CCInfoWindows.Tests/Services/ClaudeApiServiceTests.cs

key-decisions:
  - "UsageHistoryService directoryOverride constructor param for test isolation (same pattern as cacheDirectory in ClaudeApiService)"
  - "ChartBackgroundBrush was already in AppTheme.xaml from a prior commit - no change needed"

patterns-established:
  - "Test isolation via constructor directoryOverride: inject temp path in tests, use default %LOCALAPPDATA% in production"
  - "Defensive file I/O: all Load/Save/Clear wrapped in try/catch, never crash app on I/O errors"

requirements-completed: [5HUR-07, 5HUR-08]

# Metrics
duration: 4min
completed: 2026-03-11
---

# Phase 3 Plan 1: Usage History Data Layer Summary

**UsageHistory JSON persistence layer with Win2D package, 6-test TDD coverage, and singleton DI wiring for the area chart foundation**

## Performance

- **Duration:** 4 min
- **Started:** 2026-03-11T09:17:25Z
- **Completed:** 2026-03-11T09:21:28Z
- **Tasks:** 3
- **Files modified:** 7

## Accomplishments
- UsageHistoryPoint and UsageHistory models with JSON snake_case attributes and properly typed fields (0.0-1.0 utilization range)
- UsageHistoryService mirrors SettingsService defensive I/O pattern: try/catch on all disk operations, directory auto-create on save
- 6 unit tests cover all edge cases: missing file, corrupt JSON, round-trip fidelity, clear/reload, 300-point bulk, directory creation
- Win2D 1.3.2 NuGet installed and IUsageHistoryService registered as DI singleton

## Task Commits

Each task was committed atomically:

1. **Task 1: Create UsageHistory models and IUsageHistoryService interface** - `0d5f954` (feat)
2. **Task 2: Implement UsageHistoryService with JSON persistence and unit tests** - `9260343` (feat)
3. **Task 3: Install Win2D NuGet, add chart theme resources, register DI** - `d7814f8` (feat)

## Files Created/Modified
- `CCInfoWindows/CCInfoWindows/Models/UsageHistory.cs` - UsageHistoryPoint (Timestamp, Utilization 0.0-1.0) and UsageHistory (ResetsAt, Points[])
- `CCInfoWindows/CCInfoWindows/Services/Interfaces/IUsageHistoryService.cs` - LoadHistory/SaveHistory/ClearHistory contract
- `CCInfoWindows/CCInfoWindows/Services/UsageHistoryService.cs` - JSON persistence to usage-history.json with directoryOverride for testing
- `CCInfoWindows.Tests/Services/UsageHistoryServiceTests.cs` - 6 xunit tests with IDisposable temp directory cleanup
- `CCInfoWindows/CCInfoWindows/CCInfoWindows.csproj` - Added Microsoft.Graphics.Win2D 1.3.2
- `CCInfoWindows/CCInfoWindows/App.xaml.cs` - Registered IUsageHistoryService singleton
- `CCInfoWindows.Tests/Services/ClaudeApiServiceTests.cs` - Fixed to use Mock<IWebViewBridge> (auto-fix for pre-existing bug)

## Decisions Made
- `directoryOverride` constructor parameter for test isolation matches the `cacheDirectory` parameter pattern already established in `ClaudeApiService`
- `ChartBackgroundBrush` was already present in `AppTheme.xaml` (Dark: #2C2C2E, Light: #EBEBF0) from prior work, no change needed

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Fixed ClaudeApiServiceTests to use Mock<IWebViewBridge>**
- **Found during:** Task 2 (running tests)
- **Issue:** ClaudeApiServiceTests still used the old HttpClient-based constructor. The Cloudflare fix (plan 01-03) changed ClaudeApiService to use IWebViewBridge, breaking the test project compilation and preventing any tests from running.
- **Fix:** Rewrote ClaudeApiServiceTests to mock IWebViewBridge.FetchJsonAsync() instead of intercepting HttpClient calls. Tests now verify same behaviors via the bridge abstraction.
- **Files modified:** CCInfoWindows.Tests/Services/ClaudeApiServiceTests.cs
- **Verification:** All 39 tests pass (33 existing + 6 new UsageHistoryServiceTests)
- **Committed in:** 9260343 (Task 2 commit)

---

**Total deviations:** 1 auto-fixed (Rule 1 bug fix)
**Impact on plan:** Required to unblock test execution. No scope change to planned work.

## Issues Encountered
None beyond the auto-fixed ClaudeApiServiceTests compilation error.

## Next Phase Readiness
- UsageHistory data layer complete: models, persistence, DI, tests all ready
- Win2D package installed - chart rendering (03-02) can proceed
- IUsageHistoryService injectable into MainViewModel for history accumulation during polling

---
*Phase: 03-area-chart*
*Completed: 2026-03-11*
