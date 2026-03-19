# Roadmap: ccInfo Windows

## Milestones

- ✅ **v1.0 CCInfoWindows MVP** — Phases 1-8 (shipped 2026-03-17)
- [ ] **v1.1 UI Polish & UX Improvements** — Phases 9-11

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

### v1.1 UI Polish & UX Improvements

- [ ] **Phase 9: Layout & Structure** — Restructure MainView.xaml section order, padding, and separators
- [ ] **Phase 10: Visual Styles** — Unify progress bars, badges, dropdown, tabs, and chart axis colors via AppTheme.xaml
- [ ] **Phase 11: Behavior & Interaction** — Timer format logic, button icons, and refresh animation

## Phase Details

### Phase 9: Layout & Structure
**Goal**: Users see a visually consistent layout with correct section order, equal padding on all sides, and clear separators between sections
**Depends on**: Nothing (first v1.1 phase, builds on shipped v1.0)
**Requirements**: LAYOUT-01, LAYOUT-02, LAYOUT-03, LAYOUT-04, LAYOUT-05, LAYOUT-06
**Success Criteria** (what must be TRUE):
  1. The app's vertical padding visually matches the horizontal padding — the window content has equal inset on all four sides
  2. A localized "Active Session" / "Aktive Sitzung" label appears above the project dropdown, styled identically to other section headers
  3. A visible horizontal separator line appears below the project dropdown, above the next section
  4. The Context Window section (main + subagent rows) is positioned between "Active Session" and "5-Hour Window", with separators above and below it
  5. The footer scrolls with content and is no longer fixed to the bottom of the window; a separator appears above it
  6. A horizontal separator appears between the "Models" row and the "Input" row in the Statistics section
**Plans**: 2 plans
Plans:
- [ ] 09-01-PLAN.md — Active Session header, padding, separators, Context Window reorder
- [ ] 09-02-PLAN.md — Footer relocation into scroll area, Statistics grid separator

### Phase 10: Visual Styles
**Goal**: Users see a visually unified interface where progress bars, model badges, controls, and chart labels follow a consistent style system
**Depends on**: Phase 9
**Requirements**: STYLE-01, STYLE-02, STYLE-03, STYLE-04, STYLE-05, TEXT-02, TEXT-03, TEXT-04
**Success Criteria** (what must be TRUE):
  1. All progress bars (5-hour window, weekly usage, context window) render at 6 px height with a gray semi-transparent track (rgba(128,128,128,0.45))
  2. The project dropdown and the Statistics tab bar share the same background color and have a CornerRadius of at least 8 px in both light and dark mode
  3. All model badges (e.g., "Sonnet 4.6", "Opus 4") are displayed as pills with fully rounded corners (CornerRadius=999)
  4. The 5-hour chart's axis labels (percentage values and time values) render in the same color as the timer text (clock icon label)
  5. The "Total" and "Cost (API equiv.)" labels in Statistics appear in the same text color and font weight as "Cache Read" / "Cache Write", with consistent vertical spacing before "Total"
**Plans**: 2 plans
Plans:
- [ ] 09-01-PLAN.md — Active Session header, padding, separators, Context Window reorder
- [ ] 09-02-PLAN.md — Footer relocation into scroll area, Statistics grid separator

### Phase 11: Behavior & Interaction
**Goal**: Users see correct timer formatting for long durations, icon-decorated buttons, and a smooth refresh animation that completes its cycle before stopping
**Depends on**: Phase 9
**Requirements**: TEXT-01, INTER-01, INTER-02, INTER-03
**Success Criteria** (what must be TRUE):
  1. Timer values of 24 hours or more display as "Xd Yh" (e.g., "3d 22h") with localized unit abbreviations (DE: "3T 22h"), not as raw hours/minutes
  2. The logout button has a red background, white text, and a logout icon left of the label
  3. The login button has a login icon left of its label
  4. The refresh icon rotates continuously (360°) while the API call is in flight, always completing the current full rotation before stopping — never snapping mid-turn
**Plans**: 2 plans
Plans:
- [ ] 09-01-PLAN.md — Active Session header, padding, separators, Context Window reorder
- [ ] 09-02-PLAN.md — Footer relocation into scroll area, Statistics grid separator

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
| 9. Layout & Structure | v1.1 | 0/2 | Planning complete | — |
| 10. Visual Styles | v1.1 | 0/? | Not started | — |
| 11. Behavior & Interaction | v1.1 | 0/? | Not started | — |
