---
phase: 29-fix-subagent-activity-detection-switch-from-assistant-timest
plan: 01
type: execute
wave: 1
depends_on: []
files_modified:
  - CCInfoWindows/CCInfoWindows/Services/JsonlService.cs
  - CCInfoWindows.Tests/Services/JsonlServiceSubagentTests.cs
autonomous: false
requirements: [SUBAGENT-01, SUBAGENT-02, SUBAGENT-03, SUBAGENT-04, SUBAGENT-05]
must_haves:
  truths:
    - "BuildSubagentContext uses File.GetLastWriteTimeUtc(file) as the activity-cutoff comparison source (assistant-entry timestamp no longer drives the filter)"
    - "The mtime cutoff is applied BEFORE ReadTailLines — stale subagent files are never opened for parsing"
    - "SubagentContextData.LastActivity equals the mtime-derived DateTimeOffset (not lastEntry.Timestamp)"
    - "A subagent with stale assistant entry (5 min old) but fresh file mtime (now) remains visible in the result list"
    - "A subagent with stale assistant entry AND stale mtime (both 5 min old) is filtered out"
    - "The entries.Count == 0 guard remains intact — a fresh-mtime file with no assistant entries does NOT surface as an empty UI row"
    - "Visual UAT confirms 4 of 4 parallel subagents appear in the context-window panel (vs. pre-fix 2 of 4)"
  artifacts:
    - path: "CCInfoWindows/CCInfoWindows/Services/JsonlService.cs"
      provides: "mtime-based subagent activity-detection filter in BuildSubagentContext (lines 693-743)"
      contains: "File.GetLastWriteTimeUtc"
    - path: "CCInfoWindows.Tests/Services/JsonlServiceSubagentTests.cs"
      provides: "xUnit regression tests for SUBAGENT-01/-02/-05 (stale-entry-fresh-mtime visible, all-stale filtered, LastActivity tracks mtime)"
      contains: "[Fact]"
  key_links:
    - from: "JsonlService.BuildSubagentContext"
      to: "File.GetLastWriteTimeUtc"
      via: "metadata-only mtime probe before ReadTailLines"
      pattern: "File\\.GetLastWriteTimeUtc\\(file\\)"
    - from: "JsonlService.BuildSubagentContext"
      to: "SubagentContextData.LastActivity"
      via: "new DateTimeOffset(mtimeUtc, TimeSpan.Zero) assigned to LastActivity init-only field"
      pattern: "LastActivity = lastActivity"
    - from: "JsonlServiceSubagentTests"
      to: "File.SetLastWriteTimeUtc"
      via: "temp-file mtime forcing for deterministic fixture"
      pattern: "File\\.SetLastWriteTimeUtc"
---

<objective>
Phase 29 single-plan fix: replace the `lastEntry.Timestamp ?? DateTimeOffset.MinValue` cutoff source in `JsonlService.BuildSubagentContext` (`JsonlService.cs:693-743`) with `File.GetLastWriteTimeUtc(file)` to match macOS `findActiveAgents` / `FileManager.contentModificationDate` semantics. Every tool-result write on NTFS bumps mtime, so long-running tool calls now keep the subagent visible inside the 30s window even when the last assistant entry is older than the cutoff.

Three tasks land sequentially:

1. **Task 1 (RED)** — Author `JsonlServiceSubagentTests.cs` with 3 [Fact] tests; all 3 MUST FAIL on the unmodified `BuildSubagentContext`.
2. **Task 2 (GREEN)** — Patch `BuildSubagentContext` per CONTEXT.md decisions; verify all 3 new tests PASS, full suite shows no NEW failures vs. documented baseline.
3. **Task 3 (UAT checkpoint)** — Stage a 4-subagent fixture, launch Release build, verify visual UAT shows 4 of 4 subagents in the Kontextfenster panel via `mcp__windows-mcp__*` tools.

Purpose: pure backend bugfix — single function, ~10 LOC production delta + one new test file. No UI surface, no DI changes, no XAML, no localization, no data migration.
Output: working `JsonlService` mtime filter, new xUnit test class, archived UAT screenshot evidence.
</objective>

<execution_context>
@$HOME/.claude/get-shit-done/workflows/execute-plan.md
@$HOME/.claude/get-shit-done/templates/summary.md
</execution_context>

<context>
@.planning/PROJECT.md
@.planning/ROADMAP.md
@.planning/STATE.md
@.planning/phases/29-fix-subagent-activity-detection-switch-from-assistant-timest/29-CONTEXT.md
@.planning/phases/29-fix-subagent-activity-detection-switch-from-assistant-timest/29-RESEARCH.md
@CLAUDE.md

@CCInfoWindows/CCInfoWindows/Services/JsonlService.cs
@CCInfoWindows/CCInfoWindows/Models/ContextWindowData.cs

