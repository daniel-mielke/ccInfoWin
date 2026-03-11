using System.Text;
using System.Text.Json;
using CCInfoWindows.Models;
using CCInfoWindows.Services;

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
        const string SessionId = "session-ctx-1";
        var projectDir = CreateProjectSessionDir(SessionId);
        var jsonlFile = Path.Combine(projectDir, $"{SessionId}.jsonl");

        // Two assistant messages — context window should reflect only the LAST one
        await File.WriteAllLinesAsync(jsonlFile,
        [
            BuildAssistantEntry(SessionId, "uuid-1", "req-1", inputTokens: 1000, outputTokens: 100),
            BuildAssistantEntry(SessionId, "uuid-2", "req-2", inputTokens: 5000, outputTokens: 200)
        ]);

        var service = new JsonlService(_tempDir);
        await service.InitializeAsync();

        var ctx = service.GetContextWindow(SessionId);

        // Last entry: input_tokens=5000, so TotalTokens = 5000 (not 1000+5000=6000)
        Assert.Equal(5000L, ctx.TotalTokens);
    }

    [Fact]
    public async Task GetContextWindow_IgnoresSidechainMessages()
    {
        const string SessionId = "session-ctx-2";
        var projectDir = CreateProjectSessionDir(SessionId);
        var jsonlFile = Path.Combine(projectDir, $"{SessionId}.jsonl");

        await File.WriteAllLinesAsync(jsonlFile,
        [
            BuildAssistantEntry(SessionId, "uuid-1", "req-1", inputTokens: 3000, outputTokens: 50),
            BuildSidechainAssistantEntry(SessionId, "uuid-2", "req-2", inputTokens: 99000, outputTokens: 10)
        ]);

        var service = new JsonlService(_tempDir);
        await service.InitializeAsync();

        var ctx = service.GetContextWindow(SessionId);

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
        const string SessionId = "session-tok-1";
        var projectDir = CreateProjectSessionDir(SessionId);
        var jsonlFile = Path.Combine(projectDir, $"{SessionId}.jsonl");

        await File.WriteAllLinesAsync(jsonlFile,
        [
            BuildAssistantEntry(SessionId, "uuid-1", "req-1", outputTokens: 100),
            BuildAssistantEntry(SessionId, "uuid-2", "req-2", outputTokens: 200),
            BuildAssistantEntry(SessionId, "uuid-3", "req-3", outputTokens: 300)
        ]);

        var service = new JsonlService(_tempDir);
        await service.InitializeAsync();

        var summary = service.GetTokenSummary(SessionId);

        Assert.Equal(600L, summary.OutputTokens);
    }

    [Fact]
    public async Task GetTokenSummary_DeduplicatesByUuidAndRequestId()
    {
        const string SessionId = "session-tok-2";
        var projectDir = CreateProjectSessionDir(SessionId);
        var jsonlFile = Path.Combine(projectDir, $"{SessionId}.jsonl");

        // Same uuid+requestId appears twice — should only be counted once
        await File.WriteAllLinesAsync(jsonlFile,
        [
            BuildAssistantEntry(SessionId, "uuid-1", "req-1", outputTokens: 500),
            BuildAssistantEntry(SessionId, "uuid-1", "req-1", outputTokens: 500)
        ]);

        var service = new JsonlService(_tempDir);
        await service.InitializeAsync();

        var summary = service.GetTokenSummary(SessionId);

        Assert.Equal(500L, summary.OutputTokens);
    }

    [Fact]
    public async Task GetTokenSummary_IgnoresSidechainMessages()
    {
        const string SessionId = "session-tok-3";
        var projectDir = CreateProjectSessionDir(SessionId);
        var jsonlFile = Path.Combine(projectDir, $"{SessionId}.jsonl");

        await File.WriteAllLinesAsync(jsonlFile,
        [
            BuildAssistantEntry(SessionId, "uuid-1", "req-1", outputTokens: 400),
            BuildSidechainAssistantEntry(SessionId, "uuid-2", "req-2", outputTokens: 9999)
        ]);

        var service = new JsonlService(_tempDir);
        await service.InitializeAsync();

        var summary = service.GetTokenSummary(SessionId);

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
        const string Session1 = "aaaaaaaa-0000-0000-0000-000000000001";
        const string Session2 = "aaaaaaaa-0000-0000-0000-000000000002";

        CreateSessionFile(Session1, cwd: "/home/user/project-alpha");
        CreateSessionFile(Session2, cwd: "/home/user/project-beta");

        var service = new JsonlService(_tempDir);
        await service.InitializeAsync();

        Assert.Equal(2, service.Sessions.Count);
        var ids = service.Sessions.Select(s => s.Id).ToHashSet();
        Assert.Contains(Session1, ids);
        Assert.Contains(Session2, ids);
    }

    [Fact]
    public async Task Sessions_DisplayNameFromCwdField()
    {
        const string SessionId = "aaaaaaaa-0000-0000-0000-000000000010";
        CreateSessionFile(SessionId, cwd: "/home/user/my-awesome-project");

        var service = new JsonlService(_tempDir);
        await service.InitializeAsync();

        var session = service.Sessions.First(s => s.Id == SessionId);
        Assert.Equal("my-awesome-project", session.DisplayName);
    }

    [Fact]
    public async Task Sessions_SortedByLastActivityDescending()
    {
        const string OlderSession = "aaaaaaaa-0000-0000-0000-000000000020";
        const string NewerSession = "aaaaaaaa-0000-0000-0000-000000000021";

        var olderTime = DateTimeOffset.UtcNow.AddHours(-2);
        var newerTime = DateTimeOffset.UtcNow.AddMinutes(-5);

        CreateSessionFile(OlderSession, timestamp: olderTime);
        CreateSessionFile(NewerSession, timestamp: newerTime);

        var service = new JsonlService(_tempDir);
        await service.InitializeAsync();

        Assert.Equal(NewerSession, service.Sessions[0].Id);
        Assert.Equal(OlderSession, service.Sessions[1].Id);
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

        var ctx = service.GetContextWindow(SessionId);

        Assert.Single(ctx.Subagents);
        Assert.Equal(8000L, ctx.Subagents[0].TotalTokens);
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
        DateTimeOffset? timestamp = null)
    {
        return JsonSerializer.Serialize(new
        {
            uuid,
            requestId,
            sessionId,
            cwd = cwd ?? "/home/user/project",
            timestamp = (timestamp ?? DateTimeOffset.UtcNow).ToString("O"),
            isSidechain,
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
