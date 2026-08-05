# Phase 14: Session Management Polish - Context

**Gathered:** 2026-04-12
**Status:** Ready for planning
**Mode:** Auto-generated (infrastructure phase — discuss skipped)

<domain>
## Phase Boundary

Two backend polish changes: (1) Filter sessions whose project directory no longer exists on disk — hide orphaned sessions from the dropdown. (2) Sort subagent context bars alphabetically by agentId for stable display order.

</domain>

<decisions>
## Implementation Decisions

### Claude's Discretion
All implementation choices are at Claude's discretion — pure infrastructure phase. Use ROADMAP phase goal, success criteria, and codebase conventions to guide decisions.

Key notes from STATE.md:
- UNC path guard mandatory for Directory.Exists — Path.IsPathRooted AND not-UNC before calling
- Spec reference: `spec-release-from-1.7.1-to-1.8.3.md` Phase 3 (session filtering) and Phase 4 (subagent sorting)

</decisions>

<code_context>
## Existing Code Insights

Codebase context will be gathered during plan-phase research.

</code_context>

<specifics>
## Specific Ideas

- Session filtering: `Directory.Exists()` check on `SessionInfo.Cwd` during `RebuildSessionsList()`
- Subagent sorting: `result.OrderBy(a => a.AgentId, StringComparer.Ordinal).ToList()` in `BuildSubagentContext()`
- Spec says subagent sorting is a one-line change

</specifics>

<deferred>
## Deferred Ideas

None — discuss phase skipped.

</deferred>
