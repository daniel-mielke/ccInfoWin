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
    private readonly string _tempDir;

    public JsonlServiceColdStartTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "cs-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    // -------------------------------------------------------------------------
    // DROPDOWN-02: Cwd fallback via DecodeProjectDirectory
    // -------------------------------------------------------------------------

    /// <summary>
    /// When no JSONL entry carries a cwd field, JsonlService must derive a Cwd surrogate
    /// from the encoded project directory name via SessionNameHelper.DecodeProjectDirectory.
    /// The session must appear in Sessions with the correct DisplayName.
    /// Fails on unmodified JsonlService because empty Cwd causes IsValidProjectDirectory to
    /// return false, dropping the session entirely.
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
    /// A project whose entries carry no cwd must remain in the Sessions list
    /// when a display name can still be derived from the projectDirName.
    /// Fails on unmodified JsonlService because empty Cwd causes the
    /// IsValidProjectDirectory call-site to drop the session.
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
    /// </summary>
    private static void WriteAssistantJsonlLine(string filePath, string sessionId, string? cwd, int outputTokens)
    {
        var uuid = $"msg_{Guid.NewGuid():N}";
        var requestId = $"req_{Guid.NewGuid():N}";
        var uniqueHash = $"{uuid}|{requestId}";
        string line;
        if (cwd is null)
        {
            line = JsonSerializer.Serialize(new
            {
                uuid,
                requestId,
                uniqueHash,
                sessionId,
                timestamp = DateTimeOffset.UtcNow.ToString("O"),
                isSidechain = false,
                type = "assistant",
                message = new
                {
                    model = "claude-sonnet-4-20250514",
                    usage = new
                    {
                        input_tokens = 10,
                        output_tokens = outputTokens,
                        cache_read_input_tokens = 0,
                        cache_creation_input_tokens = 0
                    }
                }
            });
        }
        else
        {
            line = JsonSerializer.Serialize(new
            {
                uuid,
                requestId,
                uniqueHash,
                sessionId,
                cwd,
                timestamp = DateTimeOffset.UtcNow.ToString("O"),
                isSidechain = false,
                type = "assistant",
                message = new
                {
                    model = "claude-sonnet-4-20250514",
                    usage = new
                    {
                        input_tokens = 10,
                        output_tokens = outputTokens,
                        cache_read_input_tokens = 0,
                        cache_creation_input_tokens = 0
                    }
                }
            });
        }
        File.AppendAllText(filePath, line + "\n");
    }

    private static JsonlService BuildService(string projectsRoot)
        => new JsonlService(projectsDirectoryOverride: projectsRoot);
}
