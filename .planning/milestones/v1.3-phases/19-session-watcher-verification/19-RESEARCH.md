# Phase 19: Session Watcher Verification - Research

**Researched:** 2026-04-14
**Domain:** FileSystemWatcher configuration in JsonlService.cs
**Confidence:** HIGH

## Summary

Phase 19 is a pure code-review verification phase. The goal is to confirm that the
`FileSystemWatcher` in `JsonlService.cs` is correctly configured to catch file-level session
metadata changes — which is the Windows port of the macOS FEAT-05 fix (session dropdown not
updating project names when switching Claude Code projects without restart).

The code review has been completed during research. The watcher configuration in
`Services/JsonlService.cs` at line 812–818 already includes all three required flags:
`NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size`, and
`IncludeSubdirectories = true`. The watched path is `%USERPROFILE%\.claude\projects`, which
is where Claude Code writes its JSONL session files. The watcher handles both `Changed` and
`Created` events through the same debounce handler. The configuration is correct — no code
change is needed.

One minor observation: the watcher does not subscribe to the `Renamed` or `Deleted` events.
For this phase's requirement (SESW-01 — file-level change detection), this is not a gap
because session metadata is updated by writing to existing files (`Changed`) or creating new
files (`Created`). Rename/delete events are out of scope for SESW-01 and are not referenced
in the macOS FEAT-05 spec.

**Primary recommendation:** A single plan performs code review and documents the verdict.
Expected outcome: no code changes — the watcher is already correct.

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions
None — discuss phase was skipped (auto-generated infrastructure phase).

### Claude's Discretion
All implementation choices are at Claude's discretion — pure infrastructure phase. Use
ROADMAP phase goal, success criteria, and codebase conventions to guide decisions.

Key notes from STATE.md:
- FileSystemWatcher already correctly configured — this phase is verification only, no code expected
- Spec reference: `spec/v1.10.0-macOS/spec-release-1.8.3-to-1.10.0.md` Phase 4 (session watcher)

### Deferred Ideas (OUT OF SCOPE)
None — discuss phase skipped.
</user_constraints>

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|------------------|
| SESW-01 | FileSystemWatcher configuration is verified to catch file-level session metadata changes (NotifyFilter, IncludeSubdirectories) | Code review of `JsonlService.cs` lines 812–818 shows the watcher already has the correct flags; this requirement is satisfied by verification, not new code |
</phase_requirements>

## Standard Stack

### Core
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| System.IO.FileSystemWatcher | .NET 9 built-in | Directory/file change notifications | BCL class — no NuGet dependency |
| xunit | 2.9.3 | Test framework for verification test | Already used across the test suite |

### Supporting
No additional libraries required. This is a verification-only phase.

### Alternatives Considered
None applicable — FileSystemWatcher is the only correct choice for synchronous file watching on .NET/Windows.

## Architecture Patterns

### FileSystemWatcher Configuration (as-is in codebase)

```csharp
// Source: CCInfoWindows/CCInfoWindows/Services/JsonlService.cs, lines 812–826
var watcher = new FileSystemWatcher(_projectsDirectory)
{
    Filter = JsonlFilePattern,             // "*.jsonl"
    IncludeSubdirectories = true,
    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
    InternalBufferSize = WatcherInternalBufferSize  // 65,536 bytes = 64 KB
};

watcher.Changed += OnFileChanged;
watcher.Created += OnFileChanged;
watcher.Error += OnWatcherError;
watcher.EnableRaisingEvents = true;
```

**What this catches:**
- `NotifyFilters.LastWrite` — file content updated (session metadata written)
- `NotifyFilters.FileName` — new session file created or renamed
- `NotifyFilters.Size` — file size changed (append to existing JSONL)
- `IncludeSubdirectories = true` — watches all project subdirectories, including `subagents/` nested dirs

### Watched Path

```csharp
// Source: JsonlService.cs, constructor line 112–113
_projectsDirectory = projectsDirectoryOverride
    ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "projects");
```

Path resolves to: `%USERPROFILE%\.claude\projects`

