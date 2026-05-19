# Phase 29: Fix Subagent activity detection (mtime parity) - Research

**Researched:** 2026-05-18
**Domain:** .NET 9 / WinUI 3 desktop — filesystem-based activity detection in `JsonlService`
**Confidence:** HIGH

## Summary

Phase 29 is a small-surface bugfix in `JsonlService.BuildSubagentContext`
(`CCInfoWindows/CCInfoWindows/Services/JsonlService.cs:693-743`). The 30s
activity cutoff currently uses the last assistant entry's JSONL timestamp
(`lastEntry.Timestamp ?? DateTimeOffset.MinValue` at line 713), which drops
subagents whose tool-result writes are fresh but whose last assistant message
is older than 30s. macOS upstream uses `FileManager.contentModificationDate`
on the agent file — every tool-result append touches mtime, so long tool-calls
keep the agent visible. The Windows port should match that semantic with
`File.GetLastWriteTimeUtc(file)`.

All locked decisions in CONTEXT.md are technically sound on .NET 9 / NTFS.
The riskiest unknown is **whether NTFS reliably bumps mtime on append by an
external process** — this is the single premise the entire fix hinges on.
The Microsoft docs and NTFS behaviour confirm this (see Finding 1 below),
but the visual UAT must verify it end-to-end on the actual user machine.

**Primary recommendation:** Implement exactly as CONTEXT.md specifies. Apply
`File.GetLastWriteTimeUtc` at the **top** of the `foreach` body (before
`ReadTailLines`), catch `IOException`/`FileNotFoundException`/`UnauthorizedAccessException`
in line with the existing defensive pattern, convert via
`new DateTimeOffset(dt, TimeSpan.Zero)`, and mirror `JsonlServiceColdStartTests`
for the new test class.

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions

- Use `File.GetLastWriteTimeUtc(file)` as the cutoff comparison value — exact macOS `contentModificationDate` parity.
- Apply the cutoff **before** JSONL parsing — `File.GetLastWriteTimeUtc()` check immediately after `BuildSubagentFileList` (or at top of the foreach), so unread subagent files are never opened.
- Store `File.GetLastWriteTimeUtc(file)` (as `DateTimeOffset`) into `SubagentContextData.LastActivity` — keeps the filter logic and the UI's "last seen" display synchronized.
- Keep `SubagentActivityWindowSeconds = 30` constant.
- New dedicated file `CCInfoWindows.Tests/Services/JsonlServiceSubagentTests.cs`.
- Simulate mtime in tests with `File.SetLastWriteTimeUtc(path, DateTime.UtcNow)` against real temp-files.
- Minimum two scenarios: stale-entry-fresh-mtime → visible; all-stale → filtered.
- Visual UAT validation IS required via `mcp__windows-mcp__*` tooling.

### Claude's Discretion

- Specific test class structure, test-method names, fixture cleanup pattern, and exact placement of the `File.GetLastWriteTimeUtc` call within `BuildSubagentContext`.
- If `File.GetLastWriteTimeUtc(file)` throws `IOException` on a deleted-mid-call file — treat the same as existing `IOException` catch on `ReadTailLines`: skip silently (continue).

### Deferred Ideas (OUT OF SCOPE)

- Configurable `SubagentActivityWindowSeconds` via `AppSettings`.
- `IFileSystem` abstraction for pure unit tests.
- Pruning of subagent files older than visibility window from `BuildSubagentFileList`.
</user_constraints>

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|------------------|
| SUBAGENT-01 | `BuildSubagentContext` MUST use `File.GetLastWriteTimeUtc(file)` as the activity timestamp; the 30s cutoff is applied before `ReadTailLines`. | Findings 1, 2, 3 — mtime semantics on NTFS, deletion races, conversion idiom. |
| SUBAGENT-02 | `SubagentContextData.LastActivity` MUST equal the mtime used for the cutoff (no second `DateTimeOffset.UtcNow` call, no `lastEntry.Timestamp` leakage). | Finding 3 — single conversion site. |
| SUBAGENT-03 | New `JsonlServiceSubagentTests.cs` MUST cover: (a) stale entry + fresh mtime → visible; (b) stale entry + stale mtime → filtered. Fixture follows `JsonlServiceColdStartTests` precedent. | Findings 4, 9 — temp-file fixture pattern; xUnit naming convention. |
| SUBAGENT-04 | Visual UAT MUST verify that a 4-parallel-agent Claude CLI session is rendered with 4 visible subagents in the KONTEXTFENSTER panel. | Finding 7 — windows-mcp tool inventory; Finding 8 — synthetic reproduction strategy. |
| SUBAGENT-05 | The fix MUST NOT regress the existing `entries.Count == 0 ⇒ continue` guard, the model-name resolution, or the `result.OrderBy(AgentId, StringComparer.Ordinal)` ordering. | Finding 5 — control-flow integration with existing code. |
</phase_requirements>

## Architectural Responsibility Map

| Capability | Primary Tier | Secondary Tier | Rationale |
|------------|-------------|----------------|-----------|
| Filesystem mtime probe | Backend service (`JsonlService` static helper) | — | Pure I/O metadata read; no UI, no DI. |
| 30s cutoff arithmetic | Backend service (`JsonlService`) | — | Already lives at line 696; just changes its source. |
| Subagent visibility filter | Backend service | — | `BuildSubagentContext` is the single decision point. |
| Display of last-activity timestamp | ViewModel (`MainViewModel.SubagentContexts`) → View | — | Already binds to `SubagentContextData.LastActivity`; semantics shift but shape unchanged. |
| Test fixture (temp-files + mtime touch) | Test project (`CCInfoWindows.Tests/Services`) | — | xUnit `IDisposable` pattern, same as `JsonlServiceColdStartTests`. |

## Standard Stack

No new libraries. Phase 29 uses only existing dependencies.

| API / Type | Source | Purpose | Verified |
|------------|--------|---------|----------|
| `System.IO.File.GetLastWriteTimeUtc(string)` | .NET 9 BCL | Read NTFS last-write timestamp as UTC `DateTime`. | [CITED: learn.microsoft.com/dotnet/api/system.io.file.getlastwritetimeutc] |
| `System.IO.File.SetLastWriteTimeUtc(string, DateTime)` | .NET 9 BCL | Test helper — bump mtime to "now". | [CITED: learn.microsoft.com/dotnet/api/system.io.file.setlastwritetimeutc] |
| `System.DateTimeOffset` ctor `(DateTime, TimeSpan)` | .NET 9 BCL | Convert `DateTime{Kind=Utc}` → `DateTimeOffset` w/o offset ambiguity. | [CITED: learn.microsoft.com/dotnet/api/system.datetimeoffset.-ctor] |
| `xUnit.net 2.x` `[Fact]` + `IDisposable` fixture | already in `CCInfoWindows.Tests.csproj` | Test class scaffolding. | [VERIFIED: codebase grep — used by `JsonlServiceColdStartTests.cs:12`] |