<interfaces>
<!-- Key signatures the executor needs. All already present in the codebase. -->

From CCInfoWindows/CCInfoWindows/Models/ContextWindowData.cs:
```csharp
public record SubagentContextData
{
    public required string AgentId { get; init; }
    public long TotalTokens { get; init; }
    public long MaxTokens { get; init; }
    public string? ModelName { get; init; }
    public DateTimeOffset LastActivity { get; init; }   // SEMANTIC SHIFT: now mtime-derived, shape unchanged
    public double Utilization { get; }   // computed
}
```

From CCInfoWindows/CCInfoWindows/Services/JsonlService.cs (current shape, BEFORE this plan):
```csharp
// line 30
private const int SubagentActivityWindowSeconds = 30;   // stays — macOS parity, no rename

// line 693 — single function to modify; called from line 159 and 182
private static IReadOnlyList<SubagentContextData> BuildSubagentContext(
    List<string> subagentFiles, long sonnetContextSize)
{
    var result = new List<SubagentContextData>();
    var cutoff = DateTimeOffset.UtcNow.AddSeconds(-SubagentActivityWindowSeconds);

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
            var lastActivity = lastEntry.Timestamp ?? DateTimeOffset.MinValue;   // <-- SUBAGENT-01/-02 BUG

            if (lastActivity < cutoff)
                continue;

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
                LastActivity = lastActivity   // <-- SUBAGENT-02 BUG: leaks assistant timestamp
            });
        }
        catch (IOException ex)        { Debug.WriteLine(...); }   // covers GetLastWriteTimeUtc IOException too
        catch (UnauthorizedAccessException ex) { Debug.WriteLine(...); }
    }

    return result.OrderBy(a => a.AgentId, StringComparer.Ordinal).ToList();
}
```

Helpers already present (do NOT modify):
```csharp
private static string ExtractAgentId(string filePath);                       // line 745
private static List<string> ReadTailLines(string file);                      // existing
private static IEnumerable<JsonlEntry> ParseJsonlEntries(IEnumerable<string> lines);
private static long ComputeContextTokens(JsonlEntry entry);
internal static class ModelContextLimits { public static long GetMaxContextTokens(string? model, long sonnetCtxSize); }
```

Fixture-pattern precedent: `CCInfoWindows.Tests/Services/JsonlServiceColdStartTests.cs` (Phase 25). Use the same IDisposable temp-dir shape, the same `WriteAssistantJsonlLine` JSON shape, the same `Method_Scenario_Expectation` naming. No `Should_` prefix. No FluentAssertions.

`JsonlService` construction signature: mirror `JsonlServiceColdStartTests.BuildService(root)` (constructs `JsonlService(projectsRoot)` then awaits `RefreshAsync()` to trigger initial scan; subagent files live under `{projectsRoot}/{projectDirName}/{sessionUuid}/subagents/agent-{id}.jsonl`).

The public surface used by tests is `IJsonlService.GetContextWindow(string projectDirName) -> ContextWindowData` (returns `.Subagents : IReadOnlyList<SubagentContextData>`). Confirm exact accessor name by reading `IJsonlService.cs` and the existing call sites at `JsonlService.cs:159, 182` during Task 1.
</interfaces>
</context>

<tasks>

