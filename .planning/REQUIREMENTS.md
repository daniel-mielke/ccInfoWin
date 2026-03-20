# Requirements: ccInfo Windows

**Defined:** 2026-03-09
**Core Value:** Developers can see their Claude usage limits (5-hour window, weekly quota, context window) at a glance in real-time, preventing unexpected throttling.

## v1 Requirements

Requirements for initial release. Each maps to roadmap phases.

### Authentication

- [x] **AUTH-01**: User can log in via embedded WebView2 showing claude.ai login page
- [x] **AUTH-02**: Session tokens are securely stored in Windows Credential Manager (DPAPI-encrypted)
- [x] **AUTH-03**: App validates stored tokens on startup and shows login if expired
- [x] **AUTH-04**: User can log out, clearing all stored tokens

### 5-Hour Usage Window

- [x] **5HUR-01**: Current usage percentage within the sliding 5-hour window is displayed
- [x] **5HUR-02**: Reset countdown shows remaining time until window resets (e.g. "2h 14min")
- [x] **5HUR-03**: Interactive area chart visualizes usage over the full 5-hour window
- [x] **5HUR-04**: Chart fill and line color interpolates by zone (green 0-50%, yellow 50-75%, orange 75-90%, red 90-100%)
- [x] **5HUR-05**: Glowing position indicator at current time point in chart
- [x] **5HUR-06**: Chart shows Y-axis labels (0%, 50%, 100%) and X-axis labels (0h-5h) with dashed threshold lines
- [x] **5HUR-07**: Usage history is persisted locally and survives app restart
- [x] **5HUR-08**: Automatic reset detection clears history when 5-hour window resets
- [x] **5HUR-09**: Chart colors are slightly desaturated in dark mode

### Weekly Usage Limit

- [x] **WEEK-01**: Weekly 7-day quota displayed as percentage with progress bar
- [x] **WEEK-02**: Separate Sonnet and Opus weekly usage displayed as individual progress bars
- [x] **WEEK-03**: Reset countdown and reset date/time shown for each weekly limit

### Context Window

- [x] **CTXW-01**: Main context window utilization shown with progress bar and percentage
- [x] **CTXW-02**: Model badge displayed next to context bar (e.g. "Opus 4.6", "Sonnet 4.6")
- [x] **CTXW-03**: Active subagent context windows shown with own model badge and progress bar
- [x] **CTXW-04**: Autocompact warning displayed at >= 95% context utilization (>= 90% for 200K models)
- [x] **CTXW-05**: When no active session exists, show 0% bar with "No active session" message

### Multi-Session Management

- [x] **SESS-01**: Dropdown lists all active Claude Code sessions with project name from JSONL working directory
- [x] **SESS-02**: Configurable activity threshold to hide/mark inactive sessions
- [x] **SESS-03**: No flickering of stale data when switching sessions
- [x] **SESS-04**: App does not auto-switch away from the currently selected session when it becomes inactive
- [x] **SESS-05**: Readable session names for Claude-internal projects instead of encoded directory paths

### Token Statistics

- [x] **TOKS-01**: Input and output token counters aggregated by session, today, week, month
- [x] **TOKS-02**: Tab bar (segmented control) switches between the four time periods with loading indicator
- [x] **TOKS-03**: Subagent tokens included in all time period aggregations
- [x] **TOKS-04**: JSONL entries deduplicated by messageId and requestId to prevent double-counting

### Cost Calculation

- [x] **COST-01**: Model prices fetched live from LiteLLM Pricing API with 12-hour cache
- [x] **COST-02**: Costs primarily from costUSD field in JSONL; fallback to token count * model price
- [x] **COST-03**: Estimated costs marked with tilde prefix (~) when model not in pricing database
- [x] **COST-04**: Tiered pricing applied for 1M-context models (higher input price above 200K tokens)
- [ ] **COST-05**: ~~Burn rate (token consumption speed) calculated and displayed~~ — Removed: feature does not exist in macOS reference app
- [x] **COST-06**: Settings show pricing data source (live API or fallback) and last fetch time

### Chart Export

- [x] **EXPT-01**: 5-hour chart exportable as dark PNG via system save dialog
- [x] **EXPT-02**: Thumbnail preview shown during export
- [x] **EXPT-03**: Option to copy chart directly to clipboard

### Settings

- [x] **SETT-01**: Configurable refresh interval (manual or automatic: 30s to 10min)
- [x] **SETT-02**: Autostart option to launch app at Windows login
- [x] **SETT-03**: Session activity threshold configuration
- [x] **SETT-04**: Language support for German and English (follows system language or manual selection)
- [x] **SETT-05**: Manual dark/light mode toggle with immediate application
- [x] **SETT-06**: Color mode persisted locally, restored on startup (default: dark)
- [x] **SETT-07**: Settings displayed in-app (same window, frame navigation) — no separate window

### Auto-Update

