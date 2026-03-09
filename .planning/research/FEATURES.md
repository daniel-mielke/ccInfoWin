# Feature Landscape

**Domain:** Desktop LLM usage monitoring (Claude Code usage tracker for Windows)
**Researched:** 2026-03-09

## Competitive Landscape

The Claude usage monitoring space has exploded since mid-2025. Key competitors analyzed:

| Tool | Platform | Type | Key Differentiator |
|------|----------|------|-------------------|
| ccInfo (reference) | macOS | Native menu bar | Full-featured, our port target |
| claude-usage-widget | Windows + macOS | Electron widget | Only Windows competitor, basic features |
| SessionWatcher | macOS | Native menu bar | Zero-config, paid ($) |
| CUStats | macOS + iOS/Android | Native multi-platform | Multi-account, mobile companion, activity charts |
| ClaudeMeter | macOS | Native menu bar | JSON export, lightweight |
| Claude-Code-Usage-Monitor | Terminal (cross-platform) | Python CLI | ML predictions, P90 detection |
| ccusage | Terminal (cross-platform) | Node CLI | JSONL analysis, date filtering |

**Windows gap:** Only `claude-usage-widget` targets Windows, and it is a basic Electron widget with session/weekly progress bars, countdown timers, and threshold warnings. No JSONL parsing, no token statistics, no cost analysis, no chart visualization. ccInfoWin would be the most feature-complete Windows Claude monitor by a wide margin.

---

## Table Stakes

Features users expect. Missing = product feels incomplete compared to the reference app and competitors.

| Feature | Why Expected | Complexity | Spec Reference |
|---------|--------------|------------|----------------|
| 5-hour window percentage display | Every single competitor shows this. Core metric for avoiding throttling. | Low | FA-020 |
| Reset countdown timer | All competitors show time until reset. Without it, the percentage is less actionable. | Low | FA-021 |
| Weekly usage limit display | Shown by all competitors. Second most important limit metric. | Low | FA-030 |
| Color-coded progress bars (green/yellow/orange/red) | Visual convention across all tools. Users scan colors, not numbers. | Low | NF-013 |
| Secure authentication via WebView2 | Required to access claude.ai API. Every tool that uses the API does this. | Med | FA-010, FA-011 |
| Auto-refresh on interval | All competitors auto-refresh. Manual-only refresh would feel broken. | Low | FA-090 |
| Dark mode | Every competitor supports dark mode. Developer tools default dark. | Low | FA-094, FA-095 |
| Token credential storage (Windows Credential Manager) | Security baseline. Competitors use Keychain (macOS). Plaintext storage is unacceptable. | Med | FA-011, NF-032 |
| Session token re-validation on startup | Competitors handle expired sessions gracefully. Broken auth on launch = uninstall. | Low | FA-012 |
| Logout capability | Basic account hygiene. All competitors offer this. | Low | FA-013 |
| Persistent window position | Desktop widgets/monitors remember position. Losing position on restart is annoying. | Low | NF-010 |
| No telemetry / privacy-first | Every competitor in this space advertises zero telemetry. Privacy is a hygiene factor for dev tools. | Low | NF-030 |

---

## Differentiators

Features that set ccInfoWin apart from competitors. Not expected by all users, but create real value.

### Tier 1: Strong Differentiators (vs. Windows competition)

| Feature | Value Proposition | Complexity | Spec Reference |
|---------|-------------------|------------|----------------|
| Interactive area chart (5h window) | No Windows tool has this. Visual history of usage over time, not just current %. Signature feature of ccInfo. | High | FA-022, FA-023, FA-024, FA-025 |
| Context window status with subagents | Unique to ccInfo. Shows how close you are to autocompact. Prevents context loss mid-conversation. | Med | FA-040, FA-041, FA-042, FA-043 |
| Multi-session management | No Windows competitor tracks multiple Claude Code sessions. Essential for devs working on multiple projects. | Med | FA-050, FA-051, FA-054 |
| Token statistics by time period | Session/today/week/month aggregation. Only CLI tools (ccusage) offer this, no GUI on Windows. | Med | FA-060, FA-061 |
| Cost calculation with live pricing | Real-time cost awareness. Only the Python CLI tool does ML-based cost analysis. No Windows GUI does this. | High | FA-070, FA-071, FA-073 |
| Model-specific breakdown (Sonnet/Opus) | Shows which model eats your quota. Only ccInfo and CUStats break this down visually. | Med | FA-031, FA-041 |

