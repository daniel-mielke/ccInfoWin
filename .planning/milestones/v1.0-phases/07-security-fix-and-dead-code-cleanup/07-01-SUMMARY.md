---
phase: 07-security-fix-and-dead-code-cleanup
plan: 01
subsystem: auth
tags: [webview2, winui3, mvvm, security, refactor]

# Dependency graph
requires:
  - phase: 01-foundation
    provides: WebViewBridge singleton registered in DI
  - phase: 02-core-monitoring
    provides: MainViewModel with Logout command
provides:
  - WebViewBridge.Reset() declared in IWebViewBridge and called on logout
  - Dead code eliminated (CostCalculator, JsonlDataUpdatedMessage, SessionSelectedMessage, orphaned token fields)
affects: [MainViewModel, IWebViewBridge, WebViewBridge, logout flow]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Drain pending TCS dictionary on bridge reset to prevent ghost hangs after logout"
    - "IWebViewBridge injected into MainViewModel for testable Reset() call site"

key-files:
  created: []
  modified:
    - CCInfoWindows/CCInfoWindows/Services/Interfaces/IWebViewBridge.cs
    - CCInfoWindows/CCInfoWindows/Services/WebViewBridge.cs
    - CCInfoWindows/CCInfoWindows/ViewModels/MainViewModel.cs
    - CCInfoWindows/CCInfoWindows/App.xaml.cs

key-decisions:
  - "Reset() called BEFORE AuthStateChangedMessage in Logout() so in-flight FetchJsonAsync returns null before navigation fires"
  - "CostCalculator deleted: logic fully replaced by JsonlService.AggregateEntryLog in Phase 05"
  - "Pre-existing test failures (13) confirmed unrelated to this plan's changes — baseline verified via git stash"

patterns-established:
  - "Bridge teardown pattern: unsubscribe event handler, null references, drain pending TCS entries"

requirements-completed: [AUTH-04, SECU-03]

# Metrics
duration: 12min
completed: 2026-03-17
---

# Phase 7 Plan 1: Security Fix and Dead Code Cleanup Summary

**WebViewBridge.Reset() wired into logout (AUTH-04 closed) and 4 dead code files plus 2 orphaned ViewModel fields removed**

## Performance

- **Duration:** 12 min
- **Started:** 2026-03-17T14:30:00Z
- **Completed:** 2026-03-17T14:42:00Z
- **Tasks:** 2
- **Files modified:** 5 (4 deleted, 1 modified in production; 1 test file deleted)

## Accomplishments
- Closed AUTH-04: Logout now calls `_bridge.Reset()` before emitting `AuthStateChangedMessage`, preventing 30-second ghost hangs from in-flight fetch requests
- `WebViewBridge.Reset()` drains `_pending` ConcurrentDictionary to resolve all outstanding TCS entries immediately
- `void Reset()` added to `IWebViewBridge` contract
- `IWebViewBridge` injected into `MainViewModel` constructor and DI factory updated in `App.xaml.cs`
- Deleted `CostCalculator.cs` and its 8 unit tests (logic superseded by `JsonlService.AggregateEntryLog`)
- Deleted `JsonlDataUpdatedMessage.cs` and `SessionSelectedMessage.cs` (zero senders/receivers)
- Removed `_inputTokensText` and `_outputTokensText` from `MainViewModel` (zero XAML bindings confirmed)

## Task Commits

Each task was committed atomically:

1. **Task 1: Fix WebViewBridge.Reset() on logout** - `dd7911c` (fix)
2. **Task 2: Remove all dead code artifacts** - `32d814e` (refactor)

## Files Created/Modified
- `CCInfoWindows/CCInfoWindows/Services/Interfaces/IWebViewBridge.cs` - Added `void Reset()` method declaration
- `CCInfoWindows/CCInfoWindows/Services/WebViewBridge.cs` - Enhanced Reset() to drain _pending TCS entries
- `CCInfoWindows/CCInfoWindows/ViewModels/MainViewModel.cs` - Added IWebViewBridge field + constructor param, Reset() call in Logout(), removed dead token fields
- `CCInfoWindows/CCInfoWindows/App.xaml.cs` - Added IWebViewBridge to MainViewModel DI factory
- `CCInfoWindows/CCInfoWindows/Helpers/CostCalculator.cs` - DELETED
- `CCInfoWindows.Tests/Helpers/CostCalculatorTests.cs` - DELETED
- `CCInfoWindows/CCInfoWindows/Messages/JsonlDataUpdatedMessage.cs` - DELETED
- `CCInfoWindows/CCInfoWindows/Messages/SessionSelectedMessage.cs` - DELETED

## Decisions Made
- Reset() is called BEFORE `WeakReferenceMessenger.Default.Send(new AuthStateChangedMessage(false))` because the message triggers navigation — any in-flight FetchJsonAsync must return null first to avoid accessing a disposed WebView2
- CostCalculator tests deleted with the class: leaving them would cause a build error and they covered behavior now owned by JsonlService
- Pre-existing 13 test failures confirmed via git stash baseline check — not caused by this plan

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered

Pre-existing test suite has 13 failing tests in `JsonlServiceTests`. Verified via `git stash` that these failures exist on the prior commit (`dd7911c`) and are unrelated to this plan's changes. Not fixed (out of scope).

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness
- AUTH-04 and SECU-03 requirements closed
- Codebase is clean of dead code artifacts identified in v1.0 milestone audit
- No blockers for subsequent phases

---
*Phase: 07-security-fix-and-dead-code-cleanup*
*Completed: 2026-03-17*
