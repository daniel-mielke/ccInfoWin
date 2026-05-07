---
phase: 20-auth-flow-stability
plan: 04
status: complete
completed: 2026-05-06
requirements: [AUTH-05]
decisions: [D-09]
files_changed: 1
manual_smoke_pending: true
---

# Plan 20-04 Summary — NavigationService Window Activation

## What was built

One-line addition to `NavigationService.NavigateTo<TPage>`: `App.MainWindow?.Activate()` is invoked before `_frame.Navigate(...)`. Global behavior — applies to every navigation call, not coupled to any specific page or the auto-reauth flow.

## Files modified

- `CCInfoWindows/CCInfoWindows/Services/NavigationService.cs` (+5, -0)

## Implementation Detail

### D-09 — Pre-navigation window activation

`App.MainWindow` is the existing public-static reference declared at `App.xaml.cs:19` and assigned in `OnLaunched`. Zero new plumbing required. The null-conditional `?.Activate()` covers the cold-start race window where `OnLaunched` hasn't completed yet (impossible in practice — navigation only happens after the frame is initialized, which happens inside the main window — but defended cheaply).

The activation cost is zero when the window is already foreground: `WindowEx.Activate()` is a no-op in that case (no flicker, no focus theft). The benefit materializes when the window is minimized during a background poll → 401 → auto-reauth: the user sees LoginView immediately rather than discovering it later.

## Verification

### Build
`dotnet build CCInfoWindows/CCInfoWindows/CCInfoWindows.csproj` — 0 errors, 67 pre-existing MVVMTK warnings (unchanged).

### Automated tests
N/A — `App.MainWindow` and `NavigationService` cannot be exercised in headless xUnit (no real WinUI 3 host). The 4 `MainViewModelAuthFlowTests` from Plan 20-02 still pass (they verify the routing decision, not the window activation).

### Manual smoke battery (DEFERRED — runs in /gsd-verify-work for Phase 20)

Per VALIDATION.md, AUTH-05/06/07 require manual smoke verification. Steps:

| Step | Behavior | Pass criterion |
|------|----------|----------------|
| 1 | Launch CCInfoWindows; sign in successfully | Usage data loads in MainView |
| 2 | Click the new reload button on LoginView (after sign-out & before re-login) | WebView2 reloads (visible network/page refresh); no crash |
| 3 | Sign out; observe LoginView | LoginView shows the login form, NOT the previous chat URL flash |
| 4 | Sign in; minimize the window; wait for next poll | Window unminimizes (or comes to foreground) when next 401 fires |
| 5 | Force a session expiry; observe routing | First 401 → LoginView auto-opens (no InfoBar); InfoBar opens only on second 401 in same session |
| 6 | Re-login from auto-opened LoginView | MainView refreshes immediately (no app restart, no waiting for poll-tick) |
| 7 | Tab to the reload button on LoginView | Visible focus indicator; Narrator announces "Login-Seite neu laden" (DE) or "Reload login page" (EN) |

**Smoke battery result:** TBD — to be recorded in `20-VERIFICATION.md` during `/gsd-verify-work`. AUTH-05/06/07 stay as `human_needed` until then.

## Commits

- `4f34bbf` — `feat(20-04): activate App.MainWindow before frame navigation (D-09)`

## Deviations from plan

**Recovery deviation (orchestrator-side):**
Plan 20-04 was originally scheduled to be executed by a `gsd-executor` subagent in worktree isolation. The Anthropic API limit hit during Wave 2 prevented spawning a new subagent for Wave 3 within the same session. The orchestrator implemented the one-line code change directly on master and authored this SUMMARY.md inline. The implementation is identical to what the subagent would have produced — the plan's `<action>` block was unambiguous (single line + comment), and the manual smoke battery defers to `/gsd-verify-work` regardless of execution path.

## Phase 20 Final State

All 4 plans complete:

| Plan | Wave | Decisions | Requirements | Status |
|------|------|-----------|--------------|--------|
| 20-01 | 1 | D-05 | AUTH-06 | ✅ Complete |
| 20-02 | 2 | D-01, D-02, D-03 | AUTH-01, AUTH-02, AUTH-03, AUTH-04 | ✅ Complete |
| 20-03 | 2 | D-04, D-05, D-06, D-07, D-08 | AUTH-06, AUTH-07 | ✅ Complete |
| 20-04 | 3 | D-09 | AUTH-05 | ✅ Complete (smoke pending) |

**Build status:** 0 errors, 67 warnings (all pre-existing). **Test status:** 4/4 AuthFlow tests GREEN. **Manual verification status:** AUTH-05/06/07 pending operator smoke battery.