### Tier 2: Nice-to-Have Differentiators

| Feature | Value Proposition | Complexity | Spec Reference |
|---------|-------------------|------------|----------------|
| Burn rate display | "At this rate you'll hit the limit in X minutes." Actionable insight, few tools offer this. | Med | FA-074 |
| Chart export (PNG + clipboard) | Share usage charts in Slack/Teams. Unique to ccInfo among GUI tools. | Med | FA-080, FA-081, FA-082 |
| Tiered pricing for extended context | Accurate cost for 1M-context models. Only relevant for Max subscribers but shows attention to detail. | Med | FA-073 |
| Autocompact warning at 95% context | Proactive warning before Claude auto-compresses context. Prevents surprise context loss. | Low | FA-043 |
| Localization (DE/EN) | Broader accessibility. No competitor offers multi-language. Low effort for two languages. | Low | FA-093 |
| Light mode | Some devs prefer light themes. Most competitors offer this toggle. | Low | FA-094 |
| Autostart with Windows | Convenience for always-on monitoring. claude-usage-widget also offers this. | Low | FA-091 |
| Auto-update check with banner | Keeps users on latest version without manual checking. Standard for desktop apps. | Med | FA-100, FA-101, FA-102 |

---

## Anti-Features

Features to explicitly NOT build. Either out of scope, harmful to UX, or not worth the complexity.

| Anti-Feature | Why Avoid | What to Do Instead |
|--------------|-----------|-------------------|
| System tray / taskbar integration | No standard Windows API for rich metrics in taskbar. Complexity vs. value is terrible. The persistent window IS the dashboard. | Keep all metrics in the main window (PROJECT.md decision) |
| Transparent/blur background | Unreliable across Windows versions, GPU-dependent, accessibility nightmare. macOS vibrancy has no good Windows equivalent. | Opaque background, clean dark/light themes (PROJECT.md decision) |
| ML-based usage predictions | The Python CLI tool does this with P90 percentile calculations. Over-engineered for a desktop widget. Predictions are noisy and create false confidence. | Show burn rate (simple math) instead of ML predictions |
| Multi-account support | CUStats does this. Adds auth complexity, UI complexity, edge cases. Target audience (individual dev) rarely needs this. | Single account, clean logout/re-login flow |
| Mobile companion app | CUStats has iOS/Android. Massive scope expansion. Claude Code is a desktop tool -- you monitor it at your desk. | Stay desktop-only |
| GitHub-style activity heatmaps | CUStats has these. Looks nice but low information density for the use case. Users need real-time status, not historical heatmaps. | Token stats by time period (session/day/week/month) covers the same need more concisely |
| JSON/CSV data export | ClaudeMeter offers JSON export. Niche feature, adds file format maintenance burden. Chart PNG export covers the sharing use case. | Chart PNG export + clipboard copy |
| Notification toasts for usage warnings | SessionWatcher and CUStats push notifications. Windows toast notifications are unreliable, often ignored, and require packaging complexity. | Color-coded in-app warnings (the window is always visible anyway) |
| Configurable color thresholds | claude-usage-widget lets users set amber/red thresholds. Adds settings complexity for marginal value. The 50/75/90 breakpoints match Claude's actual behavior. | Fixed thresholds matching Claude's rate limit zones |
| Separate settings window | macOS ccInfo opens a separate settings window. On Windows, a persistent-window app should navigate in-place. | In-app settings view with back navigation (NF-010a) |
| OpenAI Codex / other LLM support | CUStats monitors Codex too. Dilutes focus, adds API complexity, different data formats. | Claude-only. Do one thing well. |
| SQLite or database storage | Overkill for the data volumes involved (few KB of usage data). | JSON files via System.Text.Json (PROJECT.md decision) |

