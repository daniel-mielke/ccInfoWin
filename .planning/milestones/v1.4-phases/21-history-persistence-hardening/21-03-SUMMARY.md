---
phase: 21-history-persistence-hardening
plan: 03
subsystem: viewmodel-logout-routing
tags: [gap-closure, messenger, logout, single-source-of-truth, d13, uat-fix]
gap_closure: true
gap_source: 21-UAT.md Test 2
dependency_graph:
  requires: [IUsageHistoryService.ClearHistory (21-01), MainViewModel.Logout body (21-02 unchanged)]
  provides: [LogoutRequestedMessage, SettingsViewModel-publisher-only-logout, MainViewModel-IRecipient<LogoutRequestedMessage>]
  affects: [HIST-01 D-13 ordering on every logout path, UAT Test 2]
tech_stack:
  added: [LogoutRequestedMessage (parameterless WeakReferenceMessenger signal)]
  patterns: [publish-subscribe logout routing, WeakReferenceMessenger IRecipient<T>, xUnit [Collection] messenger isolation]
key_files:
  created:
    - CCInfoWindows/CCInfoWindows/Messages/LogoutRequestedMessage.cs
    - CCInfoWindows.Tests/ViewModels/SettingsLogoutMessageRoundtripTests.cs
    - CCInfoWindows.Tests/MessengerTestCollection.cs
  modified:
    - CCInfoWindows/CCInfoWindows/ViewModels/SettingsViewModel.cs
    - CCInfoWindows/CCInfoWindows/ViewModels/MainViewModel.cs
    - CCInfoWindows.Tests/ViewModels/MainViewModelAuthFlowTests.cs
decisions:
  - "Single source of truth for logout via LogoutRequestedMessage — SettingsViewModel publishes, MainViewModel owns the sequence"
  - "D-13 honored on every logout path: LogoutRequestedMessage routes to MainViewModel.Logout() which calls ClearHistory() first"
  - "Messenger isolation via [Collection(WeakReferenceMessenger)] prevents cross-test contamination in parallel xUnit runs"
  - "Times.AtLeastOnce used for NavigateTo<LoginView> assertions: Logout() fires AuthStateChangedMessage(false) which triggers Receive, AND calls NavigateTo directly — double-navigate is the established contract (verified by Logout_ResetsAutoReauthFlag_NextFalseNavigatesAgain)"
metrics:
  duration: "~40 minutes (including rebase of 21-01/02 commits + messenger isolation debug)"
  completed_date: "2026-05-07"
  tasks_completed: 3
  files_modified: 6
---

# Phase 21 Plan 03: Settings Logout Gap Closure Summary

Closes UAT Test 2 gap: Settings → Abmelden button now runs the full logout sequence including D-13's `IUsageHistoryService.ClearHistory()` via a parameterless `LogoutRequestedMessage` round-trip. Single source of truth enforced — SettingsViewModel publishes, MainViewModel owns the sequence.

## What Was Built

### Task 1: LogoutRequestedMessage signal (commit 89965e3)

New file `CCInfoWindows/CCInfoWindows/Messages/LogoutRequestedMessage.cs`:
- Parameterless class in `CCInfoWindows.Messages` namespace
- No `ValueChangedMessage<T>` inheritance — pure signal, no payload
- XML doc documents the single-source-of-truth pattern and D-13 ordering contract

### Task 2: ViewModel refactoring (commit b6a0eb9)

**SettingsViewModel.cs** — `Logout()` body replaced:
- Before: `_credentialService.ClearCredentials()` + `AuthStateChangedMessage(false)` + `NavigateTo<LoginView>()` (missing ClearHistory — UAT gap)
- After: `WeakReferenceMessenger.Default.Send(new LogoutRequestedMessage())` — publisher only
- `_credentialService`, `_navigationService` fields kept — used by `IsTokenValid` and `GoBack`

