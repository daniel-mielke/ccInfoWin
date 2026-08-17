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
    // v1.7 redesign: the staleness gate applies per RUN, and journal.jsonl feeds the counts
    // -------------------------------------------------------------------------

    /// <summary>
    /// The core of the redesign. A workflow row reports the SUMMED context of its run, and a
    /// finished agent stops writing — so a per-agent gate drops exactly the agents the sum needs.
    /// Measured on the real 43-agent run, the per-agent gate left a median 22 % of the true token
    /// sum visible and 4.4 % at the moment of the last write. As long as any agent of the run is
    /// fresh, every agent of that run must be read.
    /// </summary>
    [Fact]
    public async Task GetContextWindow_WorkflowRun_StaleAgentStaysVisibleWhileASiblingIsFresh()
    {
        var staleFile = ArrangeSubagentFixture(
            assistantTimestamp: DateTimeOffset.UtcNow.AddMinutes(-5),
            agentId: "finished",
            workflowId: "wf_gate-1");
        var freshFile = ArrangeSubagentFixture(
            assistantTimestamp: DateTimeOffset.UtcNow,
            agentId: "running",
            workflowId: "wf_gate-1",
            sessionUuid: SessionUuidOf(staleFile));

        var staleMtime = DateTime.UtcNow.AddMinutes(-5);
        File.SetLastWriteTimeUtc(staleFile, staleMtime);
        AssertMtimeWasSet(staleFile, staleMtime);
        AssertMtimeIsFresh(freshFile);

        using var svc = BuildService();
        await svc.InitializeAsync();

        var subagents = svc.GetContextWindow(ProjectDirName).Subagents;

        // Pre-fix: only "running" survives and the row under-reports the run's tokens.
        Assert.Contains(subagents, s => s.AgentId == "finished");
        Assert.Contains(subagents, s => s.AgentId == "running");
    }

    /// <summary>
    /// The other half of the per-run gate: once nothing in the run is fresh, the whole run goes —
    /// not one agent of it stays behind. This is what retires a finished run from the display.
    /// </summary>
    [Fact]
    public async Task GetContextWindow_WorkflowRun_AllAgentsStale_WholeRunDisappears()
    {
        var firstFile = ArrangeSubagentFixture(
            assistantTimestamp: DateTimeOffset.UtcNow.AddMinutes(-5),
            agentId: "gone-a",
            workflowId: "wf_gate-2");
        var secondFile = ArrangeSubagentFixture(
            assistantTimestamp: DateTimeOffset.UtcNow.AddMinutes(-5),
            agentId: "gone-b",
            workflowId: "wf_gate-2",
            sessionUuid: SessionUuidOf(firstFile));

        var staleMtime = DateTime.UtcNow.AddMinutes(-5);
        foreach (var file in new[] { firstFile, secondFile })
        {
            File.SetLastWriteTimeUtc(file, staleMtime);
            AssertMtimeWasSet(file, staleMtime);
        }

        using var svc = BuildService();
        await svc.InitializeAsync();

        var subagents = svc.GetContextWindow(ProjectDirName).Subagents;

        Assert.DoesNotContain(subagents, s => s.WorkflowId == "wf_gate-2");
    }

    /// <summary>
    /// Regression guard for the grouping key. Plain Agent-tool files have a null run id; grouping
    /// them by that null would put every plain agent of a session into ONE group and let a single
    /// fresh agent drag all of its stale siblings back onto the screen. Each plain file must stay a
    /// group of one.
    /// </summary>
    [Fact]
    public async Task GetContextWindow_PlainSubagents_FreshOneDoesNotReviveStaleSibling()
    {
        var staleFile = ArrangeSubagentFixture(
            assistantTimestamp: DateTimeOffset.UtcNow.AddMinutes(-5),
            agentId: "plain-stale");
        var freshFile = ArrangeSubagentFixture(
            assistantTimestamp: DateTimeOffset.UtcNow,
            agentId: "plain-fresh",
            sessionUuid: SessionUuidOf(staleFile));

        var staleMtime = DateTime.UtcNow.AddMinutes(-5);
        File.SetLastWriteTimeUtc(staleFile, staleMtime);
        AssertMtimeWasSet(staleFile, staleMtime);
        AssertMtimeIsFresh(freshFile);

        using var svc = BuildService();
        await svc.InitializeAsync();

        var subagents = svc.GetContextWindow(ProjectDirName).Subagents;

        Assert.Contains(subagents, s => s.AgentId == "plain-fresh");
        Assert.DoesNotContain(subagents, s => s.AgentId == "plain-stale");
    }

    /// <summary>
    /// journal.jsonl is the only place the agent counts exist: one "started" line per spawned agent,
    /// one "result" line per finished one. Both numbers land on every agent of the run, because the
    /// display groups by run id and reads them off one member.
    /// </summary>
    [Fact]
    public async Task GetContextWindow_WorkflowRunWithJournal_CarriesStartedAndDoneCounts()
    {
        var agentFile = ArrangeSubagentFixture(
            assistantTimestamp: DateTimeOffset.UtcNow,
            agentId: "counted",
            workflowId: "wf_journal-1");
        ArrangeWorkflowJournal(agentFile, started: 30, done: 29);

        using var svc = BuildService();
        await svc.InitializeAsync();

        var subagent = svc.GetContextWindow(ProjectDirName).Subagents.Single(s => s.AgentId == "counted");

        Assert.Equal(30, subagent.RunAgentsStarted);
        Assert.Equal(29, subagent.RunAgentsDone);
    }

    /// <summary>
    /// No journal — older runs and other harness versions have none. Zero counts are the signal for
    /// the label to drop the count rather than render a fabricated "0/0".
    /// </summary>
    [Fact]
    public async Task GetContextWindow_WorkflowRunWithoutJournal_ReportsZeroCounts()
    {
        ArrangeSubagentFixture(
            assistantTimestamp: DateTimeOffset.UtcNow,
            agentId: "uncounted",
            workflowId: "wf_journal-2");

        using var svc = BuildService();
        await svc.InitializeAsync();

        var subagent = svc.GetContextWindow(ProjectDirName).Subagents.Single(s => s.AgentId == "uncounted");

        Assert.Equal(0, subagent.RunAgentsStarted);
        Assert.Equal(0, subagent.RunAgentsDone);
    }

    /// <summary>
    /// A journal beyond the 1 MB tail window would be read only from its tail, producing a count
    /// that is quietly too low. The guard reports no count at all instead — the row then shows
    /// tokens only, which is honest, where "12/14 agents done" for a 300-agent run is not.
    /// </summary>
    [Fact]
    public async Task GetContextWindow_OversizedJournal_ReportsZeroCountsRatherThanAPartialOne()
    {
        var agentFile = ArrangeSubagentFixture(
            assistantTimestamp: DateTimeOffset.UtcNow,
            agentId: "oversized",
            workflowId: "wf_journal-3");
        ArrangeWorkflowJournal(agentFile, started: 4, done: 4, padToBytes: 1_100_000);

        using var svc = BuildService();
        await svc.InitializeAsync();

        var subagent = svc.GetContextWindow(ProjectDirName).Subagents.Single(s => s.AgentId == "oversized");

        Assert.Equal(0, subagent.RunAgentsStarted);
        Assert.Equal(0, subagent.RunAgentsDone);
    }

    // -------------------------------------------------------------------------
    // Phase 4: run metadata for the row tooltip
    // -------------------------------------------------------------------------

    /// <summary>
    /// The script lives in a DIFFERENT tree from the agent transcripts —
    /// {session}/workflows/scripts/{name}-{runId}.js against
    /// {session}/subagents/workflows/{runId}/ — and both trees contain a directory called
    /// "workflows" at different depths. The parser itself is covered in WorkflowScriptMetaTests;
    /// this is the guard on the service walking to the right place and matching the right file.
    /// </summary>
    [Fact]
    public async Task GetContextWindow_WorkflowRunWithScript_ReadsNameDescriptionAndPhases()
    {
        var agentFile = ArrangeSubagentFixture(
            assistantTimestamp: DateTimeOffset.UtcNow,
            agentId: "described",
            workflowId: "wf_meta-1");
        ArrangeWorkflowScript(
            agentFile,
            """
            export const meta = {
              name: 'review-v16-to-v17',
              description: 'Multi-dimensional review of the diff',
              phases: [
                { title: 'Review', detail: 'per dimension' },
                { title: 'Verify' },
              ],
            }
            """);

        using var svc = BuildService();
        await svc.InitializeAsync();

        var subagent = svc.GetContextWindow(ProjectDirName).Subagents.Single(s => s.AgentId == "described");

        Assert.Equal("review-v16-to-v17", subagent.RunName);
        Assert.Equal("Multi-dimensional review of the diff", subagent.RunDescription);
        Assert.Equal(["Review", "Verify"], subagent.RunPhases.Select(p => p.Title));
        Assert.Equal("per dimension", subagent.RunPhases[0].Detail);
    }

    /// <summary>
    /// The run directory's creation time is the ONLY start time the display ever uses. A run also
    /// writes an exact startTime into a completed-run JSON, but that file appears at completion, and
    /// a completed run stops writing — the staleness gate has dropped its row before the file
    /// exists, so reading it would be code that never runs against a visible row.
    /// </summary>
    [Fact]
    public async Task GetContextWindow_WorkflowRun_TakesTheStartTimeFromTheRunDirectory()
    {
        var agentFile = ArrangeSubagentFixture(
            assistantTimestamp: DateTimeOffset.UtcNow,
            agentId: "live",
            workflowId: "wf_meta-2");
        var runDirectory = Path.GetDirectoryName(agentFile)!;

        using var svc = BuildService();
        await svc.InitializeAsync();

        var subagent = svc.GetContextWindow(ProjectDirName).Subagents.Single(s => s.AgentId == "live");

        Assert.Equal(
            new DateTimeOffset(Directory.GetCreationTimeUtc(runDirectory), TimeSpan.Zero),
            subagent.RunStartedUtc);
        Assert.Null(subagent.RunName);
        Assert.Null(subagent.RunDescription);
        Assert.Empty(subagent.RunPhases);
    }

    /// <summary>
    /// A completed-run JSON next to a run must change nothing — it is the file whose handling was
    /// deliberately removed. Without this the fallback could be reintroduced unnoticed.
    /// </summary>
    [Fact]
    public async Task GetContextWindow_CompletedRunJsonPresent_IsIgnored()
    {
        var agentFile = ArrangeSubagentFixture(
            assistantTimestamp: DateTimeOffset.UtcNow,
            agentId: "ignores-json",
            workflowId: "wf_meta-4");
        ArrangeCompletedRunJson(agentFile, name: "json-name", summary: "json-summary");

        using var svc = BuildService();
        await svc.InitializeAsync();

        var subagent = svc.GetContextWindow(ProjectDirName).Subagents.Single(s => s.AgentId == "ignores-json");

        Assert.Null(subagent.RunName);
        Assert.Null(subagent.RunDescription);
    }

    [Fact]
    public async Task GetContextWindow_PlainSubagent_CarriesNoRunMetadata()
    {
        ArrangeSubagentFixture(assistantTimestamp: DateTimeOffset.UtcNow, agentId: "plain-meta");

        using var svc = BuildService();
        await svc.InitializeAsync();

        var subagent = svc.GetContextWindow(ProjectDirName).Subagents.Single(s => s.AgentId == "plain-meta");

        Assert.Equal(default, subagent.RunStartedUtc);
        Assert.Null(subagent.RunName);
        Assert.Null(subagent.RunDescription);
    }

    /// <summary>
    /// Both fields are free text out of a user-written script (CLAUDE.md: file content is
    /// untrusted). A newline would break the tooltip's line layout and an unbounded length would
    /// stretch it, so control characters become spaces and the description is capped at 200
    /// characters. Asserted through the service, not the parser, so the sanitisation cannot be
    /// bypassed by a future caller that skips it.
    /// </summary>
    [Fact]
    public async Task GetContextWindow_ScriptWithNewlineAndOverlongDescription_ArrivesSingleLineAndCapped()
    {
        var agentFile = ArrangeSubagentFixture(
            assistantTimestamp: DateTimeOffset.UtcNow,
            agentId: "hostile",
            workflowId: "wf_meta-3");
        var description = new string('a', 150) + @"\nsecond line\t" + new string('b', 150);
        ArrangeWorkflowScript(
            agentFile,
            $"export const meta = {{ name: 'nasty', description: '{description}' }}");

        using var svc = BuildService();
        await svc.InitializeAsync();

        var subagent = svc.GetContextWindow(ProjectDirName).Subagents.Single(s => s.AgentId == "hostile");

        Assert.Equal(200, subagent.RunDescription!.Length);
        Assert.All(subagent.RunDescription, c => Assert.False(char.IsControl(c)));
    }

    // -------------------------------------------------------------------------
    // Fixture helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Writes a run's script — which lives in a DIFFERENT tree from the agent transcripts:
    /// {session}/workflows/scripts/{name}-{runId}.js, not {session}/subagents/workflows/{runId}/.
    /// Both trees hold a directory called "workflows"; conflating them is the fixture's version of
    /// the same mistake the production path can make.
    ///
    /// The file name deliberately carries a leading name segment, because production matches the run
    /// id as a SUFFIX and a fixture named plainly "{runId}.js" would not exercise that.
    /// </summary>
    private static void ArrangeWorkflowScript(string workflowAgentFile, string script)
    {
        var runDirectory = Path.GetDirectoryName(workflowAgentFile)!;
        var sessionDirectory = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(runDirectory)))!;
        var scriptsDirectory = Path.Combine(sessionDirectory, WorkflowsDirName, "scripts");
        Directory.CreateDirectory(scriptsDirectory);

        File.WriteAllText(
            Path.Combine(scriptsDirectory, $"some-workflow-{Path.GetFileName(runDirectory)}.js"), script);
    }

    /// <summary>
    /// Writes the completed-run JSON a finished run leaves behind. Only used to prove it is IGNORED
    /// — nothing reads it any more, see GetContextWindow_CompletedRunJsonPresent_IsIgnored.
    /// </summary>
    private static void ArrangeCompletedRunJson(string workflowAgentFile, string name, string summary)
    {
        var runDirectory = Path.GetDirectoryName(workflowAgentFile)!;
        var sessionDirectory = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(runDirectory)))!;
        var metadataDirectory = Path.Combine(sessionDirectory, WorkflowsDirName);
        Directory.CreateDirectory(metadataDirectory);

        var json = JsonSerializer.Serialize(new
        {
            workflowName = name,
            summary,
            startTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            timestamp = DateTimeOffset.UtcNow.ToString("O")
        });

        File.WriteAllText(Path.Combine(metadataDirectory, Path.GetFileName(runDirectory) + ".json"), json);
    }

    /// <summary>
    /// Writes a journal.jsonl next to a workflow agent file: one "started" line per spawned agent
    /// and one "result" line per finished one, in the interleaved order a real run produces.
    /// padToBytes inflates the result payloads to exercise the oversized-journal guard.
    /// </summary>
    private static void ArrangeWorkflowJournal(string workflowAgentFile, int started, int done, int padToBytes = 0)
    {
        var runDir = Path.GetDirectoryName(workflowAgentFile)!;
        var padding = padToBytes > 0 ? new string('x', Math.Max(1, padToBytes / Math.Max(done, 1))) : string.Empty;

        var lines = new List<string>();
        for (var i = 0; i < started; i++)
        {
            lines.Add(JsonSerializer.Serialize(new { type = "started", key = $"v2:{i:x8}", agentId = $"a{i:x4}" }));
            if (i < done)
                lines.Add(JsonSerializer.Serialize(new { type = "result", key = $"v2:{i:x8}", agentId = $"a{i:x4}", result = padding }));
        }

        File.WriteAllLines(Path.Combine(runDir, "journal.jsonl"), lines);
    }

    /// <summary>
    /// Asserts a fixture file's mtime is inside the 30s activity window, so a failure downstream
    /// points at the code rather than at an AV product that touched the file.
    /// </summary>
    private static void AssertMtimeIsFresh(string filePath)
    {
        var age = DateTime.UtcNow - File.GetLastWriteTimeUtc(filePath);
        Assert.True(
            age < TimeSpan.FromSeconds(20),
            $"fixture file should be fresh but its mtime is {age} old — test environment hostile to mtime control (AV likely).");
    }

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
