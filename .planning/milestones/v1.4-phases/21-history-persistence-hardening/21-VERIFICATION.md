---
phase: 21-history-persistence-hardening
verified: 2026-05-06T19:15:00Z
status: human_needed
score: 4/5
overrides_applied: 0
human_verification:
  - test: "HIST-01 manual smoke: launch app, wait for poll, close via X-button (NOT taskkill), verify usage-history.json point count >= pre-close count, restart and confirm chart shows persisted points"
    expected: "Post-close point count >= pre-close count; chart on restart matches; logout-then-close does NOT recreate usage-history.json (D-13 regression)"
    why_human: "AppWindow.Closing cannot be triggered from headless xUnit. The hook is a native WinUI 3 event that only fires when the actual window is closed via the OS title-bar button. No test host simulates this."
---

# Phase 21: History Persistence Hardening — Verification Report

**Phase Goal:** Usage history survives unexpected app termination, poll-cycle saves no longer block the UI thread, and 5-hour window resets clear the chart cleanly without a vertical cliff.
**Verified:** 2026-05-06T19:15:00Z
**Status:** human_needed
**Re-verification:** No — initial verification

---

## Goal Achievement

### Observable Truths

| # | Truth | Status | Evidence |
|---|-------|--------|----------|
| 1 | Closing MainWindow via X persists history points before process exit | ? UNCERTAIN (human) | Code path verified: `OnClosing` calls `PeekLastSnapshot()` → `SaveHistory(snapshot)` synchronously. Cannot headlessly trigger `AppWindow.Closing`. |
| 2 | Poll cycle uses `SaveHistoryAsync` (File.WriteAllTextAsync) with no UI-thread block | ✓ VERIFIED | `AppendHistoryPointAsync` line 554: `await _historyService.SaveHistoryAsync(history)`. Full await-chain confirmed: `PollUsageAsync` → `await UpdateUsagePropertiesAsync` → `await AppendHistoryPointAsync` → `await _historyService.SaveHistoryAsync`. |
| 3 | `IUsageHistoryService` exposes both sync `SaveHistory` and async `SaveHistoryAsync`; both produce byte-identical JSON | ✓ VERIFIED | Interface: 5 members confirmed. `SaveHistoryAsync` at line 15, `PeekLastSnapshot` at line 18. `SaveSync_VS_SaveAsync_ProducesByteIdenticalJson` test passes (15/15 green). Both methods use same static `JsonOptions` instance. |
| 4 | When API returns new `ResetsAt` > previous, `Points` cleared and persisted immediately — no vertical cliff | ✓ VERIFIED | `AppendHistoryPointAsync` lines 534-539: `IsWindowReset(history.ResetsAt, apiResetsAt)` → `history = new UsageHistory()` → `await _historyService.SaveHistoryAsync(history)`. `WindowReset_ClearsPointsAndPersists` test passes. |
| 5 | First poll after app start does not erase history (null-previous-`ResetsAt` guard) | ✓ VERIFIED | `IsWindowReset` line 566: `if (!storedResetsAt.HasValue \|\| !apiResetsAt.HasValue) return false;`. Promoted to `internal static`. `FirstPoll_AfterAppStart_DoesNotEraseHistory` test passes (asserts false for all null combinations). |

**Score:** 4/5 truths automated-verified. Truth 1 requires human smoke test.

---

## Required Artifacts

| Artifact | Expected | Status | Details |
|----------|----------|--------|---------|
| `CCInfoWindows/CCInfoWindows/Services/Interfaces/IUsageHistoryService.cs` | 5-member interface with `SaveHistoryAsync` + `PeekLastSnapshot` | ✓ VERIFIED | 5 members confirmed. `Task SaveHistoryAsync(UsageHistory history)` at line 15; `UsageHistory? PeekLastSnapshot()` at line 18. |
| `CCInfoWindows/CCInfoWindows/Services/UsageHistoryService.cs` | `SemaphoreSlim _writeLock`, `_lastSavedSnapshot`, async save, snapshot accessor | ✓ VERIFIED | Line 26: `private readonly SemaphoreSlim _writeLock = new(1, 1)`. Line 29: `private UsageHistory? _lastSavedSnapshot`. `SaveHistoryAsync` lines 81-102. `PeekLastSnapshot()` line 126. |
| `CCInfoWindows/CCInfoWindows/ViewModels/MainViewModel.cs` | Async cascade + `internal static IsWindowReset` | ✓ VERIFIED | `AppendHistoryPointAsync` (private async Task) at line 530. `UpdateUsagePropertiesAsync` (private async Task) at line 443. `IsWindowReset` promoted to `internal static` at line 564. Both call sites await: lines 370 (InitializeAsync cache path) and 416 (PollUsageAsync). |
| `CCInfoWindows/CCInfoWindows/MainWindow.xaml.cs` | `_historyService` field + constructor injection + `OnClosing` snapshot flush | ✓ VERIFIED | Line 30: `private readonly IUsageHistoryService _historyService`. Line 38: `GetRequiredService<IUsageHistoryService>()`. Lines 121-125: `PeekLastSnapshot()` + null-guard + `SaveHistory(snapshot)`. Handler remains `private void` (no async void). |
| `CCInfoWindows.Tests/Services/UsageHistoryServiceTests.cs` | 15 tests (6 existing + 9 new) | ✓ VERIFIED | 15 `[Fact]` methods confirmed. All 15 pass (0 failures). All 9 new test names from Plan 21-01 Task 3 present. |

