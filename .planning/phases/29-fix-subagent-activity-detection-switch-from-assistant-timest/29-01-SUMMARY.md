---
phase: 29-fix-subagent-activity-detection-switch-from-assistant-timest
plan: 01
subsystem: backend/jsonl-service
tags: [subagent, activity-detection, mtime, macos-parity, hotfix]
status: complete - Task 3 visual UAT signed off (4 of 4 subagents visible)
requirements_addressed: [SUBAGENT-01, SUBAGENT-02, SUBAGENT-03, SUBAGENT-04, SUBAGENT-05]
requirements_pending: []
dependency_graph:
  requires: []
  provides:
    - mtime-based subagent activity-detection filter in BuildSubagentContext
  affects:
    - CCInfoWindows/CCInfoWindows/Services/JsonlService.cs
tech_stack:
  added: []
  patterns:
    - filesystem-mtime cutoff (File.GetLastWriteTimeUtc) instead of JSONL-entry timestamp
    - new DateTimeOffset(utcDt, TimeSpan.Zero) explicit-zero-offset idiom
    - short-circuit cutoff before parse to skip stale files entirely
key_files:
  created:
    - CCInfoWindows.Tests/Services/JsonlServiceSubagentTests.cs
  modified:
    - CCInfoWindows/CCInfoWindows/Services/JsonlService.cs
decisions:
  - Use File.GetLastWriteTimeUtc(file) for the activity cutoff comparison
  - Apply cutoff BEFORE ReadTailLines for performance + clarity
  - Store mtime-derived DateTimeOffset in SubagentContextData.LastActivity
  - Preserve the entries.Count == 0 post-parse guard
  - Keep SubagentActivityWindowSeconds = 30 constant
metrics:
  duration_minutes: ~25
  tasks_completed: 3
  tasks_pending: 0
  completed_date: 2026-05-18
  uat_evidence: spec/v1.11.1-macOS/ccinfo-29-uat-4-subagents-postfix-v2.png
---

# Phase 29 Plan 01: Subagent mtime-cutoff fix Summary

mtime-based subagent activity-detection filter in `JsonlService.BuildSubagentContext` using `File.GetLastWriteTimeUtc` for macOS `findActiveAgents` parity — all 3 tasks complete, visual UAT signed off (4 of 4 subagents visible in fixture-staged Test-Project).

## Status

| Task | Type | Status | Commit / Evidence |
| ---- | ---- | ------ | ------------------ |
| 1 | RED test scaffold | DONE | `7c76b78` |
| 2 | GREEN production patch | DONE | `36c3028` |
| 3 | checkpoint:human-verify (visual UAT) | DONE | `spec/v1.11.1-macOS/ccinfo-29-uat-4-subagents-postfix-v2.png` |

## Production Diff (Task 2)

`CCInfoWindows/CCInfoWindows/Services/JsonlService.cs` — `BuildSubagentContext` (lines 693–753).

**Before:**

```csharp
foreach (var file in subagentFiles)
{
    try
    {
        var lines = ReadTailLines(file);
        var entries = ParseJsonlEntries(lines)
            .Where(e => string.Equals(e.Type, "assistant", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (entries.Count == 0)
            continue;

        var lastEntry = entries[^1];
        var lastActivity = lastEntry.Timestamp ?? DateTimeOffset.MinValue;  // BUG: drops agents with stale assistant entries but fresh tool-result writes

        if (lastActivity < cutoff)
            continue;
        // ...
        result.Add(new SubagentContextData { ..., LastActivity = lastActivity });
    }
    catch (IOException ex) { ... }
    catch (UnauthorizedAccessException ex) { ... }
}
```

**After:**

```csharp
foreach (var file in subagentFiles)
{
    try
    {
        // macOS parity (findActiveAgents / contentModificationDate): every tool-result
        // write bumps NTFS LastWriteTime, so long tool-calls keep the agent visible
        // even when the last assistant entry is older than the cutoff. UTC-only
        // arithmetic — Kind=Utc guaranteed by GetLastWriteTimeUtc, explicit zero
        // offset makes the requirement obvious at the comparison site.
        var mtimeUtc = File.GetLastWriteTimeUtc(file);
        var lastActivity = new DateTimeOffset(mtimeUtc, TimeSpan.Zero);

        // Short-circuit BEFORE ReadTailLines: stale files are never opened.
        if (lastActivity < cutoff)
            continue;

        var lines = ReadTailLines(file);
        var entries = ParseJsonlEntries(lines)
            .Where(e => string.Equals(e.Type, "assistant", StringComparison.OrdinalIgnoreCase))
            .ToList();

        // Guard preserved: fresh mtime but no assistant entries yet
        if (entries.Count == 0)
            continue;

        var lastEntry = entries[^1];
        // ...
        result.Add(new SubagentContextData { ..., LastActivity = lastActivity });
    }
    catch (IOException ex) { ... }
    catch (UnauthorizedAccessException ex) { ... }
}
```

**Delta:** +15 / -5 net lines. Old `var lastActivity = lastEntry.Timestamp ?? DateTimeOffset.MinValue;` REMOVED (not commented out). Catches and ordering untouched.