Claude Code session files are located at:
- `%USERPROFILE%\.claude\projects\{encoded-project-path}\{session-uuid}.jsonl`
- Subagent files: `%USERPROFILE%\.claude\projects\{encoded-project-path}\{session-uuid}\subagents\agent-*.jsonl`

`IncludeSubdirectories = true` is required and present — both the top-level JSONL files and
nested subagent files are covered.

### Event Processing Chain

```
File write → FileSystemWatcher.Changed/Created
         → OnFileChanged(sender, FileSystemEventArgs e)
         → Add e.FullPath to _pendingChangedFiles (debounce set)
         → Debounce timer (2000 ms, single-shot)
         → ProcessPendingFileChanges()
         → ProcessSingleFile(filePath)  [for each pending file]
         → RebuildSessionsList()
         → RaiseDataUpdated()           [fires DataUpdated event → UI refresh]
```

### Anti-Patterns to Avoid

- **Subscribing to `Renamed` for SESW-01:** Not needed. JSONL files are written to, not renamed,
  during normal Claude Code operation. `FileName` flag in `NotifyFilter` already handles new
  file creation detection.
- **Removing `InternalBufferSize`:** The 64 KB buffer prevents event overflow when many files
  change rapidly (e.g., on initial large scan or bulk writes). Do not reduce.

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| File change buffering | Custom polling loop | Existing debounce timer (DebounceMilliseconds = 2000) | Already implemented correctly |
| Watcher error recovery | Manual retry logic | Existing OnWatcherError with MaxWatcherRestarts = 5 | Already implemented correctly |

## Common Pitfalls

