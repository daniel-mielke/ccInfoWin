---
phase: 13-sonnet-context-window-setting
plan: "02"
subsystem: ui
tags: [csharp, dotnet, winui3, mvvm, dependency-injection, messenger, context-window]

# Dependency graph
requires:
  - phase: 13-01
    provides: SonnetContextChangedMessage type, AppSettings.SonnetContextSize field, SettingsViewModel ComboBox integration
  - phase: 12
    provides: ModelContextLimits.GetMaxContextTokens with sonnetContextSize parameter
provides:
  - ISettingsService injected into JsonlService for runtime settings reads
  - JsonlService.GetContextWindow passes user-configured SonnetContextSize to GetMaxContextTokens
  - JsonlService.BuildSubagentContext passes sonnetContextSize via parameter (static method pattern)
  - MainViewModel.SonnetContextChangedMessage handler triggers UpdateSessionData on UI thread
  - End-to-end data flow: Settings ComboBox -> SonnetContextChangedMessage -> MainViewModel -> JsonlService -> ModelContextLimits
affects: [context-window-display, subagent-context-display, settings-integration]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Static method parameter passing: when instance field access is needed in a static helper, pass as parameter from non-static callers"
    - "WeakReferenceMessenger UI-thread pattern: wrap UpdateSessionData in _dispatcherQueue.TryEnqueue with null guard"

key-files:
  created: []
  modified:
    - CCInfoWindows/CCInfoWindows/Services/JsonlService.cs
    - CCInfoWindows/CCInfoWindows/App.xaml.cs
    - CCInfoWindows/CCInfoWindows/ViewModels/MainViewModel.cs

key-decisions:
  - "BuildSubagentContext is static — sonnetContextSize passed as parameter from non-static call sites rather than accessing instance field"
  - "settingsService parameter optional (default null) in JsonlService constructor to preserve test isolation (13+ existing tests construct JsonlService without settingsService)"

patterns-established:
  - "DI constructor extension: add optional parameter with null default so existing call sites compile unchanged"
  - "Messenger handler UI-thread guard: always TryEnqueue with null-check on SelectedSession before calling UpdateSessionData"

requirements-completed: [SET-03]

# Metrics
duration: 12min
completed: 2026-04-12
---

# Phase 13 Plan 02: Sonnet Context Window Setting — Backend Wiring Summary

**ISettingsService injected into JsonlService with SonnetContextSize passthrough to GetMaxContextTokens, completing the live context refresh loop via SonnetContextChangedMessage in MainViewModel**

## Performance

- **Duration:** 12 min
- **Started:** 2026-04-12T14:42:00Z
- **Completed:** 2026-04-12T14:54:00Z
- **Tasks:** 3
- **Files modified:** 3

## Accomplishments

- JsonlService reads SonnetContextSize from ISettingsService and passes it to GetMaxContextTokens in both GetContextWindow and BuildSubagentContext
- DI registration in App.xaml.cs wires ISettingsService into JsonlService at startup
- MainViewModel registers SonnetContextChangedMessage handler that enqueues UpdateSessionData on the UI thread — context bars refresh immediately on setting change

## Task Commits

Each task was committed atomically:

1. **Task 1: Inject ISettingsService into JsonlService and pass sonnetContextSize** - `973562d` (feat)
2. **Task 2: Register SonnetContextChangedMessage in MainViewModel** - `8c62552` (feat)
3. **Task 3: Verify full build and existing tests pass** - verified at `8c62552` (no new files)

## Files Created/Modified

- `CCInfoWindows/CCInfoWindows/Services/JsonlService.cs` - Added _settingsService field, optional constructor parameter, sonnetContextSize reads in GetContextWindow and BuildSubagentContext (via parameter)
- `CCInfoWindows/CCInfoWindows/App.xaml.cs` - Updated DI registration to pass ISettingsService to JsonlService
- `CCInfoWindows/CCInfoWindows/ViewModels/MainViewModel.cs` - Added SonnetContextChangedMessage registration in InitializeAsync

## Decisions Made

- **BuildSubagentContext is static**: Cannot access `_settingsService` directly. Solution: pass `sonnetContextSize` as a parameter from the two non-static call sites (GetContextWindow and GetSubagentContext), both of which already have access to `_settingsService`.
- **Optional settingsService parameter**: Default `null` preserves backward compatibility with all existing test constructors. Null falls back to `ModelContextLimits.DefaultContextLimit` (200K), matching previous behavior.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] BuildSubagentContext is static — cannot access instance field**

- **Found during:** Task 1 (build verification)
- **Issue:** Plan instructed adding `_settingsService?.LoadSettings().SonnetContextSize` inside `BuildSubagentContext`, but this method is `private static`. Compiler error CS0120.
- **Fix:** Changed `BuildSubagentContext` signature to accept `long sonnetContextSize` as a parameter. Both call sites (GetContextWindow and GetSubagentContext) compute `sonnetContextSize` via `_settingsService` and pass it through. No behavior change.
- **Files modified:** CCInfoWindows/CCInfoWindows/Services/JsonlService.cs
- **Verification:** `dotnet build` exits 0 errors after fix
- **Committed in:** `973562d` (Task 1 commit)

---

**Total deviations:** 1 auto-fixed (1 bug — static method field access)
**Impact on plan:** Necessary fix for compilation. Semantically equivalent to plan intent. No scope creep.

## Issues Encountered

None beyond the static method fix above.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- Phase 13 complete: full end-to-end Sonnet context setting implemented (Plan 01: UI + settings persistence, Plan 02: backend wiring + live refresh)
- SET-03 requirement fulfilled: changing Sonnet ComboBox in Settings immediately updates context window progress bars
- Ready for Phase 14 (session filtering — hide sessions for deleted project directories)

---
*Phase: 13-sonnet-context-window-setting*
*Completed: 2026-04-12*

## Self-Check: PASSED

- FOUND: CCInfoWindows/CCInfoWindows/Services/JsonlService.cs
- FOUND: CCInfoWindows/CCInfoWindows/App.xaml.cs
- FOUND: CCInfoWindows/CCInfoWindows/ViewModels/MainViewModel.cs
- FOUND commit: 973562d (Task 1)
- FOUND commit: 8c62552 (Task 2)
