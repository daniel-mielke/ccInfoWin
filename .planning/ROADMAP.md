# Roadmap: ccInfo Windows

## Milestones

- ✅ **v1.0 CCInfoWindows MVP** — Phases 1-8 (shipped 2026-03-17)
- ✅ **v1.1 UI Polish & UX Improvements** — Phases 9-11 (shipped 2026-04-01)
- 🚧 **v1.2 macOS v1.8.3 Feature Parity** — Phases 12-15 (in progress)

## Phases

<details>
<summary>✅ v1.0 CCInfoWindows MVP (Phases 1-8) — SHIPPED 2026-03-17</summary>

- [x] Phase 1: Foundation and Authentication (3/3 plans) — completed 2026-03-09
- [x] Phase 2: Core Monitoring Dashboard (4/4 plans) — completed 2026-03-11
- [x] Phase 3: Area Chart (3/3 plans) — completed 2026-03-11
- [x] Phase 4: Local Data Pipeline (3/3 plans) — completed 2026-03-11
- [x] Phase 5: Cost Analytics (2/2 plans) — completed 2026-03-16
- [x] Phase 6: Export, Polish, and Distribution (4/4 plans) — completed 2026-03-17
- [x] Phase 7: Security Fix & Dead Code Cleanup (1/1 plan) — completed 2026-03-17
- [x] Phase 8: Documentation Hygiene & Verification (1/1 plan) — completed 2026-03-17

Full details: `.planning/milestones/v1.0-ROADMAP.md`
Requirements: `.planning/milestones/v1.0-REQUIREMENTS.md`
Audit: `.planning/milestones/v1.0-MILESTONE-AUDIT.md`

</details>

<details>
<summary>✅ v1.1 UI Polish & UX Improvements (Phases 9-11) — SHIPPED 2026-04-01</summary>

- [x] Phase 9: Layout & Structure (2/2 plans) — completed 2026-03-19
- [x] Phase 10: Visual Styles (2/2 plans) — completed 2026-03-19
- [x] Phase 11: Behavior & Interaction (2/2 plans) — completed 2026-03-20

Full details: `.planning/milestones/v1.1-ROADMAP.md`
Requirements: `.planning/milestones/v1.1-REQUIREMENTS.md`
Audit: `.planning/milestones/v1.1-MILESTONE-AUDIT.md`

</details>

### 🚧 v1.2 macOS v1.8.3 Feature Parity (In Progress)

**Milestone Goal:** Bring ccInfoWin to feature parity with macOS ccInfo v1.8.3 — model-based context detection, user-configurable Sonnet context, session cleanup, stable subagent ordering, and footer accessibility.

- [ ] **Phase 12: Model-Based Context Detection** — Rewrite ModelContextLimits with ModelFamily enum; correct Opus to 1M, Haiku to 200K, flat autocompact warning
- [ ] **Phase 13: Sonnet Context Window Setting** — End-to-end: AppSettings, Settings UI picker, messenger, live refresh on change
- [ ] **Phase 14: Session Management Polish** — Filter ghost sessions by Directory.Exists; stabilize subagent sort order alphabetically
- [ ] **Phase 15: Footer Tooltip & Accessibility** — Localized tooltips on all footer buttons, AutomationProperties.Name for screen readers

## Phase Details

### Phase 12: Model-Based Context Detection
**Goal**: Users see correct context window sizes based on the actual model family — 1M for Opus, 200K for Haiku, and model-family-resolved values for all subagents
**Depends on**: Phase 11 (previous milestone complete)
**Requirements**: CTX-01, CTX-02, CTX-03, CTX-04, CTX-05, CTX-06
**Success Criteria** (what must be TRUE):
  1. User sees ~967K effective context for an Opus session (1M minus 33K buffer)
  2. User sees ~167K effective context for a Haiku session (200K minus 33K buffer)
  3. User sees autocompact warning when any session has ≤20K tokens remaining, regardless of model
  4. User sees correct progress bar percentage for both main session and all subagent bars
  5. User sees correct model-based context limits on all subagent context progress bars
**Plans**: 1 plan
Plans:
- [ ] 12-01-PLAN.md — Rewrite ModelContextLimits with ModelFamily enum, update callers, update tests

### Phase 13: Sonnet Context Window Setting
**Goal**: Users can configure Sonnet's context window size in Settings and see the change reflected immediately in the context display
**Depends on**: Phase 12
**Requirements**: SET-01, SET-02, SET-03, SET-04, SET-05
**Success Criteria** (what must be TRUE):
  1. User sees a 200K / 1M ComboBox picker in the Settings view
  2. User sees 200K selected by default when no preference has been saved
  3. User sees the context window display update immediately after changing the picker (no manual refresh)
  4. User sees the same Sonnet context setting after restarting the app
  5. User sees the picker label and options in the correct language (de-DE and en-US)
**Plans**: TBD
**UI hint**: yes

### Phase 14: Session Management Polish
**Goal**: Users see only sessions for existing project directories, and subagent context bars appear in a stable, predictable order
**Depends on**: Phase 11 (independent of Phases 12-13)
**Requirements**: SES-01, SES-02, SES-03
**Success Criteria** (what must be TRUE):
  1. User does not see sessions whose project directory has been deleted in the session dropdown
  2. User sees the session selection reset to the next valid session when the active project directory is deleted
  3. User sees subagent context bars in the same alphabetical order on every refresh
**Plans**: TBD

### Phase 15: Footer Tooltip & Accessibility
**Goal**: Users can discover footer button functions via tooltips and screen readers can announce button purposes
**Depends on**: Phase 11 (independent of Phases 12-14)
**Requirements**: ACC-01, ACC-02, ACC-03
**Success Criteria** (what must be TRUE):
  1. User sees a localized tooltip when hovering over each footer button (Refresh, Settings, Quit)
  2. User's screen reader announces the purpose of each footer button via AutomationProperties.Name
  3. User sees tooltip text in the language currently selected in the app (de-DE or en-US)
**Plans**: TBD
**UI hint**: yes

## Progress

| Phase | Milestone | Plans Complete | Status | Completed |
|-------|-----------|----------------|--------|-----------|
| 1. Foundation and Authentication | v1.0 | 3/3 | Complete | 2026-03-09 |
| 2. Core Monitoring Dashboard | v1.0 | 4/4 | Complete | 2026-03-11 |
| 3. Area Chart | v1.0 | 3/3 | Complete | 2026-03-11 |
| 4. Local Data Pipeline | v1.0 | 3/3 | Complete | 2026-03-11 |
| 5. Cost Analytics | v1.0 | 2/2 | Complete | 2026-03-16 |
| 6. Export, Polish, and Distribution | v1.0 | 4/4 | Complete | 2026-03-17 |
| 7. Security Fix & Dead Code Cleanup | v1.0 | 1/1 | Complete | 2026-03-17 |
| 8. Documentation Hygiene & Verification | v1.0 | 1/1 | Complete | 2026-03-17 |
| 9. Layout & Structure | v1.1 | 2/2 | Complete | 2026-03-19 |
| 10. Visual Styles | v1.1 | 2/2 | Complete | 2026-03-19 |
| 11. Behavior & Interaction | v1.1 | 2/2 | Complete | 2026-03-20 |
| 12. Model-Based Context Detection | v1.2 | 0/1 | In progress | - |
| 13. Sonnet Context Window Setting | v1.2 | 0/? | Not started | - |
| 14. Session Management Polish | v1.2 | 0/? | Not started | - |
| 15. Footer Tooltip & Accessibility | v1.2 | 0/? | Not started | - |