- [x] **UPDT-01**: Hourly check for new version via GitHub Releases API
- [x] **UPDT-02**: In-app banner (InfoBar) shown when update available with download link
- [x] **UPDT-03**: No intrusive OS toast notifications — banner only

### UI & Platform

- [x] **UIPF-01**: Persistent standalone window (not popup, not tray icon)
- [x] **UIPF-02**: Opaque background following light/dark color scheme
- [x] **UIPF-03**: Compact layout matching macOS MenuBar popup layout order
- [x] **UIPF-04**: Unified color thresholds for all progress bars (green/yellow/orange/red)
- [x] **UIPF-05**: Window position saved on close and restored on startup
- [x] **UIPF-06**: Fixed window width (~360px), not resizable, minimizable
- [x] **UIPF-07**: All interactive elements screen-reader compatible (accessibility labels)
- [x] **UIPF-08**: Runs on Windows 10 (19041+) and Windows 11 without admin rights

### Data Sources

- [x] **DATA-01**: Claude.ai API polled for 5-hour and weekly usage data via authenticated requests
- [x] **DATA-02**: Organization IDs percent-encoded in API URLs
- [x] **DATA-03**: JSONL files read from %USERPROFILE%\.claude\projects\ with streaming (last ~1MB only)
- [x] **DATA-04**: JSONL file changes detected via FileSystemWatcher with debouncing
- [x] **DATA-05**: LiteLLM pricing cache persisted locally with fallback to bundled prices

### Security

- [x] **SECU-01**: Zero hardcoded secrets in source code
- [x] **SECU-02**: Tokens stored exclusively in Windows Credential Manager (DPAPI)
- [x] **SECU-03**: No telemetry, no tracking, no data collection
- [x] **SECU-04**: Network communication only to claude.ai and raw.githubusercontent.com (HTTPS)
- [x] **SECU-05**: WebView2 user data isolated in %LOCALAPPDATA% directory
- [x] **SECU-06**: Comprehensive .gitignore preventing accidental secret exposure

### Distribution

- [x] **DIST-01**: Inno Setup EXE installer (per-user, no admin)
- [x] **DIST-02**: GitHub public repository with README, LICENSE (MIT), screenshots
- [x] **DIST-03**: Self-contained or framework-dependent publish with runtime prerequisite check

## v1.1 Requirements

Requirements for the v1.1 UI Polish & UX Improvements milestone. Based on `spec/ui-changes-after-v.1.7.1-macOS/instructions-ui-changes.md`.

### Layout & Structure

- [x] **LAYOUT-01**: User sees vertical padding of the main app matching the horizontal padding (equal spacing on all sides)
- [x] **LAYOUT-02**: User sees a localized "Active Session" / "Aktive Sitzung" label above the project dropdown, styled like other section headers
- [x] **LAYOUT-03**: User sees a horizontal separator below the project dropdown visually separating it from the next section
- [x] **LAYOUT-04**: User sees the "Context Window" section (including sub-agent row) positioned between "Active Session" and "5-Hour Window", with separators above and below
- [x] **LAYOUT-05**: User can scroll to reach the footer (footer is no longer fixed/sticky), with a horizontal separator above it
- [x] **LAYOUT-06**: User sees a horizontal separator between the "Models" row and the "Input" row in the Statistics section

### Visual Styles

- [x] **STYLE-01**: User sees all progress bars at a uniform height of 6 px (both foreground and background/track)
- [x] **STYLE-02**: User sees all progress bar backgrounds (track) in color rgba(128, 128, 128, 0.45), applied globally via style
- [x] **STYLE-03**: User sees the project dropdown and Statistics tab bar sharing the same background color and a CornerRadius of at least 8 px, in both light and dark mode
- [x] **STYLE-04**: User sees all model badges (e.g., "Sonnet 4.6", "Haiku 4.5") displayed as pill shapes with CornerRadius=999
- [x] **STYLE-05**: User sees the 5-hour chart axis labels (percentage and time values) in the same color as the timer text (clock icon label)

### Text & Formatting

- [x] **TEXT-01**: User sees timer values ≥ 24h displayed as "Xd Yh" format (e.g., "3d 22h") instead of hours/minutes, with localized unit abbreviations
- [x] **TEXT-02**: User sees the "Total" and "Cost (API equiv.)" labels in the Statistics section with the same text color as other statistic labels (e.g., "Cache Read", "Cache Write")
- [x] **TEXT-03**: User sees the "Cost (API equiv.)" label and its value with the same FontWeight as "Cache Read" (thinner weight)
- [x] **TEXT-04**: User sees consistent vertical spacing before "Total" row — matching the spacing between other statistic rows

### Interaction

