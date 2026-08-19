using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using CCInfoWindows.Helpers;

namespace CCInfoWindows.Tests.Helpers;

/// <summary>
/// Keeps the whole test assembly's diagnostics out of the maintainer's real
/// %LOCALAPPDATA%\CCInfoWindows\app.log.
///
/// Why a module initializer and not a per-class fixture: production code logs handled failures through the plain
/// AppLog.Write, not through the directory seam AppLogTests uses — JsonlService records a dropped cwd for every
/// project whose entries carry none, which is most of the JSONL corpus. Redirecting per test class would need the
/// redirect in every class that instantiates a service, and xUnit runs collections in parallel by default, so one
/// class's teardown would clear another class's redirect mid-run. Installed once, before any test body runs,
/// neither problem exists.
///
/// The target is a stable path rather than a per-run GUID: xUnit 2.9 has no assembly teardown hook, so a unique
/// directory per run would accumulate. AppLog's own 1 MiB cap and single roll bound this one at ~2 MiB forever,
/// and keeping it lets a failing test's production diagnostics be read afterwards.
/// </summary>
internal static class TestLogSinkRedirect
{
    internal static readonly string SinkDirectory = Path.Combine(Path.GetTempPath(), "ccinfo-tests", "app-log");

    [ModuleInitializer]
    internal static void Install() => AppLog.TryRedirectToDirectory(SinkDirectory);
}

/// <summary>
/// Covers the AppLog contract: append, single roll at the cap, never throw, one line per entry under concurrency,
/// no credentials on disk, and the one-shot directory redirect. Every file-level test drives the internal
/// directory seam against a private temp directory, so nothing here touches the real %LOCALAPPDATA% log.
/// </summary>
public class AppLogTests : IDisposable
{
    private const long NoRollCap = long.MaxValue;
    private const long RollOnEveryWriteCap = 1;
    private const long OneMebibyte = 1024L * 1024L;
    private const string TestSource = "AppLogTests.Case";
    private const string AppLogFileName = "AppLog.cs";
    // ANSI escape (0x1B) written as a code point so the source file stays pure ASCII.
    private const char EscapeCharacter = (char)0x1B;

    private static readonly string RedirectMemberName = nameof(AppLog.TryRedirectToDirectory);

    private static readonly Regex EntryLinePattern = new(@"^\[[^\]]+\] \[[^\]]+\] .+$");

    private readonly string _tempDirectory;
    private readonly string _logPath;
    private readonly string _previousLogPath;