## Architecture Patterns

### Control Flow After Fix

```
BuildSubagentContext(subagentFiles, sonnetContextSize)
├─ cutoff = DateTimeOffset.UtcNow.AddSeconds(-30)              // unchanged, line 696
├─ foreach (file in subagentFiles)
│   ├─ try
│   │   ├─ mtimeUtc = File.GetLastWriteTimeUtc(file)            // NEW — moved BEFORE ReadTailLines
│   │   ├─ lastActivity = new DateTimeOffset(mtimeUtc, TimeSpan.Zero)
│   │   ├─ if (lastActivity < cutoff) continue                  // NEW — short-circuits before any I/O on contents
│   │   ├─ lines = ReadTailLines(file)
│   │   ├─ entries = ParseJsonlEntries(lines).Where(assistant).ToList()
│   │   ├─ if (entries.Count == 0) continue                     // preserved — see Finding 5
│   │   ├─ lastEntry = entries[^1]
│   │   ├─ totalTokens = ComputeContextTokens(lastEntry)        // unchanged
│   │   ├─ modelName = lastEntry.Message?.Model                 // unchanged
│   │   ├─ result.Add(new SubagentContextData {
│   │   │     ...,
│   │   │     LastActivity = lastActivity                        // mtime, not lastEntry.Timestamp
│   │   │   })
│   ├─ catch (FileNotFoundException) skip
│   ├─ catch (IOException) skip — race with deletion / lock contention
│   └─ catch (UnauthorizedAccessException) skip
└─ return result.OrderBy(AgentId, StringComparer.Ordinal).ToList()
```

### Anti-Patterns to Avoid

- **Don't** call `File.GetLastWriteTime(...)` (without `Utc`) — returns local time; mixing with `DateTimeOffset.UtcNow` introduces a DST-shift bug twice a year.
- **Don't** convert via `DateTime.SpecifyKind(...).ToUniversalTime()` — `GetLastWriteTimeUtc` already returns `Kind=Utc`; `ToUniversalTime` on an already-UTC `DateTime` is a no-op but obscures intent.
- **Don't** open a `FileStream` solely to read mtime. The static `File.GetLastWriteTimeUtc(string)` is a `GetFileAttributesEx` Win32 metadata call — no file handle, no read lock (see Finding 6).
- **Don't** abandon the `entries.Count == 0 ⇒ continue` guard — a brand-new agent file with mtime fresh but only a `user` entry must not surface as a visible subagent with `TotalTokens = 0` and `ModelName = null` (Finding 5).

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Filesystem metadata abstraction | A custom `IFileSystem` interface | `File.GetLastWriteTimeUtc` directly | CONTEXT.md explicitly defers this. Phase 25's tests already follow the temp-file pattern (see `JsonlServiceColdStartTests`). |
| Cross-process mtime bumping | A `FileSystemWatcher` to detect external writes | Trust NTFS to bump mtime on append | NTFS bumps `LastWriteTime` on every successful write that flushes (Finding 1). A watcher would only confirm this redundantly. |
| Custom time-zone handling | `TimeZoneInfo.ConvertTimeFromUtc(...)` | Plain UTC arithmetic | Cutoff and mtime are both UTC; comparison stays in UTC. |

## Findings

### Finding 1 — NTFS bumps mtime on every successful append

[VERIFIED: learn.microsoft.com/windows/win32/api/fileapi/nf-fileapi-getfileattributesexa]
[VERIFIED: learn.microsoft.com/dotnet/api/system.io.file.getlastwritetimeutc]

`File.GetLastWriteTimeUtc` is a thin wrapper over Win32 `GetFileAttributesEx`,
which returns the `ftLastWriteTime` field of NTFS' `$STANDARD_INFORMATION`
attribute. NTFS updates this field on every write that flushes through the
cache manager — i.e., every time the Claude CLI calls `write()` on the
agent JSONL file, the timestamp advances. Tool-result appends (which CONTEXT.md
identifies as the "fresh writes" that the current bug ignores) ARE writes
through the same code path as assistant-message appends, so they DO bump mtime.

**However:** Windows has a per-volume `NtfsDisableLastAccessUpdate` registry
flag and a "last access" optimisation that is sometimes confused with
"last write." `LastWriteTime` is **not** subject to that throttling — it
updates synchronously on every write. **Last access** time (`GetLastAccessTimeUtc`)
is the throttled one. We use **write** time, so this concern does not apply.

[CITED: learn.microsoft.com/windows-server/administration/windows-commands/fsutil-behavior]
"`disablelastaccess` … affects only the file last access time; last modify/write time is updated synchronously."

**Edge case:** Files modified through memory-mapped I/O may delay mtime updates
until the mapping is closed/flushed. The Claude CLI does NOT mmap its JSONL
files (it appends with sequential `write()` calls — confirmed by the existing
`JsonlServiceTests.ReadTailLines_OpenWithReadWriteShare_DoesNotThrowWhenFileIsHeldOpen`
test at `JsonlServiceTests.cs:73-82` which holds the file via `FileStream`,
not mmap). Mark this as an [ASSUMED] sub-finding: we are confident the Claude
CLI uses normal stream-based appends, but we have not source-traced it.

**Confidence:** HIGH for normal-write case; MEDIUM for "every conceivable
write path." The visual UAT (Finding 7) is the authoritative confirmation
on the user's actual machine.

### Finding 2 — Behavior on file-deletion race

[VERIFIED: learn.microsoft.com/dotnet/api/system.io.file.getlastwritetimeutc]

The .NET docs are explicit:

> "If the file described in the path parameter does not exist, this method
> returns 12:00 midnight, January 1, 1601 A.D. (C.E.) Coordinated Universal
> Time (UTC), adjusted to local time."

So `File.GetLastWriteTimeUtc(...)` on a non-existent path does **not throw** —
it returns `1601-01-01T00:00:00Z`. This is a critical surprise for the cutoff
logic: a deleted-mid-call file would compare as `1601 < cutoff`, so the
`continue` branch is correctly taken even WITHOUT an exception. **However:**

