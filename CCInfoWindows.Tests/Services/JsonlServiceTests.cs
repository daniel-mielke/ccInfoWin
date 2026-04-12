using System.Text;
using System.Text.Json;
using CCInfoWindows.Helpers;
using CCInfoWindows.Models;
using CCInfoWindows.Services;
using CCInfoWindows.Services.Interfaces;
using Moq;

namespace CCInfoWindows.Tests.Services;

/// <summary>
/// Unit tests for JsonlService: tail read, tolerant parsing, session discovery,
/// context window calculation (last assistant only), token aggregation with dedup,
/// incremental reads, and cache persistence.
/// </summary>
public class JsonlServiceTests : IDisposable
{
    private readonly string _tempDir;

    public JsonlServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

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
        var projectDir = CreateProjectSessionDir(SessionId);
        var projectDirName = Path.GetFileName(projectDir);
        var jsonlFile = Path.Combine(projectDir, $"{SessionId}.jsonl");

        // Two assistant messages — context window should reflect only the LAST one
        await File.WriteAllLinesAsync(jsonlFile,
        [
            BuildAssistantEntry(SessionId, "uuid-1", "req-1", inputTokens: 1000, outputTokens: 100),
            BuildAssistantEntry(SessionId, "uuid-2", "req-2", inputTokens: 5000, outputTokens: 200)
        ]);

        var service = new JsonlService(_tempDir);
        await service.InitializeAsync();

        var ctx = service.GetContextWindow(projectDirName);