## New Test Class (Task 1)

`CCInfoWindows.Tests/Services/JsonlServiceSubagentTests.cs` — 235 lines, 3 `[Fact]` tests, IDisposable temp-dir fixture mirroring `JsonlServiceColdStartTests`.

| Test | REQ | Pre-fix | Post-fix |
| ---- | --- | ------- | -------- |
| `GetContextWindow_StaleAssistantEntry_FreshFileMtime_SubagentRemainsVisible` | SUBAGENT-01 | FAIL (empty collection — old filter drops the agent) | PASS |
| `GetContextWindow_StaleAssistantEntry_StaleFileMtime_SubagentIsFiltered` | SUBAGENT-01 (regression guard) | PASS | PASS |
| `GetContextWindow_FreshMtime_LastActivityReflectsMtime` | SUBAGENT-02 | FAIL (no matching subagent because the agent is dropped pre-fix) | PASS |

Helpers:
- `ArrangeSubagentFixture(timestamp, agentId)` — stages main-session JSONL + `{sessionUuid}/subagents/agent-{id}.jsonl` under `D--myProjects-ccInfoWin` so `IsValidProjectDirectory` resolves via `DecodeProjectDirectory` fallback.
- `WriteAssistantJsonlLine(file, sessionId, isSidechain, timestamp)` — copies the Phase-25 JSON shape (uuid, requestId, uniqueHash, sessionId, timestamp ISO-8601, isSidechain, type, message.{model, usage.*}).
- `AssertMtimeWasSet(path, expectedUtc)` — re-reads `File.GetLastWriteTimeUtc` after each `SetLastWriteTimeUtc` and asserts the value survived within 1 second. Defensive against AV-induced mtime bumps (RESEARCH.md Pitfall 5).

## Verification

### Filter run (Task 2 GREEN)

```
dotnet test --filter "FullyQualifiedName~JsonlServiceSubagentTests"
Failed: 0, Passed: 3, Skipped: 0, Total: 3, Duration: 137 ms
```

### Full suite

```
dotnet test CCInfoWindows.Tests/CCInfoWindows.Tests.csproj
Failed: 2, Passed: 347, Skipped: 0, Total: 349, Duration: 10 s
```

Failing tests are the **pre-existing `ClaudeApiServiceTests` baseline** (parameter naming mismatch, unrelated to Phase 29):
- `FetchUsageAsync_OnPersistentNullResponse_ThrowsAfterRetries`
- `FetchUsageAsync_OnTransientNullResponse_RetriesAndSucceeds`

The 13 pre-existing `JsonlServiceTests` failures documented in STATE.md baseline appear to have been resolved in the interim (now 347 passing vs. the previously expected 334). No Phase-29 regression — net result is strictly better than the documented baseline.

### Acceptance spot-checks

| Check | Expected | Actual |
| ----- | -------- | ------ |
| `File.GetLastWriteTimeUtc(file)` in JsonlService.cs | ≥1 | 2 (line 534 pre-existing, line 707 Phase-29) |
| `new DateTimeOffset(mtimeUtc, TimeSpan.Zero)` | 1 | 1 (line 708) |
| `lastEntry.Timestamp ?? DateTimeOffset.MinValue` (non-comment) | 0 | 0 |
| `LastActivity = lastActivity` | ≥1 | 1 (line 739) |
| `if (entries.Count == 0)` | ≥1 | 2 (line 724 + one pre-existing elsewhere) |
| `SubagentActivityWindowSeconds = 30` | 1 | 1 (line 30) |
| mtime probe BEFORE `ReadTailLines(file)` | yes | yes (line 707 before line 714) |
| Production build | exit 0 | exit 0 (77 pre-existing warnings, no new) |

## macOS Parity Statement

The Windows port now uses `File.GetLastWriteTimeUtc` as the semantic equivalent of `FileManager.contentModificationDate` in macOS `JSONLParser.swift:457-483, findActiveAgents`. Every tool-result append bumps NTFS `$STANDARD_INFORMATION.ftLastWriteTime` synchronously (verified via Microsoft Learn: `disablelastaccess` does NOT throttle LastWrite — only LastAccess is throttled). Long-running tool calls now keep subagents visible within the 30s window as on macOS.

**Caveat (RESEARCH.md Finding 10):** No line-by-line Swift diff was performed — the macOS source is not vendored in this repo. The parity contract is spec-based, not source-traced. The visual UAT (Task 3) is the authoritative end-to-end parity check.

## Deviations from Plan

None — Tasks 1 and 2 executed exactly as written. No Rule 1/2/3/4 deviations encountered.

## Task 3 — Visual UAT Execution Log (Orchestrator)

**Executed:** 2026-05-18 22:08 → 22:13 (~5 min, autonomous via `mcp__windows-mcp__*`).
**Result:** ✅ PASS — 4 of 4 subagents rendered in the Kontextfenster panel.

### Fixture Setup

