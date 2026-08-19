using System.Text;
using System.Text.Json;
using CCInfoWindows.Helpers;
using CCInfoWindows.Models;
using CCInfoWindows.Services;
using CCInfoWindows.Services.Interfaces;
using CCInfoWindows.Tests.TestSupport;
using Moq;

namespace CCInfoWindows.Tests.Services;

/// <summary>
/// Unit tests for JsonlService: tail read, tolerant parsing, session discovery,
/// context window calculation (last assistant only), token aggregation with dedup,
/// incremental reads, and cache persistence.
/// </summary>
public class JsonlServiceTests : JsonlServiceTestBase
{
    // -------------------------------------------------------------------------
    // ReadTailLines
    // -------------------------------------------------------------------------

    [Fact]
    public void ReadTailLines_SmallFile_ReturnsAllLines()
    {
        var file = WriteTempFile("line1\nline2\nline3");

        var lines = JsonlService.ReadTailLines(file).ToList();

        Assert.Equal(3, lines.Count);
        Assert.Equal("line1", lines[0]);
        Assert.Equal("line3", lines[2]);
    }

    [Fact]
    public void ReadTailLines_LargeFile_DiscardsFirstPartialLine()
    {
        // Write a file larger than 1MB so the tail seek lands mid-line.
        // The prefix line must be larger than TailWindowBytes (1MB) so that after
        // the seek we land inside the 'A'-line and the partial fragment is discarded.
        const int TailWindowBytes = 1_048_576;
        var sb = new StringBuilder();

        // A single huge line of 'A's that spans more than 1MB
        sb.Append(new string('A', TailWindowBytes + 100));
        sb.Append("\nfirst_complete_line\nlast_complete_line\n");

        var content = sb.ToString();
        var file = WriteTempFile(content);

        var lines = JsonlService.ReadTailLines(file).ToList();

        // The partial line containing 'A's should be discarded; only complete lines returned
        Assert.DoesNotContain(lines, l => l.Contains('A'));
        Assert.Contains("first_complete_line", lines);
        Assert.Contains("last_complete_line", lines);
    }

    [Fact]
    public void ReadTailLines_OpenWithReadWriteShare_DoesNotThrowWhenFileIsHeldOpen()
    {
        var file = WriteTempFile("line1\nline2");

        // Hold the file open for write (simulates Claude Code writing to it)
        using var holder = new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);

