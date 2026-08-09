using System.Text.Json;
using CCInfoWindows.Services;

namespace CCInfoWindows.Tests.Services;

/// <summary>
/// Regression tests for subagent activity-detection via filesystem mtime
/// (Phase 29 SUBAGENT-01..05). Replaces the previous assistant-entry timestamp
/// filter with File.GetLastWriteTimeUtc to match macOS contentModificationDate
/// semantics. Every tool-result write bumps NTFS LastWriteTime, so long
/// tool-calls keep the subagent visible even when the last assistant entry
/// is older than the 30s cutoff.
/// </summary>
public class JsonlServiceSubagentTests : IDisposable
{
    // Synthetic project name — decodes via SessionNameHelper.DecodeProjectDirectory
    // to "fixture" without depending on any real machine path. Hermetic for CI and
    // any maintainer layout. The fixture never creates the cwd its entries would name
    // (they carry no cwd at all), so it also stays clear of the BuildSessionList
    // validity filter; GetContextWindow is queried by project directory name directly.
    private const string ProjectDirName = "X--phase29-subagent-fixture";
    private const string CacheDirectoryName = "cache";
    private const string SubagentsDirName = "subagents";
    private const string WorkflowsDirName = "workflows";

    private readonly string _tempDir;
    private readonly string _cacheDir;

