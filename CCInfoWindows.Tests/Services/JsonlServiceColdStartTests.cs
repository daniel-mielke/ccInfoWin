using CCInfoWindows.Services;
using CCInfoWindows.Tests.Helpers;
using CCInfoWindows.Tests.TestSupport;

namespace CCInfoWindows.Tests.Services;

/// <summary>
/// Regression tests for cold-start session hydration hardening (Phase 25 DROPDOWN-02/03/06).
/// Covers Cwd fallback via DecodeProjectDirectory, softened empty-Cwd filter, deleted-dir filter,
/// and the stream.Position race fix.
/// </summary>
public class JsonlServiceColdStartTests : JsonlServiceTestBase
{
    /// <summary>The model these fixtures name, kept distinct from the shared fixture default.</summary>
    private const string FixtureModel = "claude-sonnet-4-20250514";

    public JsonlServiceColdStartTests() : base("cs-tests-")
    {
    }

    // -------------------------------------------------------------------------
    // DROPDOWN-02: Cwd fallback via DecodeProjectDirectory
    // -------------------------------------------------------------------------

    /// <summary>
    /// When no JSONL entry carries a cwd field, JsonlService must derive a display name from the
    /// encoded project directory name via SessionNameHelper.DecodeProjectDirectory, and the session
    /// must appear in Sessions under it. The validity filter in BuildSessionList only judges a
    /// NON-empty Cwd; an empty one is kept precisely because there is nothing to judge.
    /// </summary>
    [Fact]
    public async Task ApplyFileSlice_NoEntryHasCwd_FallsBackToDecodedProjectDirName()
    {
        const string ProjectDirName = "D--myProjects-ccInfoWin";
        var projectDir = CreateProjectSubdir(ProjectsDir, ProjectDirName);
        var sessionFile = Path.Combine(projectDir, "abc-session.jsonl");
        WriteAssistantJsonlLine(sessionFile, "sess-1", cwd: null, outputTokens: 100);
        WriteAssistantJsonlLine(sessionFile, "sess-1", cwd: null, outputTokens: 200);

        var svc = BuildService();
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
    public async Task BuildSessionList_EmptyCwd_KeepsSessionWhenDisplayNameDerivable()
    {
        const string ProjectDirName = "D--myProjects-ccInfoWin";
        var projectDir = CreateProjectSubdir(ProjectsDir, ProjectDirName);
        var sessionFile = Path.Combine(projectDir, "xyz.jsonl");
        WriteAssistantJsonlLine(sessionFile, "sess-2", cwd: null, outputTokens: 50);

        var svc = BuildService();
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
    public async Task BuildSessionList_NonEmptyCwdPointingAtDeletedDir_DropsSession()
    {
        const string ProjectDirName = "X--ghostpath";
        var projectDir = CreateProjectSubdir(ProjectsDir, ProjectDirName);
        var sessionFile = Path.Combine(projectDir, "ghost.jsonl");
        var deadCwd = Path.Combine(Path.GetTempPath(), $"phase25-deleted-{Guid.NewGuid():N}");
        Assert.False(Directory.Exists(deadCwd));
        WriteAssistantJsonlLine(sessionFile, "sess-3", cwd: deadCwd, outputTokens: 10);

        var svc = BuildService();
        await svc.InitializeAsync();

        Assert.DoesNotContain(svc.Sessions, s => s.Id == ProjectDirName);
        svc.Stop();
    }

    // -------------------------------------------------------------------------
    // DROPDOWN-06: stream.Position race fix
    // -------------------------------------------------------------------------

    /// <summary>
    /// The plumbing half: whatever offset the cold-start read stored, the incremental read that follows
    /// resumes from it and adds the lines appended in between. Both appends happen strictly between the
    /// two passes, so <c>stream.Position</c> and <c>stream.Length</c> name the same offset here and this
    /// test cannot distinguish them — the race itself is covered by
    /// <see cref="ReadLinesToEnd_FileGrowsAsTheReaderReachesEndOfFile_ReturnsTheConsumedOffsetNotTheGrownLength"/>.
    /// Renamed from ReadFileSlice_LinesWrittenDuringRace_AreNotSilentlyDropped, which claimed the race.
    /// </summary>
    [Fact]
    public async Task ProcessFilesForTest_LinesAppendedBetweenTwoPasses_ArePickedUpByTheIncrementalRead()
    {
        const string ProjectDirName = "R--race";
        const int LinesBeforeFirstPass = 3;
        const int LinesAppendedBetweenPasses = 2;

        var projectDir = CreateProjectSubdir(ProjectsDir, ProjectDirName);
        var sessionFile = Path.Combine(projectDir, "race.jsonl");

        for (var i = 0; i < LinesBeforeFirstPass; i++)
            WriteAssistantJsonlLine(sessionFile, "sess-r", cwd: null, outputTokens: 1);

        var svc = BuildService();

        // First full read (arms the file-position marker)
        await svc.InitializeAsync();

        for (var i = 0; i < LinesAppendedBetweenPasses; i++)
            WriteAssistantJsonlLine(sessionFile, "sess-r", cwd: null, outputTokens: 1);

        // Mirrors the FileSystemWatcher debounce path (incremental, not forceFullRead).
        await svc.ProcessFilesForTestAsync([sessionFile]);

        var session = svc.Sessions.SingleOrDefault(s => s.Id == ProjectDirName);
        Assert.NotNull(session);

        Assert.Equal(
            LinesBeforeFirstPass + LinesAppendedBetweenPasses,
            GetEntryCountForProject(svc, ProjectDirName));
        svc.Stop();
    }

    /// <summary>
    /// The DROPDOWN-06 race itself, at the only instant it is observable: the bytes land after the reader
    /// has reported end-of-file and before the resume offset is captured. From that point on
    /// <c>stream.Position</c> (the bytes this pass consumed) and <c>stream.Length</c> (the file as it now
    /// is) disagree, and only Position can resume without skipping the appended lines.
    ///
    /// Appending after the pass has finished — what the sibling test above does — cannot reproduce it,
    /// because both values then name the same offset; appending while lines are still being consumed
    /// cannot either, because the reader picks the new bytes up in the same pass. Hence the stream seam:
    /// <see cref="ControllableStreamProxy"/> injects at exactly the EOF boundary.
    /// </summary>
    [Fact]
    public void ReadLinesToEnd_FileGrowsAsTheReaderReachesEndOfFile_ReturnsTheConsumedOffsetNotTheGrownLength()
    {
        const int LinesBeforeTheRead = 3;
        const int LinesAppendedAtEndOfFile = 2;

        var sessionFile = Path.Combine(ProjectsDir, "midread.jsonl");
        for (var i = 0; i < LinesBeforeTheRead; i++)
            WriteAssistantJsonlLine(sessionFile, "sess-m", cwd: null, outputTokens: 1);

        var consumedBytes = new FileInfo(sessionFile).Length;

        // Ownership passes to the proxy, which disposes the inner stream — hence no second using here.
        var stream = new FileStream(sessionFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var proxy = new ControllableStreamProxy(stream)
        {
            OnEndOfStream = () =>
            {
                for (var i = 0; i < LinesAppendedAtEndOfFile; i++)
                    WriteAssistantJsonlLine(sessionFile, "sess-m", cwd: null, outputTokens: 1);
            }
        };

        var (lines, endPosition) = JsonlService.ReadLinesToEnd(proxy);

        Assert.Equal(LinesBeforeTheRead, lines.Count);
        Assert.Equal(consumedBytes, endPosition);
        // Guards the assertion above against passing vacuously: if the append were invisible to Length,
        // Position and Length would agree and returning either would look correct.
        Assert.True(
            proxy.Length > endPosition,
            $"the append must be visible to Length ({proxy.Length}) but not to the resume offset ({endPosition})");
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

        var projectDir = CreateProjectSubdir(ProjectsDir, ProjectDirName);
        var sessionFile = Path.Combine(projectDir, "streamed.jsonl");
        WriteAssistantJsonlLine(sessionFile, "sess-s", cwd: null, PartialOutputTokens, SharedMessageId);

        var svc = BuildService();
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

        var projectDir = CreateProjectSubdir(ProjectsDir, ProjectDirName);
        var sessionFile = Path.Combine(projectDir, "appended.jsonl");
        WriteAssistantJsonlLine(sessionFile, "sess-a", cwd: null, FirstOutputTokens, "msg_first");

        var svc = BuildService();
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
    // Publication of the cold-start scan (finding 28)
    // -------------------------------------------------------------------------

    /// <summary>
    /// The DataUpdated contract the dashboard depends on, in both directions. The FIRST event arrives
    /// while IsScanning is true: it is the only thing that makes the scanning indicator visible, because
    /// MainViewModel samples IsScanning before awaiting InitializeAsync, when the flag is still clear.
    /// The LAST event arrives with IsScanning false AND the scanned sessions already published, so the
    /// refresh it triggers renders a populated picker instead of an empty one.
    ///
    /// Both halves are load-bearing: the review proposed moving the pre-scan raise to after the scan,
    /// which would silently retire the indicator. The freeze it was blamed for came from the Sessions
    /// getter blocking on the lock the scan held, and that is fixed at the getter.
    /// </summary>
    [Fact]
    public async Task InitializeAsync_RaisesWhileScanningFirstAndWithPublishedSessionsLast()
    {
        const string ProjectDirName = "P--publishorder";
        var projectDir = CreateProjectSubdir(ProjectsDir, ProjectDirName);
        WriteAssistantJsonlLine(Path.Combine(projectDir, "order.jsonl"), "sess-o", cwd: null, outputTokens: 5);

        var svc = BuildService();
        var observed = new List<(bool IsScanning, int SessionCount)>();
        svc.DataUpdated += (_, _) => observed.Add((svc.IsScanning, svc.Sessions.Count));

        await svc.InitializeAsync();
        svc.Stop();

        Assert.Equal(2, observed.Count);
        Assert.True(observed[0].IsScanning, "The first event must report a scan in progress.");
        Assert.False(observed[^1].IsScanning);
        Assert.Equal(1, observed[^1].SessionCount);
    }

    /// <summary>
    /// The cold-start pass builds a private graph and swaps it in, so running it twice must land on the
    /// same numbers. A swap that merged into the previous graph instead of replacing it would count every
    /// entry twice for any line without a deduplication key. Documents the swap rather than catching the
    /// old defect: supersede-by-identity already made the in-place merge idempotent for real Claude Code
    /// lines, which is exactly why the swap is safe to make.
    /// </summary>
    [Fact]
    public async Task InitializeAsync_RunTwice_PublishesTheSameAggregates()
    {
        const string ProjectDirName = "P--idempotent";
        const long FirstOutputTokens = 70L;
        const long SecondOutputTokens = 30L;

        var projectDir = CreateProjectSubdir(ProjectsDir, ProjectDirName);
        var sessionFile = Path.Combine(projectDir, "twice.jsonl");
        WriteAssistantJsonlLine(sessionFile, "sess-i", cwd: null, FirstOutputTokens);
        WriteAssistantJsonlLine(sessionFile, "sess-i", cwd: null, SecondOutputTokens);

        var svc = BuildService();
        await svc.InitializeAsync();

        Assert.Single(svc.Sessions);
        Assert.Equal(2, GetEntryCountForProject(svc, ProjectDirName));
        Assert.Equal(FirstOutputTokens + SecondOutputTokens, svc.GetTokenSummary(ProjectDirName).OutputTokens);

        await svc.InitializeAsync();
        svc.Stop();

        Assert.Single(svc.Sessions);
        Assert.Equal(2, GetEntryCountForProject(svc, ProjectDirName));
        Assert.Equal(FirstOutputTokens + SecondOutputTokens, svc.GetTokenSummary(ProjectDirName).OutputTokens);
    }

    /// <summary>
    /// Stop cancels the scan token, so the source has to be replaced on the next Initialize. Cheap to
    /// get wrong: one CancellationTokenSource for the service's lifetime passes every other test in this
    /// file and leaves the dashboard permanently empty after the first Settings round-trip, which tears
    /// the singleton down and re-initializes it. The second project is created while the service is
    /// stopped, so only a scan that really ran can find it.
    /// </summary>
    [Fact]
    public async Task InitializeAsync_AfterAStop_ScansAgain()
    {
        var firstDir = CreateProjectSubdir(ProjectsDir, "P--restart");
        WriteAssistantJsonlLine(Path.Combine(firstDir, "first.jsonl"), "sess-r2", cwd: null, outputTokens: 12);

        var svc = BuildService();
        await svc.InitializeAsync();
        Assert.Single(svc.Sessions);

        svc.Stop();

        var laterDir = CreateProjectSubdir(ProjectsDir, "P--restart-second");
        WriteAssistantJsonlLine(Path.Combine(laterDir, "later.jsonl"), "sess-r3", cwd: null, outputTokens: 8);

        await svc.InitializeAsync();
        svc.Stop();

        Assert.Equal(2, svc.Sessions.Count);
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
    /// Appends one JSONL assistant entry line to filePath. A null cwd omits the key entirely,
    /// reproducing entries from projects where Claude Code never writes the cwd field — see
    /// <see cref="JsonlFixture.AssistantLine"/> for the shape.
    /// </summary>
    private static void WriteAssistantJsonlLine(
        string filePath,
        string sessionId,
        string? cwd,
        long outputTokens,
        string? messageId = null)
    {
        var resolvedMessageId = messageId ?? $"msg_{Guid.NewGuid():N}";

        // Measured on the live corpus (6,941 usage-bearing lines, 3,322 distinct message.id): of the 2,552 ids
        // written on more than one line, zero span more than one requestId. requestId is a function of message.id,
        // so lines sharing a message must share the request id or the fixture invents a shape that never occurs.
        var line = JsonlFixture.AssistantLine(
            sessionId,
            uuid: Guid.NewGuid().ToString(),
            requestId: $"req_{resolvedMessageId}",
            cwd: cwd,
            model: FixtureModel,
            inputTokens: 10,
            outputTokens: outputTokens,
            messageId: resolvedMessageId);

        JsonlFixture.AppendLine(filePath, line);
    }
}
