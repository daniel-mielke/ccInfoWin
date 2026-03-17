# Roadmap: ccInfo Windows

## Overview

ccInfoWin is a Windows port of the macOS ccInfo app for real-time Claude Code usage monitoring. The roadmap follows the two independent data paths (API polling and JSONL file watching) as the natural architectural seam. Authentication gates all API features, so it comes first. The signature area chart gets its own phase due to Win2D complexity. JSONL pipeline enables sessions, context, and tokens. Cost analytics builds on token data. Export, polish, and distribution close out the release.

## Phases

**Phase Numbering:**
- Integer phases (1, 2, 3): Planned milestone work
- Decimal phases (2.1, 2.2): Urgent insertions (marked with INSERTED)

Decimal phases appear between their surrounding integers in numeric order.

- [x] **Phase 1: Foundation and Authentication** - Project scaffold, navigation shell, WebView2 login, credential storage, security baseline (completed 2026-03-09)
- [x] **Phase 2: Core Monitoring Dashboard** - API polling for 5-hour and weekly usage display with progress bars, theme support, refresh settings (completed 2026-03-11)
- [x] **Phase 3: Area Chart** - Win2D interactive area chart with color zones, glow indicator, usage history persistence (gap closure in progress) (completed 2026-03-11)
- [x] **Phase 4: Local Data Pipeline** - JSONL file watching, multi-session management, context window status, token counting (completed 2026-03-11)
- [x] **Phase 5: Cost Analytics** - LiteLLM pricing integration, cost calculation with tiered pricing, burn rate, token stats UI (completed 2026-03-16)
- [x] **Phase 6: Export, Polish, and Distribution** - Chart export, localization, autostart, auto-update, accessibility, Inno Setup installer (completed 2026-03-17)
- [x] **Phase 7: Security Fix & Dead Code Cleanup** - WebViewBridge.Reset() on logout, remove dead code (CostCalculator, unused messages, dead properties) (completed 2026-03-17)
- [ ] **Phase 8: Documentation Hygiene & Verification** - Update stale REQUIREMENTS.md checkboxes, create missing VERIFICATION.md for Phase 02 and 04

## Phase Details

### Phase 1: Foundation and Authentication
**Goal**: User can launch the app, authenticate with Claude, and have credentials securely stored -- the app is a working shell that gates API access
**Depends on**: Nothing (first phase)
**Requirements**: AUTH-01, AUTH-02, AUTH-03, AUTH-04, SECU-01, SECU-02, SECU-03, SECU-04, SECU-05, SECU-06, UIPF-01, UIPF-03, UIPF-06, UIPF-08
**Success Criteria** (what must be TRUE):
  1. User can launch the app and see a persistent standalone window with compact layout and fixed width
  2. User can log in via embedded WebView2 showing the claude.ai login page
  3. User can close and reopen the app with stored credentials automatically validated (login skipped if valid)
  4. User can log out and all stored tokens are cleared from Windows Credential Manager
  5. App runs on Windows 10 (19041+) and Windows 11 without admin rights, with zero hardcoded secrets and no telemetry
**Plans**: 3 plans

Plans:
- [x] 01-01-PLAN.md — Project scaffold, DI container, navigation shell, window management, project infrastructure
- [ ] 01-02-PLAN.md — WebView2 login flow, cookie extraction, credential storage
- [ ] 01-03-PLAN.md — Startup token validation, logout, expiry handling, end-to-end verification

### Phase 2: Core Monitoring Dashboard
**Goal**: User can see their 5-hour usage percentage, weekly quota, and reset countdowns at a glance with auto-refresh and theme support
**Depends on**: Phase 1
**Requirements**: 5HUR-01, 5HUR-02, WEEK-01, WEEK-02, WEEK-03, DATA-01, DATA-02, UIPF-02, UIPF-04, SETT-01, SETT-05, SETT-06
**Success Criteria** (what must be TRUE):
  1. User sees current 5-hour window usage percentage and reset countdown timer updating in real-time
  2. User sees weekly usage quota with separate Sonnet and Opus progress bars and reset dates
  3. All progress bars use unified color thresholds (green/yellow/orange/red) with opaque background in current theme
  4. User can configure refresh interval and toggle between dark and light mode with immediate effect (persisted across restarts)
**Plans**: 4 plans