    public JsonlServiceSubagentTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "subagent-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _cacheDir = Path.Combine(_tempDir, CacheDirectoryName);
        Directory.CreateDirectory(_cacheDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, recursive: true); }
            catch (IOException) { /* AV / handle race — leave it for the OS to clean */ }
        }
    }

    // -------------------------------------------------------------------------
    // SUBAGENT-01: Stale assistant entry + fresh file mtime ⇒ subagent visible
    // -------------------------------------------------------------------------

    /// <summary>
    /// A subagent file whose last assistant entry is 5 minutes old (well outside
    /// the 30s cutoff under the OLD entry-timestamp filter) but whose filesystem
    /// mtime is "now" (simulating a fresh tool-result write) MUST remain visible
    /// in the result list. This is the Phase-29 core bug fix.
    /// </summary>
    [Fact]
    public async Task GetContextWindow_StaleAssistantEntry_FreshFileMtime_SubagentRemainsVisible()
    {
        // Arrange: assistant entry stamped 5 min ago, mtime forced to "now"
        var agentFile = ArrangeSubagentFixture(
            assistantTimestamp: DateTimeOffset.UtcNow.AddMinutes(-5),
            agentId: "alpha");

        var freshMtime = DateTime.UtcNow;
        File.SetLastWriteTimeUtc(agentFile, freshMtime);
        AssertMtimeWasSet(agentFile, freshMtime);

        using var svc = BuildService();
        await svc.InitializeAsync();

        // Act
        var subagents = svc.GetContextWindow(ProjectDirName).Subagents;

        // Assert: subagent must appear despite stale assistant timestamp.
        // Pre-fix: FAILS (old filter compares 5-min-old entry timestamp < cutoff).
        // Post-fix: PASSES (mtime is fresh).
        Assert.Contains(subagents, s => s.AgentId == "alpha");
    }

    // -------------------------------------------------------------------------
    // SUBAGENT-01 regression guard: All-stale ⇒ subagent filtered
    // -------------------------------------------------------------------------

    /// <summary>
    /// Regression guard (SUBAGENT-01). Passes both pre-fix and post-fix.
    /// Purpose: prove the 30s cutoff is still enforced after the mtime switch.
    /// </summary>
    [Fact]
    public async Task GetContextWindow_StaleAssistantEntry_StaleFileMtime_SubagentIsFiltered()
    {
        // Arrange: assistant entry 5 min ago AND mtime 5 min ago — both signals stale.
        var agentFile = ArrangeSubagentFixture(
            assistantTimestamp: DateTimeOffset.UtcNow.AddMinutes(-5),
            agentId: "bravo");

        var staleMtime = DateTime.UtcNow.AddMinutes(-5);
        File.SetLastWriteTimeUtc(agentFile, staleMtime);
        AssertMtimeWasSet(agentFile, staleMtime);

        using var svc = BuildService();
        await svc.InitializeAsync();

        // Act
        var subagents = svc.GetContextWindow(ProjectDirName).Subagents;

        // Assert: subagent must NOT appear — 30s cutoff still enforced.
        Assert.DoesNotContain(subagents, s => s.AgentId == "bravo");
    }

    // -------------------------------------------------------------------------
    // SUBAGENT-02: LastActivity field reflects mtime, NOT assistant timestamp
    // -------------------------------------------------------------------------

    /// <summary>
    /// SubagentContextData.LastActivity MUST equal the mtime-derived value, not
    /// the (stale) assistant-entry timestamp. Verifies the LastActivity assignment
    /// site uses the mtime variable, preserving filter/UI synchronization.
    /// </summary>
    [Fact]
    public async Task GetContextWindow_FreshMtime_LastActivityReflectsMtime()
    {
        // Arrange: stale assistant entry, fresh mtime
        var staleStamp = DateTimeOffset.UtcNow.AddMinutes(-5);
        var agentFile = ArrangeSubagentFixture(
            assistantTimestamp: staleStamp,
            agentId: "charlie");

        // Capture freshMtime BEFORE the SetLastWriteTimeUtc call and reuse it
        // in the assertion — avoids sub-second precision loss on round-trip read.
        var freshMtime = DateTime.UtcNow;
        File.SetLastWriteTimeUtc(agentFile, freshMtime);
        AssertMtimeWasSet(agentFile, freshMtime);

        using var svc = BuildService();
        await svc.InitializeAsync();

        // Act
        var subagent = svc.GetContextWindow(ProjectDirName).Subagents.Single(s => s.AgentId == "charlie");

        // Assert: LastActivity should track mtime within 2s, NOT the 5-min-old assistant entry.
        var deltaFromMtime = (subagent.LastActivity - new DateTimeOffset(freshMtime, TimeSpan.Zero)).Duration();
        var deltaFromAssistant = (subagent.LastActivity - staleStamp).Duration();

        Assert.True(
            deltaFromMtime < TimeSpan.FromSeconds(2),
            $"LastActivity should track mtime (delta={deltaFromMtime}), not assistant timestamp (delta={deltaFromAssistant}).");
    }

    // -------------------------------------------------------------------------
    // Watcher batch: a subagent write must not register a phantom session
    // -------------------------------------------------------------------------

    /// <summary>
    /// A subagent write reaches ProcessSingleFile through the watcher batch. The walk-up there
    /// stopped one directory short of the project, so the SESSION UUID became a _projectData key and
    /// therefore a SessionInfo.Id — a session named after a UUID fragment appeared in the picker
    /// whenever the visibility window was set to "Unlimited". Nothing was ever parsed out of the
    /// file: subagent content is read on demand by FindSubagentFilesForSession, which is why
    /// the bar below still shows the agent.
    /// </summary>
    [Fact]
    public async Task ProcessFilesForTest_SubagentFile_RegistersNoPhantomSession()
    {
        var agentFile = ArrangeSubagentFixture(
            assistantTimestamp: DateTimeOffset.UtcNow,
            agentId: "delta");
        var sessionUuid = SessionUuidOf(agentFile);

        using var svc = BuildService();
        await svc.InitializeAsync();

        await svc.ProcessFilesForTestAsync([agentFile]);

        Assert.DoesNotContain(svc.Sessions, s => s.Id == sessionUuid);
        Assert.Contains(svc.Sessions, s => s.Id == ProjectDirName);
        Assert.Contains(svc.GetContextWindow(ProjectDirName).Subagents, s => s.AgentId == "delta");
    }

    // -------------------------------------------------------------------------
    // v1.7 WORKFLOW-01: workflow agents live one level deeper and were invisible
    // -------------------------------------------------------------------------

    /// <summary>
    /// The core v1.7 bug. A workflow agent sits at subagents/workflows/{runId}/agent-*.jsonl,
    /// which the previous top-level-only scan never reached. Pre-fix the whole section stayed
    /// empty for a pure workflow session — silently, because the empty result then took the
    /// project-level fallback path instead of logging anything.
    /// </summary>
    [Fact]
    public async Task GetContextWindow_WorkflowAgent_IsFoundAndCarriesRunId()
    {
        ArrangeSubagentFixture(
            assistantTimestamp: DateTimeOffset.UtcNow,
            agentId: "echo",
            workflowId: "wf_11f45d5b-27d");

        using var svc = BuildService();
        await svc.InitializeAsync();

        var subagent = svc.GetContextWindow(ProjectDirName).Subagents.Single(s => s.AgentId == "echo");

        Assert.Equal("wf_11f45d5b-27d", subagent.WorkflowId);
    }

    /// <summary>
    /// The flat Agent-tool layout must stay classified as "not a workflow" — WorkflowId null is
    /// what keeps those agents on their own individual bars instead of being collapsed away.
    /// </summary>
    [Fact]
    public async Task GetContextWindow_PlainSubagent_HasNoWorkflowId()
    {
        ArrangeSubagentFixture(assistantTimestamp: DateTimeOffset.UtcNow, agentId: "foxtrot");

        using var svc = BuildService();
        await svc.InitializeAsync();

        var subagent = svc.GetContextWindow(ProjectDirName).Subagents.Single(s => s.AgentId == "foxtrot");

        Assert.Null(subagent.WorkflowId);
    }

    /// <summary>
    /// Both layouts coexist in one session (a workflow started from a session that also used the
    /// Agent tool). One recursive scan has to return both, each with its own classification.
    /// </summary>
    [Fact]
    public async Task GetContextWindow_MixedSession_ReturnsBothLayouts()
    {
        var plainFile = ArrangeSubagentFixture(assistantTimestamp: DateTimeOffset.UtcNow, agentId: "golf");
        ArrangeSubagentFixture(
            assistantTimestamp: DateTimeOffset.UtcNow,
            agentId: "hotel",
            workflowId: "wf_deadbeef-01",
            sessionUuid: SessionUuidOf(plainFile));

        using var svc = BuildService();
        await svc.InitializeAsync();

        var subagents = svc.GetContextWindow(ProjectDirName).Subagents;

        Assert.Null(subagents.Single(s => s.AgentId == "golf").WorkflowId);
        Assert.Equal("wf_deadbeef-01", subagents.Single(s => s.AgentId == "hotel").WorkflowId);
    }

    /// <summary>
    /// Two concurrent runs must keep their own run ids — the grouping downstream turns each into
    /// its own row (D-5), so a shared or swapped id would merge two unrelated runs.
    /// </summary>
    [Fact]
    public async Task GetContextWindow_TwoConcurrentRuns_StayDistinct()
    {
        var firstFile = ArrangeSubagentFixture(
            assistantTimestamp: DateTimeOffset.UtcNow,
            agentId: "india",
            workflowId: "wf_aaaa-1");
        ArrangeSubagentFixture(
            assistantTimestamp: DateTimeOffset.UtcNow,
            agentId: "juliett",
            workflowId: "wf_bbbb-2",
            sessionUuid: SessionUuidOf(firstFile));

        using var svc = BuildService();
        await svc.InitializeAsync();

        var subagents = svc.GetContextWindow(ProjectDirName).Subagents;

        Assert.Equal("wf_aaaa-1", subagents.Single(s => s.AgentId == "india").WorkflowId);
        Assert.Equal("wf_bbbb-2", subagents.Single(s => s.AgentId == "juliett").WorkflowId);
    }

    /// <summary>
    /// A workflow agent write must not register a phantom session either — the walk-up in
    /// ProcessSingleFile now has two extra levels to climb before it reaches the project.
    /// </summary>
    [Fact]
    public async Task ProcessFilesForTest_WorkflowAgentFile_RegistersNoPhantomSession()
    {
        var agentFile = ArrangeSubagentFixture(
            assistantTimestamp: DateTimeOffset.UtcNow,
            agentId: "kilo",
            workflowId: "wf_cccc-3");

        using var svc = BuildService();
        await svc.InitializeAsync();

        await svc.ProcessFilesForTestAsync([agentFile]);

        Assert.DoesNotContain(svc.Sessions, s => s.Id == SessionUuidOf(agentFile));
        Assert.DoesNotContain(svc.Sessions, s => s.Id == WorkflowsDirName);
        Assert.Contains(svc.Sessions, s => s.Id == ProjectDirName);
        Assert.Contains(svc.GetContextWindow(ProjectDirName).Subagents, s => s.AgentId == "kilo");
    }

    // -------------------------------------------------------------------------
    // Fixture helpers
    // -------------------------------------------------------------------------

    private JsonlService BuildService()
        => new(projectsDirectoryOverride: _tempDir, cacheDirectoryOverride: _cacheDir);

    /// <summary>
    /// Walks up to the "subagents" directory and takes one more hop — the session UUID, the value
    /// that must never become a session id. A fixed hop count would not do: the Agent-tool layout
    /// puts the file two levels below the session directory, the workflow layout four
    /// (subagents/workflows/{runId}/), where two hops would return "workflows".
    /// </summary>
    private static string SessionUuidOf(string agentFilePath)
    {
        var dir = Path.GetDirectoryName(agentFilePath);
        while (dir is not null && !string.Equals(Path.GetFileName(dir), SubagentsDirName, StringComparison.OrdinalIgnoreCase))
            dir = Path.GetDirectoryName(dir);

        return Path.GetFileName(Path.GetDirectoryName(dir))!;
    }

    /// <summary>
    /// Stages: {_tempDir}/{ProjectDirName}/{sessionUuid}.jsonl (main session,
    /// one fresh assistant entry — required by FindSubagentFilesForSession)
    /// + the subagent file with one assistant entry at assistantTimestamp, either at
    /// {sessionUuid}/subagents/agent-{id}.jsonl (workflowId null, Agent tool) or at
    /// {sessionUuid}/subagents/workflows/{workflowId}/agent-{id}.jsonl (workflow run).
    /// Pass an existing sessionUuid to stage several agents into the same session — a second
    /// session file would otherwise become the newest one and hide the first agent.
    /// Returns the absolute path of the subagent file so the caller can adjust its mtime.
    /// </summary>
    private string ArrangeSubagentFixture(
        DateTimeOffset assistantTimestamp,
        string agentId,
        string? workflowId = null,
        string? sessionUuid = null)
    {
        var projectDir = Path.Combine(_tempDir, ProjectDirName);
        Directory.CreateDirectory(projectDir);

        sessionUuid ??= Guid.NewGuid().ToString();

        // Main session JSONL — fresh assistant entry, must exist + be newest.
        var sessionFile = Path.Combine(projectDir, $"{sessionUuid}.jsonl");
        if (!File.Exists(sessionFile))
        {
            WriteAssistantJsonlLine(
                sessionFile,
                sessionId: sessionUuid,
                isSidechain: false,
                timestamp: DateTimeOffset.UtcNow);
        }

        var subagentDir = workflowId is null
            ? Path.Combine(projectDir, sessionUuid, SubagentsDirName)
            : Path.Combine(projectDir, sessionUuid, SubagentsDirName, WorkflowsDirName, workflowId);
        Directory.CreateDirectory(subagentDir);

        var agentFile = Path.Combine(subagentDir, $"agent-{agentId}.jsonl");
        WriteAssistantJsonlLine(
            agentFile,
            sessionId: sessionUuid,
            isSidechain: true,
            timestamp: assistantTimestamp);

        return agentFile;
    }

    /// <summary>
    /// Re-reads the file's mtime and asserts it matches the value we just set
    /// within 1 second. Mitigates RESEARCH.md Pitfall 5 (antivirus bumping
    /// mtime mid-test). If this assertion fires, the test environment is
    /// hostile and the result is environmental, not a code defect.
    /// </summary>
    private static void AssertMtimeWasSet(string filePath, DateTime expectedUtc)
    {
        var actualUtc = File.GetLastWriteTimeUtc(filePath);
        var diff = (actualUtc - expectedUtc).Duration();
        Assert.True(
            diff < TimeSpan.FromSeconds(1),
            $"mtime not preserved (expected {expectedUtc:O}, actual {actualUtc:O}) — test environment hostile to mtime control (AV likely).");
    }

    /// <summary>
    /// Appends one assistant JSONL entry to filePath. Uses File.AppendAllText
    /// (closes handle before returning) so the subsequent File.SetLastWriteTimeUtc
    /// call is safe. Property names mirror JsonlServiceColdStartTests for
    /// deserialization compatibility against the production JsonlEntry record —
    /// including message.id, which is the identity JsonlService deduplicates on.
    /// </summary>
    private static void WriteAssistantJsonlLine(string filePath, string sessionId, bool isSidechain, DateTimeOffset timestamp)
    {
        var uuid = Guid.NewGuid().ToString();
        var requestId = $"req_{Guid.NewGuid():N}";
        var line = JsonSerializer.Serialize(new
        {
            uuid,
            requestId,
            sessionId,
            timestamp = timestamp.ToString("O"),
            isSidechain,
            type = "assistant",
            message = new
            {
                id = $"msg_{Guid.NewGuid():N}",
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
