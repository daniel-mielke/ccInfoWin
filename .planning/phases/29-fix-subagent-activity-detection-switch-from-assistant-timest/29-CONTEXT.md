# Phase 29: Fix Subagent activity detection: switch from assistant-timestamp to filesystem mtime (macOS parity) - Context

**Gathered:** 2026-05-18
**Status:** Ready for planning

<domain>
## Phase Boundary

Replace the `lastEntry.Timestamp ?? DateTimeOffset.MinValue` activity-detection signal in `JsonlService.BuildSubagentContext` with `File.GetLastWriteTimeUtc(file)` — matching the macOS reference implementation (`JSONLParser.swift:457–483, findActiveAgents` uses `contentModificationDate`). The 30-second visibility window stays at the existing constant `SubagentActivityWindowSeconds`. The fix MUST close the UAT-evidenced gap where Claude CLI shows 4 parallel agents but ccInfo Windows shows only 2 because some agents' last assistant entry is older than 30s while their tool-result writes (which still touch file mtime) are fresh.

**In scope:**
- `JsonlService.BuildSubagentContext` cutoff logic (lines 696, 712–716)
- `SubagentContextData.LastActivity` field semantics (now mtime-derived)
- New `JsonlServiceSubagentTests.cs` test class with at least two scenarios
- Visual UAT re-validation of the 4-parallel-agent scenario via `mcp__windows-mcp__*` tooling

**Out of scope:**
- Making `SubagentActivityWindowSeconds` user-configurable (no demand, breaks macOS parity)
- Refactoring the broader subagent-file discovery pipeline (`BuildSubagentFileList`, `ReadTailLines`)
- Any change to main-session activity detection (`RebuildSessionsList`) — only subagent path

</domain>

<decisions>
## Implementation Decisions

### Activity-Detection Strategy
- Use `File.GetLastWriteTimeUtc(file)` as the cutoff comparison value — exact macOS `contentModificationDate` parity; every tool-result write touches mtime, so long tool-calls keep the agent visible.
- Apply the cutoff **before** JSONL parsing — `File.GetLastWriteTimeUtc()` check immediately after `BuildSubagentFileList` (or at the top of the foreach), so unread subagent files are never opened. Performance win + cleaner control flow.
- Store `File.GetLastWriteTimeUtc(file)` (as `DateTimeOffset`) into `SubagentContextData.LastActivity` — keeps the filter logic and the UI's "last seen" display synchronized; no risk of the UI showing a different timestamp than what the filter used.
- Keep `SubagentActivityWindowSeconds = 30` constant — macOS parity, no user request to make this configurable, keep the surface minimal for a hotfix phase.

### Test Strategy & Validation
- New dedicated file `CCInfoWindows.Tests/Services/JsonlServiceSubagentTests.cs` — clear isolation, mirrors the Phase 25 precedent of `JsonlServiceColdStartTests`. Avoid bloating the existing `JsonlServiceTests` (which already carries 13 pre-existing failures).
- Simulate mtime in tests with `File.SetLastWriteTimeUtc(path, DateTime.UtcNow)` against real temp-files in the test working directory — fast, deterministic, no `IFileSystem` abstraction overhead.
- Minimum two scenarios:
  1. **Stale assistant entry + fresh file mtime** → agent MUST remain in the result (Phase-29 core bug fix).
  2. **All timestamps stale (assistant entry AND mtime older than cutoff)** → agent MUST be filtered out (regression guard so the cutoff is still enforced).
- Visual UAT validation IS required: reproduce the 4-parallel-agent Claude CLI session and verify ccInfo Windows displays all 4 agents (vs. the pre-fix 2). Use `mcp__windows-mcp__*` tooling for autonomous capture. Acceptance: count of visible subagents in the context-window panel matches the CLI count.

### Claude's Discretion
- Specific test class structure, test-method names, fixture cleanup pattern, and the exact placement of the `File.GetLastWriteTimeUtc` call within `BuildSubagentContext` are at Claude's discretion — follow established conventions (xUnit Theory/Fact, `IDisposable` for temp-folder cleanup, `Assert.Contains`/`Assert.DoesNotContain` against `result`).
- If `File.GetLastWriteTimeUtc(file)` throws `IOException` on a deleted-mid-call file, treat it the same as the existing `IOException` catch on `ReadTailLines` — skip the file silently (continue), do not surface to UI.

</decisions>

<code_context>
## Existing Code Insights

### Reusable Assets
- `JsonlService.BuildSubagentContext` (`JsonlService.cs:693–743`) — the single function to modify. Called from two sites: `JsonlService.cs:159` and `JsonlService.cs:182`. Both share the same `BuildSubagentContext` path, so a single edit fixes both invocations.
- `SubagentContextData` record (`Models/ContextWindowData.cs:8`) — already has a `LastActivity DateTimeOffset` field; semantics shift but the shape stays.
- `SubagentActivityWindowSeconds = 30` const (`JsonlService.cs:30`) — reuse as-is; do not rename.
- `ReadTailLines` and `ParseJsonlEntries` — still required after the cutoff check to extract `ComputeContextTokens(lastEntry)` and `lastEntry.Message?.Model`. We cannot skip parsing; only the filter switch changes.
- Existing test infrastructure: `JsonlServiceColdStartTests.cs` shows the Phase-25 pattern for temp-file-based subagent fixtures — copy the fixture/cleanup shape, do not depend on it.

### Established Patterns
- Static helper methods on `JsonlService` follow defensive `try { ... } catch (IOException) { Debug.WriteLine(...) } catch (UnauthorizedAccessException) { ... }` — extend this pattern to `File.GetLastWriteTimeUtc` if it can race with a deletion.
- Cutoff arithmetic: `DateTimeOffset.UtcNow.AddSeconds(-SubagentActivityWindowSeconds)` is already the project idiom (`JsonlService.cs:696`). Keep it.
- `LastActivity < cutoff` comparison style (line 715) stays — only the source of `lastActivity` changes.

### Integration Points
- No new DI registrations.
- No new public API surface (everything stays inside `BuildSubagentContext`).
- No XAML changes (UI already binds to `SubagentContextData.LastActivity`).
- No localization keys (no user-facing text changes).

</code_context>

<specifics>
## Specific Ideas

- The UAT screenshots `spec/v1.11.1-macOS/{claude-cli-4-agents-aktiv,ccinfo-nur-2-sub-agents}.png` (memory-tracked, may exist under different path) are the canonical reproduction artifact — try to reproduce the same 4-parallel-agent CLI scenario for the post-fix visual UAT.
- macOS reference: `JSONLParser.swift:457–483, findActiveAgents` is the parity target. The Windows port should match its mtime-based filter semantically (not necessarily line-for-line — Swift's `FileManager.contentModificationDate` ≙ .NET's `File.GetLastWriteTimeUtc`).

</specifics>

<deferred>
## Deferred Ideas

- Configurable `SubagentActivityWindowSeconds` via `AppSettings` — no user demand, keeps the API surface minimal. Reopen if a UAT user explicitly asks for it.
- `IFileSystem` abstraction to enable pure unit tests without temp-files — out of scope; would touch every static helper in `JsonlService` and require a large refactor.
- Pruning of subagent files older than the visibility window from `BuildSubagentFileList` — current behavior already short-circuits, no extra cleanup needed for v1.5.

</deferred>