Plans:
- [ ] 02-01-PLAN.md — Models, interfaces, theme resources, helpers, test infrastructure
- [ ] 02-02-PLAN.md — API service with retry/caching, credential extension for org ID, login flow update
- [ ] 02-03-PLAN.md — Dashboard UI (MainView sections, progress bars, countdowns, footer, polling)
- [ ] 02-04-PLAN.md — Settings page (refresh interval, theme toggle, logout), theme application

### Phase 3: Area Chart
**Goal**: User can visualize their 5-hour usage history through an interactive, color-coded area chart that persists across app restarts
**Depends on**: Phase 2
**Requirements**: 5HUR-03, 5HUR-04, 5HUR-05, 5HUR-06, 5HUR-07, 5HUR-08, 5HUR-09
**Success Criteria** (what must be TRUE):
  1. Interactive area chart displays usage over the full 5-hour window with Y-axis (0-100%) and X-axis (0h-5h) labels and dashed threshold lines
  2. Chart fill color interpolates by zone (green/yellow/orange/red) with slightly desaturated colors in dark mode
  3. Glowing position indicator shows current time point on the chart
  4. Usage history survives app restart and automatically clears when the 5-hour window resets
**Plans**: 3 plans

Plans:
- [x] 03-01-PLAN.md — Usage history data model, persistence service, Win2D NuGet, DI registration
- [x] 03-02-PLAN.md — Chart renderer, Win2D CanvasControl, Draw handler, poll cycle wiring, reset detection
- [ ] 03-03-PLAN.md — Gap closure: fix single-point segment rendering, right-edge extension, stale history on startup

### Phase 4: Local Data Pipeline
**Goal**: User can see context window status, switch between sessions, and view token counts -- all derived from local JSONL files without API dependency
**Depends on**: Phase 1
**Requirements**: DATA-03, DATA-04, SESS-01, SESS-02, SESS-03, SESS-04, SESS-05, CTXW-01, CTXW-02, CTXW-03, CTXW-04, CTXW-05, TOKS-01, SETT-03
**Success Criteria** (what must be TRUE):
  1. User sees a session dropdown listing all active Claude Code sessions with readable project names, and can switch sessions without flicker or auto-switching away from current selection
  2. User sees main context window utilization with progress bar, percentage, and model badge, plus subagent context bars when active
  3. Autocompact warning appears at >= 95% context utilization (>= 90% for 200K models), and "No active session" shown when none exists
  4. User sees input/output token counters aggregated by session with JSONL deduplication preventing double-counting
  5. JSONL file changes are detected automatically via FileSystemWatcher with debouncing, reading only the last ~1MB
**Plans**: 3 plans

Plans:
- [ ] 04-01-PLAN.md — Models, interfaces, helpers (TokenFormatter, ModelContextLimits, SessionNameHelper), unit tests
- [ ] 04-02-PLAN.md — JsonlService implementation (tail read, JSONL parsing, FileSystemWatcher, cache, DI registration)
- [ ] 04-03-PLAN.md — Dashboard UI (session dropdown, context window section, token counters, settings threshold)

### Phase 5: Cost Analytics
**Goal**: User can see what their Claude usage costs with live pricing, time-period breakdowns, and burn rate
**Depends on**: Phase 4
**Requirements**: TOKS-02, TOKS-03, TOKS-04, COST-01, COST-02, COST-03, COST-04, COST-05, COST-06
**Success Criteria** (what must be TRUE):
  1. User can switch between session/today/week/month token aggregations via tab bar with loading indicator, including subagent tokens
  2. Costs are calculated from JSONL costUSD field with fallback to token count * live LiteLLM price, and estimated costs are marked with tilde (~)
  3. Tiered pricing is applied for 1M-context models and burn rate (token consumption speed) is displayed
  4. Settings show pricing data source (live API or fallback) and last fetch time, with LiteLLM cache persisted locally
**Plans**: 2 plans

Plans:
- [ ] 05-01-PLAN.md — Models, pricing service, cost calculation, burn rate, time-period aggregation, unit tests
- [ ] 05-02-PLAN.md — STATISTIKEN UI (Segmented tab bar, data table, shimmer, settings pricing info, DI wiring)

