# Phase 4: Local Data Pipeline - Context

**Gathered:** 2026-03-11
**Status:** Ready for planning

<domain>
## Phase Boundary

User can see context window status, switch between sessions, and view token counts — all derived from local JSONL files without API dependency. Includes FileSystemWatcher-based file monitoring, JSONL parsing with deduplication, multi-session management with grouped dropdown, context window progress bars with model badges and subagent bars, and per-session token counters. No cost calculation (Phase 5), no token aggregation by time period tabs (Phase 5), no chart export (Phase 6).

</domain>

<decisions>
## Implementation Decisions

### JSONL Parsing Strategy
- Tolerant parsing: unknown fields are ignored, missing fields are filled with defaults — robust against Claude Code version changes
- Read last ~1 MB of each JSONL file (seek to end, read backwards) — no need to parse entire file
- Deduplication by messageId and requestId to prevent double-counting (TOKS-04)
- System.Text.Json deserialization with JsonSerializerOptions that ignore unknown properties

### File Watching
- Single FileSystemWatcher on `%USERPROFILE%\.claude\projects\` watching ALL project directories simultaneously
- NotifyFilters: LastWrite, FileName, Size
- 300ms debounce timer to prevent double-triggers from rapid writes
- Watcher stays active across session switches — no restart needed

### Startup Behavior
- Background-scan with loading indicator: UI shows "Scanning sessions..." while JSONL files are parsed asynchronously
- API-based data (5h/Weekly) displays immediately from cache — JSONL data loads independently
- Two independent data pipelines: API polling (existing) + JSONL file watching (new)

### JSONL Cache
- Parsed token sums and session info cached in `%LOCALAPPDATA%\CCInfoWindows\jsonl-cache.json`
- On startup: load cache (display immediately), then incrementally parse only new JSONL lines
- Cache invalidation via file size + last-write timestamp comparison
- Cache format includes per-file position markers to enable incremental reads

### Session Dropdown
- Placement: top of dashboard, above all sections (5h, Weekly, Context, Tokens)
- Session name: last folder name from working directory path (e.g., `D:\myProjects\ccInfoWin` → "ccInfoWin")
- Encoded/hashed directory names decoded or shown as "Unbekanntes Projekt" (SESS-05)
- Grouped dropdown: "Aktiv" group at top, "Inaktiv" group below with visual separator
- Configurable inactivity threshold in Settings (SETT-03), default: 30 minutes
- Last selected session persisted in AppSettings (`lastSelectedSessionId`), restored on startup
- If persisted session no longer exists: select first active session, or show "Keine aktive Session"
- No auto-switching away from current selection when it becomes inactive (SESS-04)
- No flickering of stale data when switching sessions (SESS-03) — cache provides instant data

### Context Window UI
- Layout: full-width progress bar, percentage left-aligned, model badge ("Opus 4.6") as chip right-aligned
- Subagent context bars: dynamically appear/disappear, indented with smaller font, own model badge
- Autocompact warning: inline orange text "⚠ Autocompact bald" below the affected bar at >= 95% (>= 90% for 200K models) (CTXW-04)
- No active session: 0% bar (grayed out) + text "Keine aktive Session" (CTXW-05) — section stays visible, no layout jump
- Color thresholds reused from existing ColorThresholds.cs for progress bar coloring

### Token Counter Display
- Own "TOKENS" section below Context Window section, consistent with existing section pattern (header + content + divider)
- Input and output token counters side-by-side
- Compact formatting with suffix: 1,234 → "1.2K", 1,234,567 → "1.2M"
- Per-session aggregation only in Phase 4 (today/week/month tabs are Phase 5, TOKS-02)
- Session switch: instant display from cache data, no loading indicator needed

### Dashboard Section Order (updated)
1. Session Dropdown (new — Phase 4)
2. 5-STUNDEN-FENSTER (existing — Phase 2/3)
3. WOCHENLIMIT + SONNET WOCHENLIMIT (existing — Phase 2)
4. KONTEXTFENSTER (new — Phase 4)
5. TOKENS (new — Phase 4)
6. Footer (existing — Phase 2)

### Claude's Discretion
- FileSystemWatcher initialization and error recovery (AccessDenied, too many watchers)
- Exact JSONL field extraction (which fields beyond model, tokens, cost, context, workingDirectory)
- Cache file format and incremental read implementation details
- Subagent detection logic (how to identify subagent vs main conversation in JSONL)
- Session ID generation strategy (hash of working directory path or similar)
- Exact debounce timer implementation (DispatcherQueueTimer vs System.Threading.Timer)
- Context window size detection per model (how to map model name to max context tokens)

</decisions>

<specifics>
## Specific Ideas

- Session dropdown should feel like a project switcher — quick, no delay, grouped for clarity
- "Keine aktive Session" state should be calm, not alarming — just informational
- Autocompact warning must be noticeable but not intrusive — orange text, not a banner or popup
- Token counters should use German number formatting (dot as thousands separator) with English suffixes (K, M)
- The two data pipelines (API + JSONL) must be visually seamless — user shouldn't notice they come from different sources

</specifics>

<code_context>
## Existing Code Insights

### Reusable Assets
- `ColorThresholds.cs`: Maps 0.0-1.0 utilization to theme brush key names — reuse for context window progress bars
- `PercentageToColorConverter.cs`: Resolves brush keys to SolidColorBrush from ThemeResources — reuse for context bars
- `BoolToVisibilityConverter.cs`: bool → Visibility — reuse for dynamic subagent bars and warning text
- `SettingsService` + `AppSettings`: JSON persistence in %LOCALAPPDATA% — extend for lastSelectedSessionId and sessionActivityThresholdMinutes
- `CountdownFormatter.cs`: Number formatting helpers — potential base for token formatting
- `WeakReferenceMessenger`: Established pattern for cross-ViewModel events — use for SessionSelectedMessage, JsonlDataUpdatedMessage

### Established Patterns
- Singleton DI registration for stateful services (SettingsService, UsageHistoryService, ClaudeApiService)
- Transient ViewModels (MainViewModel, SettingsViewModel)
- DispatcherQueue.TryEnqueue() for UI thread marshaling from background threads
- DispatcherQueueTimer for periodic operations (polling, countdown)
- Constructor parameter injection for test directory overrides (UsageHistoryService, ClaudeApiService)
- Graceful error handling: return empty defaults on corrupt/missing files
- System.Text.Json with WriteIndented for human-readable cache files

### Integration Points
- `MainViewModel.cs`: Add observable properties for sessions, context window, tokens; inject new JSONL service
- `MainView.xaml`: Add session ComboBox (Row 0), context window section, tokens section in ScrollViewer
- `App.xaml.cs`: Register IJsonlService, IFileWatcherService, ISessionService in DI container
- `AppSettings.cs`: Add lastSelectedSessionId (string), sessionActivityThresholdMinutes (int, default 30)
- `AppTheme.xaml`: Add context window and token section ThemeResources if needed
- `SettingsView.xaml`: Add session activity threshold configuration (SETT-03)

</code_context>

<deferred>
## Deferred Ideas

- Token aggregation by time period tabs (today/week/month) — Phase 5 (TOKS-02)
- Subagent tokens in time period aggregations — Phase 5 (TOKS-03)
- Cost calculation from JSONL costUSD field — Phase 5 (COST-02)
- LiteLLM pricing integration — Phase 5 (COST-01)

</deferred>

---

*Phase: 04-local-data-pipeline*
*Context gathered: 2026-03-11*
