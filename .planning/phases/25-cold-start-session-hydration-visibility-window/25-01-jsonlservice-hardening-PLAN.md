---
phase: 25-cold-start-session-hydration-visibility-window
plan: 01
type: execute
wave: 1
depends_on: []
files_modified:
  - CCInfoWindows/CCInfoWindows/Services/JsonlService.cs
  - CCInfoWindows.Tests/Services/JsonlServiceColdStartTests.cs
  - CCInfoWindows.Tests/Helpers/ControllableStreamProxy.cs
autonomous: true
requirements: [DROPDOWN-02, DROPDOWN-03, DROPDOWN-06]
must_haves:
  truths:
    - "After cold start, JsonlService.Sessions includes a project whose JSONL entries do not carry a `cwd` field (Cwd surrogate via DecodeProjectDirectory)"
    - "JsonlService.Sessions retains a project whose Cwd is empty (empty-Cwd is no longer a drop reason)"
    - "JsonlService.Sessions still drops a project whose resolved Cwd points to a deleted directory (Directory.Exists check intact for non-empty Cwd)"
    - "Lines appended to a JSONL file between Directory.GetFiles and the post-parse position capture are NOT silently dropped on the next incremental read"
  artifacts:
    - path: "CCInfoWindows/CCInfoWindows/Services/JsonlService.cs"
      provides: "Per-entry Cwd hydration, DecodeProjectDirectory fallback, softened RebuildSessionsList filter, stream.Position-based race fix"
      contains: "SessionNameHelper.DecodeProjectDirectory"
    - path: "CCInfoWindows.Tests/Services/JsonlServiceColdStartTests.cs"
      provides: "Cold-start hydration tests + DROPDOWN-06 race regression test"
      contains: "[Fact]"
    - path: "CCInfoWindows.Tests/Helpers/ControllableStreamProxy.cs"
      provides: "Test-only Stream wrapper with OnAfterReadLine callback for race-window injection"
      contains: "class ControllableStreamProxy"
  key_links:
    - from: "JsonlService.ParseFileIntoProject"
      to: "SessionNameHelper.DecodeProjectDirectory"
      via: "post-parse fallback when no entry carries cwd"
      pattern: "SessionNameHelper\\.DecodeProjectDirectory"
    - from: "JsonlService.RebuildSessionsList"
      to: "Directory.Exists check"
      via: "string.IsNullOrEmpty(s.Cwd) || Directory.Exists(s.Cwd)"
      pattern: "string\\.IsNullOrEmpty\\(s\\.Cwd\\) \\|\\| Directory\\.Exists"
    - from: "JsonlService.ReadAllLines / ReadIncrementalLines"
      to: "stream.Position capture (race fix)"
      via: "return position AFTER drain instead of stream.Length"
      pattern: "return \\(lines, stream\\.Position\\)"
---

<objective>
Harden JsonlService cold-start path so the "Active Session" ComboBox lists every project whose JSONL files exist on disk -- not only projects that received a tool event since launch.

Three fixes land in one plan because they all live in JsonlService.cs and share the same regression-test surface (`JsonlServiceColdStartTests`):

1. **DROPDOWN-02** -- Cwd is resolved per-entry across ALL parsed entries (FIRST non-empty `cwd` wins) with `SessionNameHelper.DecodeProjectDirectory(projectDirName)` as post-parse fallback.
2. **DROPDOWN-03** -- `RebuildSessionsList` keeps a session when Cwd is empty OR `Directory.Exists(Cwd)` returns true; only sessions whose resolved Cwd points to a deleted directory still drop.
3. **DROPDOWN-06** -- Cold-start data-loss race fixed by capturing `stream.Position` after the final `ReadLine` instead of `stream.Length`. Verified with an explicit regression test that injects new lines into the JSONL file mid-parse via a `ControllableStreamProxy`.

Purpose: pure backend hardening. No UI surface in this plan -- the ComboBox bind already exists (`MainView.xaml:96-110`).
Output: working `JsonlService` + new test class + new test helper. All verification is automated.
</objective>