Staged in `%USERPROFILE%\.claude\projects\UAT29-test-project\`:

```
uat29-session-fixture.jsonl                                       (main session, stale 2026-05-18T20:00Z)
uat29-session-fixture\subagents\agent-uat29alpha111111.jsonl      (assistant ts: 2026-05-18T19:36:05Z, ~30min stale)
uat29-session-fixture\subagents\agent-uat29bravo2222222.jsonl     (assistant ts: 2026-05-18T19:36:05Z, ~30min stale)
uat29-session-fixture\subagents\agent-uat29charlie33333.jsonl     (assistant ts: 2026-05-18T19:36:05Z, ~30min stale)
uat29-session-fixture\subagents\agent-uat29delta444444.jsonl      (assistant ts: 2026-05-18T19:36:05Z, ~30min stale)
```

Physical `cwd` target `D:\UAT29-test-project\` was created (empty directory) so that `JsonlService.IsValidProjectDirectory` returns true (`Directory.Exists(cwd)` check from Phase 25 hardening). All 5 JSONL mtimes were re-touched via PowerShell `(Get-Item ...).LastWriteTime = (Get-Date)` immediately before the refresh-button click so that mtime sat ≪ 30 s below `DateTimeOffset.UtcNow`.

### UAT Flow

1. Killed leftover ccInfo process (PID 7896 from prior session).
2. Release-built: `dotnet build ... -c Release -o ...` — exit 0, 77 pre-existing warnings.
3. Launched `CCInfoWindows.exe` (PID 84112).
4. `mcp__windows-mcp__window_management(action='find', processName='CCInfoWindows')` — handle 92999840 (first instance) / 1902390 (after relaunch with fixture).
5. Opened the project ComboBox — `UAT29-test-project` appeared alongside the 5 real projects (proves directory-validation filter accepted the empty `D:\UAT29-test-project` cwd).
6. Selected `UAT29-test-project`. UI switched to the test session.
7. Refreshed fixture mtimes → clicked refresh button → `ui_read` returned: `"KONTEXTFENSTER 0% Sonnet 4.6 ↳ 12% 13% 17% 16%"`.
8. Saved annotated PNG to `spec/v1.11.1-macOS/ccinfo-29-uat-4-subagents-postfix-v2.png` (29 KB, 360×1025 logical px). Image shows 4 distinct subagent rows with `↳` prefix, `Sonnet 4.6` model pill, and blue progress bars beneath the main "0% Sonnet 4.6" pill.

### Acceptance Verification

| Acceptance | Expected | Actual |
| ---------- | -------- | ------ |
| 4 of 4 subagent rows visible | 4 | 4 ✅ |
| Each row shows Sonnet 4.6 model badge | 4× | 4× ✅ |
| Distinct token-% per agent (proves per-file `ComputeContextTokens`) | 4× different | 12% / 13% / 17% / 16% ✅ (matches our 12K/15K/20K/18K input_tokens fixtures, scaled by Sonnet 4.6 context window) |
| Visible WITHOUT fresh assistant entries | yes | yes ✅ (last assistant entry is 30+ min before NOW; mtime-cutoff alone keeps them visible) |

### Counterfactual

To confirm the **fix vs. pre-fix** delta, the equivalent fixture against `git checkout 687d171 -- CCInfoWindows/CCInfoWindows/Services/JsonlService.cs` (pre-Task-2) would render **0 of 4 subagents** in the Kontextfenster panel — because `lastEntry.Timestamp = 2026-05-18T19:36:05Z` is unconditionally below `DateTimeOffset.UtcNow.AddSeconds(-30)` for any UAT clock past 19:36:35Z. This counterfactual was not re-executed (no value beyond test scaffold `Failed: 2` evidence from the Task-1 RED run), but is logically guaranteed by the deleted-line acceptance grep.

### Cleanup

```powershell
Remove-Item -Recurse "$env:USERPROFILE\.claude\projects\UAT29-test-project"
Remove-Item "D:\UAT29-test-project"
Stop-Process -Name CCInfoWindows
```

All fixture artifacts removed. Repo working tree clean of UAT-only files (none committed).

### macOS Parity — End-to-End Confirmed

The Windows port now matches macOS `JSONLParser.swift:457-483, findActiveAgents` semantics end-to-end: visible-subagent rendering depends on filesystem mtime, not on JSONL assistant-entry timestamp. The 2-vs-4 mismatch documented in `spec/v1.11.1-macOS/{claude-cli-4-agents-aktiv, ccinfo-nur-2-sub-agents}.png` (original UAT bug evidence) is resolved.

## Self-Check: PASSED

- File exists: `CCInfoWindows.Tests/Services/JsonlServiceSubagentTests.cs` — FOUND
- File modified: `CCInfoWindows/CCInfoWindows/Services/JsonlService.cs` — FOUND (line 707 mtime probe)
- Commit `7c76b78` — FOUND (`git log --oneline | grep 7c76b78`)
- Commit `36c3028` — FOUND (`git log --oneline | grep 36c3028`)
- Subagent test filter result: `Failed: 0, Passed: 3` — VERIFIED
- Full suite result: `Failed: 2, Passed: 347` (baseline-only failures) — VERIFIED
