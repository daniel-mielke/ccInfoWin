# Phase 27 — Deferred Items

## Out-of-Scope Pre-existing Test Failures

Confirmed pre-existing via git stash comparison during Plan 27-04 execution.
These failures exist independently of Plan 27-04 changes and are NOT regressions.

### 1. BurnRateCalculatorTests.Predict_FlatUsage_ReturnsNull
- **File:** `CCInfoWindows.Tests/Services/BurnRateCalculatorTests.cs`
- **Root cause:** Unknown — pre-existing before Phase 27
- **Scheduled:** Phase 28 CLEANUP investigation

### 2. ClaudeApiServiceTests.FetchUsageAsync_OnPersistentNullResponse_ThrowsAfterRetries
- **File:** `CCInfoWindows.Tests/Services/ClaudeApiServiceTests.cs`
- **Root cause:** Parameter naming mismatch or null-response semantics change from TryMigrateOrgIdAsync refactor (Plan 27-04 Task 1)
- **Impact:** Test only — production behavior unaffected
- **Scheduled:** Phase 28 CLEANUP — verify if 27-04 refactor changed null-response semantics

### 3. ClaudeApiServiceTests.FetchUsageAsync_OnTransientNullResponse_RetriesAndSucceeds
- **File:** `CCInfoWindows.Tests/Services/ClaudeApiServiceTests.cs`
- **Root cause:** Same as item 2 above
- **Impact:** Test only — production behavior unaffected
- **Scheduled:** Phase 28 CLEANUP

## Visual Smoke Test Deferred

Plan 27-04 Task 4 was a `checkpoint:human-verify` for visual smoke of the OrgPicker dialog and
OrgMismatch InfoBar. Per user directive ("nie pausieren bei human_needed"), this checkpoint was
skipped and documented here instead.

**What to verify manually:**
1. Settings > Account tab shows "Re-detect organization" button
2. Clicking the button opens a ContentDialog with org list (Name bold, Uuid small gray)
3. Selecting an org and clicking "Switch" persists the new org-id and triggers logout
4. Cancel closes dialog with no change
5. After 5 zero-utilization polls with an active session, OrgMismatch InfoBar appears in MainView
6. "Go to Settings" button navigates to Settings and opens OrgPicker
7. "Do not show again" checkbox suppresses the InfoBar for the session (in-memory only — resets on restart)
