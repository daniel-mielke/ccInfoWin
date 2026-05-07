---
gsd_state_version: 1.0
milestone: v1.4
milestone_name: macOS v1.11.1 Feature Parity
status: complete
shipped: 2026-05-07
last_updated: "2026-05-07T15:30:00.000Z"
last_activity: 2026-05-07 -- v1.4 milestone archived
progress:
  total_phases: 4
  completed_phases: 4
  total_plans: 13
  completed_plans: 13
  percent: 100
---

# Project State

## Project Reference

See: .planning/PROJECT.md (updated 2026-05-07)

**Core value:** Developers can see their Claude usage limits (5-hour window, weekly quota, context window) at a glance in real-time, preventing unexpected throttling.
**Current focus:** Planning next milestone (v1.5 — to be defined via `/gsd-new-milestone`)

## Current Position

Milestone: v1.4 — SHIPPED 2026-05-07
Status: Complete, archived
Last activity: 2026-05-07 -- v1.4 milestone archived

Progress: [██████████] 100%

## Performance Metrics

**v1.4 totals:**

- Total phases: 4 (Phase 20-23)
- Total plans: 13 (10 base + 3 gap-closure)
- Total commits: 51 (range `21a73bb..0d9c483` + audit + archive)
- LOC delta: 64 files, +11,115 / -42 lines
- Test coverage delta: +26 tests on modified surface, 4 new test classes

**By Phase:**

| Phase | Plans | Status | Completed |
|-------|-------|--------|-----------|
| 20 Auth Flow Stability | 5 | Complete | 2026-05-07 |
| 21 History Persistence Hardening | 3 | Complete | 2026-05-07 |
| 22 UI Polish | 4 | Complete | 2026-05-07 |
| 23 Localization Gaps | 1 | Complete | 2026-05-07 |

## Accumulated Context

### Decisions

Decisions are logged in PROJECT.md Key Decisions table. Recent v1.4 additions:

- `_autoReauthAttempted` single bool flag for first-vs-second 401 routing
- Hybrid sync+async history persistence (sync at termination, async during poll)
- `IDispatcherTimer` adapter for headless About-tab timer testing
- Direct DI call instead of `WeakReferenceMessenger` for logout (production hotfix lesson)
- Gap-closure as additional wave within parent phase
- Belt-and-suspenders `IsEnabled` x:Bind on `[RelayCommand]` buttons

### Open Tech Debt (carried into v1.5+)

- WeakReferenceMessenger + AddTransient ViewModels = recipient GC pitfall (`architecture_weakreferencemessenger_with_transient_vms.md`)
- Cold-start session scanning (`backlog_session_dropdown_recent_sessions.md`) — blocks POLISH-04 visual smoke
- Multi-account org-id picker (`backlog_org_id_picker.md`) — `TryMigrateOrgIdAsync` blindly takes `orgs[0]`
- Pricing service silent failure (`backlog_pricing_never_loaded.md`) — blocks POLISH-07 visual smoke
- Next 5h-window start label feature request (`backlog_next_window_start_label.md`)
- 2 pre-existing `ClaudeApiServiceTests` failures (parameter naming mismatch, production unaffected, unchanged from v1.3)
- 13 pre-existing `JsonlServiceTests` failures (parameter naming mismatch, production unaffected, unchanged from v1.0)
- AUTH-01/02 visual smoke deferred — dev build can't easily force a 401 (full unit-test coverage applies)

### Blockers/Concerns

(None — milestone complete, ready to plan v1.5)

## Session Continuity

Last session: v1.4 milestone close
Stopped at: All archives written, ready for safety commit + git tag
Resume file: —
