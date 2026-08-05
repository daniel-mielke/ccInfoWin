# Phase 12: Model-Based Context Detection - Context

**Gathered:** 2026-04-12
**Status:** Ready for planning
**Mode:** Auto-generated (infrastructure phase — spec-driven decisions)

<domain>
## Phase Boundary

Replace the token-count heuristic in ModelContextLimits with model-family-based detection. Opus always gets 1M context, Haiku always gets 200K, Sonnet defaults to 200K (configurable in Phase 13). Autocompact buffer becomes flat 33K for all models. Warning triggers at 20K tokens remaining.

</domain>

<decisions>
## Implementation Decisions

### ModelFamily Architecture
- Add `ModelFamily` enum nested inside `ModelContextLimits` (Opus, Sonnet, Haiku, Unknown) — tightly coupled, only used by this helper
- Add `GetModelFamily(string? modelName)` method — parse from model name string (contains "opus" / "sonnet" / "haiku")
- Replace `ContextLimits` dictionary with `ModelFamily`-based switch — the dictionary has wrong values (200K for all), a switch on family is simpler and extensible
- Add `sonnetContextSize` parameter to `GetMaxContextTokens(string? modelName, long sonnetContextSize = 200_000)` — Phase 13 will pass the setting value; Phase 12 uses default
- Simplify `GetEffectiveMaxTokens` to `GetEffectiveMaxTokens(long maxTokens)` — remove `currentTokens` param since buffer is now flat 33K

### Autocompact Warning Logic
- Replace percentage-based thresholds (90%/95%) with flat buffer: `totalTokens >= maxTokens - 20_000`
- Add constant `AutocompactWarningBuffer = 20_000`
- Delete `LargeModelAutocompactThreshold`, `SmallModelAutocompactThreshold` — no longer needed
- Delete `ExtendedAutocompactBuffer` (165K) and `ExtendedContextDetectionThreshold` (180K) — the heuristic is the problem being fixed
- Add constant `ExtendedContextLimit = 1_000_000`

### Subagent Context Resolution
- Subagents resolve model family from `lastEntry.Message.Model` in JSONL — existing pattern in `BuildSubagentContext()`
- Update `SubagentContextData.Utilization` to use simplified `GetEffectiveMaxTokens(maxTokens)`
- Null model name falls back to 200K (DefaultContextLimit) — existing behavior, safe default

### Claude's Discretion
- Internal code structure within `ModelContextLimits` (method ordering, doc comments)
- Whether to inline `GetModelFamily()` or keep as separate method

</decisions>

<code_context>
## Existing Code Insights

### Reusable Assets
- `ModelContextLimits.cs` — the primary target, currently has dictionary-based lookup and heuristic buffer selection
- `ContextWindowData.cs` — record types with `Utilization` computed property that calls `GetEffectiveMaxTokens`
- `JsonlService.cs:GetContextWindow()` — calls `GetMaxContextTokens` at line ~151 and `BuildSubagentContext` at line ~666

### Established Patterns
- Static helper class (`ModelContextLimits`) — no DI, pure functions with static state
- `ComputeContextTokens()` aggregates token counts from JSONL entries
- `ResolveModelName()` extracts model name from session files
- Badge colors already use `Contains("opus")` family detection pattern — identical to what `GetModelFamily` needs

### Integration Points
- `JsonlService.GetContextWindow()` — must pass sonnetContextSize param (default 200K in Phase 12)
- `JsonlService.BuildSubagentContext()` — calls `GetMaxContextTokens` per subagent
- `ContextWindowData.Utilization` / `SubagentContextData.Utilization` — computed properties calling `GetEffectiveMaxTokens`
- `MainViewModel.UpdateSessionData()` — reads context data and applies to UI bindings

</code_context>

<specifics>
## Specific Ideas

- Spec reference: `spec-release-from-1.7.1-to-1.8.3.md` Phase 1 (lines 46-151)
- Opus sessions must show ~967K effective (1M - 33K buffer)
- Haiku sessions must show ~167K effective (200K - 33K buffer)
- `GetBadgeColorHex` already has the `Contains("opus"/"sonnet"/"haiku")` pattern — reuse for `GetModelFamily`

</specifics>

<deferred>
## Deferred Ideas

- Sonnet context configuration UI (Phase 13)
- ISettingsService injection into JsonlService for reading Sonnet setting (Phase 13)

</deferred>