### Pitfall 1: Confusing "file-level" vs "directory-level" watching
**What goes wrong:** Using only `NotifyFilters.DirectoryName` would miss file content changes.
The macOS FSEvents bug was directory-level coalescing — individual file writes were not
triggering session refresh. On Windows, FileSystemWatcher avoids this by default if
`NotifyFilter` includes `LastWrite` and/or `Size`.
**Why it happens:** Developer sets only high-level filters to reduce noise.
**How to avoid:** `LastWrite | FileName | Size` covers all file-level mutation events.
**Warning signs:** Sessions refresh correctly on new project start but not on project switch
(metadata in existing files isn't picked up).

### Pitfall 2: Missing IncludeSubdirectories for subagent files
**What goes wrong:** Subagent JSONL files live two levels deep
(`{projectDir}/{sessionUUID}/subagents/agent-*.jsonl`). Without `IncludeSubdirectories = true`,
changes to these files would be silently ignored.
**Why it happens:** Developer watches only `_projectsDirectory` without subdirectory recursion.
**How to avoid:** `IncludeSubdirectories = true` — already set.
**Warning signs:** Main session context updates but subagent context data stays stale.

### Pitfall 3: Watcher internal buffer overflow (silently drops events)
**What goes wrong:** If more file system events occur than the buffer can hold (default 4 KB),
events are silently dropped. The `Error` event fires with a `InternalBufferOverflowException`.
**Why it happens:** Many large files written quickly — e.g., a fast-running agentic session.
**How to avoid:** `InternalBufferSize = 65536` (64 KB) — already set. The `OnWatcherError`
handler also restarts the watcher up to 5 times.

## Code Examples

### FileSystemWatcher Configuration (verified — no changes needed)

```csharp
// Source: Services/JsonlService.cs lines 812–826 — CURRENT STATE (already correct)
var watcher = new FileSystemWatcher(_projectsDirectory)
{
    Filter = JsonlFilePattern,           // "*.jsonl"
    IncludeSubdirectories = true,        // REQUIRED: subagent files live in nested dirs
    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
    // LastWrite: content updated    ← catches session metadata writes
    // FileName:  file created       ← catches new session files
    // Size:      file grown         ← catches JSONL line appends
    InternalBufferSize = WatcherInternalBufferSize  // 64 KB — prevents overflow
};
watcher.Changed += OnFileChanged;   // file content changed
watcher.Created += OnFileChanged;   // new file created
watcher.Error += OnWatcherError;    // overflow / path-not-found → auto-restart
watcher.EnableRaisingEvents = true;
```

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| macOS FSEvents directory-level watching | .NET FileSystemWatcher with file-level NotifyFilter | Windows port (v1.0) | No equivalent bug on Windows — FileSystemWatcher is file-level by default |

**Notes:**
- The macOS bug (FEAT-05) involved FSEvents coalescing directory-level events and missing
  individual file writes. FileSystemWatcher on Windows does not have this behavior — it
  fires per-file events when `NotifyFilter` is set at the file level.
- This means Phase 19 is a confirmation phase, not a bugfix phase.

## Environment Availability

Step 2.6: SKIPPED (no external dependencies — pure code review of existing C# source)

## Validation Architecture

### Test Framework
| Property | Value |
|----------|-------|
| Framework | xunit 2.9.3 |
| Config file | none (uses default xunit discovery) |
| Quick run command | `dotnet test CCInfoWindows.Tests/CCInfoWindows.Tests.csproj -r win-x64 --filter "JsonlService"` |
| Full suite command | `dotnet test CCInfoWindows.Tests/CCInfoWindows.Tests.csproj -r win-x64` |

### Phase Requirements → Test Map
| Req ID | Behavior | Test Type | Automated Command | File Exists? |
|--------|----------|-----------|-------------------|--------------|
| SESW-01 | FileSystemWatcher configuration has correct NotifyFilter flags and IncludeSubdirectories | unit (config inspection) | `dotnet test CCInfoWindows.Tests/CCInfoWindows.Tests.csproj -r win-x64 --filter "WatcherConfig"` | ❌ Wave 0 |

### Sampling Rate
- **Per task commit:** `dotnet test CCInfoWindows.Tests/CCInfoWindows.Tests.csproj -r win-x64 --filter "WatcherConfig"`
- **Per wave merge:** `dotnet test CCInfoWindows.Tests/CCInfoWindows.Tests.csproj -r win-x64`
- **Phase gate:** Full suite green before `/gsd:verify-work`

### Wave 0 Gaps
- [ ] `CCInfoWindows.Tests/Services/JsonlServiceWatcherTests.cs` — covers SESW-01 (watcher config verification test)

*(Note: The 13 pre-existing test failures in `JsonlServiceTests.cs` are known tech debt from parameter
naming mismatch — they are unrelated to this phase and must not regress further.)*

## Open Questions

1. **Should the `Renamed` event be subscribed?**
   - What we know: `Renamed` fires when a file is renamed (e.g., `.jsonl.tmp` → `.jsonl`). Some
     editors and tools write files atomically via rename. Claude Code's write pattern is unknown.
   - What's unclear: Does Claude Code use atomic rename writes for JSONL files?
   - Recommendation: Out of scope for SESW-01. The current `Changed` + `Created` combination
     handles the standard append-write pattern. If Claude Code uses atomic rename writes, that
     would be a separate bug report.

2. **Should `Deleted` event be subscribed?**
   - What we know: Deleted sessions currently remain in the session list until next restart
     or full rescan.
   - What's unclear: Is stale-session-after-delete an observed user complaint?
   - Recommendation: Out of scope for SESW-01. Separate backlog item if needed.

## Sources

### Primary (HIGH confidence)
- `CCInfoWindows/CCInfoWindows/Services/JsonlService.cs` — direct code inspection of StartWatching(), lines 807–826
- `spec/v1.10.0-macOS/spec-release-1.8.3-to-1.10.0.md` FEAT-05 section — Windows port requirements
- `.planning/STATE.md` pitfall section — "FileSystemWatcher already correctly configured — this phase is verification only, no code expected"

### Secondary (MEDIUM confidence)
- Microsoft .NET 9 docs (FileSystemWatcher): NotifyFilter enum values and behavior — training knowledge confirmed by code inspection

### Tertiary (LOW confidence)
- None

## Metadata

**Confidence breakdown:**
- Current watcher config: HIGH — read directly from source code
- Config correctness verdict: HIGH — cross-checked against FEAT-05 spec requirements
- Missing Renamed/Deleted handlers: HIGH — confirmed by grep returning no matches
- Whether Renamed/Deleted are needed for SESW-01: MEDIUM — Claude Code write pattern not empirically tested

**Research date:** 2026-04-14
**Valid until:** Stable — FileSystemWatcher is BCL and JsonlService changes rarely
