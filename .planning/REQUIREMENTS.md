# Requirements: ccInfo Windows

**Defined:** 2026-03-09
**Core Value:** Developers can see their Claude usage limits (5-hour window, weekly quota, context window) at a glance in real-time, preventing unexpected throttling.

## v1 Requirements

Requirements for initial release. Each maps to roadmap phases.

### Authentication

- [x] **AUTH-01**: User can log in via embedded WebView2 showing claude.ai login page
- [x] **AUTH-02**: Session tokens are securely stored in Windows Credential Manager (DPAPI-encrypted)
- [ ] **AUTH-03**: App validates stored tokens on startup and shows login if expired
- [ ] **AUTH-04**: User can log out, clearing all stored tokens

### 5-Hour Usage Window

- [ ] **5HUR-01**: Current usage percentage within the sliding 5-hour window is displayed
- [ ] **5HUR-02**: Reset countdown shows remaining time until window resets (e.g. "2h 14min")
- [ ] **5HUR-03**: Interactive area chart visualizes usage over the full 5-hour window
- [ ] **5HUR-04**: Chart fill and line color interpolates by zone (green 0-50%, yellow 50-75%, orange 75-90%, red 90-100%)
- [ ] **5HUR-05**: Glowing position indicator at current time point in chart
- [ ] **5HUR-06**: Chart shows Y-axis labels (0%, 50%, 100%) and X-axis labels (0h-5h) with dashed threshold lines
- [ ] **5HUR-07**: Usage history is persisted locally and survives app restart
- [ ] **5HUR-08**: Automatic reset detection clears history when 5-hour window resets
- [ ] **5HUR-09**: Chart colors are slightly desaturated in dark mode

### Weekly Usage Limit

- [ ] **WEEK-01**: Weekly 7-day quota displayed as percentage with progress bar
- [ ] **WEEK-02**: Separate Sonnet and Opus weekly usage displayed as individual progress bars
- [ ] **WEEK-03**: Reset countdown and reset date/time shown for each weekly limit

### Context Window

- [ ] **CTXW-01**: Main context window utilization shown with progress bar and percentage
- [ ] **CTXW-02**: Model badge displayed next to context bar (e.g. "Opus 4.6", "Sonnet 4.6")
- [ ] **CTXW-03**: Active subagent context windows shown with own model badge and progress bar
- [ ] **CTXW-04**: Autocompact warning displayed at >= 95% context utilization (>= 90% for 200K models)
- [ ] **CTXW-05**: When no active session exists, show 0% bar with "No active session" message

### Multi-Session Management

- [ ] **SESS-01**: Dropdown lists all active Claude Code sessions with project name from JSONL working directory
- [ ] **SESS-02**: Configurable activity threshold to hide/mark inactive sessions
- [ ] **SESS-03**: No flickering of stale data when switching sessions
- [ ] **SESS-04**: App does not auto-switch away from the currently selected session when it becomes inactive
- [ ] **SESS-05**: Readable session names for Claude-internal projects instead of encoded directory paths

### Token Statistics

- [ ] **TOKS-01**: Input and output token counters aggregated by session, today, week, month
- [ ] **TOKS-02**: Tab bar (segmented control) switches between the four time periods with loading indicator
- [ ] **TOKS-03**: Subagent tokens included in all time period aggregations
- [ ] **TOKS-04**: JSONL entries deduplicated by messageId and requestId to prevent double-counting

### Cost Calculation

- [ ] **COST-01**: Model prices fetched live from LiteLLM Pricing API with 12-hour cache
- [ ] **COST-02**: Costs primarily from costUSD field in JSONL; fallback to token count * model price
- [ ] **COST-03**: Estimated costs marked with tilde prefix (~) when model not in pricing database
- [ ] **COST-04**: Tiered pricing applied for 1M-context models (higher input price above 200K tokens)
- [ ] **COST-05**: Burn rate (token consumption speed) calculated and displayed
- [ ] **COST-06**: Settings show pricing data source (live API or fallback) and last fetch time

### Chart Export

- [ ] **EXPT-01**: 5-hour chart exportable as dark PNG via system save dialog
- [ ] **EXPT-02**: Thumbnail preview shown during export
- [ ] **EXPT-03**: Option to copy chart directly to clipboard