<task type="auto" tdd="true">
  <name>Task 1: Add JsonlServiceSubagentTests.cs with 3 RED tests</name>
  <files>CCInfoWindows.Tests/Services/JsonlServiceSubagentTests.cs</files>
  <read_first>
    - CCInfoWindows.Tests/Services/JsonlServiceColdStartTests.cs (fixture pattern: IDisposable temp-dir, WriteAssistantJsonlLine shape, BuildService helper, RefreshAsync invocation)
    - CCInfoWindows/CCInfoWindows/Services/JsonlService.cs lines 30, 159, 182, 666-743 (BuildSubagentContext + FindSubagentFilesForNewestSession + call sites + the constant)
    - CCInfoWindows/CCInfoWindows/Services/Interfaces/IJsonlService.cs (confirm GetContextWindow signature returning ContextWindowData with .Subagents)
    - CCInfoWindows/CCInfoWindows/Models/ContextWindowData.cs (SubagentContextData shape — LastActivity is DateTimeOffset)
  </read_first>
  <behavior>
    Three xUnit [Fact] tests in `CCInfoWindows.Tests/Services/JsonlServiceSubagentTests.cs`. ALL THREE MUST FAIL on the unmodified `JsonlService.BuildSubagentContext`:

    1. **`GetContextWindow_StaleAssistantEntry_FreshFileMtime_SubagentRemainsVisible`** (SUBAGENT-01)
       - Arrange: write a subagent JSONL with one assistant entry timestamped `DateTimeOffset.UtcNow.AddMinutes(-5)` (well outside the 30s cutoff under both the old and new filter), then call `File.SetLastWriteTimeUtc(agentFile, DateTime.UtcNow)` to force mtime to "now".
       - Act: `svc.GetContextWindow(projectDirName).Subagents`
       - Assert: `Assert.Contains(subagents, s => s.AgentId == "alpha")`
       - Pre-fix expectation: FAILS — old filter uses `lastEntry.Timestamp` (5 min ago) and drops the agent. Post-fix expectation: PASSES — new filter uses fresh mtime.

    2. **`GetContextWindow_StaleAssistantEntry_StaleFileMtime_SubagentIsFiltered`** (SUBAGENT-01 regression guard)
       - Arrange: same as above, but `File.SetLastWriteTimeUtc(agentFile, DateTime.UtcNow.AddMinutes(-5))` so BOTH signals are stale.
       - Act: `svc.GetContextWindow(projectDirName).Subagents`
       - Assert: `Assert.DoesNotContain(subagents, s => s.AgentId == "bravo")`
       - Pre-fix expectation: PASSES (old filter already drops it). Post-fix expectation: STILL PASSES (this is the regression guard that the 30s cutoff is still enforced). Listing it under "MUST FAIL" requires care — see action note below.

    3. **`GetContextWindow_FreshMtime_LastActivityReflectsMtime`** (SUBAGENT-02)
       - Arrange: write a subagent JSONL with assistant entry timestamped `DateTimeOffset.UtcNow.AddMinutes(-5)`, then `File.SetLastWriteTimeUtc(agentFile, DateTime.UtcNow)`.
       - Act: `svc.GetContextWindow(projectDirName).Subagents.Single(s => s.AgentId == "charlie")`
       - Assert: `(subagent.LastActivity - new DateTimeOffset(freshMtime, TimeSpan.Zero)).Duration() < TimeSpan.FromSeconds(2)` — `LastActivity` tracks mtime, NOT the stale assistant timestamp.
       - Pre-fix expectation: FAILS — old code assigns `lastEntry.Timestamp` to `LastActivity`.

    Test naming follows project convention `Method_Scenario_Expectation` (no `Should_` prefix, no FluentAssertions, only `Assert.*`).
  </behavior>
  <action>
    Per RESEARCH.md Finding 4 + project conventions, create `CCInfoWindows.Tests/Services/JsonlServiceSubagentTests.cs`.

    Namespace: `CCInfoWindows.Tests.Services`. File-scoped. `public class JsonlServiceSubagentTests : IDisposable`.

    **Fixture shape (mirror `JsonlServiceColdStartTests`):**

    ```csharp
    private readonly string _tempDir;

    public JsonlServiceSubagentTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "subagent-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, recursive: true); }
            catch (IOException) { /* AV / handle race — leave it for the OS to clean */ }
        }
    }
    ```

    **Helpers (private static / instance methods inside the test class):**

    - `private (string ProjectDirName, string AgentFile) ArrangeSubagentFixture(DateTimeOffset assistantTimestamp, string agentId)` — creates the directory tree `{_tempDir}/{ProjectDirName}/{sessionUuid}.jsonl` (main-session JSONL with a fresh assistant entry to satisfy `FindSubagentFilesForNewestSession`) + `{_tempDir}/{ProjectDirName}/{sessionUuid}/subagents/agent-{agentId}.jsonl` (the subagent file with one assistant entry at `assistantTimestamp`).
    - `private static void WriteAssistantJsonlLine(string filePath, string sessionId, bool isSidechain, DateTimeOffset timestamp)` — copy the shape from `JsonlServiceColdStartTests.WriteAssistantJsonlLine` and add the missing `isSidechain` field (subagents have `isSidechain=true`). Use `File.AppendAllText` (NOT FileStream — see RESEARCH.md Pitfall 4).
    - `private IJsonlService BuildService()` — construct production `JsonlService(_tempDir)` matching `JsonlServiceColdStartTests` construction signature.

    **JSON line shape (matches RESEARCH.md test skeleton):**

    ```csharp
    var line = JsonSerializer.Serialize(new
    {
        uuid = $"msg_{Guid.NewGuid():N}",
        requestId = $"req_{Guid.NewGuid():N}",
        uniqueHash = $"msg_...|req_...",
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
    ```

    Match the exact JSON property names used by the existing `JsonlServiceColdStartTests.WriteAssistantJsonlLine` helper — read it first and reuse the same property names so deserialization succeeds against the production `JsonlEntry` record.

    **The three [Fact] tests** — full bodies as specified in `<behavior>` above. Three points of detail:

    a) After every `File.SetLastWriteTimeUtc(agentFile, target)` call, re-read `File.GetLastWriteTimeUtc(agentFile)` and assert it matches the target within 1 second (RESEARCH.md Pitfall 5: AV may bump mtime). If the re-read mismatches by >1s, skip with `Skip` is unavailable on xUnit `[Fact]` — instead `Assert.True(diff < TimeSpan.FromSeconds(1), "test environment hostile to mtime control — AV likely")`. This produces a clear assertion message rather than a flaky red.

    b) For the **stale-mtime** test (test 2), use `DateTimeOffset.UtcNow.AddMinutes(-5)` for the assistant timestamp AND `DateTime.UtcNow.AddMinutes(-5)` for the mtime. Note: this test passes BOTH pre-fix and post-fix — it is the regression guard. The RED expectation for Task 1 is satisfied by tests 1 and 3; test 2 must compile and execute, but its pass/fail status is identical before and after the fix. Document this in a code comment above the test:

    ```csharp
    // Regression guard (SUBAGENT-01). Passes both pre-fix and post-fix.
    // Purpose: prove the 30s cutoff is still enforced after the mtime switch.
    ```

    c) Test 3 must construct `freshMtime = DateTime.UtcNow` BEFORE the `File.SetLastWriteTimeUtc` call and reuse that exact value in the assertion — do NOT read it back via `File.GetLastWriteTimeUtc` (that round-trip can lose sub-second precision on some filesystems per RESEARCH.md Pitfall 6).

    **Three [Fact] tests total; one fixture; one helper for arrangement; one helper for JSON line writing. ~120 LOC.**
  </action>
  <verify>
    <automated>dotnet build CCInfoWindows.Tests/CCInfoWindows.Tests.csproj</automated>
    <automated>dotnet test CCInfoWindows.Tests/CCInfoWindows.Tests.csproj --filter "FullyQualifiedName~JsonlServiceSubagentTests" --no-restore</automated>
    Expected RED outcome on the unmodified JsonlService.cs:
    - Test 1 (`...FreshFileMtime_SubagentRemainsVisible`): FAIL — old code drops the agent.
    - Test 2 (`...StaleFileMtime_SubagentIsFiltered`): PASS — regression guard, behavior unchanged.
    - Test 3 (`...LastActivityReflectsMtime`): FAIL — old code assigns assistant timestamp.

    So the expected initial result is `Failed: 2, Passed: 1` on the JsonlServiceSubagentTests filter. If the count differs, investigate before proceeding to Task 2.
  </verify>
  <acceptance_criteria>
    - File `CCInfoWindows.Tests/Services/JsonlServiceSubagentTests.cs` exists.
    - `grep -c "\[Fact\]" CCInfoWindows.Tests/Services/JsonlServiceSubagentTests.cs` returns 3.
    - `grep -c "GetContextWindow_StaleAssistantEntry_FreshFileMtime_SubagentRemainsVisible" CCInfoWindows.Tests/Services/JsonlServiceSubagentTests.cs` returns 1.
    - `grep -c "GetContextWindow_StaleAssistantEntry_StaleFileMtime_SubagentIsFiltered" CCInfoWindows.Tests/Services/JsonlServiceSubagentTests.cs` returns 1.
    - `grep -c "GetContextWindow_FreshMtime_LastActivityReflectsMtime" CCInfoWindows.Tests/Services/JsonlServiceSubagentTests.cs` returns 1.
    - `grep -c "File.SetLastWriteTimeUtc" CCInfoWindows.Tests/Services/JsonlServiceSubagentTests.cs` returns >= 3 (one per test).
    - `dotnet build CCInfoWindows.Tests/CCInfoWindows.Tests.csproj` exits 0 (no compile errors).
    - `dotnet test ... --filter "FullyQualifiedName~JsonlServiceSubagentTests"` reports `Failed: 2, Passed: 1` (RED state for Task 2 to address).
  </acceptance_criteria>
  <done>Test scaffold compiles cleanly; 2 of 3 tests fail with assertion mismatches (proving they exercise the SUBAGENT-01 and SUBAGENT-02 surfaces); 1 regression-guard test passes.</done>