---

## Key Link Verification

| From | To | Via | Status | Details |
|------|----|-----|--------|---------|
| `MainViewModel.PollUsageAsync` | `UpdateUsagePropertiesAsync` | `await` at line 416 | ✓ WIRED | Confirmed in source |
| `MainViewModel.UpdateUsagePropertiesAsync` | `AppendHistoryPointAsync` | `await` at line 454 | ✓ WIRED | Confirmed in source |
| `MainViewModel.AppendHistoryPointAsync` | `_historyService.SaveHistoryAsync` | `await` at line 554 | ✓ WIRED | Confirmed in source |
| `MainWindow.OnClosing` | `_historyService.SaveHistory(snapshot)` | sync call after null-check, line 124 | ✓ WIRED | Confirmed in source; no async, no `.Wait()` |
| `UsageHistoryService.SaveHistory` / `SaveHistoryAsync` | `_lastSavedSnapshot` | post-write assignment, lines 68 / 91 | ✓ WIRED | Both assign AFTER `File.WriteAll[Text/Async]` succeeds |
| `UsageHistoryService.ClearHistory` | `_lastSavedSnapshot = null` | line 111, BEFORE `File.Delete` line 112 | ✓ WIRED | D-13 ordering confirmed |
| `InternalsVisibleTo` | `CCInfoWindows.Tests` | `.csproj` lines 46-48 `AssemblyAttribute` | ✓ WIRED | Enables `MainViewModel.IsWindowReset` access in tests |

---

## Data-Flow Trace (Level 4)

| Artifact | Data Variable | Source | Produces Real Data | Status |
|----------|--------------|--------|--------------------|--------|
| `UsageHistoryService.SaveHistory` | `history` | Caller passes in-memory `UsageHistory` object | Yes — serialized to disk via `File.WriteAllText` | ✓ FLOWING |
| `UsageHistoryService.SaveHistoryAsync` | `history` | Caller passes same in-memory object | Yes — serialized via `File.WriteAllTextAsync` + shared `JsonOptions` | ✓ FLOWING |
| `MainWindow.OnClosing` snapshot flush | `snapshot` | `_historyService.PeekLastSnapshot()` returns `_lastSavedSnapshot` — set by last successful poll write | Real data from last poll cycle | ✓ FLOWING |

---

## Behavioral Spot-Checks

| Behavior | Command | Result | Status |
|----------|---------|--------|--------|
| Build: 0 errors | `dotnet build CCInfoWindows.csproj` | 0 errors, 0 warnings | ✓ PASS |
| UsageHistoryService tests: 15/15 | `dotnet test --filter UsageHistoryServiceTests` | 15 passed, 0 failed | ✓ PASS |
| Full test suite (Phase 21 scope) | `dotnet test CCInfoWindows.Tests.csproj` | 260 passed, 2 failed (pre-existing `ClaudeApiServiceTests` failures unrelated to Phase 21) | ✓ PASS (Phase 21 tests unaffected) |
| No sync `SaveHistory` in ViewModel poll path | grep `_historyService\.SaveHistory\b` in MainViewModel.cs | 0 matches (only `ClearHistory` calls remain) | ✓ PASS |
| No remaining old method names | grep `\bUpdateUsageProperties\b\(` / `\bAppendHistoryPoint\b\(` | 0 matches each | ✓ PASS |
| No async void OnClosing | grep `async void OnClosing` in MainWindow.xaml.cs | 0 matches | ✓ PASS |
| No sync-over-async in OnClosing | grep `\.Wait()\|\.GetAwaiter()` in MainWindow.xaml.cs | 0 matches | ✓ PASS |
| HIST-01 X-button close | Run app → close via X → verify file | Requires live WinUI 3 process | ? SKIP (human required) |

---

## Requirements Coverage