- [x] **INTER-01**: User sees the logout button with a red background (from the existing error/100% progress bar color resource), white text, and a logout icon left of the label
- [x] **INTER-02**: User sees a login icon left of the login button label
- [x] **INTER-03**: User sees the refresh icon rotate 360° (not 180°) continuously until the API responds, always completing the current rotation before stopping

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
| AUTH-03 | Phase 1 | Complete |
| AUTH-04 | Phase 1 | Complete |
| 5HUR-01 | Phase 2 | Complete |
| 5HUR-02 | Phase 2 | Complete |
| 5HUR-03 | Phase 3 | Complete |
| 5HUR-04 | Phase 3 | Complete |
| 5HUR-05 | Phase 3 | Complete |
| 5HUR-06 | Phase 3 | Complete |
| 5HUR-07 | Phase 3 | Complete |
| 5HUR-08 | Phase 3 | Complete |
| 5HUR-09 | Phase 3 | Complete |
| WEEK-01 | Phase 2 | Complete |
| WEEK-02 | Phase 2 | Complete |
| WEEK-03 | Phase 2 | Complete |
| CTXW-01 | Phase 4 | Complete |
| CTXW-02 | Phase 4 | Complete |
| CTXW-03 | Phase 4 | Complete |
| CTXW-04 | Phase 4 | Complete |
| CTXW-05 | Phase 4 | Complete |
| SESS-01 | Phase 4 | Complete |
| SESS-02 | Phase 4 | Complete |
| SESS-03 | Phase 4 | Complete |
| SESS-04 | Phase 4 | Complete |
| SESS-05 | Phase 4 | Complete |
| TOKS-01 | Phase 4 | Complete |
| TOKS-02 | Phase 5 | Complete |
| TOKS-03 | Phase 5 | Complete |
| TOKS-04 | Phase 5 | Complete |
| COST-01 | Phase 5 | Complete |
| COST-02 | Phase 5 | Complete |
| COST-03 | Phase 5 | Complete |
| COST-04 | Phase 5 | Complete |
| COST-05 | Phase 5 | Removed (not in reference) |
| COST-06 | Phase 5 | Complete |
| EXPT-01 | Phase 6 | Complete |
| EXPT-02 | Phase 6 | Complete |
| EXPT-03 | Phase 6 | Complete |
| SETT-01 | Phase 2 | Complete |
| SETT-02 | Phase 6 | Complete |
| SETT-03 | Phase 4 | Complete |
| SETT-04 | Phase 6 | Complete |
| SETT-05 | Phase 2 | Complete |
| SETT-06 | Phase 2 | Complete |
| SETT-07 | Phase 6 | Complete |
| UPDT-01 | Phase 6 | Complete |
| UPDT-02 | Phase 6 | Complete |
| UPDT-03 | Phase 6 | Complete |
| UIPF-01 | Phase 1 | Complete |
| UIPF-02 | Phase 2 | Complete |
| UIPF-03 | Phase 1 | Complete |
| UIPF-04 | Phase 2 | Complete |
| UIPF-05 | Phase 6 | Complete |
| UIPF-06 | Phase 1 | Complete |
| UIPF-07 | Phase 6 | Complete |
| UIPF-08 | Phase 1 | Complete |
| DATA-01 | Phase 2 | Complete |
| DATA-02 | Phase 2 | Complete |
| DATA-03 | Phase 4 | Complete |
| DATA-04 | Phase 4 | Complete |
| DATA-05 | Phase 5 | Complete |
| SECU-01 | Phase 1 | Complete |
| SECU-02 | Phase 1 | Complete |
| SECU-03 | Phase 1 | Complete |
| SECU-04 | Phase 1 | Complete |
| SECU-05 | Phase 1 | Complete |
| SECU-06 | Phase 1 | Complete |
| DIST-01 | Phase 6 | Complete |
| DIST-02 | Phase 6 | Complete |
| DIST-03 | Phase 6 | Complete |

**Coverage (v1.0):**
- v1 requirements: 68 total
- Mapped to phases: 68
- Unmapped: 0 ✓

**Coverage (v1.1):**
- v1.1 requirements: 18 total
- Mapped to phases: 18
- Unmapped: 0 ✓

| Requirement | Phase | Status |
|-------------|-------|--------|
| LAYOUT-01 | Phase 9 | Complete |
| LAYOUT-02 | Phase 9 | Complete |
| LAYOUT-03 | Phase 9 | Complete |
| LAYOUT-04 | Phase 9 | Complete |
| LAYOUT-05 | Phase 9 | Complete |
| LAYOUT-06 | Phase 9 | Complete |
| STYLE-01 | Phase 10 | Complete |
| STYLE-02 | Phase 10 | Complete |
| STYLE-03 | Phase 10 | Complete |
| STYLE-04 | Phase 10 | Complete |
| STYLE-05 | Phase 10 | Complete |
| TEXT-01 | Phase 11 | Complete |
| TEXT-02 | Phase 10 | Complete |
| TEXT-03 | Phase 10 | Complete |
| TEXT-04 | Phase 10 | Complete |
| INTER-01 | Phase 11 | Complete |
| INTER-02 | Phase 11 | Complete |
| INTER-03 | Phase 11 | Complete |

---
*Requirements defined: 2026-03-09*
*Last updated: 2026-03-19 after v1.1 roadmap created (Phases 9-11)*
