---
status: partial
phase: 23-localization-gaps
source: 23-01-SUMMARY.md
started: 2026-05-07T00:00:00+02:00
updated: 2026-05-07T12:40:00+02:00
---

## Current Test

[v1.4 milestone UAT complete]

## Tests

### 1. Runtime language switch — InactiveSessionTooltip
expected: |
  In Settings → Language, set to English. Return to MainView, hover an
  inactive session. Tooltip second line: "Inactive for > <N>min".
  Switch language to German. Return to MainView, hover the same session.
  Tooltip second line: "Inaktiv seit > <N>min".
  No app restart required (L10N-03).
result: blocked
blocked_by: cold-start-session-scanning-bug
reason: |
  Same root cause as Phase 22 Tests 2 and 3: no inactive sessions are
  visible in the ComboBox to host the tooltip whose live-switch we'd
  observe. ResourceCoverageTests (Plan 23-01 Task 2) statically verify
  that the `InactiveSessionTooltip` resw key exists in both EN and DE
  with the expected values (4/4 GREEN). Visual smoke deferred until the
  cold-start scan fix lands. See backlog_session_dropdown_recent_sessions.md.

### 2. Runtime language switch — LoginReloadButton tooltip + Narrator
expected: |
  Sign out (or trigger session expiry to reach LoginView).
  In English mode: hover reload button → "Reload page". Tab to it →
  Narrator announces "Reload login page".
  Switch to German (back through Settings, then re-trigger LoginView):
  hover → "Seite neu laden". Tab → Narrator announces
  "Login-Seite neu laden". (L10N-03 + AUTH-07 cross-check)
result: skipped
reason: |
  User skipped due to setup overhead (sign-out + cross-view language
  switch). Static verification by ResourceCoverageTests (Plan 23-01 Task
  2, 4/4 GREEN) confirms both LoginReloadButton.* keys exist in both
  locales with the locked CONTEXT D-01 values. Phase 20 Test 5 PASS
  earlier confirmed the EN/DE Narrator-readable name worked correctly
  when first loaded. Runtime-switch infrastructure (WinUI3Localizer) is
  project baseline — if it were broken, the existing 130+ keys would
  also break, and the user would have noticed long before now.

### 3. Resw structural integrity verified by xUnit
expected: |
  Already verified: `dotnet test --filter ~ResourceCoverage` returned 4/4
  GREEN. This test is the operator's confirmation that no manual
  intervention is needed for L10N-01 / static structure.
result: pass
reported: "auto-verified by xUnit (4/4 ResourceCoverageTests GREEN, 15ms)"

## Summary

total: 3
passed: 1
issues: 0
pending: 0
skipped: 1
blocked: 1

## Gaps
