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
    // (they carry no cwd at all), so it also stays clear of the RebuildSessionsList
    // validity filter; GetContextWindow is queried by project directory name directly.
    private const string ProjectDirName = "X--phase29-subagent-fixture";
    private const string CacheDirectoryName = "cache";

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
    /// file: subagent content is read on demand by FindSubagentFilesForNewestSession, which is why
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
    // Fixture helpers
    // -------------------------------------------------------------------------

    private JsonlService BuildService()
        => new(projectsDirectoryOverride: _tempDir, cacheDirectoryOverride: _cacheDir);

    /// <summary>
    /// Subagent files live at {projectDir}/{sessionUuid}/subagents/agent-{id}.jsonl, so two hops up
    /// from the agent file is the session UUID — the value that must never become a session id.
    /// </summary>
    private static string SessionUuidOf(string agentFilePath) =>
        Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(agentFilePath)))!;

    /// <summary>
    /// Stages: {_tempDir}/{ProjectDirName}/{sessionUuid}.jsonl (main session,
    /// one fresh assistant entry — required by FindSubagentFilesForNewestSession)
    /// + {_tempDir}/{ProjectDirName}/{sessionUuid}/subagents/agent-{id}.jsonl
    /// (the subagent file with one assistant entry at assistantTimestamp).
    /// Returns the absolute path of the subagent file so the caller can adjust
    /// its mtime.
    /// </summary>
    private string ArrangeSubagentFixture(DateTimeOffset assistantTimestamp, string agentId)
    {
        var projectDir = Path.Combine(_tempDir, ProjectDirName);
        Directory.CreateDirectory(projectDir);

        var sessionUuid = Guid.NewGuid().ToString();

        // Main session JSONL — fresh assistant entry, must exist + be newest.
        var sessionFile = Path.Combine(projectDir, $"{sessionUuid}.jsonl");
        WriteAssistantJsonlLine(
            sessionFile,
            sessionId: sessionUuid,
            isSidechain: false,
            timestamp: DateTimeOffset.UtcNow);

        // Subagent file under {sessionUuid}/subagents/agent-{id}.jsonl
        var subagentDir = Path.Combine(projectDir, sessionUuid, "subagents");
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