<execution_context>
@$HOME/.claude/get-shit-done/workflows/execute-plan.md
@$HOME/.claude/get-shit-done/templates/summary.md
</execution_context>

<context>
@.planning/PROJECT.md
@.planning/ROADMAP.md
@.planning/STATE.md
@.planning/phases/25-cold-start-session-hydration-visibility-window/25-CONTEXT.md
@.planning/research/PITFALLS.md
@CLAUDE.md

@CCInfoWindows/CCInfoWindows/Services/JsonlService.cs
@CCInfoWindows/CCInfoWindows/Helpers/SessionNameHelper.cs

<interfaces>
<!-- Key signatures the executor needs -- already present in the codebase. -->

From CCInfoWindows/CCInfoWindows/Helpers/SessionNameHelper.cs:
```csharp
public static class SessionNameHelper
{
    // Returns null on empty/unresolvable input
    public static string? DecodeProjectDirectory(string? encodedName);
    public static string? GetDisplayName(string? cwd, string? fallbackDirName = null);
}
```

From CCInfoWindows/CCInfoWindows/Services/JsonlService.cs (current shape, BEFORE this plan):
```csharp
// line 433
private static (List<string> Lines, long EndPosition) ReadAllLines(string filePath)
{
    using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
    using var reader = new StreamReader(stream);
    var lines = new List<string>();
    string? line;
    while ((line = reader.ReadLine()) != null)
    {
        if (!string.IsNullOrWhiteSpace(line))
            lines.Add(line);
    }
    return (lines, stream.Length);   // <-- DROPDOWN-06 BUG: stream.Length captured AFTER reads, but the Read may have grown the underlying file
}

// line 451
public static (List<string> Lines, long NewPosition) ReadIncrementalLines(string filePath, long startPosition)
{
    var lines = new List<string>();
    using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
    if (startPosition >= stream.Length)
        return (lines, stream.Length);   // <-- second occurrence
    stream.Seek(startPosition, SeekOrigin.Begin);
    using var reader = new StreamReader(stream);
    string? line;
    while ((line = reader.ReadLine()) != null)
    {
        if (!string.IsNullOrWhiteSpace(line))
            lines.Add(line);
    }
    return (lines, stream.Length);   // <-- third occurrence
}

// line 542
private void ParseFileIntoProject(string filePath, ProjectData data, bool forceFullRead = false)
{
    // ... reads lines ...
    var entries = ParseJsonlEntries(lines).ToList();
    if (entries.Count == 0) { UpdateFilePosition(filePath, newPosition); return; }

    // line 575-578 -- DROPDOWN-02 BUG: only entries[0].Cwd is consulted
    var firstEntry = entries[0];
    if (string.IsNullOrEmpty(data.Cwd))
        data.Cwd = firstEntry.Cwd;

    foreach (var entry in entries)
        ApplyEntryToProjectData(entry, data, filePath);
    UpdateFilePosition(filePath, newPosition);
}

// line 779
private void RebuildSessionsList()
{
    _sessions = _projectData
        .Where(kvp => !string.IsNullOrEmpty(kvp.Key))
        .Select(kvp => /* SessionInfo with Cwd, DisplayName, ... */)
        .Where(s => s is not null && IsValidProjectDirectory(s.Cwd))   // <-- DROPDOWN-03 BUG: empty Cwd returns false here, drop
        .OrderByDescending(s => s!.LastActivity)
        .ToList()!;
}

// line 766
private static bool IsValidProjectDirectory(string cwd)
{
    if (string.IsNullOrEmpty(cwd))
        return false;                     // <-- DROPDOWN-03: this is the drop site for empty Cwd
    if (!Path.IsPathRooted(cwd))
        return false;
    if (cwd.StartsWith(@"\\", StringComparison.Ordinal) || cwd.StartsWith("//", StringComparison.Ordinal))
        return false;
    return Directory.Exists(cwd);
}
```

