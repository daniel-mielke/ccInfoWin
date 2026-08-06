using System.Text.Json;
using CCInfoWindows.Helpers;
using CCInfoWindows.Services;

namespace CCInfoWindows.Tests.Services;

/// <summary>
/// Regression tests for cold-start session hydration hardening (Phase 25 DROPDOWN-02/03/06).
/// Covers Cwd fallback via DecodeProjectDirectory, softened empty-Cwd filter, deleted-dir filter,
/// and the stream.Position race fix.
/// </summary>
public class JsonlServiceColdStartTests : IDisposable
{
    private const string CacheDirectoryName = "cache";

    private readonly string _tempDir;
    private readonly string _cacheDir;
    private readonly List<JsonlService> _services = [];

    public JsonlServiceColdStartTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "cs-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _cacheDir = Path.Combine(_tempDir, CacheDirectoryName);
        Directory.CreateDirectory(_cacheDir);
    }

    public void Dispose()
    {
        // The in-test Stop() calls do not run when an assertion fails, and a live FileSystemWatcher
        // on _tempDir would then race the delete below and mask the real failure.
        foreach (var service in _services)
            service.Dispose();
        _services.Clear();

        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    // -------------------------------------------------------------------------
    // DROPDOWN-02: Cwd fallback via DecodeProjectDirectory
    // -------------------------------------------------------------------------

    /// <summary>
    /// When no JSONL entry carries a cwd field, JsonlService must derive a display name from the
    /// encoded project directory name via SessionNameHelper.DecodeProjectDirectory, and the session
    /// must appear in Sessions under it. The validity filter in RebuildSessionsList only judges a
    /// NON-empty Cwd; an empty one is kept precisely because there is nothing to judge.
    /// </summary>
    [Fact]
    public async Task ParseFileIntoProject_NoEntryHasCwd_FallsBackToDecodedProjectDirName()
    {
        const string ProjectDirName = "D--myProjects-ccInfoWin";
        var projectDir = CreateProjectSubdir(_tempDir, ProjectDirName);
        var sessionFile = Path.Combine(projectDir, "abc-session.jsonl");
        WriteAssistantJsonlLine(sessionFile, "sess-1", cwd: null, outputTokens: 100);
        WriteAssistantJsonlLine(sessionFile, "sess-1", cwd: null, outputTokens: 200);

        var svc = BuildService(_tempDir);
        await svc.InitializeAsync();

        var session = svc.Sessions.SingleOrDefault(s => s.Id == ProjectDirName);
        Assert.NotNull(session);
        // DecodeProjectDirectory("D--myProjects-ccInfoWin") extracts "ccInfoWin"
        Assert.Equal("ccInfoWin", session!.DisplayName);
        svc.Stop();
    }

    // -------------------------------------------------------------------------
    // DROPDOWN-03: empty Cwd no longer drops session
    // -------------------------------------------------------------------------

    /// <summary>
    /// A project whose entries carry no cwd must remain in the Sessions list when a display name can
    /// still be derived from the projectDirName. Pre-DROPDOWN-03 the filter was
    /// <c>IsValidProjectDirectory(s.Cwd)</c> alone, which returned false for an empty Cwd and dropped
    /// the session; the empty-Cwd clause in front of it is what this test locks.
    /// </summary>
    [Fact]
    public async Task RebuildSessionsList_EmptyCwd_KeepsSessionWhenDisplayNameDerivable()
    {
        const string ProjectDirName = "D--myProjects-ccInfoWin";
        var projectDir = CreateProjectSubdir(_tempDir, ProjectDirName);
        var sessionFile = Path.Combine(projectDir, "xyz.jsonl");
        WriteAssistantJsonlLine(sessionFile, "sess-2", cwd: null, outputTokens: 50);

        var svc = BuildService(_tempDir);
        await svc.InitializeAsync();

        Assert.Contains(svc.Sessions, s => s.Id == ProjectDirName);
        svc.Stop();
    }

    /// <summary>
    /// A project whose Cwd points to a directory that no longer exists must be
    /// dropped from Sessions. The empty-Cwd softening must NOT disable the
    /// deleted-directory filter for projects with a non-empty Cwd.
    /// This test verifies the existing drop-on-deleted-directory behavior is preserved
    /// after the DROPDOWN-03 filter change.
    /// </summary>
    [Fact]
    public async Task RebuildSessionsList_NonEmptyCwdPointingAtDeletedDir_DropsSession()
    {
        const string ProjectDirName = "X--ghostpath";
        var projectDir = CreateProjectSubdir(_tempDir, ProjectDirName);
        var sessionFile = Path.Combine(projectDir, "ghost.jsonl");
        var deadCwd = Path.Combine(Path.GetTempPath(), $"phase25-deleted-{Guid.NewGuid():N}");
        Assert.False(Directory.Exists(deadCwd));
        WriteAssistantJsonlLine(sessionFile, "sess-3", cwd: deadCwd, outputTokens: 10);

        var svc = BuildService(_tempDir);
        await svc.InitializeAsync();

        Assert.DoesNotContain(svc.Sessions, s => s.Id == ProjectDirName);
        svc.Stop();
    }

    // -------------------------------------------------------------------------
    // DROPDOWN-06: stream.Position race fix
    // -------------------------------------------------------------------------

    /// <summary>
    /// Lines appended to a JSONL file between the first full read and the second
    /// incremental read must NOT be silently dropped.
    /// This test verifies that stream.Position (not stream.Length) is used as the
    /// end-position after a full read, so the subsequent incremental read correctly
    /// picks up lines written after the initial parse completes.
    /// The sequential append-then-refresh pattern exercises the most common
    /// real-world form of the race: Claude Code appends entries while CCInfoWindows
    /// is between two refresh cycles.
    /// </summary>
    [Fact]
    public async Task ParseFileIntoProject_LinesWrittenDuringRace_AreNotSilentlyDropped()
    {
        const string ProjectDirName = "R--race";
        var projectDir = CreateProjectSubdir(_tempDir, ProjectDirName);
        var sessionFile = Path.Combine(projectDir, "race.jsonl");

        // Write 3 lines before first refresh
        for (var i = 0; i < 3; i++)
            WriteAssistantJsonlLine(sessionFile, "sess-r", cwd: null, outputTokens: 1);

        var svc = BuildService(_tempDir);

        // First full read (arms the file-position marker)
        await svc.InitializeAsync();

        // Append 2 more lines AFTER first read -- simulates Claude Code writing during
        // the window between two refresh cycles. Both stream.Length and stream.Position
        // should equal the end of the 3-line content here, so the incremental read
        // starting from that position should pick up the 2 new lines.
        for (var i = 0; i < 2; i++)
            WriteAssistantJsonlLine(sessionFile, "sess-r", cwd: null, outputTokens: 1);

        // Second incremental read via test seam -- mirrors the FileSystemWatcher debounce
        // path (incremental, not forceFullRead). Must pick up the 2 new lines.
        await svc.ProcessFilesForTestAsync([sessionFile]);

        // Total token output == 5 (5 lines x outputTokens=1 each), confirming all 5 entries parsed
        var session = svc.Sessions.SingleOrDefault(s => s.Id == ProjectDirName);
        Assert.NotNull(session);

        // Use the internal test seam to verify total entry count
        Assert.Equal(5, GetEntryCountForProject(svc, ProjectDirName));
        svc.Stop();
    }

    // -------------------------------------------------------------------------
    // Deduplication across two reads (finding 2)
    // -------------------------------------------------------------------------

    /// <summary>
    /// A streamed assistant message can straddle two refresh cycles: the first read sees a partial
    /// content block (output_tokens 1), the incremental read that follows sees the completed line
    /// for the SAME message.id. The result must be one entry carrying the final token count — not
    /// two entries, and not the stub frozen in place. Skipping the repeat instead of superseding it
    /// would report 1 output token for a 300-token answer, forever.
    /// </summary>
    [Fact]
    public async Task ProcessFilesForTest_FinalLineOfAMessageArrivesLater_SupersedesThePartialLine()
    {
        const string ProjectDirName = "S--supersede";
        const string SharedMessageId = "msg_011Cdk9QkpQnEytv4hRV5nPU";
        const long PartialOutputTokens = 1L;
        const long FinalOutputTokens = 300L;

        var projectDir = CreateProjectSubdir(_tempDir, ProjectDirName);
        var sessionFile = Path.Combine(projectDir, "streamed.jsonl");
        WriteAssistantJsonlLine(sessionFile, "sess-s", cwd: null, PartialOutputTokens, SharedMessageId);

        var svc = BuildService(_tempDir);
        await svc.InitializeAsync();

        Assert.Equal(PartialOutputTokens, svc.GetTokenSummary(ProjectDirName).OutputTokens);

        WriteAssistantJsonlLine(sessionFile, "sess-s", cwd: null, FinalOutputTokens, SharedMessageId);
        await svc.ProcessFilesForTestAsync([sessionFile]);

        Assert.Equal(1, GetEntryCountForProject(svc, ProjectDirName));
        Assert.Equal(FinalOutputTokens, svc.GetTokenSummary(ProjectDirName).OutputTokens);
        svc.Stop();
    }

    /// <summary>
    /// Two distinct messages must still accumulate across an incremental read — proof that the
    /// supersede path keys on message identity and not on "anything seen in this file before".
    /// </summary>
    [Fact]
    public async Task ProcessFilesForTest_DistinctMessageArrivesLater_IsAddedNotSuperseded()
    {
        const string ProjectDirName = "S--append";
        const long FirstOutputTokens = 40L;
        const long SecondOutputTokens = 60L;

        var projectDir = CreateProjectSubdir(_tempDir, ProjectDirName);
        var sessionFile = Path.Combine(projectDir, "appended.jsonl");
        WriteAssistantJsonlLine(sessionFile, "sess-a", cwd: null, FirstOutputTokens, "msg_first");

        var svc = BuildService(_tempDir);
        await svc.InitializeAsync();

        WriteAssistantJsonlLine(sessionFile, "sess-a", cwd: null, SecondOutputTokens, "msg_second");
        await svc.ProcessFilesForTestAsync([sessionFile]);

        Assert.Equal(2, GetEntryCountForProject(svc, ProjectDirName));
        Assert.Equal(
            FirstOutputTokens + SecondOutputTokens,
            svc.GetTokenSummary(ProjectDirName).OutputTokens);
        svc.Stop();
    }

    // -------------------------------------------------------------------------
    // Test seam
    // -------------------------------------------------------------------------

    private static int GetEntryCountForProject(JsonlService svc, string projectDirName)
        => svc.GetEntryCountForProject(projectDirName);

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static string CreateProjectSubdir(string root, string projectDirName)
    {
        var path = Path.Combine(root, projectDirName);
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>
    /// Appends one JSONL assistant entry line to filePath.
    /// When cwd is null the key is omitted entirely, reproducing entries from
    /// projects where Claude Code never writes the cwd field.
    /// The line shape mirrors real Claude Code output: a per-line uuid plus a message.id that
    /// identifies the assistant message. Passing messageId makes several lines belong to one
    /// message, which is how a streamed response is written. There is no uniqueHash key —
    /// Claude Code never writes one.
    /// </summary>
    private static void WriteAssistantJsonlLine(
        string filePath,
        string sessionId,
        string? cwd,
        long outputTokens,
        string? messageId = null)
    {
        var uuid = Guid.NewGuid().ToString();
        var resolvedMessageId = messageId ?? $"msg_{Guid.NewGuid():N}";

        // Measured on the live corpus (6,941 usage-bearing lines, 3,322 distinct message.id): of the 2,552 ids
        // written on more than one line, zero span more than one requestId. requestId is a function of message.id,
        // so lines sharing a message must share the request id or the fixture invents a shape that never occurs.
        var requestId = $"req_{resolvedMessageId}";
        var message = new
        {
            id = resolvedMessageId,
            model = "claude-sonnet-4-20250514",
            usage = new
            {
                input_tokens = 10,
                output_tokens = outputTokens,
                cache_read_input_tokens = 0,
                cache_creation_input_tokens = 0
            }
        };
        var timestamp = DateTimeOffset.UtcNow.ToString("O");

        var line = cwd is null
            ? JsonSerializer.Serialize(new
            {
                uuid,
                requestId,
                sessionId,
                timestamp,
                isSidechain = false,
                type = "assistant",
                message
            })
            : JsonSerializer.Serialize(new
            {
                uuid,
                requestId,
                sessionId,
                cwd,
                timestamp,
                isSidechain = false,
                type = "assistant",
                message
            });

        File.AppendAllText(filePath, line + "\n");
    }

    /// <summary>
    /// The cache override is mandatory, not cosmetic: without it JsonlService writes
    /// jsonl-cache.json to the real %LOCALAPPDATA%\CCInfoWindows and the suite overwrites the
    /// developer's live cache.
    /// </summary>
    private JsonlService BuildService(string projectsRoot)
    {
        var service = new JsonlService(
            projectsDirectoryOverride: projectsRoot,
            cacheDirectoryOverride: _cacheDir);
        _services.Add(service);
        return service;
    }
}