| Requirement | Source Plan | Description | Status | Evidence |
|-------------|------------|-------------|--------|----------|
| HIST-01 | 21-02 | Termination flush on AppWindow.Closing | ? HUMAN | Code path exists and verified; cannot headlessly trigger AppWindow.Closing |
| HIST-02 | 21-01 | Async poll-cycle saves via `File.WriteAllTextAsync` | ✓ SATISFIED | `SaveHistoryAsync` uses `await File.WriteAllTextAsync`; full await-chain in VM confirmed |
| HIST-03 | 21-01 | Sync + async variants on interface; byte-identical output | ✓ SATISFIED | Interface has both methods; `SaveSync_VS_SaveAsync_ProducesByteIdenticalJson` passes |
| HIST-04 | 21-01 | Window-reset clears chart cleanly (no vertical cliff) | ✓ SATISFIED | `IsWindowReset` → `history = new UsageHistory()` → `await SaveHistoryAsync`; `WindowReset_ClearsPointsAndPersists` passes |
| HIST-05 | 21-01 | First-poll guard (null-previous-ResetsAt returns false) | ✓ SATISFIED | `IsWindowReset` line 566 returns false when either arg is null; `FirstPoll_AfterAppStart_DoesNotEraseHistory` passes |

---

## Anti-Patterns Found

| File | Line | Pattern | Severity | Impact |
|------|------|---------|----------|--------|
| `UsageHistoryService.cs` | 17-20 | `new()` instead of `new JsonSerializerOptions` | ℹ️ Info | Target-typed new — NOT a violation. One shared static instance confirmed. D-07 satisfied. |

No blocking anti-patterns found. No `lock` keyword, no per-call `JsonSerializerOptions`, no `_lastSavedSnapshot` update before write, no async void in closing handler, no `.Wait()` / `.GetAwaiter().GetResult()`.

---

## Human Verification Required

### 1. HIST-01 Termination Flush Smoke Test

**Test:** Build the app in debug mode, launch it, wait for at least one successful poll (chart shows a data point), note the `Points.Count` and last-point timestamp in `%LOCALAPPDATA%\CCInfoWindows\usage-history.json`. Then close the window via the X-button (do NOT use `taskkill /F` — that bypasses `AppWindow.Closing`). Read the file again and confirm count >= pre-close count. Restart the app and confirm the chart shows the persisted points.

**Expected:**
- Post-close `Points.Count` >= pre-close count (HIST-01 PASS)
- Chart on restart shows the same points that were on disk before restart (HIST-01 PASS)
- D-13 regression: click Logout in Settings → confirm `usage-history.json` is deleted → close via X → confirm file is NOT recreated

**Why human:** `AppWindow.Closing` is a native WinUI 3 event that only fires when the OS window frame X-button is clicked. There is no headless xUnit test host that can host a WinUI 3 `AppWindow` and simulate this event. The VALIDATION.md plan explicitly categorizes HIST-01 as manual-only.

**Execution guidance using windows-mcp:**
1. `dotnet run --project CCInfoWindows/CCInfoWindows/CCInfoWindows.csproj` (background)
2. Wait for window to appear and first poll
3. Read `$env:LOCALAPPDATA\CCInfoWindows\usage-history.json` — record point count
4. `window_management(action='find', title='ccInfoWin')` → get handle
5. `window_management(action='close', windowHandle=<handle>)` — X-button equivalent
6. Read file again — confirm count >= step 3 count
7. Restart app — confirm chart shows same history

---

## Pre-Existing Test Failures (Out of Phase 21 Scope)

The full test suite shows 2 failures in `ClaudeApiServiceTests`:
- `FetchUsageAsync_OnTransientNullResponse_RetriesAndSucceeds`
- `FetchUsageAsync_OnPersistentNullResponse_ThrowsAfterRetries`

Per Plan 21-02 SUMMARY: these tests were written against behavior changed in Phase 20 (`ClaudeApiService` no longer throws `ArgumentNullException` or retries on null response). They pre-date Phase 21 and are not caused by any Phase 21 change. Phase 21's 15 `UsageHistoryServiceTests` and 4 `AuthFlowTests` are all green.

---

## Gaps Summary

No implementation gaps found. All 5 success criteria have working code in place. The only outstanding item is HIST-01 manual smoke — this is by design (documented in VALIDATION.md as the sole manual-verification item for this phase) and does not indicate missing implementation.

The code for HIST-01 is fully implemented and correctly wired:
- `_historyService` injected in `MainWindow` constructor via `GetRequiredService<IUsageHistoryService>()`
- `OnClosing` calls `PeekLastSnapshot()` → null-guard → `SaveHistory(snapshot)` synchronously
- `PeekLastSnapshot()` returns `_lastSavedSnapshot`, which is populated by every successful poll via `SaveHistoryAsync`

Status is `human_needed` (not `gaps_found`) because the implementation is complete; only live execution can confirm the `AppWindow.Closing` event fires and the flush actually reaches disk before process teardown.

---

_Verified: 2026-05-06T19:15:00Z_
_Verifier: Claude (gsd-verifier)_