        var lines = JsonlService.ReadTailLines(file).ToList();
        Assert.Equal(2, lines.Count);
    }

    // -------------------------------------------------------------------------
    // ParseJsonlEntries (tolerant parsing)
    // -------------------------------------------------------------------------

    [Fact]
    public void ParseJsonlEntries_ValidLines_ReturnsEntries()
    {
        var lines = new[]
        {
            BuildAssistantEntry("session-1", "uuid-1", "req-1", model: "claude-sonnet-4-6", outputTokens: 100),
            BuildAssistantEntry("session-1", "uuid-2", "req-2", model: "claude-sonnet-4-6", outputTokens: 200)
        };

        var entries = JsonlService.ParseJsonlEntries(lines).ToList();

        Assert.Equal(2, entries.Count);
    }

    [Fact]
    public void ParseJsonlEntries_MalformedLines_SkipsWithoutException()
    {
        var lines = new[]
        {
            "{ this is not valid json !!!",
            BuildAssistantEntry("session-1", "uuid-1", "req-1", outputTokens: 50),
            "null",
            "   "
        };

        var entries = JsonlService.ParseJsonlEntries(lines).ToList();

        // Only the one valid entry should be returned
        Assert.Single(entries);
    }

    // -------------------------------------------------------------------------
    // GetContextWindow (last assistant message, not cumulative)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetContextWindow_ReturnsLastAssistantMessageTokens_NotCumulative()
    {
        const string SessionId = "ctx1aaaa-0000-0000-0000-000000000001";

        // Two assistant messages — context window should reflect only the LAST one
        var (_, projectDirName, _) = await SeedSessionAsync(SessionId,
            BuildAssistantEntry(SessionId, "uuid-1", "req-1", inputTokens: 1000, outputTokens: 100),
            BuildAssistantEntry(SessionId, "uuid-2", "req-2", inputTokens: 5000, outputTokens: 200));

        var service = BuildService();
        await service.InitializeAsync();

        var ctx = service.GetContextWindow(projectDirName);

        // Last entry: input_tokens=5000, so TotalTokens = 5000 (not 1000+5000=6000)
        Assert.Equal(5000L, ctx.TotalTokens);
    }

    [Fact]
    public async Task GetContextWindow_IgnoresSidechainMessages()
    {
        const string SessionId = "ctx2aaaa-0000-0000-0000-000000000002";

        var (_, projectDirName, _) = await SeedSessionAsync(SessionId,
            BuildAssistantEntry(SessionId, "uuid-1", "req-1", inputTokens: 3000, outputTokens: 50),
            BuildSidechainAssistantEntry(SessionId, "uuid-2", "req-2", inputTokens: 99000, outputTokens: 10));

        var service = BuildService();
        await service.InitializeAsync();

        var ctx = service.GetContextWindow(projectDirName);

        // Sidechain entry must be ignored — only the first non-sidechain entry
        Assert.Equal(3000L, ctx.TotalTokens);
    }

    [Fact]
    public async Task GetContextWindow_UnknownSession_ReturnsEmpty()
    {
        var service = BuildService();
        await service.InitializeAsync();

        var ctx = service.GetContextWindow("nonexistent-session-id");

        Assert.Equal(ContextWindowData.Empty.TotalTokens, ctx.TotalTokens);
        Assert.Equal(ContextWindowData.Empty.MaxTokens, ctx.MaxTokens);
    }

    /// <summary>
    /// The newest-session-file pointer is captured during the scan, so deleting that file leaves it
    /// naming a path that is gone. Before the guard the next refresh tick threw FileNotFoundException
    /// out of GetContextWindow, App.OnUnhandledException swallowed it as handled, and — because the
    /// pointer was never cleared — every later tick repeated the throw for the process lifetime,
    /// adding a stack trace to crash.log each time.
    /// </summary>
    [Fact]
    public async Task GetContextWindow_NewestSessionFileDeleted_ReturnsEmptyAndDoesNotThrow()
    {
        const string SessionId = "del1aaaa-0000-0000-0000-000000000070";

        var (_, projectDirName, jsonlFile) = await SeedSessionAsync(SessionId,
            BuildAssistantEntry(SessionId, "uuid-1", "req-1", inputTokens: 4000, outputTokens: 100));

        var service = BuildService();
        await service.InitializeAsync();
        Assert.Equal(4000L, service.GetContextWindow(projectDirName).TotalTokens);

        // Stop the watcher first: the deletion must be survivable by GetContextWindow alone, which
        // is the only line of defence when a whole directory is removed or the app was not running.
        service.Stop();
        File.Delete(jsonlFile);

        Assert.Equal(ContextWindowData.Empty.TotalTokens, service.GetContextWindow(projectDirName).TotalTokens);

        // Recreating the file must NOT resurrect the pointer — proof it was cleared rather than
        // merely tolerated, which is what stops the dead read being retried on every tick.
        await File.WriteAllLinesAsync(jsonlFile,
        [
            BuildAssistantEntry(SessionId, "uuid-2", "req-2", inputTokens: 7000, outputTokens: 50)
        ]);
        Assert.Equal(ContextWindowData.Empty.TotalTokens, service.GetContextWindow(projectDirName).TotalTokens);

        // ...and one processed write re-establishes it, so the blank state is transient.
        await service.ProcessFilesForTestAsync([jsonlFile]);
        Assert.Equal(7000L, service.GetContextWindow(projectDirName).TotalTokens);
    }

    /// <summary>
    /// The debounce batch drains and clears the pending set before reading anything, so a file that
    /// cannot be opened must not cost the files behind it in iteration order. The locked file is
    /// first in the batch and stays unread (its appended line is not counted) while the second file
    /// is parsed normally.
    /// </summary>
    [Fact]
    public async Task ProcessFilesForTest_UnreadableFileFirstInBatch_LaterFilesStillParse()
    {
        const string LockedSessionId = "lck1aaaa-0000-0000-0000-000000000071";
        const string ReadableSessionId = "lck2aaaa-0000-0000-0000-000000000072";

        var lockedDir = CreateProjectSessionDir(LockedSessionId);
        var lockedDirName = Path.GetFileName(lockedDir);
        var lockedFile = Path.Combine(lockedDir, $"{LockedSessionId}.jsonl");
        await File.WriteAllLinesAsync(lockedFile,
        [
            BuildAssistantEntry(LockedSessionId, "uuid-locked-1", "req-locked-1", outputTokens: 10)
        ]);

        var service = BuildService();
        await service.InitializeAsync();
        service.Stop();

        // A line the batch would pick up if it could open the file, and a second project that only
        // exists from now on — so both observations depend solely on this one batch.
        await File.AppendAllTextAsync(lockedFile,
            BuildAssistantEntry(LockedSessionId, "uuid-locked-2", "req-locked-2", outputTokens: 20) + "\n");

        var readableDir = CreateProjectSessionDir(ReadableSessionId);
        var readableDirName = Path.GetFileName(readableDir);
        var readableFile = Path.Combine(readableDir, $"{ReadableSessionId}.jsonl");
        await File.WriteAllLinesAsync(readableFile,
        [
            BuildAssistantEntry(ReadableSessionId, "uuid-readable-1", "req-readable-1", outputTokens: 30)
        ]);

        using (new FileStream(lockedFile, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            await service.ProcessFilesForTestAsync([lockedFile, readableFile]);
        }

        Assert.Equal(10L, service.GetTokenSummary(lockedDirName).OutputTokens);
        Assert.Equal(30L, service.GetTokenSummary(readableDirName).OutputTokens);
    }

    // -------------------------------------------------------------------------
    // Lock scope (finding 28)
    // -------------------------------------------------------------------------

    /// <summary>
    /// GetContextWindow used to hold _sessionsLock across its whole body — a tail read of up to 1 MB
    /// plus a subagent directory glob — so the UI thread's first act after every DataUpdated, the
    /// Sessions getter, queued behind the filesystem.
    ///
    /// The pricing lookup is the seam this test parks on: GetMaxContextTokens invokes it from inside
    /// GetContextWindow, after the file read. Parking there reproduces "a context read is in flight"
    /// exactly, with no sleep and without depending on how long a real read happens to take. Against
    /// the pre-fix code the Sessions read below never returns and this fails on its probe timeout;
    /// against the current code it returns while the context read is still parked. The timeouts are
    /// failure detectors only — nothing in the passing path waits on one.
    /// </summary>
    [Fact]
    public async Task Sessions_WhileAContextReadIsInFlight_IsNotBlockedByIt()
    {
        const string SessionId = "lck3aaaa-0000-0000-0000-000000000073";

        // The directory has to exist before the line is built: cwd must name a directory that exists,
        // because RebuildSessionsList drops a session whose non-empty cwd fails IsValidProjectDirectory
        // and the fixture default is a Unix path that does not. CreateProjectSessionDir is idempotent,
        // so the seeding call below reuses this directory.
        var projectDir = CreateProjectSessionDir(SessionId);
        var (_, projectDirName, _) = await SeedSessionAsync(SessionId,
            BuildAssistantEntry(SessionId, "uuid-ctx-1", "req-ctx-1", cwd: projectDir, inputTokens: 5000, outputTokens: 100));

        using var pricingReached = new ManualResetEventSlim(false);
        using var releasePricing = new ManualResetEventSlim(false);

        var service = BuildService(BuildParkingPricingService(pricingReached, releasePricing));
        await service.InitializeAsync();

        // No watcher for the duration of the assertion: a debounce batch is a second writer and would
        // muddy what the probe below is measuring.
        service.Stop();

        var contextRead = Task.Run(() => service.GetContextWindow(projectDirName));

        try
        {
            Assert.True(
                pricingReached.Wait(BlockingProbeTimeout),
                "GetContextWindow never reached the pricing lookup — the seam this test parks on is gone.");

            var sessionsRead = Task.Run(() => service.Sessions.Count);

            Assert.True(
                sessionsRead.Wait(BlockingProbeTimeout),
                "Sessions blocked while a context-window read was in flight — the read holds the lock again.");
            Assert.Equal(1, await sessionsRead);
        }
        finally
        {
            releasePricing.Set();
            // Let the parked call leave the gate before the events it waits on are disposed.
            await Task.WhenAny(contextRead, Task.Delay(BlockingProbeTimeout));
        }

        Assert.Equal(5000L, (await contextRead).TotalTokens);
    }

    /// <summary>
    /// Generous upper bound for "this call should already have returned". Only the failure path ever
    /// waits this long.
    /// </summary>
    private static readonly TimeSpan BlockingProbeTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// A pricing service whose GetPrice parks until released, so a test can hold one JsonlService
    /// operation open at a known point and observe what else can still run.
    /// </summary>
    private static IPricingService BuildParkingPricingService(
        ManualResetEventSlim reached,
        ManualResetEventSlim release)
    {
        var mock = new Mock<IPricingService>();
        mock.Setup(p => p.GetPrice(It.IsAny<string>())).Returns<string>(_ =>
        {
            reached.Set();
            release.Wait();
            return null;
        });
        mock.Setup(p => p.EnsurePricesLoadedAsync()).Returns(Task.CompletedTask);
        mock.SetupGet(p => p.Source).Returns(PricingSource.Unknown);
        mock.SetupGet(p => p.LastFetch).Returns((DateTimeOffset?)null);
        return mock.Object;
    }

    // -------------------------------------------------------------------------
    // GetTokenSummary (output_tokens sum with dedup by message.id + requestId)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetTokenSummary_SumsOutputTokensAcrossAllAssistantMessages()
    {
        const string SessionId = "tok1aaaa-0000-0000-0000-000000000001";

        var (_, projectDirName, _) = await SeedSessionAsync(SessionId,
            BuildAssistantEntry(SessionId, "uuid-1", "req-1", outputTokens: 100),
            BuildAssistantEntry(SessionId, "uuid-2", "req-2", outputTokens: 200),
            BuildAssistantEntry(SessionId, "uuid-3", "req-3", outputTokens: 300));

        var service = BuildService();
        await service.InitializeAsync();

        var summary = service.GetTokenSummary(projectDirName);

        Assert.Equal(600L, summary.OutputTokens);
    }

    [Fact]
    public async Task GetTokenSummary_DeduplicatesByMessageIdAndRequestId()
    {
        const string SessionId = "tok2aaaa-0000-0000-0000-000000000002";
        const string SharedMessageId = "msg_011CdkFMUPJsJvagAd5DXbrB";

        // One assistant message written as two lines: distinct per-line uuids, shared
        // message.id + requestId. This is the shape Claude Code actually produces.
        var (_, projectDirName, _) = await SeedSessionAsync(SessionId,
            BuildAssistantEntry(SessionId, "uuid-1", "req-1", outputTokens: 500, messageId: SharedMessageId),
            BuildAssistantEntry(SessionId, "uuid-2", "req-1", outputTokens: 500, messageId: SharedMessageId));

        var service = BuildService();
        await service.InitializeAsync();

        var summary = service.GetTokenSummary(projectDirName);

        Assert.Equal(500L, summary.OutputTokens);
    }

    /// <summary>
    /// Regression lock for the deduplication blocker (finding 2). Reproduces
    /// msg_011CdkFMUPJsJvagAd5DXbrB from the maintainer's live corpus: four lines, four distinct
    /// uuids, one message.id, every line repeating the identical usage block. Before the fix the
    /// key was read from a uniqueHash field Claude Code never writes, so all four were summed and
    /// every token, statistic and cost figure came out 4x too high for this message.
    /// </summary>
    [Fact]
    public async Task GetStatistics_FourLinesSharingOneMessageId_AreCountedOnce()
    {
        const string SessionId = "tok4aaaa-0000-0000-0000-000000000004";
        const string SharedMessageId = "msg_011CdkFMUPJsJvagAd5DXbrB";
        const string SharedRequestId = "req_011CdkFMRhavpWWeU3wA89TB";
        const long InputTokens = 2L;
        const long OutputTokens = 681L;
        const long CacheCreationTokens = 65_563L;

        var withinCurrentHour = CurrentHourTimestamp(minute: 5);

        var lines = Enumerable.Range(1, 4)
            .Select(lineNumber => BuildAssistantEntry(
                SessionId,
                uuid: $"line-{lineNumber}",
                requestId: SharedRequestId,
                inputTokens: InputTokens,
                outputTokens: OutputTokens,
                cacheCreation: CacheCreationTokens,
                timestamp: withinCurrentHour,
                messageId: SharedMessageId))
            .ToArray();

        var (_, projectDirName, _) = await SeedSessionAsync(SessionId, lines);

        var service = BuildService(BuildNullPricingService());
        await service.InitializeAsync();

        var summary = service.GetTokenSummary(projectDirName);
        var stats = service.GetStatistics(TimePeriod.Session, projectDirName);

        Assert.Equal(InputTokens, summary.InputTokens);
        Assert.Equal(OutputTokens, summary.OutputTokens);
        Assert.Equal(InputTokens, stats.InputTokens);
        Assert.Equal(OutputTokens, stats.OutputTokens);
        Assert.Equal(CacheCreationTokens, stats.CacheCreationTokens);
    }

    /// <summary>
    /// The lines of one message are not always clones: in the live corpus every multi-line group
    /// whose output_tokens differ (670 of 670) carries the completed value on the LAST line, the
    /// earlier ones being partial content blocks. First-seen-wins would keep the stub and report
    /// 1 output token instead of 288, so the later line must supersede the earlier one.
    /// </summary>
    [Fact]
    public async Task GetTokenSummary_PartialThenFinalLineForOneMessage_KeepsFinalOutputTokens()
    {
        const string SessionId = "tok5aaaa-0000-0000-0000-000000000005";
        const string SharedMessageId = "msg_011Cdk9QkpQnEytv4hRV5nPU";
        const long PartialOutputTokens = 1L;
        const long FinalOutputTokens = 288L;

        var (_, projectDirName, _) = await SeedSessionAsync(SessionId,
            BuildAssistantEntry(SessionId, "uuid-partial-1", "req-1", outputTokens: PartialOutputTokens, messageId: SharedMessageId),
            BuildAssistantEntry(SessionId, "uuid-partial-2", "req-1", outputTokens: PartialOutputTokens, messageId: SharedMessageId),
            BuildAssistantEntry(SessionId, "uuid-final", "req-1", outputTokens: FinalOutputTokens, messageId: SharedMessageId));

        var service = BuildService();
        await service.InitializeAsync();

        var summary = service.GetTokenSummary(projectDirName);

        Assert.Equal(FinalOutputTokens, summary.OutputTokens);
    }

    /// <summary>
    /// Distinct messages must still accumulate — a dedup key that collapsed too much would make
    /// every assertion above pass while zeroing the app's actual purpose.
    /// </summary>
    [Fact]
    public async Task GetTokenSummary_DistinctMessageIdsSharingOneRequestId_AreCountedSeparately()
    {
        const string SessionId = "tok6aaaa-0000-0000-0000-000000000006";
        const string SharedRequestId = "req-shared";
        var (_, projectDirName, _) = await SeedSessionAsync(SessionId,
            BuildAssistantEntry(SessionId, "uuid-1", SharedRequestId, outputTokens: 100, messageId: "msg_first"),
            BuildAssistantEntry(SessionId, "uuid-2", SharedRequestId, outputTokens: 200, messageId: "msg_second"));

        var service = BuildService();
        await service.InitializeAsync();

        var summary = service.GetTokenSummary(projectDirName);

        Assert.Equal(300L, summary.OutputTokens);
    }

    [Fact]
    public async Task GetTokenSummary_IgnoresSidechainMessages()
    {
        const string SessionId = "tok3aaaa-0000-0000-0000-000000000003";

        var (_, projectDirName, _) = await SeedSessionAsync(SessionId,
            BuildAssistantEntry(SessionId, "uuid-1", "req-1", outputTokens: 400),
            BuildSidechainAssistantEntry(SessionId, "uuid-2", "req-2", outputTokens: 9999));

        var service = BuildService();
        await service.InitializeAsync();

        var summary = service.GetTokenSummary(projectDirName);

        Assert.Equal(400L, summary.OutputTokens);
    }

    [Fact]
    public async Task GetTokenSummary_UnknownSession_ReturnsEmpty()
    {
        var service = BuildService();
        await service.InitializeAsync();

        var summary = service.GetTokenSummary("nonexistent-session-id");

        Assert.Equal(0L, summary.OutputTokens);
        Assert.Equal(0L, summary.InputTokens);
    }

    // -------------------------------------------------------------------------
    // Session discovery
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Sessions_AfterInitialize_DiscoversSessions()
    {
        const string Session1 = "alpha001-0000-0000-0000-000000000001";
        const string Session2 = "beta0002-0000-0000-0000-000000000002";

        var dir1 = CreateProjectSessionDir(Session1);
        var dir2 = CreateProjectSessionDir(Session2);
        var dirName1 = Path.GetFileName(dir1);
        var dirName2 = Path.GetFileName(dir2);

        var cwd1 = Path.Combine(ProjectsDir, "project-alpha");
        var cwd2 = Path.Combine(ProjectsDir, "project-beta");
        Directory.CreateDirectory(cwd1);
        Directory.CreateDirectory(cwd2);

        CreateSessionFile(Session1, cwd: cwd1);
        CreateSessionFile(Session2, cwd: cwd2);

        var service = BuildService();
        await service.InitializeAsync();

        Assert.Equal(2, service.Sessions.Count);
        var ids = service.Sessions.Select(s => s.Id).ToHashSet();
        Assert.Contains(dirName1, ids);
        Assert.Contains(dirName2, ids);
    }

    [Fact]
    public async Task Sessions_DisplayNameFromCwdField()
    {
        const string SessionId = "aaaaaaaa-0000-0000-0000-000000000010";
        var cwd = Path.Combine(ProjectsDir, "my-awesome-project");
        Directory.CreateDirectory(cwd);

        CreateSessionFile(SessionId, cwd: cwd);

        var service = BuildService();
        await service.InitializeAsync();

        var session = service.Sessions.First();
        Assert.Equal("my-awesome-project", session.DisplayName);
    }

    [Fact]
    public async Task Sessions_SortedByLastActivityDescending()
    {
        const string OlderSession = "older001-0000-0000-0000-000000000020";
        const string NewerSession = "newer002-0000-0000-0000-000000000021";

        var olderTime = DateTimeOffset.UtcNow.AddHours(-2);
        var newerTime = DateTimeOffset.UtcNow.AddMinutes(-5);

        var olderDir = CreateProjectSessionDir(OlderSession);
        var newerDir = CreateProjectSessionDir(NewerSession);
        var olderDirName = Path.GetFileName(olderDir);
        var newerDirName = Path.GetFileName(newerDir);

        var cwd = Path.Combine(ProjectsDir, "shared-project-cwd");
        Directory.CreateDirectory(cwd);

        CreateSessionFile(OlderSession, cwd: cwd, timestamp: olderTime);
        CreateSessionFile(NewerSession, cwd: cwd, timestamp: newerTime);

        var service = BuildService();
        await service.InitializeAsync();

        Assert.Equal(newerDirName, service.Sessions[0].Id);
        Assert.Equal(olderDirName, service.Sessions[1].Id);
    }

    // -------------------------------------------------------------------------
    // Session filtering (orphan detection, SES-01/SES-02)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task BuildSessionList_ExcludesDeletedDirectories()
    {
        const string SurvivingSession = "aaa00001-0000-0000-0000-000000000001";
        const string OrphanSession = "bbb00002-0000-0000-0000-000000000002";

        var realCwdA = Path.Combine(ProjectsDir, "real-project-aaa");
        var realCwdB = Path.Combine(ProjectsDir, "real-project-bbb");
        Directory.CreateDirectory(realCwdA);
        Directory.CreateDirectory(realCwdB);

        CreateSessionFile(SurvivingSession, cwd: realCwdA);
        CreateSessionFile(OrphanSession, cwd: realCwdB);

        Directory.Delete(realCwdB, recursive: true);

        var service = BuildService();
        await service.InitializeAsync();

        Assert.Single(service.Sessions);
        Assert.Contains(service.Sessions, s => s.Id.Contains("aaa00001"[..8]));
    }

    /// <summary>
    /// UNC paths are rejected before the filesystem call: Directory.Exists blocks on SMB name
    /// resolution for tens of seconds when the host is unreachable. The session list is no longer built
    /// under the sessions lock, but it is still built inside a write pass, so the stall would hold up
    /// every subsequent refresh.
    /// </summary>
    [Fact]
    public async Task BuildSessionList_ExcludesUncPaths()
    {
        const string SessionId = "ccc00003-0000-0000-0000-000000000003";
        CreateSessionFile(SessionId, cwd: @"\\server\share\project");

        var service = BuildService();
        await service.InitializeAsync();

        Assert.Empty(service.Sessions);
    }

    /// <summary>
    /// A relative cwd cannot be validated: Directory.Exists would resolve it against the test host's
    /// working directory instead of the one the JSONL was written from. "." is the fixture precisely
    /// because it exists under that wrong base — a validity check that reached the filesystem would
    /// therefore keep the session.
    /// </summary>
    [Fact]
    public async Task BuildSessionList_ExcludesRelativeCwd()
    {
        const string SessionId = "fff00006-0000-0000-0000-000000000006";
        CreateSessionFile(SessionId, cwd: ".");

        var service = BuildService();
        await service.InitializeAsync();

        Assert.Empty(service.Sessions);
    }

    [Fact]
    public async Task BuildSessionList_EmptyCwd_KeepsSessionWithDecodedDisplayName()
    {
        // DROPDOWN-03: sessions with empty cwd are kept when a display name can be derived
        // from the encoded project directory name. The old behaviour (Assert.Empty) described
        // the pre-Phase-25 bug and has been updated to reflect the hardened filter.
        const string SessionId = "ddd00004-0000-0000-0000-000000000004";
        CreateSessionFile(SessionId, cwd: "");

        var service = BuildService();
        await service.InitializeAsync();

        // The project dir is "project-ddd00004" -- DecodeProjectDirectory extracts "ddd00004".
        Assert.Single(service.Sessions);
        Assert.Equal("project-ddd00004", service.Sessions[0].Id);
    }

    [Fact]
    public async Task BuildSubagentContext_ReturnsAlphabeticOrder()
    {
        const string SessionId = "eee00005-0000-0000-0000-000000000005";
        var realCwd = Path.Combine(ProjectsDir, "real-project-eee");
        Directory.CreateDirectory(realCwd);

        CreateSessionFile(SessionId, cwd: realCwd);

        var projectDir = CreateProjectSessionDir(SessionId);
        var subagentDir = Path.Combine(projectDir, "subagents");
        Directory.CreateDirectory(subagentDir);

        var recentTime = DateTimeOffset.UtcNow.AddSeconds(-5);

        await File.WriteAllLinesAsync(Path.Combine(subagentDir, "agent-zebra.jsonl"),
        [
            BuildAssistantEntry(SessionId, "uuid-z", "req-z", inputTokens: 1000, outputTokens: 10, timestamp: recentTime)
        ]);
        await File.WriteAllLinesAsync(Path.Combine(subagentDir, "agent-alpha.jsonl"),
        [
            BuildAssistantEntry(SessionId, "uuid-a", "req-a", inputTokens: 1000, outputTokens: 10, timestamp: recentTime)
        ]);
        await File.WriteAllLinesAsync(Path.Combine(subagentDir, "agent-middle.jsonl"),
        [
            BuildAssistantEntry(SessionId, "uuid-m", "req-m", inputTokens: 1000, outputTokens: 10, timestamp: recentTime)
        ]);

        var service = BuildService();
        await service.InitializeAsync();

        var projectDirName = Path.GetFileName(projectDir);
        var ctx = service.GetContextWindow(projectDirName);

        Assert.Equal(3, ctx.Subagents.Count);
        Assert.Equal("alpha", ctx.Subagents[0].AgentId);
        Assert.Equal("middle", ctx.Subagents[1].AgentId);
        Assert.Equal("zebra", ctx.Subagents[2].AgentId);
    }

    // -------------------------------------------------------------------------
    // Incremental read
    // -------------------------------------------------------------------------

    [Fact]
    public void ReadIncrementalLines_ReturnsOnlyNewLines()
    {
        const string Line1 = "first line";
        const string Line2 = "second line";
        const string Line3 = "third line";

        var file = WriteTempFile($"{Line1}\n{Line2}\n");
        var firstPosition = new FileInfo(file).Length;

        File.AppendAllText(file, $"{Line3}\n");

        var (lines, newPosition) = JsonlService.ReadIncrementalLines(file, firstPosition);

        Assert.Single(lines);
        Assert.Equal(Line3, lines[0]);
        Assert.True(newPosition > firstPosition);
    }

    // -------------------------------------------------------------------------
    // Cache
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Cache_PersistsToConfiguredCacheDirectory()
    {
        const string SessionId = "aaaaaaaa-0000-0000-0000-000000000030";

        CreateSessionFile(SessionId);

        var service = BuildService();
        await service.InitializeAsync();

        Assert.True(File.Exists(Path.Combine(CacheDir, "jsonl-cache.json")));
    }

    /// <summary>
    /// An existing installation's cache was written while every streamed content block counted as
    /// its own assistant message. Its read positions therefore mark lines that were aggregated
    /// under the old semantics, so the schema stamp must reject the whole file and force a full
    /// re-read. Observed through a marker for a path that no longer exists: nothing prunes
    /// _filePositions, so the stale key survives into the next save if — and only if — the file
    /// was adopted.
    /// </summary>
    [Fact]
    public async Task LoadCache_UnstampedLegacyFile_IsDiscardedAndRestamped()
    {
        const string SessionId = "cac1aaaa-0000-0000-0000-000000000060";
        var ghostPath = VanishedJsonlPath();

        WriteCacheFile(CacheDir, JsonSerializer.Serialize(new
        {
            filePositions = BuildGhostPositions(ghostPath)
        }));

        CreateSessionFile(SessionId);
        var sessionFile = Path.Combine(ProjectsDir, "project-" + SessionId[..8], $"{SessionId}.jsonl");

        using var service = BuildService();
        await service.InitializeAsync();

        var saved = ReadCacheFile(CacheDir);
        Assert.Equal(JsonlCache.CurrentSchemaVersion, saved.SchemaVersion);
        Assert.DoesNotContain(ghostPath, saved.FilePositions.Keys);
        Assert.Contains(sessionFile, saved.FilePositions.Keys);
    }

    /// <summary>
    /// Complement to the discard test: a cache already stamped with the current schema is adopted,
    /// so the marker it carries survives. Without this the discard above would also pass for a
    /// version gate that rejected every file unconditionally.
    /// </summary>
    [Fact]
    public async Task LoadCache_CurrentSchemaVersion_IsAdopted()
    {
        const string SessionId = "cac2aaaa-0000-0000-0000-000000000061";
        var ghostPath = VanishedJsonlPath();

        WriteCacheFile(CacheDir, JsonSerializer.Serialize(new
        {
            schemaVersion = JsonlCache.CurrentSchemaVersion,
            filePositions = BuildGhostPositions(ghostPath)
        }));

        CreateSessionFile(SessionId);

        using var service = BuildService();
        await service.InitializeAsync();

        var saved = ReadCacheFile(CacheDir);
        Assert.Equal(JsonlCache.CurrentSchemaVersion, saved.SchemaVersion);
        Assert.Contains(ghostPath, saved.FilePositions.Keys);
    }

    /// <summary>
    /// A path that is never created, so session discovery cannot reintroduce it and only cache
    /// adoption can explain its presence in the saved file.
    /// </summary>
    private static string VanishedJsonlPath() =>
        Path.Combine(Path.GetTempPath(), $"ccinfo-vanished-{Guid.NewGuid():N}", "gone.jsonl");

    private static Dictionary<string, FilePositionMarker> BuildGhostPositions(string ghostPath) =>
        new()
        {
            [ghostPath] = new FilePositionMarker
            {
                LastReadPosition = 4096,
                FileSize = 4096,
                LastWriteTime = DateTimeOffset.UnixEpoch
            }
        };

    private static void WriteCacheFile(string cacheDir, string json) =>
        File.WriteAllText(Path.Combine(cacheDir, "jsonl-cache.json"), json);

    private static JsonlCache ReadCacheFile(string cacheDir)
    {
        var json = File.ReadAllText(Path.Combine(cacheDir, "jsonl-cache.json"));
        return JsonSerializer.Deserialize<JsonlCache>(json)
            ?? throw new InvalidOperationException("Saved jsonl-cache.json did not deserialize.");
    }

    // -------------------------------------------------------------------------
    // Subagent discovery
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetContextWindow_IncludesSubagentData()
    {
        const string SessionId = "aaaaaaaa-0000-0000-0000-000000000040";
        var projectDir = CreateProjectSessionDir(SessionId);
        var projectDirName = Path.GetFileName(projectDir);
        var jsonlFile = Path.Combine(projectDir, $"{SessionId}.jsonl");

        await File.WriteAllLinesAsync(jsonlFile,
        [
            BuildAssistantEntry(SessionId, "uuid-1", "req-1", inputTokens: 2000, outputTokens: 50)
        ]);

        // Create subagent file
        var subagentDir = Path.Combine(projectDir, "subagents");
        Directory.CreateDirectory(subagentDir);
        var agentFile = Path.Combine(subagentDir, "agent-agent-001.jsonl");
        await File.WriteAllLinesAsync(agentFile,
        [
            BuildAssistantEntry(SessionId, "uuid-sub-1", "req-sub-1", inputTokens: 8000, outputTokens: 100)
        ]);

        var service = BuildService();
        await service.InitializeAsync();

        var ctx = service.GetContextWindow(projectDirName);

        Assert.Single(ctx.Subagents);
        Assert.Equal(8000L, ctx.Subagents[0].TotalTokens);
    }

    // -------------------------------------------------------------------------
    // GetStatistics
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetStatistics_Session_ReturnsCorrectTokenAggregation()
    {
        const string SessionId = "aaaaaaaa-0000-0000-0000-000000000050";
        var projectDir = CreateProjectSessionDir(SessionId);
        var projectDirName = Path.GetFileName(projectDir); // "project-aaaaaaaa"
        var jsonlFile = Path.Combine(projectDir, $"{SessionId}.jsonl");

        await File.WriteAllLinesAsync(jsonlFile,
        [
            BuildAssistantEntry(SessionId, "uuid-1", "req-1",
                inputTokens: 500, outputTokens: 100, cacheCreation: 200, cacheRead: 50),
            BuildAssistantEntry(SessionId, "uuid-2", "req-2",
                inputTokens: 300, outputTokens: 80, cacheCreation: 0, cacheRead: 20)
        ]);

        var pricingService = BuildNullPricingService();
        var service = BuildService(pricingService);
        await service.InitializeAsync();

        var stats = service.GetStatistics(TimePeriod.Session, projectDirName);

        Assert.Equal(800L, stats.InputTokens);
        Assert.Equal(180L, stats.OutputTokens);
        Assert.Equal(200L, stats.CacheCreationTokens);
        Assert.Equal(70L, stats.CacheReadTokens);
    }

    [Fact]
    public async Task GetStatistics_Today_FiltersEntriesByTimestamp()
    {
        const string SessionId = "aaaaaaaa-0000-0000-0000-000000000051";
        var projectDir = CreateProjectSessionDir(SessionId);
        var jsonlFile = Path.Combine(projectDir, $"{SessionId}.jsonl");

        var recentTime = TodayLocalTimestamp(minute: 1);
        var oldTime = DateTimeOffset.UtcNow.AddDays(-5);

        await File.WriteAllLinesAsync(jsonlFile,
        [
            BuildAssistantEntry(SessionId, "uuid-1", "req-1", inputTokens: 1000, outputTokens: 100, timestamp: recentTime),
            BuildAssistantEntry(SessionId, "uuid-2", "req-2", inputTokens: 9999, outputTokens: 999, timestamp: oldTime)
        ]);

        var pricingService = BuildNullPricingService();
        var service = BuildService(pricingService);
        await service.InitializeAsync();

        var stats = service.GetStatistics(TimePeriod.Today);

        Assert.Equal(1000L, stats.InputTokens);
        Assert.Equal(100L, stats.OutputTokens);
    }

    [Fact]
    public async Task GetStatistics_DeduplicatesByMessageIdAndRequestIdAcrossProjects()
    {
        const string Session1 = "aaaaaaaa-0000-0000-0000-000000000052";
        const string Session2 = "aaaaaaaa-0000-0000-0000-000000000053";
        const string SharedMessageId = "msg_shared";
        var projectDir1 = CreateProjectSessionDir(Session1);
        var projectDir2 = CreateProjectSessionDir(Session2);

        // Same message.id+requestId under two different project dirs — must only count once
        var today = TodayLocalTimestamp(minute: 1);
        var json1 = BuildAssistantEntry(Session1, "uuid-1", "shared-req", messageId: SharedMessageId,
            inputTokens: 500, outputTokens: 100, timestamp: today);
        var json2 = BuildAssistantEntry(Session2, "uuid-2", "shared-req", messageId: SharedMessageId,
            inputTokens: 500, outputTokens: 100, timestamp: today);

        await File.WriteAllTextAsync(Path.Combine(projectDir1, $"{Session1}.jsonl"), json1 + "\n");
        await File.WriteAllTextAsync(Path.Combine(projectDir2, $"{Session2}.jsonl"), json2 + "\n");

        var pricingService = BuildNullPricingService();
        var service = BuildService(pricingService);
        await service.InitializeAsync();

        var stats = service.GetStatistics(TimePeriod.Today);

        Assert.Equal(500L, stats.InputTokens);
    }

    [Fact]
    public async Task GetStatistics_Session_ReturnsTokenSumsForCurrentHour()
    {
        const string SessionId = "aaaaaaaa-0000-0000-0000-000000000054";
        var projectDir = CreateProjectSessionDir(SessionId);
        var projectDirName = Path.GetFileName(projectDir);
        var jsonlFile = Path.Combine(projectDir, $"{SessionId}.jsonl");

        // Use timestamps guaranteed to be within the current hour
        var safeTimestamp1 = CurrentHourTimestamp(minute: 1);
        var safeTimestamp2 = CurrentHourTimestamp(minute: 2);

        await File.WriteAllLinesAsync(jsonlFile,
        [
            BuildAssistantEntry(SessionId, "uuid-1", "req-1",
                inputTokens: 100, outputTokens: 50, timestamp: safeTimestamp1),
            BuildAssistantEntry(SessionId, "uuid-2", "req-2",
                inputTokens: 200, outputTokens: 80, timestamp: safeTimestamp2)
        ]);

        var pricingService = BuildNullPricingService();
        var service = BuildService(pricingService);
        await service.InitializeAsync();

        var stats = service.GetStatistics(TimePeriod.Session, projectDirName);

        Assert.Equal(300L, stats.InputTokens);
        Assert.Equal(130L, stats.OutputTokens);
    }

    /// <summary>
    /// A local timestamp inside the current clock hour, which is what TimePeriod.Session filters on.
    /// Independent of when in the hour the test runs — the cutoff is the start of the hour.
    /// </summary>
    private static DateTimeOffset CurrentHourTimestamp(int minute)
    {
        var now = DateTimeOffset.Now;
        return new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, minute, 0, now.Offset);
    }

    /// <summary>
    /// A local timestamp just after today's LOCAL midnight, which is the cutoff
    /// BuildTimePeriodStatistics applies for TimePeriod.Today. Anchoring the fixture to
    /// <c>UtcNow.AddHours(-n)</c> instead put it before that cutoff whenever the test ran within
    /// n hours of local midnight east of UTC, so these tests failed between 00:00 and 02:00.
    /// </summary>
    private static DateTimeOffset TodayLocalTimestamp(int minute)
    {
        var now = DateTimeOffset.Now;
        return new DateTimeOffset(now.Date, now.Offset).AddMinutes(minute);
    }

    private static IPricingService BuildNullPricingService()
    {
        var mock = new Mock<IPricingService>();
        mock.Setup(p => p.GetPrice(It.IsAny<string>())).Returns((ModelPricing?)null);
        mock.Setup(p => p.EnsurePricesLoadedAsync()).Returns(Task.CompletedTask);
        mock.SetupGet(p => p.Source).Returns(PricingSource.Unknown);
        mock.SetupGet(p => p.LastFetch).Returns((DateTimeOffset?)null);
        return mock.Object;
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private string WriteTempFile(string content)
    {
        var path = Path.Combine(ProjectsDir, Guid.NewGuid() + ".tmp");
        File.WriteAllText(path, content);
        return path;
    }

    private string CreateProjectSessionDir(string sessionId)
    {
        var projectDir = Path.Combine(ProjectsDir, "project-" + sessionId[..8]);
        Directory.CreateDirectory(projectDir);
        return projectDir;
    }

    /// <summary>
    /// Seeds one project directory holding one session file with the given lines, and returns the
    /// project directory name the query methods take plus the paths some tests still touch. Ten test
    /// methods opened with this same four-statement sequence, which is what made a change to how a
    /// project is seeded a ten-site edit whose missed instance looks like a service bug.
    /// </summary>
    private async Task<(string ProjectDir, string ProjectDirName, string JsonlFile)> SeedSessionAsync(
        string sessionId,
        params string[] lines)
    {
        var projectDir = CreateProjectSessionDir(sessionId);
        var jsonlFile = Path.Combine(projectDir, $"{sessionId}.jsonl");
        await File.WriteAllLinesAsync(jsonlFile, lines);
        return (projectDir, Path.GetFileName(projectDir), jsonlFile);
    }

    private void CreateSessionFile(
        string sessionId,
        string? cwd = null,
        DateTimeOffset? timestamp = null)
    {
        var projectDir = CreateProjectSessionDir(sessionId);
        var jsonlFile = Path.Combine(projectDir, $"{sessionId}.jsonl");
        var line = BuildAssistantEntry(
            sessionId,
            "uuid-" + sessionId[..8],
            "req-" + sessionId[..8],
            cwd: cwd ?? "/home/user/test-project",
            outputTokens: 10,
            timestamp: timestamp ?? DateTimeOffset.UtcNow);
        File.WriteAllText(jsonlFile, line + "\n");
    }

    /// <summary>
    /// Adapts this suite's call shape onto the shared line fixture — see
    /// <see cref="JsonlFixture.AssistantLine"/> for the schema and why it is stated in one place.
    /// </summary>
    private static string BuildAssistantEntry(
        string sessionId,
        string uuid,
        string requestId,
        string? cwd = null,
        string? model = JsonlFixture.DefaultModel,
        long inputTokens = 0,
        long outputTokens = 0,
        long cacheCreation = 0,
        long cacheRead = 0,
        bool isSidechain = false,
        DateTimeOffset? timestamp = null,
        string? messageId = null)
    {
        return JsonlFixture.AssistantLine(
            sessionId,
            uuid,
            requestId,
            cwd: cwd ?? JsonlFixture.DefaultCwd,
            model: model,
            inputTokens: inputTokens,
            outputTokens: outputTokens,
            cacheCreation: cacheCreation,
            cacheRead: cacheRead,
            isSidechain: isSidechain,
            timestamp: timestamp,
            messageId: messageId);
    }

    private static string DefaultMessageId(string uuid) => JsonlFixture.MessageIdFor(uuid);

    private static string BuildSidechainAssistantEntry(
        string sessionId,
        string uuid,
        string requestId,
        long inputTokens = 0,
        long outputTokens = 0)
    {
        return BuildAssistantEntry(sessionId, uuid, requestId,
            inputTokens: inputTokens,
            outputTokens: outputTokens,
            isSidechain: true);
    }
}
