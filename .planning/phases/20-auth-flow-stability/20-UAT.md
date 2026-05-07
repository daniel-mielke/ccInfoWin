---
status: partial
phase: 20-auth-flow-stability
source: 20-01-SUMMARY.md, 20-02-SUMMARY.md, 20-03-SUMMARY.md, 20-04-SUMMARY.md
started: 2026-05-07T00:00:00+02:00
updated: 2026-05-07T10:50:00+02:00
---

## Current Test

[Phase 20 testing complete — moving to Phase 21]

## Test Run History

- 2026-05-07 first attempt: tried "delete CCInfoWindows/claude-session via
  Windows Credential Manager" as the trigger. App behaviour: refresh worked
  normally, no LoginView. Diagnosed as INVALID TRIGGER — `ClaudeApiService.
  FetchUsageAsync` routes through `WebViewBridge.FetchJsonAsync` (verified
  at ClaudeApiService.cs:73) which uses WebView2 cookies via
  `credentials: 'include'`, not the Credential Manager token. Reset Test 1
  to pending; new trigger uses WebView2 DevTools to clear cookies.

## Tests

### 1. First HTTP 401 → LoginView auto-navigation (no InfoBar)
expected: After first 401 in a session, LoginView opens automatically; existing InfoBar does not appear; no user interaction required (AUTH-01).
result: skipped
reason: DevTools-based 401 trigger not feasible in current build; user requested skip. First trigger attempt (Credential Manager token deletion) was diagnosed as invalid — API auth runs through WebView2 cookies via `_bridge.FetchJsonAsync`, not the Credential Manager token.

### 2. Second HTTP 401 → existing InfoBar fallback
expected: After SECOND 401 in same session (e.g., login again, then force another expiry), the yellow "Session Expired" InfoBar opens on MainView; LoginView does NOT auto-open again (AUTH-02 / `_autoReauthAttempted` flag works).
result: skipped
reason: Depends on Test 1 trigger; skipped together.

### 3. Post-login refresh is immediate
expected: After auto-opened LoginView, sign in successfully. MainView refreshes within ~2 seconds. No app restart, no manual reload (AUTH-03).
result: skipped
reason: Depends on Test 1 trigger; skipped together.

### 4. LoginView reload button — click reloads
expected: On LoginView, click the new reload icon button at the top-right. WebView2 reloads (visible page refresh). No crash, no error UI (AUTH-06).
result: issue
reported: "ja, funktional passt es, aber der button ist schlecht zu erkennen. der button benötigt einen wrapper 30x30px mit dunklem hintergrund, damit das icon sichtbar ist."
severity: cosmetic
notes: |
  Functional behavior PASSES — click reloads WebView2 cleanly, no crash.
  Visual contrast issue: the reload icon (SecondaryTextBrush, transparent
  background per UI-SPEC D-05) becomes nearly invisible against the
  claude.ai login page's cream-white background. UI-SPEC locked the visual
  to MainView footer parity, but MainView has a Slate-900 dark background
  where SecondaryTextBrush has adequate contrast — LoginView's WebView2
  surface has a completely different contrast context.

  User-proposed mitigation: 30x30px wrapper with dark background (e.g., a
  rounded pill/circle container) so the icon is legible regardless of the
  WebView2 content underneath.

### 5. LoginView reload button — accessibility (Tab + Narrator)
expected: Tab to the reload button on LoginView. Visible focus indicator. Narrator (Win+Ctrl+Enter) announces "Reload login page" (EN) or "Login-Seite neu laden" (DE) per current language (AUTH-07).
result: pass

### 6. Sign-out shows clean LoginView (no chat URL flash)
expected: Sign in, wait for usage data, then sign out. LoginView shows the login form ONLY. No flash of the previous chat URL (AUTH-05 visual / D-07 IsLoading gate).
result: pass

### 7. Minimized window restores on background 401
expected: Sign in, wait for usage data, MINIMIZE the window. Wait for next poll cycle (or force a 401 in the background). Window unminimizes (or comes to foreground) when LoginView is auto-navigated (AUTH-05 / D-09 App.MainWindow.Activate).
result: skipped
reason: Depends on Test 1 trigger (real 401); skipped together. Will be naturally exercised in production when session token expires while window is minimized.

## Summary

total: 7
passed: 2
issues: 1
pending: 0
skipped: 4
blocked: 0

## Gaps

- truth: "LoginView reload button is visually legible against the WebView2 login page background"
  status: failed
  reason: "User reported: 'funktional passt es, aber der button ist schlecht zu erkennen. der button benötigt einen wrapper 30x30px mit dunklem hintergrund, damit das icon sichtbar ist.'"
  severity: cosmetic
  test: 4
  root_cause: ""     # to be filled by diagnosis
  artifacts: []      # to be filled by diagnosis
  missing: []        # to be filled by diagnosis
  debug_session: ""  # to be filled by diagnosis