| Failure mode | What `GetLastWriteTimeUtc` does | Correct catch |
|--------------|--------------------------------|---------------|
| File deleted between `BuildSubagentFileList` and the call | Returns `1601-01-01Z` (no throw) | None needed — falls through `< cutoff` filter |
| Path malformed | `ArgumentException` | Should not happen; paths come from `Directory.GetFiles` |
| `PathTooLongException` | `PathTooLongException` | Same as above — defensive `IOException` catch covers it |
| Caller lacks read-attribute permission | `UnauthorizedAccessException` | Existing catch at line 736 covers it |
| Underlying I/O failure (network share offline, disk error) | `IOException` | Existing catch at line 732 covers it |

**Recommendation:** Catch `UnauthorizedAccessException` and `IOException`
(both already present in the existing `try/catch` at lines 732-739). Do
NOT add a `FileNotFoundException` catch — `GetLastWriteTimeUtc` does not
throw it. CONTEXT.md's "skip silently on IOException" guidance is correct.

[CITED: learn.microsoft.com/dotnet/api/system.io.file.getlastwritetimeutc#exceptions]

**Confidence:** HIGH.

### Finding 3 — `DateTime` → `DateTimeOffset` conversion idiom

[VERIFIED: learn.microsoft.com/dotnet/api/system.datetimeoffset.-ctor#system-datetimeoffset-ctor(system-datetime))]

`File.GetLastWriteTimeUtc` returns `DateTime` with `Kind=Utc`. Three viable
conversions:

| Idiom | Behavior | Verdict |
|-------|----------|---------|
| `new DateTimeOffset(dt, TimeSpan.Zero)` | Constructs explicit-UTC `DateTimeOffset`. | **PREFERRED** — explicit, no kind inspection. |
| `new DateTimeOffset(dt)` | Inspects `dt.Kind`: `Utc` → `Zero`; `Local` → local offset; `Unspecified` → throws. | Works because `Kind=Utc` is guaranteed, but reading the code requires knowing the kind contract. |
| `dt.ToDateTimeOffset()` | No such extension in BCL. | N/A. |

**Recommendation:** `new DateTimeOffset(mtimeUtc, TimeSpan.Zero)` — most
explicit, defensible at code review, immune to a future caller switching
to `GetLastWriteTime` (non-UTC) which would silently produce wrong offsets
with the single-arg ctor.

**Confidence:** HIGH.

### Finding 4 — Test fixture pattern (project convention)

[VERIFIED: codebase grep — `CCInfoWindows.Tests/Services/JsonlServiceColdStartTests.cs:12-26`]

The Phase-25 precedent is the authoritative template:

```csharp
public class JsonlServiceColdStartTests : IDisposable
{
    private readonly string _tempDir;

    public JsonlServiceColdStartTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "cs-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }
    // ...
}
```

Conventions observed across `JsonlServiceColdStartTests.cs` and `JsonlServiceTests.cs`:

- **Fixture lifetime:** Per-test (xUnit default — new instance per `[Fact]`),
  NOT `IClassFixture`. Each test gets a fresh, isolated `_tempDir`.
- **Cleanup:** `IDisposable.Dispose` with `Directory.Delete(..., recursive: true)`.
- **Test names:** `Method_Scenario_ExpectedBehavior` (e.g.,
  `ParseFileIntoProject_NoEntryHasCwd_FallsBackToDecodedProjectDirName`).
  Pascal-with-underscores, no `Should_` prefix.
- **Temp-file helper pattern:** Static method writing JSONL lines via
  `File.AppendAllText` after building objects with anonymous-type JSON
  serialization. See `WriteAssistantJsonlLine` at
  `JsonlServiceColdStartTests.cs:180-236`.
- **Asserts:** `Assert.Contains(...)` / `Assert.DoesNotContain(...)` /
  `Assert.Equal(...)` — no FluentAssertions in this codebase.

**Recommendation for `JsonlServiceSubagentTests`:**

```csharp
public class JsonlServiceSubagentTests : IDisposable
{
    private readonly string _tempDir;

    public JsonlServiceSubagentTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "subagent-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    // Helper: writes a session JSONL + subagent JSONL with a STALE assistant timestamp
    // (e.g., 5 minutes ago), then optionally bumps mtime to "now".
    private static string CreateSubagentFile(
        string projectDir,
        string agentId,
        DateTimeOffset assistantTimestamp,
        DateTime? mtimeOverride = null) { ... }

    [Fact]
    public async Task BuildSubagentContext_StaleAssistantEntry_FreshMtime_AgentRemainsVisible() { ... }

    [Fact]
    public async Task BuildSubagentContext_StaleMtime_AgentIsFiltered() { ... }

    // Optional third test — boundary
    [Fact]
    public async Task BuildSubagentContext_MtimeExactlyAtCutoff_AgentIsVisible() { ... }
}
```

**Confidence:** HIGH — direct citation from existing precedent.

### Finding 5 — Preserve the empty-entries guard

[VERIFIED: `JsonlService.cs:709-710`]

```csharp
if (entries.Count == 0)
    continue;
```

This guard MUST stay. Reason: an agent file may have just been created (mtime
fresh) but contain only the user prompt entry — no assistant entries yet. The
new mtime-cutoff lets the file PAST the time filter, but the existing code
needs `lastEntry.Message?.Model` and `ComputeContextTokens(lastEntry)` from
an assistant entry. Without the guard, we'd `result.Add` a `SubagentContextData`
with `TotalTokens = 0` and `ModelName = null` — surfacing an empty subagent
bar in the UI for ~1-2s until the first assistant write arrives.

**macOS parity check:** Does Swift `findActiveAgents` also require ≥1
assistant entry? Without access to the upstream Swift source in this repo
(searched — no `JSONLParser.swift` found), we cannot do a line-by-line
comparison. The CONTEXT.md spec says "every tool-result write touches mtime"
— note: tool-result writes are `user` type entries, not `assistant`. So
mtime can be fresh while the file has NO assistant entries (only user +
tool-result). The guard is the correct Windows behavior because we need
model+token data; an empty card would be visual noise.

**Recommendation:** Keep the guard. Document in a code comment that the
mtime-cutoff filter MUST run before this guard so we don't waste
`ReadTailLines` on stale files.

**Confidence:** HIGH for the guard's necessity; MEDIUM for macOS-parity
since we cannot diff the Swift source.

### Finding 6 — Concurrent file access semantics

[VERIFIED: learn.microsoft.com/windows/win32/api/fileapi/nf-fileapi-getfileattributesexa]

`GetFileAttributesEx` is a **metadata** query, not a file-open. It does not
require `FILE_SHARE_READ` permission on the file — it queries the directory
entry. Even if the Claude CLI process holds the file open with `FileShare.None`
(it does not, but hypothetically), `File.GetLastWriteTimeUtc` still succeeds.

This is confirmed by existing test `ReadTailLines_OpenWithReadWriteShare_DoesNotThrowWhenFileIsHeldOpen`
(`JsonlServiceTests.cs:73-82`) which holds the file open via
`new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite)`
and successfully reads it. `File.GetLastWriteTimeUtc` is strictly weaker
in its locking requirements.

**Recommendation:** No special handling needed for concurrent access.

**Confidence:** HIGH.

### Finding 7 — `windows-mcp` tool inventory for visual UAT

[VERIFIED: MCP server tools available in session — see system-reminder block]

The session-loaded `windows-mcp` server exposes these tools (per the
PreToolUse hook context block):

| Tool | Action(s) | Use for Phase 29 UAT |
|------|-----------|----------------------|
| `mcp__windows-mcp__window_management` | `action='find'`, `title='ccInfo*'` → returns handle | Locate the running CCInfoWindows.exe window. |
| `mcp__windows-mcp__ui_automation` | `action='find'` / `'click'` / `'type'` | Find the "KONTEXTFENSTER" / "Context Window" panel and enumerate subagent rows by UIA. |
| `mcp__windows-mcp__screenshot_control` | `annotate=true` | Take an annotated screenshot of the panel as UAT evidence. |
| `mcp__windows-mcp__keyboard_control` | `action='press'` / `'type'` | Trigger a manual refresh if needed. |
| `mcp__windows-mcp__mouse_control` | `action='drag'` | Not needed — no canvas drawing. |

**UAT methodology:**

1. Build Release: `dotnet build CCInfoWindows/CCInfoWindows/CCInfoWindows.csproj -c Release -o CCInfoWindows/CCInfoWindows/bin/x64/Release/net9.0-windows10.0.19041.0/`
2. Reproduce a 4-parallel-agent scenario in `%USERPROFILE%\.claude\projects\` (see Finding 8 for synthetic option).
3. Launch CCInfoWindows.exe.
4. `window_management(action='find', title='ccInfo')` → handle.
5. `ui_automation(action='find', windowHandle=handle, ...)` → enumerate `SubagentDisplayData` rows in the `SubagentContexts` ItemsControl.
6. Assert count == 4.
7. `screenshot_control(annotate=true)` → archive as `spec/v1.11.1-macOS/ccinfo-4-sub-agents-postfix.png`.

**Note on UIA labels:** `SubagentDisplayData.AgentId` is bound to a TextBlock
in MainView. The exact `AutomationProperties.Name` on the ItemsControl is
not researched here — the UAT plan task should `screenshot_control(annotate=true)`
to identify the correct UIA element before scripting the assertion.

**Confidence:** HIGH for tool availability; MEDIUM for the exact UIA element
to query (will be discovered at UAT-execution time, not planning time).

### Finding 8 — Synthetic 4-agent reproduction without launching Claude CLI

A real 4-parallel-agent Claude CLI session is hard to reproduce on demand
(requires actual agent workload). For the visual UAT, the test fixture
can be staged manually:

```
%USERPROFILE%\.claude\projects\
└── D--myProjects-ccInfoWin\
    └── <sessionUUID>.jsonl                       (main session — must exist & be newest)
    └── <sessionUUID>\
        └── subagents\
            ├── agent-aaaa.jsonl                  (mtime: now, assistant entry 5min old)
            ├── agent-bbbb.jsonl                  (mtime: now, assistant entry 2min old)
            ├── agent-cccc.jsonl                  (mtime: now-15s, assistant entry 10min old)
            └── agent-dddd.jsonl                  (mtime: now-10s, assistant entry 30min old)
```

Each `agent-*.jsonl` needs at minimum one valid assistant JSONL line so
`entries.Count > 0` (Finding 5). Use the same JSON shape from
`JsonlServiceColdStartTests.WriteAssistantJsonlLine` with an `isSidechain: true`
flag (subagents always have `isSidechain=true` per `JsonlService.cs:703-704`
comment, but the subagent filter at line 706 already does NOT apply the
sidechain exclusion to subagent files).

After writing the files, use `File.SetLastWriteTimeUtc(path, DateTime.UtcNow)`
(or PowerShell `(Get-Item path).LastWriteTimeUtc = [DateTime]::UtcNow`)
to set the mtimes precisely. Then launch CCInfoWindows.

**Pre-fix verification:** Running this fixture against the CURRENT unfixed
build MUST show 0-2 visible agents (depending on assistant-entry recency).
This pre-fix screenshot is the regression baseline.
**Post-fix verification:** Same fixture, post-fix build MUST show all 4.

**Confidence:** HIGH — straightforward filesystem staging.

### Finding 9 — Time-zone correctness

[VERIFIED: .NET docs — both `DateTimeOffset.UtcNow` and `File.GetLastWriteTimeUtc` return UTC]

Both sides of the comparison are UTC:

- LHS (cutoff): `DateTimeOffset.UtcNow.AddSeconds(-30)` — UTC `DateTimeOffset` with offset `TimeSpan.Zero`.
- RHS (last activity): `new DateTimeOffset(File.GetLastWriteTimeUtc(file), TimeSpan.Zero)` — UTC `DateTimeOffset` with offset `TimeSpan.Zero`.

`DateTimeOffset` comparison is offset-aware: `a < b` compares
`a.UtcDateTime < b.UtcDateTime` regardless of offsets. Since both offsets
are zero, this is pure UTC arithmetic. No DST risk. No local-time leakage.

**Caveat:** If a future maintainer accidentally swaps `GetLastWriteTimeUtc`
for `GetLastWriteTime` (local time), the constructor
`new DateTimeOffset(localDt, TimeSpan.Zero)` would throw at runtime
(`ArgumentException: The UTC Offset for Utc DateTime values must be 0`)
when `Kind=Local`, which is a safe failure. Add a code comment to flag
the UTC requirement.

**Confidence:** HIGH.

### Finding 10 — macOS parity contract (not line-by-line)

[CITED: STATE.md Roadmap Evolution + CONTEXT.md specifics block]

The upstream Swift source `JSONLParser.swift:457-483, findActiveAgents` is
NOT present in this repo (verified — no `*.swift` files found, no
`findActiveAgents` matches anywhere). The parity contract is therefore
documented by spec, not by diff:

- macOS uses `FileManager.contentModificationDate` of the agent file (per CONTEXT.md spec, sourced from STATE.md commit log).
- .NET equivalent: `File.GetLastWriteTimeUtc`.
- macOS does NOT (per spec) use any additional signals (e.g., file size growth, atime, ctime). The contract is: **mtime > cutoff ⇒ visible**.
- macOS keeps a 30s window. We keep a 30s window (`SubagentActivityWindowSeconds = 30`).

[ASSUMED] We cannot independently verify Swift behavior in this session.
The visual UAT against the actual macOS reference (parallel observation of
both apps with the same Claude CLI session) is the authoritative parity
check. If line-by-line Swift verification is desired, fetch
`stefanlange/ccInfo` v1.12.0 source separately.

**Confidence:** MEDIUM-HIGH — the contract is well-specified, the line-by-line check is unfulfilled.

## Runtime State Inventory

> Phase 29 is a code-only fix. No data migration. The inventory is included for completeness.

| Category | Items Found | Action Required |
|----------|-------------|------------------|
| Stored data | None — `SubagentContextData` is in-memory only, never persisted. | None. |
| Live service config | None — no external service stores subagent-related config. | None. |
| OS-registered state | None — no Task Scheduler / pm2 / systemd entries. | None. |
| Secrets / env vars | None — no credentials referenced by `BuildSubagentContext`. | None. |
| Build artifacts | None — pure source change recompiles into existing `CCInfoWindows.exe`. | None. |

**Nothing found in any category** — Phase 29 is a single-function code edit
with no runtime-state ripple. Verified by codebase grep for
`SubagentContextData`, `SubagentActivityWindowSeconds`, and `BuildSubagentContext`
(see `JsonlService.cs`, `MainViewModel.cs`, `ContextWindowData.cs`,
`Services/Interfaces/IJsonlService.cs` — no persistence, no IPC).

## Common Pitfalls

### Pitfall 1: Mixing UTC and Local in the conversion

**What goes wrong:** Code reviewer sees `File.GetLastWriteTimeUtc` returns
`DateTime`, instinctively wraps with `new DateTimeOffset(dt)` (single-arg).
Single-arg ctor reads `dt.Kind` — works because `Kind=Utc`, but is brittle.

**Why it happens:** The single-arg ctor's `Kind`-inspection contract is
non-obvious; the explicit `(dt, TimeSpan.Zero)` form removes the ambiguity.

**How to avoid:** Use `new DateTimeOffset(mtimeUtc, TimeSpan.Zero)` and add
a one-line code comment: `// GetLastWriteTimeUtc guarantees Kind=Utc; explicit zero offset for review clarity`.

**Warning signs:** Any later refactor that swaps `GetLastWriteTimeUtc` for
`GetLastWriteTime`. The explicit `(dt, TimeSpan.Zero)` form will throw an
`ArgumentException` at runtime if `Kind=Local` — a loud failure beats a
silent off-by-DST bug.

### Pitfall 2: Forgetting to drop the now-unused `lastActivity` from `lastEntry.Timestamp`

**What goes wrong:** After moving the cutoff up and setting
`LastActivity = mtimeAsDto`, the old line 713 `var lastActivity = lastEntry.Timestamp ?? DateTimeOffset.MinValue;`
gets left in place as dead code (still computed but never used). Worse — a
maintainer later "fixes" the dead code by re-introducing `lastActivity` into
the `result.Add(...)` block, partially reverting the fix.

**Why it happens:** Two locals named similarly (`lastActivity` vs.
`mtimeAsDto` vs. `lastEntryTimestamp`).

**How to avoid:** Remove line 713 entirely. Rename the new local
unambiguously: `subagentFileMtime` or `lastWriteTimeOffset`.

**Warning signs:** Grep `lastEntry.Timestamp` in `BuildSubagentContext`
post-fix — should return zero matches.

### Pitfall 3: Empty agent file surfaces as visible row

**What goes wrong:** A subagent file has been created (mtime fresh) but the
first assistant entry hasn't been written yet. If the `entries.Count == 0`
guard is moved AFTER the mtime check incorrectly, the agent appears with
`TotalTokens = 0` and `ModelName = null` until the first assistant write.

**Why it happens:** Refactor enthusiasm — "let's just guard on mtime, drop
the entry-count guard."

**How to avoid:** Keep both guards. mtime guard FIRST (cheap), entry-count
guard SECOND (after `ReadTailLines` + `ParseJsonlEntries`). See Finding 5.

**Warning signs:** Visual UAT shows a flickering empty subagent bar at the
start of a new agent's lifecycle.

### Pitfall 4: `File.SetLastWriteTimeUtc` on a held-open file

**What goes wrong:** In tests, calling `File.SetLastWriteTimeUtc(path, ...)`
while a `StreamWriter` still holds the file open via `File.AppendAllText`
internals — works on Windows because `AppendAllText` opens, writes, closes
synchronously. But if a test author switches to `FileStream` + write-without-dispose,
`SetLastWriteTimeUtc` may throw `IOException: file in use`.

**Why it happens:** Test refactor — switching from helper static methods
to direct `FileStream` use for "control."

**How to avoid:** Use `File.AppendAllText` / `File.WriteAllText` in test
helpers. They close the file before returning. Then `SetLastWriteTimeUtc`
on the closed file is safe.

**Warning signs:** Intermittent `IOException` in the new test class on
CI / antivirus-scanning machines.

### Pitfall 5: Antivirus mtime touch

**What goes wrong:** Some Windows AV products (Defender, Sophos) re-write
file metadata after a scan, which can update mtime AFTER our
`File.SetLastWriteTimeUtc(path, fiveMinAgo)` call but BEFORE the assertion
runs. The test then sees a fresh mtime instead of the stale one and the
"stale mtime ⇒ filtered" test fails intermittently.

**Why it happens:** AV scans newly-written files in `Path.GetTempPath()`.

**How to avoid:** After `File.SetLastWriteTimeUtc(path, staleTime)`,
re-read `File.GetLastWriteTimeUtc(path)` and assert it matches the value
we just set. If it doesn't, the test environment is hostile; skip with
a clear message rather than producing a flaky red.

**Confidence:** LOW — observed in some CI environments, not consistently.
Add the re-read assertion as a defensive measure; if it fires, document
in test source.

**Warning signs:** Flaky test on Defender-equipped CI; passes on dev box.

### Pitfall 6: Test running in <1s — mtime granularity

**What goes wrong:** NTFS mtime resolution is 100 ns, but FAT32 / network
shares can be 2 s. If a test runs in a temp directory that happens to be
on a low-resolution filesystem (rare on dev machines, possible on shared
network home dirs), `File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddSeconds(-29))`
may round to `AddSeconds(-30)`, putting the file exactly at the cutoff
boundary and the test becomes timing-dependent.

**Why it happens:** Path.GetTempPath() can be redirected by env vars
(`TEMP`, `TMP`).

**How to avoid:** Use generous offsets: stale = 5 minutes ago, fresh = 0 s
ago. Don't test boundary precision unless intentional (and then write a
boundary-specific test that allows ±2 s tolerance).

**Confidence:** LOW probability, but documented for completeness.

## Code Examples

### Reference Patch (illustrative — exact placement at Claude's discretion)

```csharp
// Source: Locked in CONTEXT.md; structured for readability.
// CCInfoWindows/CCInfoWindows/Services/JsonlService.cs, replaces lines 693-743.

private static IReadOnlyList<SubagentContextData> BuildSubagentContext(
    List<string> subagentFiles, long sonnetContextSize)
{
    var result = new List<SubagentContextData>();
    var cutoff = DateTimeOffset.UtcNow.AddSeconds(-SubagentActivityWindowSeconds);

    foreach (var file in subagentFiles)
    {
        try
        {
            // macOS parity: contentModificationDate. Every tool-result write
            // bumps NTFS LastWriteTime, so long tool-calls keep the agent visible
            // even when the last assistant entry is older than the cutoff.
            // GetLastWriteTimeUtc guarantees Kind=Utc; explicit zero offset
            // makes the UTC requirement explicit at the comparison site.
            var mtimeUtc = File.GetLastWriteTimeUtc(file);
            var lastActivity = new DateTimeOffset(mtimeUtc, TimeSpan.Zero);

            // Short-circuit BEFORE ReadTailLines: stale files are never opened.
            if (lastActivity < cutoff)
                continue;

            var lines = ReadTailLines(file);
            // Subagent files have isSidechain=true on all entries by design —
            // do not apply the sidechain filter here.
            var entries = ParseJsonlEntries(lines)
                .Where(e => string.Equals(e.Type, "assistant", StringComparison.OrdinalIgnoreCase))
                .ToList();

            // Guard preserved: fresh mtime but no assistant entries yet
            // (e.g., agent just started — only user/tool-result lines).
            // Without an assistant entry we have no model and no tokens to display.
            if (entries.Count == 0)
                continue;

            var lastEntry = entries[^1];
            var totalTokens = ComputeContextTokens(lastEntry);
            var modelName = lastEntry.Message?.Model;
            var maxTokens = ModelContextLimits.GetMaxContextTokens(modelName, sonnetContextSize);
            var agentId = ExtractAgentId(file);

            result.Add(new SubagentContextData
            {
                AgentId = agentId,
                TotalTokens = totalTokens,
                MaxTokens = maxTokens,
                ModelName = modelName,
                LastActivity = lastActivity   // mtime, not lastEntry.Timestamp
            });
        }
        catch (IOException ex)
        {
            Debug.WriteLine($"[JsonlService] Failed to parse subagent file {file}: {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            Debug.WriteLine($"[JsonlService] Access denied for subagent file {file}: {ex.Message}");
        }
    }

    return result.OrderBy(a => a.AgentId, StringComparer.Ordinal).ToList();
}
```

### Test Skeleton (illustrative)

```csharp
// Source: New file. Pattern derived from JsonlServiceColdStartTests.cs.
// CCInfoWindows.Tests/Services/JsonlServiceSubagentTests.cs

using System.Text.Json;
using CCInfoWindows.Services;

namespace CCInfoWindows.Tests.Services;

/// <summary>
/// Regression tests for subagent activity-detection via filesystem mtime
/// (Phase 29 SUBAGENT-01..05). Replaces the previous assistant-entry timestamp
/// filter with File.GetLastWriteTimeUtc to match macOS contentModificationDate.
/// </summary>
public class JsonlServiceSubagentTests : IDisposable
{
    private const int CutoffSeconds = 30;
    private readonly string _tempDir;

    public JsonlServiceSubagentTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "subagent-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public async Task GetContextWindow_StaleAssistantEntry_FreshFileMtime_SubagentRemainsVisible()
    {
        // Arrange: assistant entry timestamped 5 min ago (well outside the 30s entry-based cutoff),
        // but file mtime set to "now" (simulates fresh tool-result write).
        var (projectDirName, agentFile) = ArrangeSubagentFixture(
            assistantTimestamp: DateTimeOffset.UtcNow.AddMinutes(-5),
            agentId: "alpha");

        // Force mtime to "now" AFTER all writes
        File.SetLastWriteTimeUtc(agentFile, DateTime.UtcNow);

        var svc = new JsonlService(_tempDir);
        await svc.InitializeAsync();

        // Act
        var context = svc.GetContextWindow(projectDirName);

        // Assert: subagent must appear despite stale assistant timestamp
        Assert.Contains(context.Subagents, s => s.AgentId == "alpha");
    }

    [Fact]
    public async Task GetContextWindow_StaleAssistantEntry_StaleFileMtime_SubagentIsFiltered()
    {
        var (projectDirName, agentFile) = ArrangeSubagentFixture(
            assistantTimestamp: DateTimeOffset.UtcNow.AddMinutes(-5),
            agentId: "bravo");

        // Force mtime to 5 minutes ago — both signals stale
        File.SetLastWriteTimeUtc(agentFile, DateTime.UtcNow.AddMinutes(-5));

        var svc = new JsonlService(_tempDir);
        await svc.InitializeAsync();

        var context = svc.GetContextWindow(projectDirName);

        Assert.DoesNotContain(context.Subagents, s => s.AgentId == "bravo");
    }

    [Fact]
    public async Task GetContextWindow_FreshMtime_LastActivityReflectsMtimeNotAssistantTimestamp()
    {
        var staleStamp = DateTimeOffset.UtcNow.AddMinutes(-5);
        var (projectDirName, agentFile) = ArrangeSubagentFixture(
            assistantTimestamp: staleStamp, agentId: "charlie");

        var freshMtime = DateTime.UtcNow;
        File.SetLastWriteTimeUtc(agentFile, freshMtime);

        var svc = new JsonlService(_tempDir);
        await svc.InitializeAsync();

        var subagent = svc.GetContextWindow(projectDirName).Subagents.Single();

        // LastActivity must come from mtime, NOT from the stale assistant entry timestamp.
        var deltaFromMtime = (subagent.LastActivity - new DateTimeOffset(freshMtime, TimeSpan.Zero)).Duration();
        var deltaFromAssistant = (subagent.LastActivity - staleStamp).Duration();

        Assert.True(deltaFromMtime < TimeSpan.FromSeconds(2),
            $"LastActivity should track mtime (delta={deltaFromMtime}), not assistant timestamp (delta={deltaFromAssistant}).");
    }

    // -------------------------------------------------------------------------
    // Fixture helpers
    // -------------------------------------------------------------------------

    private (string ProjectDirName, string AgentFile) ArrangeSubagentFixture(
        DateTimeOffset assistantTimestamp, string agentId)
    {
        const string ProjectDirName = "D--myProjects-ccInfoWin";
        var projectDir = Path.Combine(_tempDir, ProjectDirName);
        Directory.CreateDirectory(projectDir);

        // Newest session JSONL — required by FindSubagentFilesForNewestSession.
        var sessionUuid = Guid.NewGuid().ToString();
        var sessionFile = Path.Combine(projectDir, $"{sessionUuid}.jsonl");
        WriteAssistantJsonlLine(sessionFile, sessionUuid, isSidechain: false,
            timestamp: DateTimeOffset.UtcNow);

        // Subagent file under {sessionUuid}/subagents/agent-{id}.jsonl
        var subagentDir = Path.Combine(projectDir, sessionUuid, "subagents");
        Directory.CreateDirectory(subagentDir);
        var agentFile = Path.Combine(subagentDir, $"agent-{agentId}.jsonl");
        WriteAssistantJsonlLine(agentFile, sessionUuid, isSidechain: true,
            timestamp: assistantTimestamp);

        return (ProjectDirName, agentFile);
    }

    private static void WriteAssistantJsonlLine(string filePath, string sessionId,
        bool isSidechain, DateTimeOffset timestamp)
    {
        var uuid = $"msg_{Guid.NewGuid():N}";
        var requestId = $"req_{Guid.NewGuid():N}";
        var line = JsonSerializer.Serialize(new
        {
            uuid,
            requestId,
            uniqueHash = $"{uuid}|{requestId}",
            sessionId,
            timestamp = timestamp.ToString("O"),
            isSidechain,
            type = "assistant",
            message = new
            {
                model = "claude-sonnet-4-20250514",
                usage = new
                {
                    input_tokens = 10,
                    output_tokens = 5,
                    cache_read_input_tokens = 0,
                    cache_creation_input_tokens = 0
                }
            }
        });
        File.AppendAllText(filePath, line + "\n");
    }
}
```

### "Cutoff before parsing" — how to verify without instrumentation

The optimization is internal: skipping `ReadTailLines` on stale files. A
spy/counter would require either an `IFileSystem` abstraction (deferred) or
reflection. CONTEXT.md accepts code-review-only verification. Practical
verification options ordered by effort:

1. **Code review** (recommended): inspect the patch — does `File.GetLastWriteTimeUtc`
   appear BEFORE `ReadTailLines` in the `try` block?
2. **Indirect timing assertion** (optional): create 1000 stale subagent files
   with 10 MB content each in a test. Time the call. If parsing-first, it
   takes seconds; if mtime-first, it takes milliseconds. Threshold-based,
   somewhat brittle.
3. **Reflection-based call counter** (overkill): not recommended for a 30s
   hotfix.

**Recommendation:** Option 1. The patch is small enough that code review is
the right tool.

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| Last assistant entry timestamp as activity signal (`lastEntry.Timestamp ?? DateTimeOffset.MinValue`) | NTFS file mtime via `File.GetLastWriteTimeUtc` | Phase 29 (this fix, 2026-05-18) | Subagents stay visible during long tool-calls; matches macOS contentModificationDate semantic. |

**Deprecated / removed:**

- `lastEntry.Timestamp` as the source for `SubagentContextData.LastActivity` — replaced by mtime.

## Assumptions Log

| # | Claim | Section | Risk if Wrong |
|---|-------|---------|---------------|
| A1 | Claude CLI uses normal stream-based appends (not memory-mapped I/O) for subagent JSONL files, so every write flushes mtime. | Finding 1 | LOW — fix would still work for the common case; UAT would catch any mmap-related gap. |
| A2 | macOS `findActiveAgents` uses **only** mtime, no additional signal. | Finding 10 | MEDIUM — if macOS also requires ≥1 assistant entry, our Windows guard at Finding 5 already matches; if macOS uses file-size growth, we'd diverge. UAT against macOS reference is the authoritative check. |
| A3 | NTFS in normal-user temp-path has 100 ns mtime resolution (not 2 s). | Pitfall 6 | LOW — affects tests only, mitigated by generous time offsets (5 min, not 30 s). |
| A4 | Windows Defender / AV does not silently bump mtime during the test window. | Pitfall 5 | LOW — mitigated by post-set re-read assertion. |
| A5 | The `windows-mcp` UIA exposes the subagent ItemsControl with enumerable rows. | Finding 7 | MEDIUM — falls back to `screenshot_control` + manual count if UIA enumeration fails. |

**Action for planner:** A2 is the only assumption that could re-shape the
fix. If high-fidelity macOS parity is required, schedule a one-off fetch
of `stefanlange/ccInfo` v1.12.0 source as a planning prerequisite. If
"behaviorally equivalent" is sufficient, proceed.

## Open Questions

1. **What is the UIA AutomationProperties.Name of the subagent ItemsControl in MainView?**
   - What we know: `MainViewModel.SubagentContexts` is an `ObservableCollection<SubagentDisplayData>` (`MainViewModel.cs:252`); the binding lives in MainView.xaml.
   - What's unclear: the exact UIA element name to query via `windows-mcp ui_automation`.
   - Recommendation: Discover at UAT-execution time via `screenshot_control(annotate=true)`. Do not block planning on this.

2. **Does macOS upstream require ≥1 assistant entry, or does it surface
   subagents with only user/tool-result entries?**
   - What we know: Windows currently requires it (line 709 guard); CONTEXT.md does not state the macOS contract.
   - What's unclear: Whether macOS shows an "empty" subagent bar at lifecycle start.
   - Recommendation: Keep the Windows guard (Finding 5). If a UAT user reports a 1-2s "missing agent" gap at start of new agents, revisit. Not blocking for v1.5.

3. **Should the fix include a final-state cache flush?**
   - What we know: Subagent state lives only in-memory; `_projectData` is keyed by `projectDirName`, not by agentId.
   - What's unclear: None — verified by grep, no subagent persistence.
   - Recommendation: No cache flush needed.

## Validation Architecture

### Test Framework

| Property | Value |
|----------|-------|
| Framework | xUnit.net 2.x (already on `CCInfoWindows.Tests.csproj`) |
| Config file | `xunit.runner.json` (project default — no per-phase override) |
| Quick run command | `dotnet test CCInfoWindows.Tests/CCInfoWindows.Tests.csproj --filter "FullyQualifiedName~JsonlServiceSubagentTests"` |
| Full suite command | `dotnet test CCInfoWindows.Tests/CCInfoWindows.Tests.csproj` |

### Phase Requirements → Test Map

| Req ID | Behavior | Test Type | Automated Command | File Exists? |
|--------|----------|-----------|-------------------|-------------|
| SUBAGENT-01 | `BuildSubagentContext` uses mtime as cutoff source | unit | `dotnet test --filter "FullyQualifiedName~JsonlServiceSubagentTests.GetContextWindow_StaleAssistantEntry_FreshFileMtime_SubagentRemainsVisible"` | ❌ Wave 0 |
| SUBAGENT-01 (regression) | 30s cutoff still rejects stale files | unit | `dotnet test --filter "FullyQualifiedName~JsonlServiceSubagentTests.GetContextWindow_StaleAssistantEntry_StaleFileMtime_SubagentIsFiltered"` | ❌ Wave 0 |
| SUBAGENT-02 | `LastActivity` field stores mtime, not assistant timestamp | unit | `dotnet test --filter "FullyQualifiedName~JsonlServiceSubagentTests.GetContextWindow_FreshMtime_LastActivityReflectsMtimeNotAssistantTimestamp"` | ❌ Wave 0 |
| SUBAGENT-03 | Test class follows Phase-25 fixture pattern | code review | manual | n/a |
| SUBAGENT-04 | 4-parallel-agent scenario shows 4 visible subagents | visual UAT | `mcp__windows-mcp__*` tools (see Finding 7) | ❌ Manual-only |
| SUBAGENT-05 | No regression on entries-count guard, ordering, model-name resolution | unit (existing) | full suite | ✅ existing `JsonlServiceTests` |

### Sampling Rate

- **Per task commit:** `dotnet test --filter "FullyQualifiedName~JsonlServiceSubagentTests"` (~3 tests, <5s)
- **Per wave merge:** `dotnet test CCInfoWindows.Tests/CCInfoWindows.Tests.csproj` (full suite, ~30-60s)
- **Phase gate:** Full suite green + visual UAT screenshot archived before `/gsd-verify-work`

### Wave 0 Gaps

- [ ] `CCInfoWindows.Tests/Services/JsonlServiceSubagentTests.cs` — new test class, covers SUBAGENT-01..03 (skeleton in Finding 4)
- [ ] No framework install needed — xUnit already on project.

## Sources

### Primary (HIGH confidence)

- **Codebase grep / Read** — Authoritative for all Windows-specific facts:
  - `CCInfoWindows/CCInfoWindows/Services/JsonlService.cs:693-743` (BuildSubagentContext)
  - `CCInfoWindows/CCInfoWindows/Services/JsonlService.cs:30` (`SubagentActivityWindowSeconds` constant)
  - `CCInfoWindows/CCInfoWindows/Services/JsonlService.cs:159,182` (two call sites)
  - `CCInfoWindows/CCInfoWindows/Services/JsonlService.cs:666-691` (FindSubagentFilesForNewestSession)
  - `CCInfoWindows/CCInfoWindows/Models/ContextWindowData.cs:8-24` (`SubagentContextData`)
  - `CCInfoWindows.Tests/Services/JsonlServiceColdStartTests.cs:12-240` (fixture pattern)
  - `CCInfoWindows.Tests/Services/JsonlServiceTests.cs:73-82` (FileShare.ReadWrite precedent)
  - `CCInfoWindows/CCInfoWindows/ViewModels/MainViewModel.cs:22,252,1043-1057` (UI binding)

- **Microsoft Learn — .NET 9 docs:**
  - `learn.microsoft.com/dotnet/api/system.io.file.getlastwritetimeutc` — return value, exception list, 1601 fallback for non-existent files
  - `learn.microsoft.com/dotnet/api/system.io.file.setlastwritetimeutc` — test helper contract
  - `learn.microsoft.com/dotnet/api/system.datetimeoffset.-ctor` — `(DateTime, TimeSpan)` ctor semantics

- **Windows-Internals docs (HIGH for mtime semantics):**
  - `learn.microsoft.com/windows/win32/api/fileapi/nf-fileapi-getfileattributesexa` — metadata, not file open
  - `learn.microsoft.com/windows-server/administration/windows-commands/fsutil-behavior` — `disablelastaccess` does NOT throttle LastWrite

### Secondary (MEDIUM confidence)

- **CONTEXT.md** (Phase 29) — locked decisions, macOS parity contract by spec.
- **STATE.md Roadmap Evolution** — Phase 29 root cause documented.
- **`spec/v1.11.1-macOS/spec-release-1.10.0-to-1.11.1.md`** — searched, contains no subagent-detection details; the contract comes from CONTEXT.md / STATE.md commit log.

### Tertiary (LOW confidence)

- macOS `JSONLParser.swift:457-483` — referenced in CONTEXT.md but NOT present in this repo; treated as [ASSUMED] parity per Finding 10.

## Metadata

**Confidence breakdown:**

- Standard stack (`File.GetLastWriteTimeUtc`, `DateTimeOffset` ctor, xUnit fixture): **HIGH** — verified against MSFT docs and existing codebase.
- Architecture (single-function patch, no DI / no XAML / no migration): **HIGH** — verified by grep across `CCInfoWindows/`.
- Pitfalls (mtime / DST / AV / empty-file guard): **HIGH** for the .NET-mechanical risks, **LOW** for environmental risks (AV, FS resolution).
- macOS parity contract: **MEDIUM** — sourced by spec, not by Swift diff.
- Visual UAT methodology: **HIGH** for tool availability, **MEDIUM** for exact UIA target.

**Research date:** 2026-05-18
**Valid until:** 2026-06-17 (30 days — stable .NET 9 / NTFS surface, no fast-moving dependencies)