**MainViewModel.cs** — three changes:
- Class declaration: added `IRecipient<LogoutRequestedMessage>` (third interface, after SessionTimeoutChangedMessage)
- Constructor: added `WeakReferenceMessenger.Default.Register<LogoutRequestedMessage>(this)` after existing registrations
- New `Receive(LogoutRequestedMessage)` handler: invokes `Logout()` — the existing body with D-13 ordering is unchanged

### Task 3: Round-trip xUnit tests (commit 5008f5c)

**SettingsLogoutMessageRoundtripTests.cs** — 2 new `[Fact]` tests:
- `SettingsLogout_PublishesMessage_TriggersHistoryClearOnMainViewModel`: D-13 smoking gun — verifies `historyMock.Verify(h => h.ClearHistory(), Times.Once)` plus `ClearCredentials Times.Once`
- `SettingsLogout_DoesNotInvokeNavigationDirectly_OnlyViaMainViewModelRoundTrip`: publisher-only invariant — `settingsNavMock.Verify(NavigateTo<LoginView>(), Times.Never)`, `mainNavMock Times.AtLeastOnce`

**MessengerTestCollection.cs** — new xUnit `[CollectionDefinition("WeakReferenceMessenger")]` with `ICollectionFixture<MessengerTestFixture>` that resets the messenger in Dispose.

**MainViewModelAuthFlowTests.cs** — added `[Collection("WeakReferenceMessenger")]` to enforce sequential execution with `SettingsLogoutMessageRoundtripTests` and prevent cross-test messenger contamination.

## Acceptance Criteria Results

### Task 2 Grep Gates

| Check | Expected | Result |
|-------|----------|--------|
| `SettingsViewModel: Send(new LogoutRequestedMessage())` count | 1 | PASS (1) |
| `SettingsViewModel: _credentialService.ClearCredentials()` count | 0 | PASS (0) |
| `SettingsViewModel: new AuthStateChangedMessage` count | 0 | PASS (0) |
| `SettingsViewModel: _navigationService.NavigateTo<LoginView>()` (non-comment lines) | 0 | PASS (0) |
| `MainViewModel: IRecipient<LogoutRequestedMessage>` count | 1 | PASS (1) |
| `MainViewModel: Register<LogoutRequestedMessage>(this)` count | 1 | PASS (1) |
| `MainViewModel: Receive(LogoutRequestedMessage) contains Logout()` | present | PASS |
| `MainViewModel: _historyService.ClearHistory()` count | 2 | PASS (2: Logout body + InitializeAsync stale-clear) |
| Build: 0 errors | 0 | PASS |

### Verification Gates

| Gate | Expected | Result |
|------|----------|--------|
| `dotnet build CCInfoWindows.csproj` | 0 errors | PASS |
| `dotnet test --filter SettingsLogoutMessageRoundtripTests` | 2/2 passed | PASS |
| SettingsViewModel no direct credential/auth/nav calls (duplication-eliminated gate) | 0 hits | PASS |
| `MainViewModel: _historyService.ClearHistory()` count | 2 | PASS |

### Test Results

- **SettingsLogoutMessageRoundtripTests:** 2/2 PASS (new)
- **MainViewModelAuthFlowTests:** 4/4 PASS (no regression)
- **UsageHistoryServiceTests:** 15/15 PASS (no regression)
- **Full suite:** 281 passed, 2 failed (pre-existing ClaudeApiServiceTests failures — documented in 21-02-SUMMARY, unrelated to this plan)

## UAT Test 2 Status

Manual re-verification required post-merge. Expected to flip from `result: issue` → `result: pass`.