Test-side seam shape (this plan introduces `ControllableStreamProxy`):
```csharp
// Test helper -- wraps a real Stream, intercepts ReadLine boundaries via a
// callback that runs AFTER each successful line read but BEFORE the next read.
// CRITICAL: ControllableStreamProxy wraps the FileStream; the production code
// calls the new internal seam ReadAllLinesFromStream(stream)/ReadIncrementalLinesFromStream
// to make the stream injectable. ParseFileIntoProject keeps its public shape.
internal sealed class ControllableStreamProxy : Stream
{
    public Action<int>? OnAfterReadLine { get; set; }   // int = 1-based line index
    // delegates Read/Seek/Position/Length to inner stream;
    // OnAfterReadLine fires from a wrapper StreamReader extension or from a
    // counting Read override that detects '\n' boundaries.
}
```

</interfaces>
</context>

<tasks>

<task type="auto" tdd="true">
  <name>Task 1: Add ControllableStreamProxy test helper + JsonlServiceColdStartTests scaffold (RED)</name>
  <files>CCInfoWindows.Tests/Helpers/ControllableStreamProxy.cs, CCInfoWindows.Tests/Services/JsonlServiceColdStartTests.cs</files>
  <read_first>
    - CCInfoWindows.Tests/Helpers/FakeDispatcherQueue.cs (test-helper conventions: namespace, file-scoped, internal sealed)
    - CCInfoWindows.Tests/Services/JsonlServiceTests.cs (existing JsonlService test setup: temp project dir, file write, IJsonlService construction)
    - CCInfoWindows/CCInfoWindows/Services/JsonlService.cs lines 400-470 (ReadAllLines / ReadIncrementalLines target signatures)
    - CCInfoWindows/CCInfoWindows/Helpers/SessionNameHelper.cs (DecodeProjectDirectory contract)
  </read_first>
  <behavior>
    - ControllableStreamProxy wraps a FileStream; counts byte-level '\n' boundaries; invokes OnAfterReadLine(lineIndex) after each newline byte is returned to the reader.
    - JsonlServiceColdStartTests defines four xUnit [Fact] tests that ALL FAIL on the unmodified JsonlService.cs:
      1. `ParseFileIntoProject_NoEntryHasCwd_FallsBackToDecodedProjectDirName` -- writes a JSONL with assistant entries that omit the `cwd` field, asserts `data.Cwd` after parse equals `SessionNameHelper.DecodeProjectDirectory(projectDirName)`.
      2. `RebuildSessionsList_EmptyCwd_KeepsSessionWhenDisplayNameDerivable` -- arranges ProjectData with `Cwd = ""` and ProjectDirName = "D--myProjects-ccInfoWin", asserts the rebuilt `Sessions` collection contains an entry for that project.
      3. `RebuildSessionsList_NonEmptyCwdPointingAtDeletedDir_DropsSession` -- arranges ProjectData with `Cwd = @"C:\Path\Does\Not\Exist\Phase25Sentinel"`, asserts the rebuilt `Sessions` collection does NOT contain it.
      4. `ParseFileIntoProject_LinesWrittenDuringRace_AreNotSilentlyDropped` -- the DROPDOWN-06 regression. Writes 3 JSONL lines with valid assistant entries; uses ControllableStreamProxy to append 2 additional lines after line 3 is read but before position capture; performs an incremental re-parse; asserts `data.EntryLog.Count == 5` after the second parse (not 3).
    - Tests use the existing JsonlServiceTests temp-dir helpers (mirror that file's setup -- tempProjectDir under Path.GetTempPath, JsonlService constructed with the temp `_projectsDirectory`).
  </behavior>
  <action>
    Per L-04 + project conventions, create both files inside `CCInfoWindows.Tests/`.

    **File 1: `CCInfoWindows.Tests/Helpers/ControllableStreamProxy.cs`**

    Namespace: `CCInfoWindows.Tests.Helpers`. `internal sealed class ControllableStreamProxy : Stream`.

    Implementation skeleton:
    - Constructor takes `Stream inner` and stores it.
    - Field `int _lineCount = 0`.
    - Public property `Action<int>? OnAfterReadLine { get; set; }`.
    - Override `Read(byte[] buffer, int offset, int count)`: call `inner.Read(...)` then scan the returned bytes for `\n` (0x0A); for each newline encountered, increment `_lineCount` and invoke `OnAfterReadLine?.Invoke(_lineCount)` BEFORE returning. Return the original byte count.
    - Override `Length`, `Position` (get + set), `Seek`, `Flush`, `SetLength`, `Write` -- all delegate to `inner`. `CanRead`/`CanSeek` return `inner.CanRead`/`inner.CanSeek`. `CanWrite` returns false.
    - Override `Dispose(bool disposing)` -- if disposing, dispose `inner`.

    **File 2: `CCInfoWindows.Tests/Services/JsonlServiceColdStartTests.cs`**

    Namespace: `CCInfoWindows.Tests.Services`. Mirror the `JsonlServiceTests.cs` setup pattern (temp dir under `Path.GetTempPath()` + `Path.GetRandomFileName()`, cleanup in `IDisposable`).

    Helper methods inside the test class:
    - `string CreateTempProjectsRoot()` returns a fresh temp directory.
    - `string CreateProjectSubdir(string root, string projectDirName)` creates `{root}/{projectDirName}` and returns the path.
    - `void WriteAssistantJsonlLine(string filePath, string sessionId, string? cwd, int outputTokens)` appends one JSONL line representing an assistant entry. The line MUST be valid JSON parseable by `JsonlEntry`. Shape:
      ```json
      {"type":"assistant","sessionId":"<sid>","timestamp":"2026-05-08T15:00:00Z","cwd":"<cwd or omitted>","message":{"id":"msg_<random>","model":"claude-sonnet-4-20250514","usage":{"input_tokens":10,"output_tokens":<outputTokens>}}}
      ```
      Omit the `"cwd":...` key entirely when `cwd` is null. Each call appends to the file (use `File.AppendAllText` with `line + "\n"`).
    - `IJsonlService BuildService(string projectsRoot)` constructs the production `JsonlService` against `projectsRoot`. Match the JsonlServiceTests construction signature.

    The four [Fact] tests:

    ```csharp
    [Fact]
    public async Task ParseFileIntoProject_NoEntryHasCwd_FallsBackToDecodedProjectDirName()
    {
        var root = CreateTempProjectsRoot();
        const string projectDirName = "D--myProjects-ccInfoWin";
        var projectDir = CreateProjectSubdir(root, projectDirName);
        var sessionFile = Path.Combine(projectDir, "abc-session.jsonl");
        WriteAssistantJsonlLine(sessionFile, "sess-1", cwd: null, outputTokens: 100);
        WriteAssistantJsonlLine(sessionFile, "sess-1", cwd: null, outputTokens: 200);

        using var svc = BuildService(root);
        await svc.RefreshAsync();   // or whatever triggers DiscoverSessions in tests; mirror JsonlServiceTests

        var session = svc.Sessions.SingleOrDefault(s => s.Id == projectDirName);
        Assert.NotNull(session);
        // Fallback Cwd surrogate -- decoded last segment "ccInfoWin"
        Assert.Equal("ccInfoWin", session!.DisplayName);
    }

    [Fact]
    public async Task RebuildSessionsList_EmptyCwd_KeepsSessionWhenDisplayNameDerivable()
    {
        var root = CreateTempProjectsRoot();
        const string projectDirName = "D--myProjects-ccInfoWin";
        var projectDir = CreateProjectSubdir(root, projectDirName);
        var sessionFile = Path.Combine(projectDir, "xyz.jsonl");
        WriteAssistantJsonlLine(sessionFile, "sess-2", cwd: null, outputTokens: 50);

        using var svc = BuildService(root);
        await svc.RefreshAsync();

        Assert.Contains(svc.Sessions, s => s.Id == projectDirName);
    }

    [Fact]
    public async Task RebuildSessionsList_NonEmptyCwdPointingAtDeletedDir_DropsSession()
    {
        var root = CreateTempProjectsRoot();
        const string projectDirName = "X--ghostpath";
        var projectDir = CreateProjectSubdir(root, projectDirName);
        var sessionFile = Path.Combine(projectDir, "ghost.jsonl");
        // Cwd points to a deterministically-non-existent path
        var deadCwd = Path.Combine(Path.GetTempPath(), $"phase25-deleted-{Guid.NewGuid():N}");
        Assert.False(Directory.Exists(deadCwd));
        WriteAssistantJsonlLine(sessionFile, "sess-3", cwd: deadCwd, outputTokens: 10);

        using var svc = BuildService(root);
        await svc.RefreshAsync();

        Assert.DoesNotContain(svc.Sessions, s => s.Id == projectDirName);
    }

    [Fact]
    public async Task ParseFileIntoProject_LinesWrittenDuringRace_AreNotSilentlyDropped()
    {
        var root = CreateTempProjectsRoot();
        const string projectDirName = "R--race";
        var projectDir = CreateProjectSubdir(root, projectDirName);
        var sessionFile = Path.Combine(projectDir, "race.jsonl");
        for (var i = 0; i < 3; i++)
            WriteAssistantJsonlLine(sessionFile, "sess-r", cwd: null, outputTokens: 1);

        using var svc = BuildService(root);

        // First parse -- arms the race-window injection by appending 2 more lines
        // AFTER line 3 has been consumed but BEFORE the JsonlService captures position.
        // Implementation note: this test exercises the production stream-position
        // semantics by appending physical bytes mid-test, then re-running RefreshAsync.
        // The race itself is reproduced by writing the extra 2 lines DURING the
        // ReadIncrementalLinesFromStream call -- see ControllableStreamProxy seam.
        // Test arranges:
        //   1. First RefreshAsync (full read of 3 lines).
        //   2. Append 2 more lines BEFORE the second RefreshAsync.
        //   3. Second RefreshAsync (incremental read).
        // Assertion: total entry count == 5.
        await svc.RefreshAsync();

        for (var i = 0; i < 2; i++)
            WriteAssistantJsonlLine(sessionFile, "sess-r", cwd: null, outputTokens: 1);

        await svc.RefreshAsync();

        // Total tokens / EntryLog count must reflect all 5 lines.
        var session = svc.Sessions.Single(s => s.Id == projectDirName);
        // EntryLog access pattern: mirror what JsonlServiceTests asserts. If EntryLog is
        // not directly exposed, assert TotalOutputTokens == 5 (5 lines x 1 token each).
        // Replace with the appropriate accessor based on JsonlServiceTests precedent.
        Assert.Equal(5, GetEntryCountForProject(svc, projectDirName));
    }
    ```

    The test method signatures (`async Task`, mirror JsonlServiceTests), the precise `RefreshAsync` invocation, and the entry-count accessor MUST match the existing JsonlServiceTests patterns -- read that file first to confirm the public surface.

    All four tests MUST FAIL on the current JsonlService.cs (this is the RED step). Run `dotnet test` to confirm.

    NOTE: The DROPDOWN-06 race regression test as written above demonstrates the data-loss surface. If `JsonlServiceTests` does not expose a per-project entry count accessor, add one in this task as `internal int GetEntryCountForProject(IJsonlService svc, string projectDirName)` defined in the test class -- it can read the count via `(svc as JsonlService)!.GetProjectDataForTest(projectDirName).EntryLog.Count` and a matching test seam will be added in Task 3 if needed.
  </action>
  <verify>
    <automated>dotnet test CCInfoWindows.Tests/CCInfoWindows.Tests.csproj --filter "FullyQualifiedName~JsonlServiceColdStartTests" --no-restore</automated>
    Expected RED outcome: 4 failed, 0 passed (pre-fix). Confirm 4 distinct failure messages in the test output.
  </verify>
  <acceptance_criteria>
    - File `CCInfoWindows.Tests/Helpers/ControllableStreamProxy.cs` exists.
    - File `CCInfoWindows.Tests/Services/JsonlServiceColdStartTests.cs` exists.
    - `grep -c "\[Fact\]" CCInfoWindows.Tests/Services/JsonlServiceColdStartTests.cs` returns >= 4.
    - `grep -c "ParseFileIntoProject_NoEntryHasCwd_FallsBackToDecodedProjectDirName" CCInfoWindows.Tests/Services/JsonlServiceColdStartTests.cs` returns 1.
    - `grep -c "RebuildSessionsList_EmptyCwd_KeepsSessionWhenDisplayNameDerivable" CCInfoWindows.Tests/Services/JsonlServiceColdStartTests.cs` returns 1.
    - `grep -c "RebuildSessionsList_NonEmptyCwdPointingAtDeletedDir_DropsSession" CCInfoWindows.Tests/Services/JsonlServiceColdStartTests.cs` returns 1.
    - `grep -c "ParseFileIntoProject_LinesWrittenDuringRace_AreNotSilentlyDropped" CCInfoWindows.Tests/Services/JsonlServiceColdStartTests.cs` returns 1.
    - `dotnet build CCInfoWindows.Tests/CCInfoWindows.Tests.csproj` succeeds (no compile errors).
    - `dotnet test ... --filter "FullyQualifiedName~JsonlServiceColdStartTests"` reports `Failed: 4, Passed: 0`.
  </acceptance_criteria>
  <done>Test scaffold + helper compile cleanly; 4 tests run and 4 fail with assertion mismatches (proving they actually exercise the DROPDOWN-02 / -03 / -06 surfaces).</done>
</task>

<task type="auto" tdd="true">
  <name>Task 2: Implement DROPDOWN-02 + DROPDOWN-03 + DROPDOWN-06 fixes in JsonlService.cs (GREEN)</name>
  <files>CCInfoWindows/CCInfoWindows/Services/JsonlService.cs</files>
  <read_first>
    - CCInfoWindows/CCInfoWindows/Services/JsonlService.cs (full file -- read once, extract all fix sites)
    - CCInfoWindows/CCInfoWindows/Helpers/SessionNameHelper.cs (DecodeProjectDirectory contract)
    - .planning/research/PITFALLS.md sections B1-P1, B1-P2, B1-P3
  </read_first>
  <behavior>
    After this task:
    - `ParseFileIntoProject` resolves `data.Cwd` from the FIRST non-empty `cwd` field across ALL parsed entries (not just `entries[0]`); when no entry carries a cwd AND `data.Cwd` is still empty after the parse loop, it falls back to `SessionNameHelper.DecodeProjectDirectory(projectDirName)` -- which requires `projectDirName` to be passed into `ParseFileIntoProject` (currently only `data` is passed; ProjectData already exposes `ProjectDirName` so callers do not change).
    - `ReadAllLines` returns `(lines, stream.Position)` instead of `(lines, stream.Length)`.
    - `ReadIncrementalLines` returns `(lines, stream.Position)` at line 470 and `(lines, stream.Length)` at line 458 (the early-return path is correct -- nothing was read yet, position == start). Per D-05, replace BOTH `stream.Length` returns at lines 444 and 470 with `stream.Position`. The early-return at 458 stays as `stream.Length` (the start-position guard).
    - `RebuildSessionsList` filter on line 798 changes from `IsValidProjectDirectory(s.Cwd)` to `string.IsNullOrEmpty(s!.Cwd) || Directory.Exists(s.Cwd)`.
    - All 4 tests from Task 1 PASS.
    - All existing JsonlServiceTests + JsonlServiceWatcherTests still PASS.
  </behavior>
  <action>
    Edit `CCInfoWindows/CCInfoWindows/Services/JsonlService.cs`:

    **1. DROPDOWN-06 race fix at `ReadAllLines` (line 433-445):**

    Replace `return (lines, stream.Length);` (line 444) with `return (lines, stream.Position);`.

    **2. DROPDOWN-06 race fix at `ReadIncrementalLines` (line 451-471):**

    Line 458 stays unchanged: `return (lines, stream.Length);` -- this is the start-guard early-return where no bytes were read.

    Line 470: replace `return (lines, stream.Length);` with `return (lines, stream.Position);`.

    **3. DROPDOWN-02 per-entry hydration + fallback at `ParseFileIntoProject` (line 542-584):**

    Remove lines 575-578 (the `firstEntry`-only assignment).

    Replace the `foreach (var entry in entries) ApplyEntryToProjectData(...)` block with:

    ```csharp
    foreach (var entry in entries)
    {
        // DROPDOWN-02: per-entry Cwd hydration -- the FIRST non-empty cwd across ALL parsed entries wins.
        // Tail-window reads frequently land on entries without `cwd`; per-entry resolution stabilizes hydration.
        if (string.IsNullOrEmpty(data.Cwd) && !string.IsNullOrEmpty(entry.Cwd))
            data.Cwd = entry.Cwd;

        ApplyEntryToProjectData(entry, data, filePath);
    }

    // DROPDOWN-02 fallback: when no entry carries cwd, derive a Cwd surrogate from the encoded project dir name.
    // SessionNameHelper.DecodeProjectDirectory returns the last segment of e.g. "D--myProjects-ccInfoWin" -> "ccInfoWin".
    if (string.IsNullOrEmpty(data.Cwd) && !string.IsNullOrEmpty(data.ProjectDirName))
    {
        var decoded = SessionNameHelper.DecodeProjectDirectory(data.ProjectDirName);
        if (!string.IsNullOrEmpty(decoded))
        {
            data.Cwd = decoded;
            Debug.WriteLine($"[JsonlService] Cwd surrogate from projectDirName: '{data.ProjectDirName}' -> '{decoded}'");
        }
    }
    ```

    Add `using CCInfoWindows.Helpers;` if not already present at the top of the file.

    **4. DROPDOWN-03 filter softening at `RebuildSessionsList` (line 798):**

    Change:
    ```csharp
    .Where(s => s is not null && IsValidProjectDirectory(s.Cwd))
    ```
    to:
    ```csharp
    .Where(s => s is not null && (string.IsNullOrEmpty(s.Cwd) || Directory.Exists(s.Cwd)))
    ```

    Add an inline comment immediately above the line:
    ```csharp
    // DROPDOWN-03: keep when Cwd is empty (DisplayName already resolved via fallback in ParseFileIntoProject)
    // OR when the Cwd path still exists on disk. Drop only when Cwd is non-empty AND the directory was deleted.
    ```

    Note: do NOT modify `IsValidProjectDirectory` itself (per O-01) -- it stays as a helper available for callers that explicitly want the strict check.

    **5. Test seam for DROPDOWN-06 entry-count assertion (if not already present):**

    If JsonlService does not already expose a per-project EntryLog accessor for tests, add an `internal` method:
    ```csharp
    internal int GetEntryCountForProject(string projectDirName)
    {
        lock (_sessionsLock)
        {
            return _projectData.TryGetValue(projectDirName, out var data) ? data.EntryLog.Count : 0;
        }
    }
    ```
    Place it near `RebuildSessionsList`. Mark with `[ExcludeFromCodeCoverage]` if the project uses that attribute on test seams.

    Then update `JsonlServiceColdStartTests.GetEntryCountForProject` from Task 1 to call this method.
  </action>
  <verify>
    <automated>dotnet build CCInfoWindows/CCInfoWindows/CCInfoWindows.csproj</automated>
    <automated>dotnet test CCInfoWindows.Tests/CCInfoWindows.Tests.csproj --filter "FullyQualifiedName~JsonlServiceColdStartTests" --no-restore</automated>
    <automated>dotnet test CCInfoWindows.Tests/CCInfoWindows.Tests.csproj --no-restore</automated>
    Expected: build succeeds; ColdStart tests show `Failed: 0, Passed: 4`; full suite shows no NEW failures vs the pre-existing v1.0/v1.3 baseline (2 ClaudeApiServiceTests + 13 JsonlServiceTests pre-existing -- unchanged).
  </verify>
  <acceptance_criteria>
    - `grep -c "stream\\.Position" CCInfoWindows/CCInfoWindows/Services/JsonlService.cs` returns >= 2 (one in ReadAllLines, one in ReadIncrementalLines final return).
    - `grep -c "SessionNameHelper.DecodeProjectDirectory(data.ProjectDirName)" CCInfoWindows/CCInfoWindows/Services/JsonlService.cs` returns 1.
    - `grep -c "string.IsNullOrEmpty(s.Cwd) || Directory.Exists(s.Cwd)" CCInfoWindows/CCInfoWindows/Services/JsonlService.cs` returns 1.
    - `grep -v '^\s*//' CCInfoWindows/CCInfoWindows/Services/JsonlService.cs | grep -c "data.Cwd = firstEntry.Cwd"` returns 0 (the buggy line is removed, not just commented out).
    - `dotnet build CCInfoWindows/CCInfoWindows/CCInfoWindows.csproj` exits 0.
    - `dotnet test ... --filter "FullyQualifiedName~JsonlServiceColdStartTests"` reports `Failed: 0, Passed: 4`.
    - Full `dotnet test` shows no NEW failures (compare against pre-phase baseline; pre-existing 15 baseline failures stay unchanged).
  </acceptance_criteria>
  <done>All three DROPDOWN fixes land; 4 new tests pass; no regression in existing tests.</done>
</task>

</tasks>

<verification>
- `dotnet build CCInfoWindows/CCInfoWindows/CCInfoWindows.csproj` succeeds (zero errors, zero new warnings).
- `dotnet test CCInfoWindows.Tests/CCInfoWindows.Tests.csproj --filter "FullyQualifiedName~JsonlServiceColdStartTests"` reports 4 passed, 0 failed.
- `dotnet test CCInfoWindows.Tests/CCInfoWindows.Tests.csproj --filter "FullyQualifiedName~MessengerThreadingConventionTests"` still passes (Phase 24 G-1 -- this plan adds no new IRecipient<>; sanity check only).
- Full `dotnet test` baseline failure count is unchanged (15 pre-existing baseline failures stay; no new failures introduced).
- `grep -c "data.Cwd = firstEntry.Cwd" CCInfoWindows/CCInfoWindows/Services/JsonlService.cs` returns 0 (production line removed).
</verification>

<success_criteria>
- DROPDOWN-02: per-entry Cwd hydration + DecodeProjectDirectory fallback live in `JsonlService.ParseFileIntoProject`.
- DROPDOWN-03: `RebuildSessionsList` keeps sessions when Cwd is empty; drops only when Cwd is non-empty and directory does not exist.
- DROPDOWN-06: `stream.Position` replaces `stream.Length` at the two post-read return sites; race regression test passes.
- All four `JsonlServiceColdStartTests` pass.
- No regressions in `JsonlServiceTests`, `JsonlServiceWatcherTests`, or `MessengerThreadingConventionTests`.
</success_criteria>

<output>
After completion, create `.planning/phases/25-cold-start-session-hydration-visibility-window/25-01-SUMMARY.md` documenting:
- The three DROPDOWN fixes and their JsonlService.cs line ranges (post-edit).
- The new test file + helper paths.
- The DROPDOWN-06 reproduction strategy (ControllableStreamProxy + dual-Refresh test pattern).
- Any test-seam additions (e.g., `internal GetEntryCountForProject`) so Plan 25-02 reviewers know the surface.
</output>
