# Phase 5: Cost Analytics - Context

**Gathered:** 2026-03-16
**Status:** Ready for planning

<domain>
## Phase Boundary

User can see what their Claude usage costs with live pricing, time-period breakdowns, and burn rate. Includes: tab bar for switching between session/today/week/month token aggregations, cost calculation from JSONL costUSD field with LiteLLM API fallback, tiered pricing for 1M-context models, burn rate display, and settings for pricing data source status. No chart export (Phase 6), no localization (Phase 6), no new session management features.

</domain>

<decisions>
## Implementation Decisions

### Tab-Bar (Time Period Switcher)
- Segmented control style — connected bar with 4 segments, matching macOS original
- Labels fully written out: Session / Heute / Woche / Monat (not abbreviated)
- Active segment highlighted with filled background, inactive segments use muted text color
- Default tab on startup: "Session" (consistent with session dropdown above, no aggregation needed)
- Tab state is not persisted — always starts on "Session"
- Switching tabs triggers re-aggregation of token/cost data for selected time period
- Subagent tokens included in all time period aggregations (TOKS-03)

### Tab-Bar Loading State
- Shimmer placeholders on value positions during aggregation (no layout jump)
- Shimmer shown when switching to Heute/Woche/Monat tabs that require cross-session aggregation
- Session tab loads instantly from cache (no shimmer needed)

### Statistics Data Table
- Key-value table below tab bar (per styleguide section 9.2)
- Rows: Eingabe, Ausgabe, Cache-Schreiben, Cache-Lesen, **Gesamt** (bold), **Kosten** (bold)
- Labels 13px Regular gray, values 13px Medium white, bold rows 13px Semibold
- Cost value prefixed with ~ (tilde) when model not in pricing database (COST-03)
- Burn rate displayed below cost row

### Claude's Discretion
- Cost calculation implementation (costUSD primary, token*price fallback logic)
- LiteLLM API integration details (endpoint, response parsing, 12h cache strategy)
- Tiered pricing implementation for 1M-context models (COST-04)
- Burn rate calculation algorithm (time window, smoothing, unit choice)
- Burn rate visualization (number format, trend indicator)
- Shimmer animation implementation (WinUI 3 approach)
- JSONL deduplication by messageId and requestId (TOKS-04)
- Fallback/bundled prices when LiteLLM API is unreachable
- Settings UI for pricing data source and last fetch time (COST-06)
- JsonlEntry model extension to include costUSD field

</decisions>

<canonical_refs>
## Canonical References

**Downstream agents MUST read these before planning or implementing.**

### Functional Requirements
- `spec/v1.7.1/ccinfo-spec.md` §2.6 — Token statistics requirements (FA-060 to FA-063)
- `spec/v1.7.1/ccinfo-spec.md` §2.7 — Cost calculation requirements (FA-070 to FA-075)
- `spec/v1.7.1/ccinfo-spec.md` §3.3 — LiteLLM Pricing API data source (DS-030, DS-031)

### UI Design
- `spec/v1.7.1/ccinfo-styleguide.md` §9 — STATISTIKEN section: tab bar design, data table layout, typography
- `spec/v1.7.1/ccinfo-styleguide.md` §3.2 — Font sizes and weights for statistics labels/values

### Prior Phase Context
- `.planning/phases/04-local-data-pipeline/04-CONTEXT.md` — JSONL parsing decisions, token display format, cache strategy

### Project Requirements
- `.planning/REQUIREMENTS.md` — TOKS-02, TOKS-03, TOKS-04, COST-01 through COST-06, DATA-05

</canonical_refs>

<code_context>
## Existing Code Insights

### Reusable Assets
- `TokenFormatter.cs`: FormatTokenCount with K/M suffixes — reuse for all token displays
- `TokenSummary.cs`: InputTokens/OutputTokens record — extend or create CostSummary alongside
- `ModelContextLimits.cs`: Model name dictionary, GetDisplayName — reuse for model-to-price mapping
- `ColorThresholds.cs` + `PercentageToColorConverter.cs`: Threshold-based coloring — potentially reuse for cost thresholds
- `JsonlEntry.cs`: JSONL deserialization model — needs costUSD field addition
- `JsonlService.cs`: JSONL parsing, FileSystemWatcher, session management — extend for time-period aggregation
- `IJsonlService.cs`: GetTokenSummary(sessionId) — extend with GetTokenSummary(timePeriod) overload
- `SettingsService.cs` + `AppSettings.cs`: JSON persistence — extend for LiteLLM cache path and pricing settings

### Established Patterns
- Singleton DI for stateful services (JsonlService, SettingsService)
- DispatcherQueue.TryEnqueue() for UI thread updates from background aggregation
- System.Text.Json with DefaultOptions for tolerant deserialization
- WeakReferenceMessenger for cross-ViewModel events
- Constructor parameter injection for test directory overrides

### Integration Points
- `MainViewModel.cs`: Add tab-bar selected index, cost/token properties per time period
- `MainView.xaml`: Replace existing TOKENS section with STATISTIKEN section (tab bar + data table)
- `App.xaml.cs`: Register new pricing service in DI container
- `AppSettings.cs`: Add liteLlmCachePath, lastPricingFetch properties
- `AppTheme.xaml`: Add segmented control and shimmer theme resources

</code_context>

<specifics>
## Specific Ideas

- Tab-Bar soll sich anfühlen wie der macOS Segmented Control — solide, nicht wie Web-Tabs
- Labels ausgeschrieben (Session/Heute/Woche/Monat) statt Abkürzungen — Klarheit über Kompaktheit
- Shimmer-Loading statt Spinner — moderner Look, kein Layout-Sprung beim Tab-Wechsel
- Kosten-Anzeige mit Tilde (~) für geschätzte Werte muss sofort erkennbar sein
- Burn Rate sollte die "Geschwindigkeit" des Token-Verbrauchs greifbar machen

</specifics>

<deferred>
## Deferred Ideas

None — discussion stayed within phase scope

</deferred>

---

*Phase: 05-cost-analytics*
*Context gathered: 2026-03-16*