### Settings

- [ ] **SETT-01**: Configurable refresh interval (manual or automatic: 30s to 10min)
- [ ] **SETT-02**: Autostart option to launch app at Windows login
- [ ] **SETT-03**: Session activity threshold configuration
- [ ] **SETT-04**: Language support for German and English (follows system language or manual selection)
- [ ] **SETT-05**: Manual dark/light mode toggle with immediate application
- [ ] **SETT-06**: Color mode persisted locally, restored on startup (default: dark)
- [ ] **SETT-07**: Settings displayed in-app (same window, frame navigation) — no separate window

### Auto-Update

- [ ] **UPDT-01**: Hourly check for new version via GitHub Releases API
- [ ] **UPDT-02**: In-app banner (InfoBar) shown when update available with download link
- [ ] **UPDT-03**: No intrusive OS toast notifications — banner only

### UI & Platform

- [x] **UIPF-01**: Persistent standalone window (not popup, not tray icon)
- [ ] **UIPF-02**: Opaque background following light/dark color scheme
- [x] **UIPF-03**: Compact layout matching macOS MenuBar popup layout order
- [ ] **UIPF-04**: Unified color thresholds for all progress bars (green/yellow/orange/red)
- [ ] **UIPF-05**: Window position saved on close and restored on startup
- [x] **UIPF-06**: Fixed window width (~360px), not resizable, minimizable
- [ ] **UIPF-07**: All interactive elements screen-reader compatible (accessibility labels)
- [x] **UIPF-08**: Runs on Windows 10 (19041+) and Windows 11 without admin rights

### Data Sources

- [ ] **DATA-01**: Claude.ai API polled for 5-hour and weekly usage data via authenticated requests
- [ ] **DATA-02**: Organization IDs percent-encoded in API URLs
- [ ] **DATA-03**: JSONL files read from %USERPROFILE%\.claude\projects\ with streaming (last ~1MB only)
- [ ] **DATA-04**: JSONL file changes detected via FileSystemWatcher with debouncing
- [ ] **DATA-05**: LiteLLM pricing cache persisted locally with fallback to bundled prices

### Security

- [x] **SECU-01**: Zero hardcoded secrets in source code
- [x] **SECU-02**: Tokens stored exclusively in Windows Credential Manager (DPAPI)
- [ ] **SECU-03**: No telemetry, no tracking, no data collection
- [x] **SECU-04**: Network communication only to claude.ai and raw.githubusercontent.com (HTTPS)
- [x] **SECU-05**: WebView2 user data isolated in %LOCALAPPDATA% directory
- [x] **SECU-06**: Comprehensive .gitignore preventing accidental secret exposure

### Distribution

- [ ] **DIST-01**: Inno Setup EXE installer (per-user, no admin)
- [ ] **DIST-02**: GitHub public repository with README, LICENSE (MIT), screenshots
- [ ] **DIST-03**: Self-contained or framework-dependent publish with runtime prerequisite check

## v2 Requirements

Deferred to future release. Tracked but not in current roadmap.

### Enhancements

- **V2-01**: System tray icon with quick status overview
- **V2-02**: Keyboard shortcuts for common actions
- **V2-03**: Configurable color thresholds for progress bars
- **V2-04**: Historical usage trends (daily/weekly graphs)
- **V2-05**: Migration to .NET 10 LTS when WinAppSDK confirms compatibility

## Out of Scope

Explicitly excluded. Documented to prevent scope creep.

| Feature | Reason |
|---------|--------|
| ML-based usage predictions | Over-engineered for a desktop widget, predictions are noisy |
| Multi-account support | Adds auth/UI complexity, target audience is single developer |
| Mobile companion app | Claude Code is a desktop tool, monitor at your desk |
| GitHub activity heatmaps | Low information density, token stats covers the need |
| JSON/CSV data export | Niche feature, chart PNG export covers sharing use case |
| OS toast notifications | Unreliable on Windows, in-app banner sufficient (window always visible) |
| Transparent/blur background | Unreliable across Windows versions, accessibility issues |
| System tray taskbar metrics | No good Windows API, all metrics visible in main window |
| OpenAI Codex / other LLM support | Dilutes focus, different APIs and data formats |
| SQLite database | Overkill for few KB of data, JSON files sufficient |
| Separate settings window | In-app navigation preferred for persistent-window app |