    public AppLogTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "ccinfo-applog-" + Guid.NewGuid());
        Directory.CreateDirectory(_tempDirectory);
        _logPath = Path.Combine(_tempDirectory, "app.log");
        _previousLogPath = _logPath + ".1";
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDirectory, recursive: true); }
        catch (IOException) { /* another handle still open on a temp file; the OS reclaims it */ }
    }

    [Fact]
    public void DefaultLogDirectory_PointsAtLocalAppDataApplicationFolder()
    {
        var expected = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CCInfoWindows");

        Assert.Equal(expected, AppLog.DefaultLogDirectory);
    }

    // --- The one-shot directory redirect (F.I.R.S.T. Independent: the suite must not write to the real log) ---

    [Fact]
    public void ResolveLogDirectory_WithoutARedirect_IsTheDefaultDirectory()
    {
        // The fallback branch cannot be reached through ActiveLogDirectory once the module initializer has
        // installed the redirect, which is exactly why the rule is a separate pure method.
        Assert.Equal(AppLog.DefaultLogDirectory, AppLog.ResolveLogDirectory(null));
    }

    [Fact]
    public void ResolveLogDirectory_WithARedirect_PrefersTheRedirect()
    {
        Assert.Equal(_tempDirectory, AppLog.ResolveLogDirectory(_tempDirectory));
    }

    [Fact]
    public void ActiveLogDirectory_IsRedirectedAwayFromTheRealLogForTheWholeRun()
    {
        Assert.Equal(TestLogSinkRedirect.SinkDirectory, AppLog.ActiveLogDirectory);
        Assert.NotEqual(AppLog.DefaultLogDirectory, AppLog.ActiveLogDirectory);
    }

    [Fact]
    public void Write_ThroughTheProductionEntryPoint_LandsInTheRedirectedDirectoryOnly()
    {
        // Unique per run: the sink is shared with every other test class's incidental logging, so the assertion
        // has to be "my entry is in there", never "this is the only entry".
        var marker = "redirect-probe-" + Guid.NewGuid();

        AppLog.Write(TestSource, marker);

        Assert.Contains(marker, ReadEntireSink(TestLogSinkRedirect.SinkDirectory));

        // The regression this seam exists for: the entry must not have reached the maintainer's real log.
        var realLog = Path.Combine(AppLog.DefaultLogDirectory, "app.log");
        if (File.Exists(realLog))
        {
            Assert.DoesNotContain(marker, ReadWhileWritersAppend(realLog));
        }
    }

    [Fact]
    public void TryRedirectToDirectory_WhenOneIsAlreadyInstalled_IsRejected()
    {
        Assert.False(AppLog.TryRedirectToDirectory(_tempDirectory));
        Assert.Equal(TestLogSinkRedirect.SinkDirectory, AppLog.ActiveLogDirectory);
    }

    [Fact]
    public void TryRedirectToDirectory_WithABlankTarget_FailsLoudly()
    {
        // Install-time configuration, so it is outside the never-throw contract that covers Write: silently
        // keeping the real log would be the worse outcome.
        Assert.Throws<ArgumentException>(() => AppLog.TryRedirectToDirectory("   "));
    }

    [Fact]
    public void RedirectSeam_HasNoProductionCaller()
    {
        // internal + InternalsVisibleTo makes the seam visible to the tests, but it does not stop the app
        // assembly from calling it. Only a source scan can.
        var callers = ProductionSourceFiles.FilesContaining(RedirectMemberName, AppLogFileName).ToList();

        Assert.True(
            callers.Count == 0,
            $"{RedirectMemberName} is a test-only seam but is referenced by: {string.Join(", ", callers)}");
    }

    [Fact]
    public void MaxLogBytes_MatchesDocumentedOneMebibyteCap()
    {
        Assert.Equal(OneMebibyte, AppLog.MaxLogBytes);
    }

    [Fact]
    public void Write_CreatesLogFileWithTimestampSourceAndMessage()
    {
        AppLog.WriteToDirectory(_tempDirectory, NoRollCap, TestSource, "jsonl init failed");

        var line = Assert.Single(File.ReadAllLines(_logPath));
        Assert.Matches(EntryLinePattern, line);
        Assert.Contains($"[{TestSource}]", line);
        Assert.EndsWith("jsonl init failed", line);
    }

    [Fact]
    public void Write_TimestampCarriesUtcOffset()
    {
        AppLog.WriteToDirectory(_tempDirectory, NoRollCap, TestSource, "offset check");

        var line = Assert.Single(File.ReadAllLines(_logPath));
        var timestamp = line[1..line.IndexOf(']')];

        var parsed = DateTimeOffset.TryParse(
            timestamp,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out var instant);

        Assert.True(parsed, $"not a round-trippable DateTimeOffset: {timestamp}");
        Assert.Equal(DateTimeOffset.Now.Offset, instant.Offset);
    }

    [Fact]
    public void Write_SecondCall_AppendsInsteadOfTruncating()
    {
        AppLog.WriteToDirectory(_tempDirectory, NoRollCap, TestSource, "first");
        AppLog.WriteToDirectory(_tempDirectory, NoRollCap, TestSource, "second");

        var lines = File.ReadAllLines(_logPath);

        Assert.Equal(2, lines.Length);
        Assert.EndsWith("first", lines[0]);
        Assert.EndsWith("second", lines[1]);
    }

    [Fact]
    public void Write_BelowCap_DoesNotCreatePreviousLog()
    {
        AppLog.WriteToDirectory(_tempDirectory, NoRollCap, TestSource, "first");
        AppLog.WriteToDirectory(_tempDirectory, NoRollCap, TestSource, "second");

        Assert.False(File.Exists(_previousLogPath));
    }

    [Fact]
    public void Write_AtCap_RollsCurrentToPreviousAndStartsFresh()
    {
        AppLog.WriteToDirectory(_tempDirectory, RollOnEveryWriteCap, TestSource, "older");
        AppLog.WriteToDirectory(_tempDirectory, RollOnEveryWriteCap, TestSource, "newer");

        Assert.Contains("older", File.ReadAllText(_previousLogPath));

        var currentLine = Assert.Single(File.ReadAllLines(_logPath));
        Assert.EndsWith("newer", currentLine);
        Assert.DoesNotContain("older", currentLine);
    }

    [Fact]
    public void Write_ThreeEntriesAtCap_KeepsExactlyOneRollGeneration()
    {
        AppLog.WriteToDirectory(_tempDirectory, RollOnEveryWriteCap, TestSource, "oldest");
        AppLog.WriteToDirectory(_tempDirectory, RollOnEveryWriteCap, TestSource, "middle");
        AppLog.WriteToDirectory(_tempDirectory, RollOnEveryWriteCap, TestSource, "newest");

        var files = Directory.GetFiles(_tempDirectory, "app.log*");
        var previous = File.ReadAllText(_previousLogPath);

        Assert.Equal(2, files.Length);
        Assert.Contains("middle", previous);
        Assert.DoesNotContain("oldest", previous);
        Assert.Contains("newest", File.ReadAllText(_logPath));
    }

    [Fact]
    public void Write_ConcurrentWriters_RecordEveryEntryWithoutTornLines()
    {
        const int EntryCount = 240;
        const int WriterThreads = 8;

        Parallel.For(
            0,
            EntryCount,
            new ParallelOptions { MaxDegreeOfParallelism = WriterThreads },
            index => AppLog.WriteToDirectory(
                _tempDirectory,
                NoRollCap,
                "Writer" + index % WriterThreads,
                "entry-" + index));

        var lines = File.ReadAllLines(_logPath);

        Assert.Equal(EntryCount, lines.Length);
        Assert.All(lines, line => Assert.Matches(EntryLinePattern, line));
        Assert.Equal(EntryCount, lines.Distinct().Count());

        for (var index = 0; index < EntryCount; index++)
        {
            var payload = "entry-" + index;
            Assert.Single(lines, line => line.EndsWith(payload, StringComparison.Ordinal));
        }
    }

    [Fact]
    public void Write_DirectoryPathBlockedByFile_DoesNotThrow()
    {
        var blockedDirectory = Path.Combine(_tempDirectory, "blocker");
        File.WriteAllText(blockedDirectory, "occupied by a file");

        AppLog.WriteToDirectory(blockedDirectory, NoRollCap, TestSource, "cannot land anywhere");

        Assert.True(File.Exists(blockedDirectory));
        Assert.Equal("occupied by a file", File.ReadAllText(blockedDirectory));
    }

    [Fact]
    public void Write_LogFileLockedExclusively_DropsEntryWithoutThrowing()
    {
        using var exclusiveHolder = new FileStream(_logPath, FileMode.Create, FileAccess.Write, FileShare.None);

        AppLog.WriteToDirectory(_tempDirectory, NoRollCap, TestSource, "dropped");

        Assert.Equal(0L, new FileInfo(_logPath).Length);
    }

    [Fact]
    public void Write_RollTargetLockedExclusively_StillAppendsEntry()
    {
        File.WriteAllText(_logPath, "previous content" + Environment.NewLine);
        using var exclusiveHolder = new FileStream(_previousLogPath, FileMode.Create, FileAccess.Write, FileShare.None);

        AppLog.WriteToDirectory(_tempDirectory, RollOnEveryWriteCap, TestSource, "survives failed roll");

        var text = File.ReadAllText(_logPath);
        Assert.Contains("survives failed roll", text);
        Assert.Contains("previous content", text);
    }

    [Fact]
    public void Write_ReaderHoldsLogFile_StillAppendsEntry()
    {
        File.WriteAllText(_logPath, "tailed" + Environment.NewLine);
        using var tail = new FileStream(_logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

        AppLog.WriteToDirectory(_tempDirectory, NoRollCap, TestSource, "written while tailed");

        Assert.Contains("written while tailed", File.ReadAllText(_logPath));
    }

    [Fact]
    public void Write_ExceptionOverload_RecordsTypeMessageAndStackTrace()
    {
        var caught = CaptureThrownException();

        AppLog.WriteToDirectory(_tempDirectory, NoRollCap, TestSource, caught, "cache write failed");

        var text = File.ReadAllText(_logPath);
        Assert.Contains("cache write failed", text);
        Assert.Contains("System.InvalidOperationException", text);
        Assert.Contains("deliberate failure", text);
        Assert.Contains(nameof(CaptureThrownException), text);
        Assert.Contains(" at ", text);
    }

    [Fact]
    public void Write_ExceptionOverload_WithoutMessage_StillRecordsType()
    {
        AppLog.WriteToDirectory(_tempDirectory, NoRollCap, TestSource, CaptureThrownException());

        var header = File.ReadAllLines(_logPath)[0];

        Assert.StartsWith("[", header);
        Assert.EndsWith("System.InvalidOperationException", header);
    }

    [Fact]
    public void Write_ExceptionOverload_KeepsHeaderOnASingleLine()
    {
        AppLog.WriteToDirectory(_tempDirectory, NoRollCap, TestSource, CaptureThrownException(), "header stays intact");

        var header = File.ReadAllLines(_logPath)[0];

        Assert.Matches(EntryLinePattern, header);
        Assert.Contains("header stays intact", header);
    }

    [Fact]
    public void Write_MultilineMessage_CannotForgeAnAdditionalEntry()
    {
        AppLog.WriteToDirectory(
            _tempDirectory,
            NoRollCap,
            TestSource,
            "real part\r\n[2020-01-01T00:00:00.0000000+00:00] [Forged] injected entry");

        var line = Assert.Single(File.ReadAllLines(_logPath));
        Assert.Contains("real part", line);
        Assert.Contains("injected entry", line);
    }

    [Fact]
    public void Write_ControlCharactersInSource_AreFoldedToSpaces()
    {
        AppLog.WriteToDirectory(_tempDirectory, NoRollCap, "Src" + EscapeCharacter + "[31m\tX", "escaped");

        var line = Assert.Single(File.ReadAllLines(_logPath));

        // The char overloads compare ordinally. Assert.DoesNotContain(string, string) is culture-sensitive, and a
        // needle with no collation weight (ESC has none in ICU) matches at position 0 of every string.
        Assert.DoesNotContain(EscapeCharacter, line);
        Assert.DoesNotContain('\t', line);
        Assert.Contains("escaped", line);
    }

    [Fact]
    public void Write_RedactsClaudeSessionKey()
    {
        AppLog.WriteToDirectory(_tempDirectory, NoRollCap, TestSource, "auth used sk-ant-sid01-AbC123_dEf-XYZ then failed");

        var text = File.ReadAllText(_logPath);

        Assert.DoesNotContain("AbC123_dEf-XYZ", text);
        Assert.Contains("sk-ant-[REDACTED]", text);
        Assert.Contains("then failed", text);
    }

    [Fact]
    public void Write_RedactsCredentialAssignments()
    {
        AppLog.WriteToDirectory(_tempDirectory, NoRollCap, TestSource, "Cookie: sessionKey=deadbeefvalue; org=abc");

        var text = File.ReadAllText(_logPath);

        Assert.DoesNotContain("deadbeefvalue", text);
        Assert.Contains("sessionKey=[REDACTED]", text);
        Assert.Contains("org=abc", text);
    }

    [Fact]
    public void Write_RedactsBearerAuthorizationHeader()
    {
        AppLog.WriteToDirectory(_tempDirectory, NoRollCap, TestSource, "Authorization: Bearer opaquetokenvalue");

        var text = File.ReadAllText(_logPath);

        Assert.DoesNotContain("opaquetokenvalue", text);
        Assert.Contains("[REDACTED]", text);
    }

    [Fact]
    public void Write_RedactsSecretsInsideExceptionText()
    {
        var caught = CaptureThrownException("request to /api?sessionKey=deadbeefvalue rejected");

        AppLog.WriteToDirectory(_tempDirectory, NoRollCap, TestSource, caught);

        Assert.DoesNotContain("deadbeefvalue", File.ReadAllText(_logPath));
    }

    [Fact]
    public void Write_KeepsTokenCountsThatMerelyLookLikeSecrets()
    {
        AppLog.WriteToDirectory(_tempDirectory, NoRollCap, TestSource, "tokens=123456 cost=0.42");

        var text = File.ReadAllText(_logPath);

        Assert.Contains("tokens=123456", text);
        Assert.Contains("cost=0.42", text);
    }

    [Fact]
    public void Write_NullSourceAndMessage_DoesNotThrowAndStaysReadable()
    {
        string? missingSource = null;
        string? missingMessage = null;

        AppLog.WriteToDirectory(_tempDirectory, NoRollCap, missingSource!, missingMessage!);

        var line = Assert.Single(File.ReadAllLines(_logPath));

        Assert.Contains("[Unknown]", line);
    }

    /// <summary>Concatenates the sink's current file and its roll generation, so a roll cannot hide an entry.</summary>
    private static string ReadEntireSink(string directory)
    {
        if (!Directory.Exists(directory)) return string.Empty;

        return string.Concat(Directory.EnumerateFiles(directory, "app.log*").Select(ReadWhileWritersAppend));
    }

    /// <summary>
    /// Reads a log file the way a tail does. The shared sink is appended to by production code running in other
    /// xUnit collections, and File.ReadAllText requests FileShare.Read — which collides with AppLog's writer and
    /// would turn a coincidence of timing into a failed test.
    /// </summary>
    private static string ReadWhileWritersAppend(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Exception CaptureThrownException(string message = "deliberate failure")
    {
        try
        {
            throw new InvalidOperationException(message);
        }
        catch (Exception ex)
        {
            return ex;
        }
    }
}