---

## Feature Dependencies

```
FA-010 (Auth/Login)
  +-> FA-020 (5h percentage) -- requires API access
  +-> FA-030 (Weekly limit) -- requires API access
  +-> FA-022 (Area chart) -- requires 5h data
       +-> FA-080 (Chart export) -- requires rendered chart

DS-020 (JSONL file reading)
  +-> FA-040 (Context window) -- requires session data
  +-> FA-050 (Multi-session) -- requires session discovery
  +-> FA-060 (Token stats) -- requires token data from JSONL
       +-> FA-070 (Cost calc) -- requires token counts + pricing
            +-> DS-030 (LiteLLM API) -- pricing data source

FA-094 (Dark/Light mode)
  +-> FA-028 (Chart dark mode adaptation) -- chart colors depend on theme

FA-090 (Settings infrastructure)
  +-> FA-091 (Autostart)
  +-> FA-092 (Session threshold)
  +-> FA-093 (Language)
```

**Critical path:** Authentication must work first, then API data fetch, then visualization. JSONL parsing is a parallel independent track that enables session/token/cost features.

---

## MVP Recommendation

### Phase 1: Core monitoring (must ship)
1. Authentication via WebView2 + credential storage (FA-010 through FA-013)
2. 5-hour window with percentage + countdown (FA-020, FA-021)
3. Weekly limit display (FA-030)
4. Color-coded progress bars (NF-013)
5. Dark/light mode (FA-094, FA-095)
6. Basic settings (refresh interval) (FA-090)

**Rationale:** This matches what `claude-usage-widget` offers but with native WinUI 3 quality. Gets something usable out the door.

### Phase 2: Signature features (what makes it ccInfo)
1. Interactive area chart with color zones (FA-022 through FA-028) -- THE differentiator
2. Model-specific weekly breakdown (FA-031)
3. Context window status + subagents (FA-040 through FA-044)
4. Multi-session management (FA-050 through FA-054)

**Rationale:** These are the features that make ccInfoWin more than "just another progress bar widget." The area chart is the visual signature of ccInfo.

### Phase 3: Analytics and polish
1. Token statistics with time periods (FA-060 through FA-063)
2. Cost calculation with live pricing (FA-070 through FA-075)
3. Burn rate (FA-074)
4. Chart export (FA-080 through FA-082)

**Rationale:** Analytics features build on top of the core monitoring. Cost calculation depends on LiteLLM integration and tiered pricing logic, which is complex but not critical for initial use.

### Phase 4: Distribution and convenience
1. Localization DE/EN (FA-093)
2. Autostart (FA-091)
3. Auto-update (FA-100 through FA-102)
4. Inno Setup installer
5. Accessibility / screen reader support (NF-040)

**Defer indefinitely:** ML predictions, multi-account, mobile app, heatmaps, data export, Codex support.

---

## Sources

- [ccInfo (reference macOS app)](https://github.com/stefanlange/ccInfo)
- [claude-usage-widget (Windows competitor)](https://github.com/SlavomirDurej/claude-usage-widget)
- [SessionWatcher](https://www.sessionwatcher.com/)
- [CUStats](https://custats.info/)
- [ClaudeMeter](https://eddmann.com/ClaudeMeter/)
- [Claude-Code-Usage-Monitor (Python CLI)](https://github.com/Maciek-roboblog/Claude-Code-Usage-Monitor)
- [ccusage (Node CLI)](https://github.com/ryoppippi/ccusage)
- [Claude Usage Tracker](https://github.com/hamed-elfayome/Claude-Usage-Tracker)
- [Claude Code usage analytics (official)](https://support.claude.com/en/articles/12157520-claude-code-usage-analytics)