## Traceability

Which phases cover which requirements. Updated during roadmap creation.

| Requirement | Phase | Status |
|-------------|-------|--------|
| AUTH-01 | Phase 1 | Complete |
| AUTH-02 | Phase 1 | Complete |
| AUTH-03 | Phase 1 | Pending |
| AUTH-04 | Phase 1 | Pending |
| 5HUR-01 | Phase 2 | Pending |
| 5HUR-02 | Phase 2 | Pending |
| 5HUR-03 | Phase 3 | Pending |
| 5HUR-04 | Phase 3 | Pending |
| 5HUR-05 | Phase 3 | Pending |
| 5HUR-06 | Phase 3 | Pending |
| 5HUR-07 | Phase 3 | Pending |
| 5HUR-08 | Phase 3 | Pending |
| 5HUR-09 | Phase 3 | Pending |
| WEEK-01 | Phase 2 | Pending |
| WEEK-02 | Phase 2 | Pending |
| WEEK-03 | Phase 2 | Pending |
| CTXW-01 | Phase 4 | Pending |
| CTXW-02 | Phase 4 | Pending |
| CTXW-03 | Phase 4 | Pending |
| CTXW-04 | Phase 4 | Pending |
| CTXW-05 | Phase 4 | Pending |
| SESS-01 | Phase 4 | Pending |
| SESS-02 | Phase 4 | Pending |
| SESS-03 | Phase 4 | Pending |
| SESS-04 | Phase 4 | Pending |
| SESS-05 | Phase 4 | Pending |
| TOKS-01 | Phase 4 | Pending |
| TOKS-02 | Phase 5 | Pending |
| TOKS-03 | Phase 5 | Pending |
| TOKS-04 | Phase 5 | Pending |
| COST-01 | Phase 5 | Pending |
| COST-02 | Phase 5 | Pending |
| COST-03 | Phase 5 | Pending |
| COST-04 | Phase 5 | Pending |
| COST-05 | Phase 5 | Pending |
| COST-06 | Phase 5 | Pending |
| EXPT-01 | Phase 6 | Pending |
| EXPT-02 | Phase 6 | Pending |
| EXPT-03 | Phase 6 | Pending |
| SETT-01 | Phase 2 | Pending |
| SETT-02 | Phase 6 | Pending |
| SETT-03 | Phase 4 | Pending |
| SETT-04 | Phase 6 | Pending |
| SETT-05 | Phase 2 | Pending |
| SETT-06 | Phase 2 | Pending |
| SETT-07 | Phase 6 | Pending |
| UPDT-01 | Phase 6 | Pending |
| UPDT-02 | Phase 6 | Pending |
| UPDT-03 | Phase 6 | Pending |
| UIPF-01 | Phase 1 | Complete |
| UIPF-02 | Phase 2 | Pending |
| UIPF-03 | Phase 1 | Complete |
| UIPF-04 | Phase 2 | Pending |
| UIPF-05 | Phase 6 | Pending |
| UIPF-06 | Phase 1 | Complete |
| UIPF-07 | Phase 6 | Pending |
| UIPF-08 | Phase 1 | Complete |
| DATA-01 | Phase 2 | Pending |
| DATA-02 | Phase 2 | Pending |
| DATA-03 | Phase 4 | Pending |
| DATA-04 | Phase 4 | Pending |
| DATA-05 | Phase 5 | Pending |
| SECU-01 | Phase 1 | Complete |
| SECU-02 | Phase 1 | Complete |
| SECU-03 | Phase 1 | Pending |
| SECU-04 | Phase 1 | Complete |
| SECU-05 | Phase 1 | Complete |
| SECU-06 | Phase 1 | Complete |
| DIST-01 | Phase 6 | Pending |
| DIST-02 | Phase 6 | Pending |
| DIST-03 | Phase 6 | Pending |

**Coverage:**
- v1 requirements: 68 total
- Mapped to phases: 68
- Unmapped: 0

---
*Requirements defined: 2026-03-09*
*Last updated: 2026-03-09 after roadmap creation*
