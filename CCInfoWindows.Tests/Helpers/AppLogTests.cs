using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using CCInfoWindows.Helpers;

namespace CCInfoWindows.Tests.Helpers;

/// <summary>
/// Covers the AppLog contract: append, single roll at the cap, never throw, one line per entry under concurrency,
/// and no credentials on disk. Every test drives the internal directory seam, so nothing here touches the real
/// %LOCALAPPDATA% log.
/// </summary>
public class AppLogTests : IDisposable
{
    private const long NoRollCap = long.MaxValue;
    private const long RollOnEveryWriteCap = 1;
    private const long OneMebibyte = 1024L * 1024L;
    private const string TestSource = "AppLogTests.Case";
    // ANSI escape (0x1B) written as a code point so the source file stays pure ASCII.
    private const char EscapeCharacter = (char)0x1B;

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
        try { Directory.Delete(_tempDirectory, recursive: true); } catch { }
    }

    [Fact]
    public void DefaultLogDirectory_PointsAtLocalAppDataApplicationFolder()
    {
        var expected = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CCInfoWindows");

        Assert.Equal(expected, AppLog.DefaultLogDirectory);
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
