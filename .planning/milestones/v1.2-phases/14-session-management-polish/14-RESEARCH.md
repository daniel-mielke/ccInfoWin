# Phase 14: Session Management Polish - Research

**Researched:** 2026-04-12
**Domain:** C# / WinUI 3 — JSONL session filtering and list sorting
**Confidence:** HIGH

## Summary

Phase 14 is a two-change polish sprint, both confined to `JsonlService.cs`. The first change filters orphaned sessions (projects whose `Cwd` directory no longer exists on disk) out of `RebuildSessionsList()`. The second change sorts subagent context bars alphabetically by `AgentId` inside `BuildSubagentContext()`. The spec (Phase 3 and Phase 4 in `spec-release-from-1.7.1-to-1.8.3.md`) already provides exact implementation guidance, and the existing `MainViewModel.RefreshSessionList()` already handles session-selection fallback when the previous session disappears — no ViewModel changes are required beyond verifying that path.

Both changes are internal-service changes with no new dependencies, no UI changes, no localization requirements, and no new model properties. Total scope: roughly four lines of code across two methods.

**Primary recommendation:** Add `Directory.Exists` guard with UNC-path pre-check to `RebuildSessionsList()` filter chain; add `.OrderBy(a => a.AgentId, StringComparer.Ordinal)` to `BuildSubagentContext()` return statement.

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions
All implementation choices are at Claude's discretion — pure infrastructure phase.

### Claude's Discretion
Use ROADMAP phase goal, success criteria, and codebase conventions to guide all decisions.

### Key Notes (from STATE.md)
- UNC path guard mandatory for Directory.Exists — Path.IsPathRooted AND not-UNC before calling
- Spec reference: `spec-release-from-1.7.1-to-1.8.3.md` Phase 3 (session filtering) and Phase 4 (subagent sorting)

### Deferred Ideas (OUT OF SCOPE)
None — discuss phase skipped.
</user_constraints>

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|------------------|
| SES-01 | User does not see sessions for deleted project directories in the session dropdown | `RebuildSessionsList()` LINQ `.Where(s => ...)` chain; add `Directory.Exists(s.Cwd)` with UNC guard |
| SES-02 | User sees session selection cleared when the selected project's directory is deleted | Already handled by `MainViewModel.RefreshSessionList()` — when `previousSessionId` is no longer in `SortedSessions`, falls through to `firstActiveItem` fallback. No ViewModel code needed beyond verifying the path. |
| SES-03 | User sees subagent context bars in stable alphabetical order by agent ID | `BuildSubagentContext()` return statement; add `.OrderBy(a => a.AgentId, StringComparer.Ordinal)` before `.ToList()` |
</phase_requirements>

## Project Constraints (from CLAUDE.md)

- **No magic numbers** — UNC guard predicate must use named constants or well-named inline expressions.
- **Meaningful names** — Any extracted helper must reveal intent.
- **No comments on obvious things** — The predicate is self-documenting; no inline comments needed beyond the UNC guard rationale.
- **Minimal, focused functions (SRP)** — If UNC guard grows complex, extract `IsValidProjectDirectory(string path) → bool`.
- **Secure coding** — `Directory.Exists()` operates on paths derived from JSONL data (external input). Must not pass raw user-supplied paths; the path comes from deserialized `Cwd` field which is already range-validated by `IsPathWithinProjectsDirectory` on write paths. For read-only existence check, UNC guard is sufficient.
- **Use `using`/`lock` correctly** — Both methods are called inside `lock (_sessionsLock)` context already.

## Standard Stack

### Core (no new packages)
| Library | Version | Purpose | Note |
|---------|---------|---------|------|
| System.IO (`Directory.Exists`) | .NET 9 BCL | Filesystem existence check | Already used in service |
| System.Linq (`OrderBy`) | .NET 9 BCL | Sorting | Already used throughout |
| `StringComparer.Ordinal` | .NET 9 BCL | Stable alphabetical comparison | Matches macOS Swift `.sorted { $0 < $1 }` semantics |

No new NuGet packages required.

## Architecture Patterns

### Existing Session Rebuild Pattern

`RebuildSessionsList()` (lines 766–788) is a pure LINQ chain over `_projectData`:

```csharp
// Current (simplified)
_sessions = _projectData
    .Where(kvp => !string.IsNullOrEmpty(kvp.Key))
    .Select(kvp => { ... return new SessionInfo { ... Cwd = kvp.Value.Cwd ?? string.Empty, ... }; })
    .Where(s => s is not null)
    .OrderByDescending(s => s!.LastActivity)
    .ToList()!;
```

The new filter slots in between the `SessionInfo` construction `.Select(...)` and the existing null guard `.Where(s => s is not null)`, because `Cwd` is only available after construction:

```csharp
.Where(s => s is not null && IsValidProjectDirectory(s.Cwd))
```

### UNC Guard Pattern (from STATE.md mandate)

`Directory.Exists` on a UNC path (`\\server\share`) blocks when the server is unreachable, causing hangs during session list rebuild (called from background thread). The guard must short-circuit before calling `Directory.Exists`:

```csharp
private static bool IsValidProjectDirectory(string cwd)
{
    if (string.IsNullOrEmpty(cwd))
        return false;
    if (!Path.IsPathRooted(cwd))
        return false;
    if (cwd.StartsWith(@"\\", StringComparison.Ordinal) || cwd.StartsWith("//", StringComparison.Ordinal))
        return false;
    return Directory.Exists(cwd);
}
```

This matches the STATE.md requirement: "Path.IsPathRooted AND not-UNC before calling".

Alternatively, an inline predicate in the `.Where()` chain works if kept short. A named private static method is preferred for testability and SRP compliance.

### Subagent Sort Pattern

`BuildSubagentContext()` (lines 674–724) builds a `List<SubagentContextData>` and currently returns `result` unsorted. The fix is a one-liner on the return:

```csharp
// Before:
return result;

// After:
return result.OrderBy(a => a.AgentId, StringComparer.Ordinal).ToList();
```

`StringComparer.Ordinal` provides byte-value ordering, matching the macOS `sorted { $0.agentId < $1.agentId }` behavior for typical `agent-UUID` identifiers.

### ViewModel Selection Fallback (SES-02)

`MainViewModel.RefreshSessionList()` (lines 569–641) already implements the correct fallback:

1. Captures `previousSessionId` before rebuilding `SortedSessions`.
2. Tries `SortedSessions.FirstOrDefault(d => d.Session.Id == previousSessionId)`.
3. If not found (session filtered out), falls through to `LastSelectedSessionId` restore, then `firstActiveItem`.
4. If nothing remains, `SelectedSession` stays null and `ClearSessionData()` is called.

No ViewModel changes required for SES-02. The existing fallback path handles deleted directories naturally once `RebuildSessionsList()` stops including those sessions.

### Anti-Patterns to Avoid

- **Do NOT call `Directory.Exists` on UNC paths** — the STATE.md mandate is explicit; risk of UI hang on network timeout.
- **Do NOT filter at the ViewModel layer** — filtering belongs in `JsonlService.RebuildSessionsList()` so all consumers of `_jsonlService.Sessions` get clean data.
- **Do NOT sort in the ViewModel** — subagent sort belongs in `BuildSubagentContext()` at the service layer, consistent with where the list is constructed.

## Don't Hand-Roll

| Problem | Don't Build | Use Instead |
|---------|-------------|-------------|
| Alphabetical string sort | Custom comparison loop | `StringComparer.Ordinal` with LINQ `OrderBy` |
| Path existence check | P/Invoke or Win32 API | `System.IO.Directory.Exists` (.NET BCL) |

## Common Pitfalls

### Pitfall 1: UNC Path Hang
**What goes wrong:** `Directory.Exists(@"\\server\share\project")` blocks for 20–30 seconds when the server is unreachable. This stalls the session rebuild which runs on a background thread, and delays subsequent UI updates.
**Why it happens:** `Directory.Exists` performs a network traversal for UNC paths; if the server doesn't respond, the call hangs until OS timeout.
**How to avoid:** Check `cwd.StartsWith(@"\\")` (or `cwd.StartsWith("//")` for forward-slash UNC) before calling `Directory.Exists`. Return `false` immediately for UNC paths.
**Warning signs:** Noticeable freeze on session list refresh when disconnected from a network.

### Pitfall 2: Null/Empty Cwd Slipping Through
**What goes wrong:** `SessionInfo.Cwd` is `required string` and initialized to `string.Empty` when `kvp.Value.Cwd` is null (line 779). An empty string passed to `Directory.Exists("")` returns `false` as expected — but it's cleaner to short-circuit with an explicit `string.IsNullOrEmpty` check.
**How to avoid:** Make `IsValidProjectDirectory` short-circuit on empty/null before reaching `Directory.Exists`.

### Pitfall 3: Filter Position in LINQ Chain
**What goes wrong:** Adding the directory filter BEFORE the `.Select(...)` that creates `SessionInfo` means `Cwd` isn't available yet (the filter would need to re-access `kvp.Value.Cwd` and duplicate the null-coalescing logic).
**How to avoid:** Apply the directory filter AFTER the `.Select(...)` and BEFORE or combined with the `.Where(s => s is not null)` guard. The combined form `.Where(s => s is not null && IsValidProjectDirectory(s.Cwd))` is cleanest.

### Pitfall 4: `StringComparer.OrdinalIgnoreCase` vs `Ordinal`
**What goes wrong:** Using `OrdinalIgnoreCase` causes `agent-A` and `agent-a` to sort identically, potentially breaking determinism on case-mixed agent IDs.
**How to avoid:** Use `StringComparer.Ordinal` (case-sensitive, byte-value order). Matches macOS reference behavior. Agent IDs in practice are all lowercase UUIDs/slugs so the difference is theoretical, but `Ordinal` is the correct choice.

## Code Examples

### SES-01 + SES-02: Session Filtering

