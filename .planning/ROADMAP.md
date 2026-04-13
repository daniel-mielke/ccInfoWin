# Roadmap: ccInfo Windows

## Milestones

- ✅ **v1.0 CCInfoWindows MVP** — Phases 1-8 (shipped 2026-03-17)
- ✅ **v1.1 UI Polish & UX Improvements** — Phases 9-11 (shipped 2026-04-01)
- ✅ **v1.2 macOS v1.8.3 Feature Parity** — Phases 12-15 (shipped 2026-04-12)
- 🚧 **v1.3 macOS v1.10.0 Feature Parity** — Phases 16-19 (in progress)

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

<details>
<summary>✅ v1.2 macOS v1.8.3 Feature Parity (Phases 12-15) — SHIPPED 2026-04-12</summary>

- [x] Phase 12: Model-Based Context Detection (1/1 plans) — completed 2026-04-12
- [x] Phase 13: Sonnet Context Window Setting (2/2 plans) — completed 2026-04-12
- [x] Phase 14: Session Management Polish (1/1 plans) — completed 2026-04-12
- [x] Phase 15: Footer Tooltip & Accessibility (1/1 plans) — completed 2026-04-13

Full details: `.planning/milestones/v1.2-ROADMAP.md`
Requirements: `.planning/milestones/v1.2-REQUIREMENTS.md`
Audit: `.planning/milestones/v1.2-MILESTONE-AUDIT.md`

</details>

### 🚧 v1.3 macOS v1.10.0 Feature Parity (In Progress)

**Milestone Goal:** Port macOS v1.9.0 and v1.10.0 features — burn rate prediction with visual warnings and toast notifications, chart gradient rendering, settings UI redesign with Segmented Control, and FileSystemWatcher verification.

- [x] **Phase 16: Burn Rate Warning** — Prediction engine, inline banner, and toast notification when token exhaustion is projected (completed 2026-04-13)
- [x] **Phase 17: Chart Horizontal Gradient** — Smooth green→yellow→orange→red gradient replacing flat zone fills on the 5-hour chart (completed 2026-04-13)
- [ ] **Phase 18: Settings Redesign** — Segmented Control with General/Updates/Account/About tabs at 360px width
- [ ] **Phase 19: Session Watcher Verification** — Confirm FileSystemWatcher catches file-level session metadata changes

## Phase Details

### Phase 16: Burn Rate Warning
**Goal**: Users are warned before their 5-hour token window runs out
**Depends on**: Phase 15 (prior milestone complete)
**Requirements**: BURN-01, BURN-02, BURN-03, BURN-04, BURN-05, BURN-06, BURN-07
**Success Criteria** (what must be TRUE):
  1. A red banner with flame icon appears in the main view when linear regression over the last 15 minutes (minimum 3 data points, minimum 20% utilization) projects exhaustion before the window resets
  2. The banner shows time-until-limit formatted as ~Xh YYmin or ~Xmin, in both German and English
  3. The banner disappears automatically when usage rate drops, the window resets, or the slope reverses
  4. A Windows toast notification fires exactly once when the warning first triggers; it does not re-fire until the warning clears and re-triggers in a new cycle
  5. All banner and toast text is fully localized (German and English)
**Plans**: 2 plans
Plans:
- [x] 16-01-PLAN.md — BurnRatePrediction model, BurnRateCalculator engine with TDD, theme brush, localization strings
- [x] 16-02-PLAN.md — Notification service, ViewModel integration, XAML banner, DI wiring

### Phase 17: Chart Horizontal Gradient
**Goal**: The 5-hour area chart renders a smooth horizontal color gradient instead of flat zone fills
**Depends on**: Phase 16
**Requirements**: CHRT-01, CHRT-02, CHRT-03, CHRT-04, CHRT-05
**Success Criteria** (what must be TRUE):
  1. The chart area fill transitions smoothly from green to yellow to orange to red across the horizontal data range at 25% opacity
  2. The line stroke renders at 100% opacity (2.0 px live, 2.5 px export) over the gradient fill
  3. The gradient spans only the actual data range, with correct gap handling — no gradient bleed into empty chart space
  4. Exported PNG matches the live gradient exactly, including theme (dark and light)
  5. The gradient renders correctly without desaturation artifacts in both dark and light themes
**Plans**: 2 plans
Plans:
- [x] 17-01-PLAN.md — Color interpolation lookup, gradient stop calculation, contiguous spans (pure math + TDD)
- [x] 17-02-PLAN.md — Win2D gradient brush rendering in ChartDrawing, ExportHelper line width, visual verification
**UI hint**: yes

### Phase 18: Settings Redesign
**Goal**: The Settings view uses a Segmented Control with four tabs, replacing the single-page layout
**Depends on**: Phase 16
**Requirements**: SETT-01, SETT-02, SETT-03, SETT-04, SETT-05, SETT-06, SETT-07, SETT-08
**Success Criteria** (what must be TRUE):
  1. A Segmented Control with General, Updates, Account, and About tabs (colored icon badges) is visible at the top of the Settings view at 360px width
  2. The General tab contains all existing settings in uniform 40px rows (label left, control right) with short time notation (30s, 1min, 5min, etc.)
  3. The Updates tab shows app version, pricing source info, and last pricing fetch timestamp
  4. The Account tab shows token status and the logout button; the About tab shows app name, version, GitHub link, and macOS original credits
  5. Switching tabs is smooth without page reload and all labels and content are localized in German and English
**Plans**: 2 plans
Plans:
- [ ] 18-01-PLAN.md — ViewModel tab switching + short labels + version/token properties, theme badge brushes, localization keys, unit tests
- [ ] 18-02-PLAN.md — Complete XAML rewrite with Segmented Control + 4 tab panels, visual verification
**UI hint**: yes

### Phase 19: Session Watcher Verification
**Goal**: FileSystemWatcher is confirmed to catch file-level session metadata changes
**Depends on**: Phase 16
**Requirements**: SESW-01
**Success Criteria** (what must be TRUE):
  1. Code review confirms NotifyFilter includes file-level change flags and IncludeSubdirectories is set correctly — or a targeted fix is applied if the configuration is wrong
  2. No regression is introduced to session refresh behavior
**Plans**: TBD

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
| 12. Model-Based Context Detection | v1.2 | 1/1 | Complete | 2026-04-12 |
| 13. Sonnet Context Window Setting | v1.2 | 2/2 | Complete | 2026-04-12 |
| 14. Session Management Polish | v1.2 | 1/1 | Complete | 2026-04-12 |
| 15. Footer Tooltip & Accessibility | v1.2 | 2/2 | Complete | 2026-04-13 |
| 16. Burn Rate Warning | v1.3 | 2/2 | Complete    | 2026-04-13 |
| 17. Chart Horizontal Gradient | v1.3 | 2/2 | Complete    | 2026-04-13 |
| 18. Settings Redesign | v1.3 | 0/2 | Planned | - |
| 19. Session Watcher Verification | v1.3 | 0/? | Not started | - |
