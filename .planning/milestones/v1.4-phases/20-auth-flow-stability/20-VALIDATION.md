---
phase: 20
slug: auth-flow-stability
status: draft
nyquist_compliant: false
wave_0_complete: false
created: 2026-05-06
---

# Phase 20 — Validation Strategy

> Per-phase validation contract for feedback sampling during execution.

---

## Test Infrastructure

| Property | Value |
|----------|-------|
| **Framework** | xUnit 2.9.3 + Moq 4.20.72 (existing test project, see RESEARCH.md) |
| **Config file** | `CCInfoWindows/CCInfoWindows.Tests/CCInfoWindows.Tests.csproj` |
| **Quick run command** | `dotnet test CCInfoWindows/CCInfoWindows.Tests/CCInfoWindows.Tests.csproj --filter "FullyQualifiedName~AuthFlow"` |
| **Full suite command** | `dotnet test CCInfoWindows/CCInfoWindows.Tests/CCInfoWindows.Tests.csproj` |
| **Estimated runtime** | ~5–15 seconds (in-memory ViewModel tests; no UI thread) |

---

## Sampling Rate

- **After every task commit:** Run quick command (filtered to AuthFlow tests)
- **After every plan wave:** Run full suite
- **Before `/gsd-verify-work`:** Full suite must be green + manual smoke (AUTH-05..AUTH-07) executed
- **Max feedback latency:** 15 seconds (quick); 30 seconds (full)

---

## Per-Task Verification Map

> The planner fills this table during plan generation. Each task's `<automated>` block must reference a row here OR a Wave 0 stub.

| Task ID | Plan | Wave | Requirement | Threat Ref | Secure Behavior | Test Type | Automated Command | File Exists | Status |
|---------|------|------|-------------|------------|-----------------|-----------|-------------------|-------------|--------|
| TBD     | TBD  | TBD  | TBD         | —          | TBD             | TBD       | TBD               | TBD         | ⬜ pending |

*Status: ⬜ pending · ✅ green · ❌ red · ⚠️ flaky*

---

## Wave 0 Requirements

- [ ] `CCInfoWindows.Tests/ViewModels/MainViewModelAuthFlowTests.cs` — scaffold test class with `MainViewModelTestHarness`, stubs for AUTH-01 through AUTH-04 (one [Fact] per requirement, marked `[Trait("Status","Pending")]` until Wave 1 implements the routing logic)
- [ ] `CCInfoWindows.Tests/ViewModels/LoginViewModelReloadTests.cs` — scaffold for the reload-button command (null-guard test)
- [ ] Localization keys for `LoginReloadButton.Tooltip` and `LoginReloadButton.AutomationName` in both `Strings/en-US/Resources.resw` and `Strings/de-DE/Resources.resw` — Phase 20 self-contained per RESEARCH.md Open Question #1

---

## Manual-Only Verifications

| Behavior | Requirement | Why Manual | Test Instructions |
|----------|-------------|------------|-------------------|
| Sign-out clears WebView2 chat URL before LoginView is shown | AUTH-05 | Requires real WebView2 navigation lifecycle; `CoreWebView2.Source` cannot be observed deterministically in unit tests | Sign in → wait for usage data → sign out → confirm LoginView shows the login form (not the previous chat URL). Verify via `mcp__windows-mcp` screenshot. |
| Reload button on LoginView triggers `CoreWebView2.Reload()` | AUTH-04 | DOM reload requires a live WebView2 host | Open LoginView → click reload button → confirm WebView2 reloads (network tab if available, or visual reload of the login form). |
| First HTTP 401 in a session navigates to LoginView automatically (no InfoBar) | AUTH-01, AUTH-02 | Real 401 from claude.ai is hard to force in unit tests; ViewModel-level test covers the routing decision but not the actual WebView2 transition | Manually invalidate the session token (or wait for natural expiry) → trigger Refresh → confirm LoginView appears, InfoBar does NOT. |
| Second HTTP 401 in same session shows the existing InfoBar fallback | AUTH-02 | Same as above | Continue from the prior step: log in again, then invalidate again → second 401 → confirm InfoBar appears (no auto-navigation loop). |
| Post-login refresh is immediate (no app restart) | AUTH-03 | Validates the `AuthStateChangedMessage(true)` → `RefreshCommand` chain end-to-end | After auto-navigated LoginView, log in successfully → confirm MainView refreshes within ~2 seconds, no manual reload required. |
| `_autoReauthAttempted` resets correctly across login cycles | AUTH-06 | Multi-cycle scenario | After completing the AUTH-01..AUTH-03 sequence, force a third 401 in the SAME session → confirm auto-navigation triggers again (because the flag was reset on `Receive(true)`). |
| LoginView reload button is keyboard-focusable + screen-reader-named in DE and EN | AUTH-07 | Accessibility verification | Tab to the reload button → confirm focus visual; Narrator should announce "Login-Seite neu laden" (DE) or "Reload login page" (EN) per current language. |

---

## Validation Sign-Off

- [ ] All tasks have `<automated>` verify or Wave 0 dependencies
- [ ] Sampling continuity: no 3 consecutive tasks without automated verify
- [ ] Wave 0 covers all MISSING references (test scaffolds + resw keys)
- [ ] No watch-mode flags (`dotnet test --watch` is forbidden in CI/automated runs)
- [ ] Feedback latency < 15s (quick) / 30s (full)
- [ ] Manual smoke checklist (AUTH-04..AUTH-07) executed and recorded in VERIFICATION.md before phase verify
- [ ] `nyquist_compliant: true` set in frontmatter (after planner fills Per-Task Verification Map)

**Approval:** pending
