---
gsd_state_version: 1.0
milestone: v1.5
milestone_name: macOS v1.12.0 Feature Parity + Hardening
status: executing
stopped_at: Completed 26-01-session-name-store-PLAN.md
last_updated: "2026-05-08T17:03:30.372Z"
last_activity: 2026-05-08
progress:
  total_phases: 5
  completed_phases: 2
  total_plans: 9
  completed_plans: 8
  percent: 89
---

# Project State

## Project Reference

See: .planning/PROJECT.md (updated 2026-05-08)

**Core value:** Developers can see their Claude usage limits (5-hour window, weekly quota, context window) at a glance in real-time, preventing unexpected throttling.
**Current focus:** Phase 26 — Persistent Session Renaming

## Current Position

Phase: 26 (Persistent Session Renaming) — EXECUTING
Plan: 3 of 3
Status: Ready to execute
Last activity: 2026-05-08

**v1.5 Phase Sequence (research-validated, do not reorder):**

1. **Phase 24** — DISPATCH foundation (`IDispatcherQueue` adapter + C-1/C-2 fix + G-1 convention enforcement)
2. **Phase 25** — DROPDOWN (Cwd hydration + visibility window + cold-start data-loss race fix)
3. **Phase 26** — RENAME (session-rename feature, biggest phase: ContentDialog + 5th Settings tab + `ISessionNameStore`)
4. **Phase 27** — NEXTWIN + ORGID + PRICING + L10N (mid-risk feature trio with non-overlapping surfaces; B3 + M-2/L10N must couple)
5. **Phase 28** — CLEANUP (M-1 + M-3 + Nits + final UAT)

## Performance Metrics

**v1.4 totals (shipped):**

- Total phases: 4 (Phase 20-23)
- Total plans: 13 (10 base + 3 gap-closure)
- Total commits: 51 (range `21a73bb..0d9c483` + audit + archive)
- LOC delta: 64 files, +11,115 / -42 lines
- Test coverage delta: +26 tests on modified surface, 4 new test classes

**v1.5 in flight:**

| Phase | Plans | Status | Completed |
|-------|-------|--------|-----------|
| 24 Dispatcher Foundation & Marshaling Convention | 0 | Not started | — |
| 25 Cold-Start Session Hydration & Visibility Window | 0 | Not started | — |
| 26 Persistent Session Renaming | 0 | Not started | — |
| 27 Next-Window Label, Org-ID Picker, Pricing Surfacing & L10N | 0 | Not started | — |
| 28 v1.4 Cleanup & Final UAT | 0 | Not started | — |
| Phase 24 P01 | 25min | 3 tasks | 5 files |
| Phase 24 P02 | 35min | 3 tasks | 5 files |
| Phase 24 P03 | 4min | 3 tasks | 3 files |
| Phase 25 P01 | 35min | 2 tasks | 4 files |
| Phase 25 P02 | 5 | 3 tasks | 7 files |
| Phase 25 P25-03 | 10 | 2 tasks | 5 files |
| Phase 26-persistent-session-renaming P01 | 18m | 3 tasks | 7 files |
| Phase 26 P02 | 45 | 2 tasks | 9 files |

## Accumulated Context

### Decisions

Decisions are logged in PROJECT.md Key Decisions table. Recent v1.4 additions:

- `_autoReauthAttempted` single bool flag for first-vs-second 401 routing
- Hybrid sync+async history persistence (sync at termination, async during poll)
- `IDispatcherTimer` adapter for headless About-tab timer testing
- Direct DI call instead of `WeakReferenceMessenger` for logout (production hotfix lesson)
- Gap-closure as additional wave within parent phase
- Belt-and-suspenders `IsEnabled` x:Bind on `[RelayCommand]` buttons

**v1.5 architecture decisions (from research/SUMMARY.md, to be logged in PROJECT.md as phases ship):**