        // Last entry: input_tokens=5000, so TotalTokens = 5000 (not 1000+5000=6000)
        Assert.Equal(5000L, ctx.TotalTokens);
    }

    [Fact]
    public async Task GetContextWindow_IgnoresSidechainMessages()
    {
        const string SessionId = "ctx2aaaa-0000-0000-0000-000000000002";
        var projectDir = CreateProjectSessionDir(SessionId);
        var projectDirName = Path.GetFileName(projectDir);
        var jsonlFile = Path.Combine(projectDir, $"{SessionId}.jsonl");

        await File.WriteAllLinesAsync(jsonlFile,
        [
            BuildAssistantEntry(SessionId, "uuid-1", "req-1", inputTokens: 3000, outputTokens: 50),
            BuildSidechainAssistantEntry(SessionId, "uuid-2", "req-2", inputTokens: 99000, outputTokens: 10)
        ]);

        var service = new JsonlService(_tempDir);
        await service.InitializeAsync();

        var ctx = service.GetContextWindow(projectDirName);

        // Sidechain entry must be ignored — only the first non-sidechain entry
        Assert.Equal(3000L, ctx.TotalTokens);
    }

    [Fact]
    public async Task GetContextWindow_UnknownSession_ReturnsEmpty()
    {
        var service = new JsonlService(_tempDir);
        await service.InitializeAsync();

        var ctx = service.GetContextWindow("nonexistent-session-id");

        Assert.Equal(ContextWindowData.Empty.TotalTokens, ctx.TotalTokens);
        Assert.Equal(ContextWindowData.Empty.MaxTokens, ctx.MaxTokens);
    }

    // -------------------------------------------------------------------------
    // GetTokenSummary (output_tokens sum with dedup by uuid+requestId)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetTokenSummary_SumsOutputTokensAcrossAllAssistantMessages()
    {
        const string SessionId = "tok1aaaa-0000-0000-0000-000000000001";
        var projectDir = CreateProjectSessionDir(SessionId);
        var projectDirName = Path.GetFileName(projectDir);
        var jsonlFile = Path.Combine(projectDir, $"{SessionId}.jsonl");

        await File.WriteAllLinesAsync(jsonlFile,
        [
            BuildAssistantEntry(SessionId, "uuid-1", "req-1", outputTokens: 100),
            BuildAssistantEntry(SessionId, "uuid-2", "req-2", outputTokens: 200),
            BuildAssistantEntry(SessionId, "uuid-3", "req-3", outputTokens: 300)
        ]);

        var service = new JsonlService(_tempDir);
        await service.InitializeAsync();

        var summary = service.GetTokenSummary(projectDirName);

        Assert.Equal(600L, summary.OutputTokens);
    }

    [Fact]
    public async Task GetTokenSummary_DeduplicatesByUuidAndRequestId()
    {
        const string SessionId = "tok2aaaa-0000-0000-0000-000000000002";
        var projectDir = CreateProjectSessionDir(SessionId);
        var projectDirName = Path.GetFileName(projectDir);
        var jsonlFile = Path.Combine(projectDir, $"{SessionId}.jsonl");

        // Same uuid+requestId appears twice — should only be counted once
        await File.WriteAllLinesAsync(jsonlFile,
        [
            BuildAssistantEntry(SessionId, "uuid-1", "req-1", outputTokens: 500),
            BuildAssistantEntry(SessionId, "uuid-1", "req-1", outputTokens: 500)
        ]);

        var service = new JsonlService(_tempDir);
        await service.InitializeAsync();

        var summary = service.GetTokenSummary(projectDirName);

        Assert.Equal(500L, summary.OutputTokens);
    }

    [Fact]
    public async Task GetTokenSummary_IgnoresSidechainMessages()
    {
        const string SessionId = "tok3aaaa-0000-0000-0000-000000000003";
        var projectDir = CreateProjectSessionDir(SessionId);
        var projectDirName = Path.GetFileName(projectDir);
        var jsonlFile = Path.Combine(projectDir, $"{SessionId}.jsonl");

        await File.WriteAllLinesAsync(jsonlFile,
        [
            BuildAssistantEntry(SessionId, "uuid-1", "req-1", outputTokens: 400),
            BuildSidechainAssistantEntry(SessionId, "uuid-2", "req-2", outputTokens: 9999)
        ]);

        var service = new JsonlService(_tempDir);
        await service.InitializeAsync();

        var summary = service.GetTokenSummary(projectDirName);

        Assert.Equal(400L, summary.OutputTokens);
    }

    [Fact]
    public async Task GetTokenSummary_UnknownSession_ReturnsEmpty()
    {
        var service = new JsonlService(_tempDir);
        await service.InitializeAsync();

        var summary = service.GetTokenSummary("nonexistent-session-id");

        Assert.Equal(0L, summary.OutputTokens);
        Assert.Equal(0L, summary.InputTokens);
    }

    // -------------------------------------------------------------------------
    // DiscoverSessions
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

        var cwd1 = Path.Combine(_tempDir, "project-alpha");
        var cwd2 = Path.Combine(_tempDir, "project-beta");
        Directory.CreateDirectory(cwd1);
        Directory.CreateDirectory(cwd2);

        CreateSessionFile(Session1, cwd: cwd1);
        CreateSessionFile(Session2, cwd: cwd2);

        var service = new JsonlService(_tempDir);
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
        var cwd = Path.Combine(_tempDir, "my-awesome-project");
        Directory.CreateDirectory(cwd);

        CreateSessionFile(SessionId, cwd: cwd);

        var service = new JsonlService(_tempDir);
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

        var cwd = Path.Combine(_tempDir, "shared-project-cwd");
        Directory.CreateDirectory(cwd);

        CreateSessionFile(OlderSession, cwd: cwd, timestamp: olderTime);
        CreateSessionFile(NewerSession, cwd: cwd, timestamp: newerTime);

        var service = new JsonlService(_tempDir);
        await service.InitializeAsync();

        Assert.Equal(newerDirName, service.Sessions[0].Id);
        Assert.Equal(olderDirName, service.Sessions[1].Id);
    }

    // -------------------------------------------------------------------------
    // Session filtering (orphan detection, SES-01/SES-02)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RebuildSessionsList_ExcludesDeletedDirectories()
    {
        const string SurvivingSession = "aaa00001-0000-0000-0000-000000000001";
        const string OrphanSession = "bbb00002-0000-0000-0000-000000000002";

        var realCwdA = Path.Combine(_tempDir, "real-project-aaa");
        var realCwdB = Path.Combine(_tempDir, "real-project-bbb");
        Directory.CreateDirectory(realCwdA);
        Directory.CreateDirectory(realCwdB);

        CreateSessionFile(SurvivingSession, cwd: realCwdA);
        CreateSessionFile(OrphanSession, cwd: realCwdB);

        Directory.Delete(realCwdB, recursive: true);

        var service = new JsonlService(_tempDir);
        await service.InitializeAsync();

        Assert.Single(service.Sessions);
        Assert.Contains(service.Sessions, s => s.Id.Contains("aaa00001"[..8]));
    }

    [Fact]
    public async Task RebuildSessionsList_ExcludesUncPaths()
    {
        const string SessionId = "ccc00003-0000-0000-0000-000000000003";
        CreateSessionFile(SessionId, cwd: @"\\server\share\project");

        var service = new JsonlService(_tempDir);
        await service.InitializeAsync();

        Assert.Empty(service.Sessions);
    }

    [Fact]
    public async Task RebuildSessionsList_ExcludesEmptyCwd()
    {
        const string SessionId = "ddd00004-0000-0000-0000-000000000004";
        CreateSessionFile(SessionId, cwd: "");

        var service = new JsonlService(_tempDir);
        await service.InitializeAsync();

        Assert.Empty(service.Sessions);
    }

    [Fact]
    public async Task BuildSubagentContext_ReturnsAlphabeticOrder()
    {
        const string SessionId = "eee00005-0000-0000-0000-000000000005";
        var realCwd = Path.Combine(_tempDir, "real-project-eee");
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

        var service = new JsonlService(_tempDir);
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
    public async Task Cache_PersistsToLocalAppDataDirectory()
    {
        const string SessionId = "aaaaaaaa-0000-0000-0000-000000000030";
        var cacheDir = Path.Combine(_tempDir, "cache");
        Directory.CreateDirectory(cacheDir);

        CreateSessionFile(SessionId);

        var service = new JsonlService(_tempDir, cacheDir);
        await service.InitializeAsync();

        Assert.True(File.Exists(Path.Combine(cacheDir, "jsonl-cache.json")));
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

        var service = new JsonlService(_tempDir);
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
            BuildAssistantEntryWithCache(SessionId, "uuid-1", "req-1",
                inputTokens: 500, outputTokens: 100, cacheCreation: 200, cacheRead: 50),
            BuildAssistantEntryWithCache(SessionId, "uuid-2", "req-2",
                inputTokens: 300, outputTokens: 80, cacheCreation: 0, cacheRead: 20)
        ]);

        var pricingService = BuildNullPricingService();
        var service = new JsonlService(_tempDir, pricingService: pricingService);
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

        var recentTime = DateTimeOffset.UtcNow.AddHours(-2);
        var oldTime = DateTimeOffset.UtcNow.AddDays(-5);

        await File.WriteAllLinesAsync(jsonlFile,
        [
            BuildAssistantEntry(SessionId, "uuid-1", "req-1", inputTokens: 1000, outputTokens: 100, timestamp: recentTime),
            BuildAssistantEntry(SessionId, "uuid-2", "req-2", inputTokens: 9999, outputTokens: 999, timestamp: oldTime)
        ]);

        var pricingService = BuildNullPricingService();
        var service = new JsonlService(_tempDir, pricingService: pricingService);
        await service.InitializeAsync();

        var stats = service.GetStatistics(TimePeriod.Today);

        Assert.Equal(1000L, stats.InputTokens);
        Assert.Equal(100L, stats.OutputTokens);
    }

    [Fact]
    public async Task GetStatistics_DeduplicatesByUuidAndRequestIdAcrossProjects()
    {
        const string Session1 = "aaaaaaaa-0000-0000-0000-000000000052";
        const string Session2 = "aaaaaaaa-0000-0000-0000-000000000053";
        var projectDir1 = CreateProjectSessionDir(Session1);
        var projectDir2 = CreateProjectSessionDir(Session2);

        // Same uuid+requestId in two different project dirs — must only count once
        var json1 = BuildAssistantEntry(Session1, "shared-uuid", "shared-req",
            inputTokens: 500, outputTokens: 100, timestamp: DateTimeOffset.UtcNow.AddHours(-1));
        var json2 = BuildAssistantEntry(Session2, "shared-uuid", "shared-req",
            inputTokens: 500, outputTokens: 100, timestamp: DateTimeOffset.UtcNow.AddHours(-1));

        await File.WriteAllTextAsync(Path.Combine(projectDir1, $"{Session1}.jsonl"), json1 + "\n");
        await File.WriteAllTextAsync(Path.Combine(projectDir2, $"{Session2}.jsonl"), json2 + "\n");

        var pricingService = BuildNullPricingService();
        var service = new JsonlService(_tempDir, pricingService: pricingService);
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
        var now = DateTimeOffset.Now;
        var safeTimestamp1 = new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, 1, 0, now.Offset);
        var safeTimestamp2 = new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, 2, 0, now.Offset);

        await File.WriteAllLinesAsync(jsonlFile,
        [
            BuildAssistantEntry(SessionId, "uuid-1", "req-1",
                inputTokens: 100, outputTokens: 50, timestamp: safeTimestamp1),
            BuildAssistantEntry(SessionId, "uuid-2", "req-2",
                inputTokens: 200, outputTokens: 80, timestamp: safeTimestamp2)
        ]);

        var pricingService = BuildNullPricingService();
        var service = new JsonlService(_tempDir, pricingService: pricingService);
        await service.InitializeAsync();

        var stats = service.GetStatistics(TimePeriod.Session, projectDirName);

        Assert.Equal(300L, stats.InputTokens);
        Assert.Equal(130L, stats.OutputTokens);
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

    private static string BuildAssistantEntryWithCache(
        string sessionId,
        string uuid,
        string requestId,
        long inputTokens = 0,
        long outputTokens = 0,
        long cacheCreation = 0,
        long cacheRead = 0,
        DateTimeOffset? timestamp = null)
    {
        return JsonSerializer.Serialize(new
        {
            uuid,
            requestId,
            sessionId,
            cwd = "/home/user/project",
            timestamp = (timestamp ?? DateTimeOffset.UtcNow).ToString("O"),
            isSidechain = false,
            type = "assistant",
            message = new
            {
                model = "claude-sonnet-4-6",
                usage = new
                {
                    input_tokens = inputTokens,
                    output_tokens = outputTokens,
                    cache_creation_input_tokens = cacheCreation,
                    cache_read_input_tokens = cacheRead
                }
            }
        });
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private string WriteTempFile(string content)
    {
        var path = Path.Combine(_tempDir, Guid.NewGuid() + ".tmp");
        File.WriteAllText(path, content);
        return path;
    }

    private string CreateProjectSessionDir(string sessionId)
    {
        var projectDir = Path.Combine(_tempDir, "project-" + sessionId[..8]);
        Directory.CreateDirectory(projectDir);
        return projectDir;
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

    private static string BuildAssistantEntry(
        string sessionId,
        string uuid,
        string requestId,
        string? cwd = null,
        string? model = "claude-sonnet-4-6",
        long inputTokens = 0,
        long outputTokens = 0,
        bool isSidechain = false,
        DateTimeOffset? timestamp = null,
        string? uniqueHash = null)
    {
        return JsonSerializer.Serialize(new
        {
            uuid,
            requestId,
            sessionId,
            cwd = cwd ?? "/home/user/project",
            timestamp = (timestamp ?? DateTimeOffset.UtcNow).ToString("O"),
            isSidechain,
            uniqueHash = uniqueHash ?? $"{uuid}|{requestId}",
            type = "assistant",
            message = new
            {
                model,
                usage = new
                {
                    input_tokens = inputTokens,
                    output_tokens = outputTokens,
                    cache_read_input_tokens = 0,
                    cache_creation_input_tokens = 0
                }
            }
        });
    }

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
