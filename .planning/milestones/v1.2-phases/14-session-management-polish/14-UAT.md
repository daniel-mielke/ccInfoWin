---
status: complete
phase: 14-session-management-polish
source: [14-01-SUMMARY.md]
started: 2026-04-12T20:16:00+02:00
updated: 2026-04-12T20:16:00+02:00
---

## Current Test

[testing complete]

## Tests

### 1. Orphaned Sessions Filtered Out
expected: Sessions for deleted project directories no longer appear in the session dropdown. Only sessions with valid, existing project directories are shown.
result: pass

### 2. Active Session Auto-Reset on Filter
expected: If the currently selected session's project directory no longer exists, the app auto-selects the next valid session from the list without crashing.
result: pass

### 3. Invalid Path Rejection
expected: Sessions with empty cwd, relative paths, or UNC paths (\\server\share) are excluded from the session list.
result: pass

### 4. Subagent Sort Order — Alphabetical by AgentId
expected: Subagent context bars are displayed in stable alphabetical order by AgentId. The order does not change on refresh.
result: pass

## Summary

total: 4
passed: 4
issues: 0
pending: 0
skipped: 0
blocked: 0

## Gaps

[none]