- Decision 1: `ISessionNameStore` hooks at the display layer in `MainViewModel.RefreshSessionList` — NOT inside `JsonlService` (preserves storage-free service tests; honors D-13 lesson)
- Decision 2: Phase build order 24 → 25 → 26 → 27 → 28 (foundation before any new `IRecipient<>` lands)
- Decision 3: `IDispatcherQueue` ships as full adapter in Phase 24 (interface + production adapter + `FakeDispatcherQueue` + convention test) — mirrors v1.4 `IDispatcherTimer` precedent

**v1.5 conventions to land in CLAUDE.md:**

- G-1: `IRecipient<>.Receive` always-TryEnqueue rule (Phase 24)
- G-2: JSON-on-disk store pattern with `SemaphoreSlim` write guard (Phase 26 first consumer: `ISessionNameStore`)
- G-3: `[ObservableProperty]` defaults — prefer real initializers over `null!` (Phase 28)
- [Phase ?]: IDispatcherQueue adapter ships as full interface + WinuiDispatcherQueueAdapter singleton + FakeDispatcherQueue test double, mirroring v1.4 IDispatcherTimer precedent (Phase 24 Plan 01)
- [Phase ?]: CD-01: ComboBox for SessionVisibilityWindowDays (mirrors SessionTimeoutMinutes precedent)
- [Phase ?]: CD-04: MainViewModel handles SessionVisibilityChangedMessage directly via IRecipient (not JsonlService re-emit)
- [Phase ?]: DROPDOWN-05: InfoBar migration toast in MainViewModel.InitializeAsync with synchronous SaveSettings on dismiss (D-04 + CD-02 + CD-05 honored)
- [Phase ?]: ISessionNameStore D-01 shape locked: GetCustomName/SetCustomName/ClearCustomName/Save/SaveAsync/NameChanged
- [Phase ?]: session-names.json path D-02: %LOCALAPPDATA%\CCInfoWindows\, Dictionary<string,string> keyed by encoded projectDirName

### Open Tech Debt (carried into v1.5)

**v1.4 code-review findings (2026-05-07, scheduled in v1.5):**

- 🔴 **C-1**: Fire-and-forget Task in `MainViewModel.Receive(AuthStateChangedMessage)` → Phase 24 (DISPATCH-04)
- 🔴 **C-2**: `Receive(AuthStateChangedMessage)` mutates UI state without DispatcherQueue marshaling → Phase 24 (DISPATCH-04)
- 🟡 **M-1**: Orphan `LogoutRequestedMessage.cs` from reverted Plan 21-03 → Phase 28 (CLEANUP-01)
- 🟡 **M-2**: `LastFetchRelativeTime` hardcoded EN strings — couples with B3 → Phase 27 (L10N-01)
- 🟡 **M-3**: `_contextModelBadgeColor = null!` → Phase 28 (CLEANUP-02)
- ⚪ **Nits**: 3 minor cleanups → Phase 28 (CLEANUP-03)

**Carried from earlier milestones / phase backlog (memory-tracked):**

- Cold-start session scanning (`backlog_session_dropdown_recent_sessions.md`) → Phase 25 (DROPDOWN-01..06)
- Multi-account org-id picker (`backlog_org_id_picker.md`) → Phase 27 (ORGID-01..05)
- Pricing service silent failure (`backlog_pricing_never_loaded.md`) → Phase 27 (PRICING-01..03)
- Next 5h-window start label (`backlog_next_window_start_label.md`) → Phase 27 (NEXTWIN-01..03)
- WeakReferenceMessenger + AddTransient ViewModels = recipient GC pitfall — codified as G-1 convention in Phase 24
- 2 pre-existing `ClaudeApiServiceTests` failures (parameter naming mismatch, production unaffected — out of scope per REQUIREMENTS.md)
- 13 pre-existing `JsonlServiceTests` failures (parameter naming mismatch, production unaffected — out of scope per REQUIREMENTS.md)
- AUTH-01/02 visual smoke deferred — dev build can't easily force a 401

### Blockers/Concerns

(None — roadmap approved, ready for Phase 24 planning)

## Session Continuity

Last session: 2026-05-08T17:03:30.362Z
Stopped at: Completed 26-01-session-name-store-PLAN.md
Resume file: None