```csharp
// Source: RebuildSessionsList() in JsonlService.cs (lines 766–788)
// Change: add IsValidProjectDirectory filter

private void RebuildSessionsList()
{
    _sessions = _projectData
        .Where(kvp => !string.IsNullOrEmpty(kvp.Key))
        .Select(kvp =>
        {
            var displayName = SessionNameHelper.GetDisplayName(kvp.Value.Cwd, kvp.Key);
            if (displayName is null)
                return null;

            return new SessionInfo
            {
                Id = kvp.Key,
                Cwd = kvp.Value.Cwd ?? string.Empty,
                DisplayName = displayName,
                LastActivity = kvp.Value.LastActivity,
                ModelName = kvp.Value.ModelName
            };
        })
        .Where(s => s is not null && IsValidProjectDirectory(s.Cwd))
        .OrderByDescending(s => s!.LastActivity)
        .ToList()!;
}

private static bool IsValidProjectDirectory(string cwd)
{
    if (string.IsNullOrEmpty(cwd))
        return false;
    if (!Path.IsPathRooted(cwd))
        return false;
    if (cwd.StartsWith(@"\\", StringComparison.Ordinal) || cwd.StartsWith("//", StringComparison.Ordinal))
        return false;
    return Directory.Exists(cwd);
}
```

### SES-03: Subagent Sort

```csharp
// Source: BuildSubagentContext() in JsonlService.cs (line 723)
// Change: replace bare `return result;` with sorted return

return result.OrderBy(a => a.AgentId, StringComparer.Ordinal).ToList();
```

## Environment Availability

Step 2.6: SKIPPED — phase is purely code changes within the existing .NET 9 / WinUI 3 project. No external CLI tools, databases, or services required.

## Validation Architecture

### Test Framework
| Property | Value |
|----------|-------|
| Framework | xUnit 2.9.3 |
| Config file | `CCInfoWindows.Tests/CCInfoWindows.Tests.csproj` |
| Quick run command | `dotnet test CCInfoWindows.Tests/CCInfoWindows.Tests.csproj -c Debug --arch x64 --filter "FullyQualifiedName~JsonlServiceTests" --no-build` |
| Full suite command | `dotnet test CCInfoWindows.Tests/CCInfoWindows.Tests.csproj -c Debug --arch x64` |

### Phase Requirements → Test Map

| Req ID | Behavior | Test Type | Automated Command | File Exists? |
|--------|----------|-----------|-------------------|--------------|
| SES-01 | Sessions with deleted `Cwd` excluded from `Sessions` list | unit | `dotnet test ... --filter "FullyQualifiedName~JsonlServiceTests"` | ❌ Wave 0 |
| SES-02 | Selection resets to next valid session when active session's directory deleted | unit (indirect via session list) | `dotnet test ... --filter "FullyQualifiedName~JsonlServiceTests"` | ❌ Wave 0 |
| SES-03 | Subagent list is alphabetically stable regardless of file modification order | unit | `dotnet test ... --filter "FullyQualifiedName~JsonlServiceTests"` | ❌ Wave 0 |

### Sampling Rate
- **Per task commit:** `dotnet test CCInfoWindows.Tests/CCInfoWindows.Tests.csproj -c Debug --arch x64 --filter "FullyQualifiedName~JsonlServiceTests"`
- **Per wave merge:** `dotnet test CCInfoWindows.Tests/CCInfoWindows.Tests.csproj -c Debug --arch x64`
- **Phase gate:** Full suite green before `/gsd:verify-work`

### Wave 0 Gaps

- [ ] `CCInfoWindows.Tests/Services/JsonlServiceTests.cs` — add `RebuildSessionsList_ExcludesOrphanedSessions`, `RebuildSessionsList_ExcludesUncPaths`, `BuildSubagentContext_ReturnsAlphabeticOrder` tests. File exists; add new test methods only.
- [ ] `IsValidProjectDirectory` — if extracted as private static, make it `internal` or test via `RebuildSessionsList` integration path with temp directories.

## Sources

### Primary (HIGH confidence)
- Direct codebase read: `JsonlService.cs` lines 674–724 (`BuildSubagentContext`), lines 766–788 (`RebuildSessionsList`)
- Direct codebase read: `MainViewModel.cs` lines 569–641 (`RefreshSessionList` fallback logic)
- `spec-release-from-1.7.1-to-1.8.3.md` Phase 3 (session filtering), Phase 4 (subagent sorting) — project spec document
- `14-CONTEXT.md` — STATE.md mandate on UNC path guard

### Secondary (MEDIUM confidence)
- .NET 9 BCL documentation for `Directory.Exists`, `Path.IsPathRooted` — standard, stable, well-known behavior

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH — no new packages, all BCL
- Architecture: HIGH — implementation locations and patterns confirmed by reading actual source
- Pitfalls: HIGH — UNC guard requirement explicitly documented in STATE.md; filter position verified against actual LINQ chain
- SES-02 fallback: HIGH — `MainViewModel.RefreshSessionList()` fallback logic read directly

**Research date:** 2026-04-12
**Valid until:** Stable (internal code changes only, no external API or library version concerns)