</task>

<task type="auto" tdd="true">
  <name>Task 2: Patch BuildSubagentContext to mtime-cutoff (GREEN)</name>
  <files>CCInfoWindows/CCInfoWindows/Services/JsonlService.cs</files>
  <read_first>
    - CCInfoWindows/CCInfoWindows/Services/JsonlService.cs lines 693-743 (the entire function — read once, edit in place)
    - .planning/phases/29-fix-subagent-activity-detection-switch-from-assistant-timest/29-RESEARCH.md Finding 1, 2, 3, 5, 9 (mtime semantics, deletion race, DateTimeOffset conversion idiom, guard preservation, UTC arithmetic)
    - .planning/phases/29-fix-subagent-activity-detection-switch-from-assistant-timest/29-RESEARCH.md "Pitfalls 1, 2, 3" (mixing UTC and Local, leftover dead code, empty-file guard)
  </read_first>
  <behavior>
    After this task, `BuildSubagentContext` (`JsonlService.cs:693-743`) MUST:
    - Compute `mtimeUtc = File.GetLastWriteTimeUtc(file)` at the TOP of the `try` block, BEFORE `ReadTailLines`.
    - Convert via `new DateTimeOffset(mtimeUtc, TimeSpan.Zero)` (explicit zero offset per RESEARCH.md Finding 3 — protects against future swap to non-UTC `GetLastWriteTime`).
    - Short-circuit `if (lastActivity < cutoff) continue;` BEFORE any `ReadTailLines` call (performance win + clearer control flow).
    - Preserve the `entries.Count == 0 ⇒ continue` guard at its current position (after `ParseJsonlEntries`). DO NOT move or delete it (Pitfall 3).
    - Remove the old `var lastActivity = lastEntry.Timestamp ?? DateTimeOffset.MinValue;` line entirely — not commented out (Pitfall 2 + CLAUDE.md "Delete commented-out code").
    - Assign the mtime-derived `lastActivity` to `SubagentContextData.LastActivity = lastActivity` in the `result.Add` block — `LastActivity` MUST reflect mtime, never `lastEntry.Timestamp`.
    - Keep the existing `catch (IOException)` + `catch (UnauthorizedAccessException)` blocks unchanged — they already cover the `File.GetLastWriteTimeUtc` failure modes per RESEARCH.md Finding 2 (deleted-mid-call returns `1601-01-01Z` without throw and falls through the `< cutoff` filter).
    - Keep `SubagentActivityWindowSeconds = 30` constant unchanged.
    - Keep `result.OrderBy(a => a.AgentId, StringComparer.Ordinal).ToList()` unchanged.
    - All 3 tests from Task 1 PASS.
    - Full test suite shows no NEW failures vs. the documented baseline (2 pre-existing `ClaudeApiServiceTests` + 13 pre-existing `JsonlServiceTests` per STATE.md line 135-136 — unchanged).
  </behavior>
  <action>
    Edit `CCInfoWindows/CCInfoWindows/Services/JsonlService.cs`. Replace the body of `BuildSubagentContext` (current lines 693-743) with the patch below. Preserve method signature, surrounding using-statements, and the `private static` modifiers.

    **Required final shape (matches RESEARCH.md "Reference Patch" section exactly):**

    ```csharp
    private static IReadOnlyList<SubagentContextData> BuildSubagentContext(
        List<string> subagentFiles, long sonnetContextSize)
    {
        var result = new List<SubagentContextData>();
        var cutoff = DateTimeOffset.UtcNow.AddSeconds(-SubagentActivityWindowSeconds);

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
                // Subagent files have isSidechain=true on all entries by design —
                // do not apply the sidechain filter here.
                var entries = ParseJsonlEntries(lines)
                    .Where(e => string.Equals(e.Type, "assistant", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                // Guard preserved: fresh mtime but no assistant entries yet
                // (agent just started — only user / tool-result lines). Without an
                // assistant entry we have no model + token data to display.
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

    Verify after edit that the previous `var lastActivity = lastEntry.Timestamp ?? DateTimeOffset.MinValue;` line is GONE (not commented out). Verify the two `Debug.WriteLine` log messages and the two catch blocks are byte-identical to the pre-edit version (only the body inside the try block changes).

    Do NOT modify `SubagentActivityWindowSeconds` (line 30). Do NOT touch the two call sites at line 159 + 182 — they continue to use the same `BuildSubagentContext` signature. Do NOT modify `SubagentContextData` (record shape stays — only the semantic of `LastActivity` shifts).

    Add no new `using` directives — `System.IO` is already imported via `FileStream`/`File` usage elsewhere in the file.
  </action>
  <verify>
    <automated>dotnet build CCInfoWindows/CCInfoWindows/CCInfoWindows.csproj</automated>
    <automated>dotnet test CCInfoWindows.Tests/CCInfoWindows.Tests.csproj --filter "FullyQualifiedName~JsonlServiceSubagentTests" --no-restore</automated>
    <automated>dotnet test CCInfoWindows.Tests/CCInfoWindows.Tests.csproj --no-restore</automated>
    Expected:
    - Production build exits 0 with zero new compiler warnings.
    - SubagentTests filter: `Failed: 0, Passed: 3`.
    - Full suite: same baseline failure count as pre-Phase-29 (2 ClaudeApiServiceTests + 13 JsonlServiceTests pre-existing, NOT counted as Phase-29 regressions; no NEW failures introduced).
  </verify>
  <acceptance_criteria>
    - `grep -c "File.GetLastWriteTimeUtc(file)" CCInfoWindows/CCInfoWindows/Services/JsonlService.cs` returns >= 1.
    - `grep -c "new DateTimeOffset(mtimeUtc, TimeSpan.Zero)" CCInfoWindows/CCInfoWindows/Services/JsonlService.cs` returns 1.
    - `grep -v '^\s*//' CCInfoWindows/CCInfoWindows/Services/JsonlService.cs | grep -c "lastEntry.Timestamp ?? DateTimeOffset.MinValue"` returns 0 (old line is REMOVED, not commented out — Pitfall 2 enforcement).
    - `grep -c "LastActivity = lastActivity" CCInfoWindows/CCInfoWindows/Services/JsonlService.cs` returns >= 1 (assignment intact).
    - `grep -c "if (entries.Count == 0)" CCInfoWindows/CCInfoWindows/Services/JsonlService.cs` returns >= 1 (guard preserved).
    - `grep -c "SubagentActivityWindowSeconds = 30" CCInfoWindows/CCInfoWindows/Services/JsonlService.cs` returns 1 (constant unchanged).
    - The `File.GetLastWriteTimeUtc(file)` line appears in the source BEFORE the `ReadTailLines(file)` line within the `try` block (cutoff applied before parsing — verified by reading the patched function).
    - `dotnet build CCInfoWindows/CCInfoWindows/CCInfoWindows.csproj` exits 0.
    - `dotnet test ... --filter "FullyQualifiedName~JsonlServiceSubagentTests"` reports `Failed: 0, Passed: 3`.
    - Full `dotnet test`: NEW-failure count is 0 (compare against baseline; pre-existing 15 baseline failures stay unchanged).
  </acceptance_criteria>
  <done>BuildSubagentContext now filters on filesystem mtime; all 3 new tests pass; no regression in existing tests; old assistant-timestamp filter line fully removed.</done>
</task>

<task type="checkpoint:human-verify" gate="blocking">
  <name>Task 3: Visual UAT — 4-parallel-subagent fixture in Release build</name>
  <what-built>
    Tasks 1 + 2 already landed: `BuildSubagentContext` now uses `File.GetLastWriteTimeUtc(file)` as the activity-cutoff source, matching macOS `findActiveAgents` semantics. Unit tests prove the filter shape is correct; this checkpoint confirms the fix actually resolves the visual UAT gap that triggered Phase 29 (4 agents in Claude CLI vs. 2 in ccInfo Windows pre-fix).
  </what-built>
  <how-to-verify>
    Claude executes steps 1-5 autonomously. Step 6 is the human-verifiable assertion.

    **Step 1 — Release build:**
    ```
    dotnet build CCInfoWindows/CCInfoWindows/CCInfoWindows.csproj -c Release -o CCInfoWindows/CCInfoWindows/bin/x64/Release/net9.0-windows10.0.19041.0/
    ```
    (per CLAUDE.md "Release Build Rules": no `dotnet publish`, always pass `-o`.)

    **Step 2 — Stage the 4-subagent fixture:** under `%USERPROFILE%\.claude\projects\<active-project-dirname>\` create:
    ```
    <sessionUuid>.jsonl                         (main session JSONL — newest mtime, one assistant entry now)
    <sessionUuid>\subagents\agent-aaaa.jsonl    (assistant entry 5 min ago — STALE; mtime = now)
    <sessionUuid>\subagents\agent-bbbb.jsonl    (assistant entry 2 min ago — STALE; mtime = now)
    <sessionUuid>\subagents\agent-cccc.jsonl    (assistant entry 10 min ago — STALE; mtime = now - 15s)
    <sessionUuid>\subagents\agent-dddd.jsonl    (assistant entry 30 min ago — STALE; mtime = now - 10s)
    ```
    Each `agent-*.jsonl` MUST contain at least one valid assistant JSONL entry (so `entries.Count == 0` guard passes) — reuse the JSON shape from Task 1's `WriteAssistantJsonlLine` helper. After writing, force mtimes precisely using PowerShell:
    ```powershell
    (Get-Item agent-aaaa.jsonl).LastWriteTimeUtc = [DateTime]::UtcNow
    (Get-Item agent-bbbb.jsonl).LastWriteTimeUtc = [DateTime]::UtcNow
    (Get-Item agent-cccc.jsonl).LastWriteTimeUtc = [DateTime]::UtcNow.AddSeconds(-15)
    (Get-Item agent-dddd.jsonl).LastWriteTimeUtc = [DateTime]::UtcNow.AddSeconds(-10)
    ```

    **Step 3 — Pre-fix regression baseline (optional but recommended):** before launching the patched build, briefly stash the patched `JsonlService.cs`, rebuild Release, launch, screenshot, count visible subagents (expect 0-2). Then restore the patch and rebuild. This produces the regression-baseline screenshot referenced in CONTEXT.md (`spec/v1.11.1-macOS/ccinfo-nur-2-sub-agents.png` equivalent). If time-constrained, skip — the post-fix count is the only blocking acceptance.

    **Step 4 — Launch patched build:**
    ```
    CCInfoWindows/CCInfoWindows/bin/x64/Release/net9.0-windows10.0.19041.0/CCInfoWindows.exe
    ```
    Wait ~3 seconds for cold-start scan.

    **Step 5 — Capture via windows-mcp:**
    ```
    mcp__windows-mcp__window_management(action='find', title='ccInfo')   # returns handle
    mcp__windows-mcp__screenshot_control(annotate=true)                  # archives panel evidence
    mcp__windows-mcp__ui_automation(action='find', windowHandle=<handle>)  # discover the subagent ItemsControl name; enumerate rows
    ```
    Archive the annotated screenshot at `spec/v1.11.1-macOS/ccinfo-4-sub-agents-postfix.png` so the v1.5 milestone evidence trail stays in one folder.

    **Step 6 — Human-verifiable assertion (the actual checkpoint):**
    Count the visible subagent rows in the "Kontextfenster" / "Context Window" panel. **Expected: 4 of 4.**

    Acceptance:
    - PASS — 4 subagent rows visible (agent-aaaa, agent-bbbb, agent-cccc, agent-dddd).
    - FAIL — < 4 rows visible. Triage immediately: re-read `JsonlService.cs:BuildSubagentContext`, re-run unit tests, check whether the staged subagent files have the expected mtimes via `(Get-Item ...).LastWriteTimeUtc`. If mtimes were silently re-bumped by AV (RESEARCH.md Pitfall 5), this is environmental, not a code defect — note in resume signal.

    Cleanup after sign-off: delete the four staged `agent-*.jsonl` files and the `<sessionUuid>.jsonl` fixture so the next real Claude CLI session is not contaminated.
  </how-to-verify>
  <resume-signal>Type "approved" if 4 of 4 subagents visible in the Kontextfenster panel. Otherwise describe what you see (count, screenshot path, any error InfoBar text).</resume-signal>
</task>

</tasks>

<threat_model>
## Trust Boundaries

| Boundary | Description |
|----------|-------------|
| Filesystem → `JsonlService` | Subagent JSONL files under `%USERPROFILE%\.claude\projects\...\subagents\` are written by the Claude CLI process. ccInfo reads metadata (mtime) + content (JSONL lines). Files originate from a local trusted process; no remote / cross-user input crosses this boundary in Phase 29. |

## STRIDE Threat Register

| Threat ID | Category | Component | Disposition | Mitigation Plan |
|-----------|----------|-----------|-------------|-----------------|
| T-29-01 | Tampering | `File.GetLastWriteTimeUtc` mtime probe — could a hostile local process fake mtime to keep a deleted agent visible past the cutoff? | accept | No new attack surface vs. existing JSONL content read. Mtime forgery requires local write access to the user profile; an attacker with that level of access already controls the JSONL content itself. Visibility window is read-only metrics — no privilege escalation, no credential exposure, no data persistence. |
| T-29-02 | Denial of Service | A pathological subagent directory with thousands of stale files could slow `BuildSubagentContext` | accept | The cutoff-before-`ReadTailLines` reordering REDUCES this risk (stale files skip the I/O entirely). Pre-fix code opened and tail-read every file regardless of staleness; post-fix code only opens fresh-mtime files. Net DoS surface improves, not regresses. |
| T-29-03 | Information Disclosure | New `Debug.WriteLine` paths in catch blocks could leak file paths to the debug log | accept (unchanged from pre-fix) | The two existing `Debug.WriteLine` calls already log full file paths and exception messages — this plan does NOT introduce any new logging. Debug output is gated on `Debug.WriteLine` (visible only in debugger / DebugView, never written to disk per Secure Coding "No sensitive data in logs"). No tokens, credentials, or session keys are logged. |
| T-29-04 | Elevation of Privilege | `File.GetLastWriteTimeUtc` runs at the existing user-context privilege | mitigate | Confirmed by RESEARCH.md Finding 6: `GetFileAttributesEx` is a metadata-only call, no file open, no privilege escalation. Existing `UnauthorizedAccessException` catch already covers any permission edge case. |

**Summary:** Phase 29 changes the *source* of an existing read-only metrics value. No new input parsing, no network I/O, no credential surface, no persistence. Threat surface is strictly smaller than the pre-fix state (less I/O on stale files).
</threat_model>

<verification>
After all three tasks complete, run in three separate Bash calls (per CLAUDE.md strict no-chaining rule):

```bash
dotnet build CCInfoWindows/CCInfoWindows/CCInfoWindows.csproj
```

```bash
dotnet test CCInfoWindows.Tests/CCInfoWindows.Tests.csproj --filter "FullyQualifiedName~JsonlServiceSubagentTests" --no-restore
```

```bash
dotnet test CCInfoWindows.Tests/CCInfoWindows.Tests.csproj --no-restore
```

Expected:
- Production build exits 0 with zero new compiler warnings.
- SubagentTests filter: `Failed: 0, Passed: 3`.
- Full suite: pre-existing baseline failures unchanged (2 ClaudeApiServiceTests + 13 JsonlServiceTests per STATE.md); zero new failures introduced on the Phase-29-modified surface.

Targeted spot checks:
```bash
grep -n "File.GetLastWriteTimeUtc(file)" CCInfoWindows/CCInfoWindows/Services/JsonlService.cs
```
Expected: at least one match inside `BuildSubagentContext` (lines 693-743 region post-edit).

```bash
grep -n "lastEntry.Timestamp ?? DateTimeOffset.MinValue" CCInfoWindows/CCInfoWindows/Services/JsonlService.cs
```
Expected: zero matches (old buggy line fully removed, not commented out).

Visual UAT (Task 3):
- 4 of 4 staged subagents render in the Kontextfenster panel of the patched Release build.
- Annotated screenshot archived at `spec/v1.11.1-macOS/ccinfo-4-sub-agents-postfix.png`.
- Human sign-off recorded via `resume-signal: approved`.
</verification>

<success_criteria>
- **SC1 (visual UAT)** — 4-parallel-subagent fixture renders 4 of 4 visible rows in the Kontextfenster panel (pre-fix baseline was 2 of 4). Verified by Task 3 checkpoint.
- **SC2 (stale-entry-fresh-mtime unit test)** — `GetContextWindow_StaleAssistantEntry_FreshFileMtime_SubagentRemainsVisible` passes. Verified by Task 2 automated `dotnet test`.
- **SC3 (all-stale unit test)** — `GetContextWindow_StaleAssistantEntry_StaleFileMtime_SubagentIsFiltered` passes (regression guard — the 30s cutoff is still enforced after the source switch). Verified by Task 2 automated `dotnet test`.
- **SC4 (no regression)** — Existing `JsonlServiceTests`, `JsonlServiceColdStartTests`, and `JsonlServiceWatcherTests` show zero NEW failures vs. the documented baseline (2 pre-existing ClaudeApiServiceTests + 13 pre-existing JsonlServiceTests). Verified by Task 2 full-suite `dotnet test`.
- **SC5 (LastActivity semantic shift)** — `SubagentContextData.LastActivity` reflects mtime (within 2-second tolerance), not the stale assistant timestamp. Verified by `GetContextWindow_FreshMtime_LastActivityReflectsMtime` unit test in Task 2.

All five SC IDs map 1:1 to a phase requirement:
| SC | REQ ID | Verified by |
|----|--------|-------------|
| SC1 | SUBAGENT-04 | Task 3 (checkpoint:human-verify) |
| SC2 | SUBAGENT-01 | Task 2 (unit test 1) |
| SC3 | SUBAGENT-01 (regression) | Task 2 (unit test 2) |
| SC4 | SUBAGENT-05 | Task 2 (full-suite `dotnet test`) |
| SC5 | SUBAGENT-02 | Task 2 (unit test 3) |

SUBAGENT-03 (test-class follows Phase-25 fixture pattern) is verified by code review on the new `JsonlServiceSubagentTests.cs` — Task 1 `<read_first>` enforces the precedent file is consulted, and Task 1 acceptance criteria require the same IDisposable temp-dir shape and `Method_Scenario_Expectation` naming.
</success_criteria>

<output>
After completion, create `.planning/phases/29-fix-subagent-activity-detection-switch-from-assistant-timest/29-01-SUMMARY.md` documenting:
- The exact diff applied to `BuildSubagentContext` (before/after, with post-edit line numbers).
- The new test class path + the 3 [Fact] names and their pre-fix RED expectations.
- Visual UAT outcome: number of subagents rendered, screenshot path, any environmental notes (AV interference, mtime drift).
- macOS parity statement: the Windows port now uses `File.GetLastWriteTimeUtc` as the semantic equivalent of `FileManager.contentModificationDate` in `JSONLParser.swift:457-483, findActiveAgents`. Note RESEARCH.md Finding 10 caveat (no line-by-line Swift diff performed).
- Any baseline regression deltas (expected: zero new failures vs. STATE.md baseline).
- Phase 29 declared complete; next: `/gsd-verify-work 29` or directly archive into v1.5+ milestone folder per established workflow.
</output>
