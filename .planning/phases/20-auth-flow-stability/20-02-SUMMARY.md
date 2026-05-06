---
phase: 20-auth-flow-stability
plan: 02
status: complete
completed: 2026-05-06
requirements: [AUTH-01, AUTH-02, AUTH-03, AUTH-04]
decisions: [D-01, D-02, D-03]
files_changed: 1
---

# Plan 20-02 Summary — 401-Routing State Machine

## What was built

Implemented the auto-reauth state machine inside `MainViewModel` per CONTEXT D-01, D-02, D-03. Single private bool field plus an extended `Receive(AuthStateChangedMessage)` handler with three reset sites — no new types, no new DI, no XAML changes.

## Files modified

- `CCInfoWindows/CCInfoWindows/ViewModels/MainViewModel.cs` (+33, -3)

## Implementation Detail

### `_autoReauthAttempted` field (D-01)
Private bool, declared near the other state flags around line 245. Constructor default is `false` (implicit C# initialization; satisfies the constructor-default reset site without an explicit `= false;` line).

### Reset sites (D-02 — 4 total)
1. **Constructor default** (implicit `= false;` on field declaration)
2. **`PollUsageAsync` HTTP 200 success path** — immediately after `UpdateUsageProperties(result);` inside the `if (result != null)` branch
3. **`Logout` command** — immediately before `_navigationService.NavigateTo<LoginView>();`
4. **`Receive(AuthStateChangedMessage(true))` post-login refresh path**

### Extended `Receive(AuthStateChangedMessage)` handler (D-01 + D-03)

Three branches, ordered for clarity:

1. **`message.Value == true` (post-login refresh)** — clears `IsSessionExpired`/`HasApiError`, resets `_autoReauthAttempted`, calls `RefreshCommand.ExecuteAsync(null)` fire-and-forget. Note: generated symbol is `RefreshCommand` (from `[RelayCommand] private async Task Refresh()`), NOT `RefreshUsageCommand` which appeared in CONTEXT D-03 prose — naming-drift correction.

2. **First 401 (`!_autoReauthAttempted`)** — sets the flag to `true`, navigates to `LoginView` via `_navigationService.NavigateTo<LoginView>()`, does NOT set `IsSessionExpired`. The InfoBar stays closed.

3. **Second 401 (and beyond) — fall-through** — sets `IsSessionExpired = true` + `StatusMessage`, opens the existing InfoBar fallback path (AUTH-02).

### Stacked-401 edge case (deferred per CONTEXT)

`ClaudeApiService` has TWO send sites for `AuthStateChangedMessage(false)` — `FetchUsageAsync` and `TryMigrateOrgIdAsync`. If both fire in a single poll cycle, the second one trips the InfoBar fallback even on the first poll. Documented as inline comment in the Receive body; explicitly accepted (not blocking) per CONTEXT directive — `Receive(true)` post-login clears `IsSessionExpired` so a stale flag resolves at the next successful login.

## Verification

### Build
`dotnet build CCInfoWindows/CCInfoWindows/CCInfoWindows.csproj` — 0 errors, 67 pre-existing MVVMTK warnings (unchanged from prior baseline).

### Tests (Wave 0 RED → Wave 2 GREEN)
`dotnet test CCInfoWindows.Tests/CCInfoWindows.Tests.csproj --filter "FullyQualifiedName~MainViewModelAuthFlow"` — **4/4 passing** in 81ms.

| Test | Verifies | Status |
|------|----------|--------|
| `Receive_FirstFalse_NavigatesToLoginView_WithoutSettingSessionExpired` | AUTH-01 (first 401 → LoginView) | GREEN |
| `Receive_SecondFalse_OpensInfoBar_WithoutSecondNavigation` | AUTH-02 (second 401 → InfoBar fallback) | GREEN |
| `Receive_True_ClearsFlagsAndResetsAutoReauth` | AUTH-03, AUTH-04 (post-login clears state, refresh fires) | GREEN |
| `Logout_ResetsAutoReauthFlag_NextFalseNavigatesAgain` | D-02 reset on Logout | GREEN |

### Static check
`grep -c "_autoReauthAttempted" CCInfoWindows/CCInfoWindows/ViewModels/MainViewModel.cs` returns 7 (1 declaration + 6 references including 3 explicit `= false;` resets and 3 reads/writes) — exceeds the `>= 5` acceptance threshold.

## Commits

- `8e1c402` — `feat(20-02): implement 401-routing state machine in MainViewModel`
- `2b70018` — `test(20-02): fix Receive_True race condition with successful API mock`

## Deviations from plan

**Test race-condition fix (post-execution):**
The Wave 0 test `Receive_True_ClearsFlagsAndResetsAutoReauth` failed initially because `RefreshCommand.ExecuteAsync(null)` (fire-and-forget) hits the default Moq `FetchUsageAsync()` returning null on the same sync stack, which trips the empty-data branch in `PollUsageAsync` and re-flips `HasApiError = true`. The implementation is correct; the test required a `CreateViewModelWithSuccessfulApi()` helper that returns a non-null `UsageResponse` so the refresh succeeds silently. Documented in commit message — pure test-side fix, no production code change.

**Recovery deviation (orchestrator-side):**
Both Wave 2 subagents hit the Anthropic API limit before completing their final commits + SUMMARY.md. The orchestrator manually rescued the work from the worktrees: cherry-picked Plan 20-03's three code commits (worktree-only) to master, committed Plan 20-02's uncommitted MainViewModel.cs edit in its worktree and cherry-picked it to master, then authored this SUMMARY.md directly. All commits preserve the original `feat(20-02):` and `feat(20-03):` provenance.

## Next

Wave 3: Plan 20-04 (NavigationService.Activate + manual smoke battery for AUTH-05/06/07). Currently blocked on Anthropic limit reset; can resume manually or via subagent after 19:10 Berlin time.