### Phase 6: Export, Polish, and Distribution
**Goal**: App is feature-complete, localized, accessible, and distributed as a standalone installer on GitHub
**Depends on**: Phase 3, Phase 5
**Requirements**: EXPT-01, EXPT-02, EXPT-03, SETT-02, SETT-04, SETT-07, UPDT-01, UPDT-02, UPDT-03, UIPF-05, UIPF-07, DIST-01, DIST-02, DIST-03
**Success Criteria** (what must be TRUE):
  1. User can export the 5-hour chart as PNG via save dialog with thumbnail preview, or copy it directly to clipboard
  2. User can switch between German and English (follows system language or manual selection) with settings displayed in-app via frame navigation
  3. App checks hourly for updates and shows in-app banner with download link (no intrusive OS notifications), and can autostart at Windows login
  4. Window position is saved on close and restored on startup, all interactive elements have accessibility labels
  5. Inno Setup per-user installer (no admin) is available on GitHub with README, LICENSE (MIT), and screenshots
**Plans**: 4 plans

Plans:
- [ ] 06-01-PLAN.md — ExportHelper (Win2D offscreen PNG), UpdateService (GitHub Releases), RegistryHelper (autostart), AppSettings extensions
- [ ] 06-02-PLAN.md — WinUI3Localizer setup, .resw resource files (DE/EN), x:Uid conversion on all XAML
- [ ] 06-03-PLAN.md — UI wiring: export button + MenuFlyout, update InfoBar, settings additions (autostart/language), accessibility labels
- [ ] 06-04-PLAN.md — Distribution: dotnet publish config, Inno Setup installer, README.md, LICENSE (MIT)

### Phase 7: Security Fix & Dead Code Cleanup
**Goal**: Logout fully cleans up WebViewBridge state and all dead code is removed — the codebase matches what's actually wired
**Depends on**: Phase 6
**Requirements**: AUTH-04, SECU-03
**Gap Closure:** Closes integration gap (WebViewBridge.Reset not called on logout), flow gap (Logout security cleanup), and dead code tech debt from audit
**Success Criteria** (what must be TRUE):
  1. Logout calls WebViewBridge.Reset() so CoreWebView2 reference and WebMessageReceived handler are released
  2. CostCalculator.cs removed (logic already in JsonlService.AggregateEntryLog)
  3. JsonlDataUpdatedMessage and SessionSelectedMessage removed (never sent/received)
  4. _inputTokensText and _outputTokensText removed from MainViewModel (never bound)
**Plans**: 1 plan

Plans:
- [ ] 07-01-PLAN.md — WebViewBridge.Reset() on logout, pending TCS drain, dead code removal (CostCalculator, unused messages, dead properties)

### Phase 8: Documentation Hygiene & Verification
**Goal**: All REQUIREMENTS.md checkboxes match actual implementation state and missing VERIFICATION.md files are created
**Depends on**: Phase 7
**Requirements**: CTXW-05, SETT-05, EXPT-01, EXPT-02, EXPT-03, SETT-02, UPDT-01, UIPF-05
**Gap Closure:** Closes 8 stale requirement checkboxes and 2 missing phase verification reports from audit
**Success Criteria** (what must be TRUE):
  1. All 7 stale REQUIREMENTS.md checkboxes updated to [x] (EXPT-01/02/03, SETT-02, SETT-05, UPDT-01, UIPF-05)
  2. CTXW-05 checkbox updated to [x] with evidence from code
  3. Phase 02 VERIFICATION.md created with requirement cross-reference
  4. Phase 04 VERIFICATION.md created with requirement cross-reference
**Plans**: TBD

## Progress

**Execution Order:**
Phases execute in numeric order: 1 -> 2 -> 3 -> 4 -> 5 -> 6 -> 7 -> 8
Note: Phase 4 depends on Phase 1 (not Phase 3), so Phases 3 and 4 could theoretically execute in parallel.
Gap closure phases 7-8 added from milestone audit (2026-03-17).

| Phase | Plans Complete | Status | Completed |
|-------|----------------|--------|-----------|
| 1. Foundation and Authentication | 3/3 | Complete   | 2026-03-09 |
| 2. Core Monitoring Dashboard | 4/4 | Complete   | 2026-03-11 |
| 3. Area Chart | 3/3 | Complete   | 2026-03-11 |
| 4. Local Data Pipeline | 3/3 | Complete   | 2026-03-11 |
| 5. Cost Analytics | 2/2 | Complete   | 2026-03-16 |
| 6. Export, Polish, and Distribution | 4/4 | Complete   | 2026-03-17 |
| 7. Security Fix & Dead Code Cleanup | 1/1 | Complete   | 2026-03-17 |
| 8. Documentation Hygiene & Verification | 0/0 | Not Started | — |
