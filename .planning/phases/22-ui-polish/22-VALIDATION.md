---
phase: 22
slug: ui-polish
status: draft
nyquist_compliant: false
wave_0_complete: true
created: 2026-05-06
---

# Phase 22 — Validation Strategy

## Test Infrastructure

| Property | Value |
|----------|-------|
| **Framework** | xUnit 2.9.3 + Moq 4.20.72 |
| **Quick run command** | `dotnet test CCInfoWindows.Tests/CCInfoWindows.Tests.csproj --filter "FullyQualifiedName~UiPolish|FullyQualifiedName~SessionDisplay|FullyQualifiedName~SettingsViewModel"` |
| **Full suite command** | `dotnet test CCInfoWindows.Tests/CCInfoWindows.Tests.csproj` |
| **Estimated runtime** | ~5–15 seconds |

## Sampling Rate

- **After every task commit:** quick filtered tests
- **Before /gsd-verify-work:** full suite + manual smoke for 3 visual behaviors

## Per-Task Verification Map

> Filled by planner.

| Task ID | Plan | Wave | Requirement | Test Type | Automated Command | Status |
|---------|------|------|-------------|-----------|-------------------|--------|
| TBD | TBD | TBD | TBD | TBD | TBD | ⬜ pending |

## Wave 0 Requirements

No new test scaffolding needed — existing patterns cover this scope.

## Manual-Only Verifications

| Behavior | Requirement | Why Manual | Test Instructions |
|----------|-------------|------------|-------------------|
| Spinner storyboard renders during refresh, disappears after; minimum 250ms visible | POLISH-01..03 | Animation requires real WinUI 3 host | Click refresh → observe icon swaps to spinner, stays >=250ms even on cached refresh, returns to refresh icon after |
| 2-line tooltip on inactive sessions in ComboBox | POLISH-04..06 | Hover-rendering requires real ComboBox host | Hover over an inactive session → tooltip shows "path\nInactive for > Nmin"; active sessions show only path |
| About-tab DispatcherTimer ticks every minute, stops on tab change/unload | POLISH-07..08 | Wall-clock validation | Open About tab → wait 60s → "X minutes ago" updates → switch tabs → no further updates |

## Validation Sign-Off

- [ ] All headless-testable behaviors covered (TooltipText composition, CanExecute predicate, Messenger handler, Timer lifecycle via mock DispatcherQueue)
- [ ] Manual smoke battery executed before phase verify
- [ ] No watch-mode flags
- [ ] Feedback latency < 15s

**Approval:** pending