Smoke procedure: Sign in → wait for poll → navigate Settings → click Abmelden → `Test-Path "$env:LOCALAPPDATA\CCInfoWindows\usage-history.json"` must return `False`.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] Phase 21-01/02 commits missing from Worktree**
- **Found during:** Task 1 build setup
- **Issue:** Worktree branch `worktree-agent-a769b344dccb72ba7` was based on `5f05a5a` (pre-Phase-21); Phase-21-01/02 commits (`23b0e44..ea4dfbf`) were committed on a sibling agent branch, not merged to this worktree
- **Fix:** `git rebase 5fb782f` to pick up all Phase-21 commits plus the full commit history through Phase 23
- **Files modified:** None (clean rebase, no conflicts)

**2. [Rule 1 - Bug] WeakReferenceMessenger cross-test contamination**
- **Found during:** Task 3 test run (combined AuthFlow + SettingsLogoutMessageRoundtrip)
- **Issue:** xUnit runs test classes in parallel by default. `WeakReferenceMessenger.Default` is process-global. When `SettingsLogoutMessageRoundtripTests` ran concurrently with `MainViewModelAuthFlowTests`, `Reset()` calls disrupted live message registrations in the sibling class, causing `Receive_SecondFalse_OpensInfoBar_WithoutSecondNavigation` to fail with "NavigateTo called 2 times instead of 1"
- **Fix:** Introduced `[CollectionDefinition("WeakReferenceMessenger")]` with `MessengerTestFixture` (ICollectionFixture). Added `[Collection("WeakReferenceMessenger")]` to both `SettingsLogoutMessageRoundtripTests` and `MainViewModelAuthFlowTests` to enforce sequential execution
- **Files modified:** `CCInfoWindows.Tests/MessengerTestCollection.cs` (new), `CCInfoWindows.Tests/ViewModels/MainViewModelAuthFlowTests.cs` (`[Collection]` added)

**3. [Rule 2 - Missing Critical] NavigateTo assertion relaxed to Times.AtLeastOnce**
- **Found during:** Task 3 test authoring
- **Issue:** The existing `Logout()` body sends `AuthStateChangedMessage(false)` AND calls `NavigateTo<LoginView>()` directly. `Receive(AuthStateChangedMessage false)` ALSO calls `NavigateTo<LoginView>()` on first call. This double-navigate is the established contract — proven by the existing `Logout_ResetsAutoReauthFlag_NextFalseNavigatesAgain` test which expects 3 navigations (Receive + Logout + Receive). The plan's instruction to assert `Times.Once` is therefore incorrect given the actual contract
- **Fix:** Changed `mainNavMock.Verify(n => n.NavigateTo<LoginView>(), Times.Once)` to `Times.AtLeastOnce` in both test methods. The critical D-13 assertion (`ClearHistory Times.Once`) and publisher-invariant assertion (`settingsNavMock Times.Never`) are unchanged and at full strength

## Threat Flags

None — no new network endpoints, auth paths, file access patterns, or schema changes at trust boundaries introduced.

T-21-03-01 (Information Disclosure: usage-history.json leaks across sessions): **Mitigated** — ClearHistory() now runs on the Settings logout path via the round-trip.
T-21-03-02 (Tampering: future drift via direct cleanup duplication): **Mitigated** — documented pattern + round-trip test acts as regression gate.
T-21-03-03 (DoS: re-entrant message loop): **Accepted** — verified by code reading, no guard added per CONTEXT.md.

## Self-Check: PASSED

Files exist:
- CCInfoWindows/CCInfoWindows/Messages/LogoutRequestedMessage.cs: FOUND
- CCInfoWindows/CCInfoWindows/ViewModels/SettingsViewModel.cs: FOUND
- CCInfoWindows/CCInfoWindows/ViewModels/MainViewModel.cs: FOUND
- CCInfoWindows.Tests/ViewModels/SettingsLogoutMessageRoundtripTests.cs: FOUND
- CCInfoWindows.Tests/MessengerTestCollection.cs: FOUND

Commits exist:
- 89965e3 (Task 1: LogoutRequestedMessage): FOUND
- b6a0eb9 (Task 2: ViewModel refactoring): FOUND
- 5008f5c (Task 3: round-trip tests): FOUND
