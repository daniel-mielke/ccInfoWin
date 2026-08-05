---
phase: 12-model-based-context-detection
plan: "01"
subsystem: helpers/models
tags: [context-detection, model-family, tokens, tests]
dependency_graph:
  requires: []
  provides: [ModelFamily enum, model-based GetMaxContextTokens, flat GetEffectiveMaxTokens, flat ShouldWarnAutocompact]
  affects: [ContextWindowData, SubagentContextData, JsonlService]
tech_stack:
  added: []
  patterns: [model-family enum switch, substring-based model detection, flat buffer constants]
key_files:
  created: []
  modified:
    - CCInfoWindows/CCInfoWindows/Helpers/ModelContextLimits.cs
    - CCInfoWindows/CCInfoWindows/Models/ContextWindowData.cs
    - CCInfoWindows.Tests/Helpers/ModelContextLimitsTests.cs
    - CCInfoWindows.Tests/Helpers/ContextWindowTests.cs
    - CCInfoWindows.Tests/Helpers/ExportHelperTests.cs
decisions:
  - "ModelFamily enum uses substring matching (contains 'opus'/'sonnet'/'haiku') — same pattern as GetBadgeColorHex, consistent and dictionary-free"
  - "Opus hardcoded to 1M, Sonnet uses sonnetContextSize default param (future Phase 13 wires Settings value), Haiku/Unknown default to 200K"
  - "GetEffectiveMaxTokens reduced to single param — currentTokens was only used for heuristic detection, now irrelevant"
  - "ShouldWarnAutocompact uses flat 20K remaining threshold — fires at maxTokens-20K regardless of model size"
metrics:
  duration: "15 minutes"
  completed: "2026-04-12"
  tasks_completed: 2
  files_changed: 5
---

# Phase 12 Plan 01: Model-Based Context Detection Summary

ModelContextLimits rewritten from token-count heuristic to ModelFamily enum with model-name substring matching. Opus returns 1M context limit, Sonnet uses configurable `sonnetContextSize` parameter (default 200K), all others default to 200K. GetEffectiveMaxTokens simplified to flat 33K buffer (single param). ShouldWarnAutocompact fires at flat 20K remaining regardless of model size. All 60 targeted tests pass.

## Tasks Completed

| Task | Name | Commit | Files |
|------|------|--------|-------|
| 1 | Rewrite ModelContextLimits with ModelFamily enum and update ContextWindowData callers | 036c16b | ModelContextLimits.cs, ContextWindowData.cs |
| 2 | Update ModelContextLimitsTests and ContextWindowTests for new behavior | aa495b0 | ModelContextLimitsTests.cs, ContextWindowTests.cs, ExportHelperTests.cs |

## Decisions Made

1. **ModelFamily enum with substring matching** — Uses same `lower.Contains("opus")` pattern as `GetBadgeColorHex`. Consistent approach, no dictionary maintenance required.

2. **Opus hardcoded to 1M** — Opus 4+ is the only model with confirmed 1M context. Sonnet uses a `sonnetContextSize` default parameter hook for Phase 13 (Settings integration).

3. **GetEffectiveMaxTokens single-param** — The `currentTokens` parameter was only used for the old heuristic (detect extended context from token count). With model-family detection, the model's max size is already known, so the parameter was removed.

4. **Flat 20K warning threshold** — Previously used 90%/95% percentage thresholds which gave wildly different absolute values per model size. A flat 20K remaining buffer provides consistent UX across both 200K and 1M sessions.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] Fixed ExportHelperTests compilation errors blocking test build**
- **Found during:** Task 2 (attempting to run test suite)
- **Issue:** ExportHelperTests.cs called `RenderChartToPng` with 4 args; signature requires 5 (added `utilization` param in a previous phase, tests not updated)
- **Fix:** Added `utilization` argument (0.8 and 0.0 respectively) to both test calls
- **Files modified:** CCInfoWindows.Tests/Helpers/ExportHelperTests.cs
- **Commit:** aa495b0

## Verification Results

- `dotnet build CCInfoWindows/CCInfoWindows/CCInfoWindows.csproj` — 0 errors
- `dotnet build CCInfoWindows.Tests/CCInfoWindows.Tests.csproj -p:Platform=x64` — 0 errors
- `dotnet test ... --filter "FullyQualifiedName~ModelContextLimits|FullyQualifiedName~ContextWindow"` — 60/60 passed
- Grep: `ModelContextLimits.cs` contains `enum ModelFamily`, `ExtendedContextLimit = 1_000_000`, `AutocompactWarningBuffer = 20_000`
- Grep: `ModelContextLimits.cs` does NOT contain any removed heuristic constants
- Grep: `ContextWindowData.cs` contains `GetEffectiveMaxTokens(MaxTokens)` in both Utilization getters

## Known Stubs

None — all wired to production logic. `sonnetContextSize` defaults to 200K until Phase 13 wires the Settings value.

## Self-Check: PASSED
