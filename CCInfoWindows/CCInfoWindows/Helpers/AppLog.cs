using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace CCInfoWindows.Helpers;

/// <summary>
/// Release-safe diagnostic sink for HANDLED failures, appending to %LOCALAPPDATA%\CCInfoWindows\app.log.
/// Complements App.OnUnhandledException's crash.log, which only ever sees UNHANDLED exceptions, and replaces the
/// [Conditional("DEBUG")] Debug.WriteLine sites that the compiler erases from the shipped binary.
///
/// Contract: never throws, from any thread. It is called from catch blocks, so an exception escaping here would
/// replace the very failure it was asked to record.
///
/// Design: process-wide lock plus open/append/close per entry. A long-lived StreamWriter would hold the handle for
/// the whole process lifetime (blocking a second app instance and external inspection), and a buffered background
/// drain would lose the newest entries in exactly the crash this sink exists to explain. The price is one file
/// open per handled failure, which is an error path, not a hot path.
///
/// Cross-process: the file is opened with FileShare.ReadWrite so a second instance or an open tail/editor cannot
/// turn an append into an IOException. Windows gives no atomicity guarantee across processes for managed append
/// (that needs FILE_APPEND_DATA via P/Invoke, which CLAUDE.md rules out), so two instances failing simultaneously
/// may interleave at the tail. Losing the tail ordering is strictly better than losing the entry.
///
/// Testability: <see cref="TryRedirectToDirectory"/> repoints the sink once per process so a test run cannot append
/// to the maintainer's real log through production code that records a handled failure.
/// </summary>
public static partial class AppLog
{
    private const string LogFileName = "app.log";
    private const string PreviousLogSuffix = ".1";
    private const string RedactionPlaceholder = "[REDACTED]";
    private const string SessionKeyPrefix = "sk-ant-";
    private const string UnknownSource = "Unknown";

    // Round-trip ("O") on DateTimeOffset.Now: local wall-clock the user can match to "it broke at 10:15", plus the
    // UTC offset so the instant stays unambiguous across DST and across machines.
    private const string TimestampFormat = "O";

    /// <summary>
    /// 1 MiB per file with a single roll. A failure recurring on every 30 s poll writes roughly 600 KB/day, so the
    /// pair retains about three days of continuous failure while costing at most 2 MiB of LOCALAPPDATA.
    /// </summary>
    internal const long MaxLogBytes = 1024 * 1024;

    private static readonly Lock Gate = new();

    // No BOM: entries are appended, and a preamble written mid-file would corrupt the line it lands in.
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    // Null in the shipped app. Only the test assembly installs a value, via TryRedirectToDirectory.
    private static string? _redirectedDirectory;

    /// <summary>Where the shipped app always writes: %LOCALAPPDATA%\CCInfoWindows.</summary>
    internal static string DefaultLogDirectory => AppPaths.DataDirectory;

    /// <summary>The directory the next entry lands in.</summary>
    internal static string ActiveLogDirectory => ResolveLogDirectory(Volatile.Read(ref _redirectedDirectory));

    /// <summary>The resolution rule: a redirect wins, absence of one means the default directory.</summary>
    internal static string ResolveLogDirectory(string? redirectedDirectory)
        => redirectedDirectory ?? DefaultLogDirectory;

    /// <summary>
    /// Test-only: repoints the sink at <paramref name="directory"/> so a test run cannot append to the real
    /// %LOCALAPPDATA%\CCInfoWindows\app.log through production code that records a handled failure. Nothing in
    /// CCInfoWindows may call this; AppLogTests scans the app sources to keep it that way.
    ///
    /// One-shot by construction: there is no way to clear or replace an installed redirect, so a redirect cannot
    /// leak from one xUnit collection into another running in parallel and no fixture teardown is required. The
    /// price is that the fallback branch of <see cref="ResolveLogDirectory"/> can only be covered directly, which
    /// is why that rule is a separate pure method.
    ///
    /// What it does NOT solve: an entry already inside <see cref="Append"/> keeps the directory it resolved, and
    /// anything logged before the redirect is installed still lands in the default directory.
    /// </summary>
    /// <returns>True when this call installed the redirect, false when one was already in place.</returns>
    internal static bool TryRedirectToDirectory(string directory)
    {
        // Deliberately not covered by the never-throw contract: this is install-time configuration called from a
        // test's module initializer, not a catch block, and a blank target must fail loudly rather than silently
        // send the suite's diagnostics back to the real log.
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        return Interlocked.CompareExchange(ref _redirectedDirectory, directory, null) is null;
    }

    /// <summary>
    /// Records a handled failure or state transition. <paramref name="source"/> is a short call-site tag such as
    /// "MainView.OnLoaded".
    /// </summary>
    public static void Write(string source, string message)
        => WriteToDirectory(ActiveLogDirectory, MaxLogBytes, source, message);

    /// <summary>
    /// Records a handled exception with its type and stack trace, optionally prefixed by a context message.
    /// </summary>
    public static void Write(string source, Exception ex, string? message = null)
        => WriteToDirectory(ActiveLogDirectory, MaxLogBytes, source, ex, message);

    // Test seam: an explicit target lets AppLog's own tests assert file-level behaviour (roll generations, locked
    // handles) against a private per-test directory instead of the process-wide one.
    internal static void WriteToDirectory(string directory, long maxBytes, string source, string message)
        => Append(directory, maxBytes, source, message, exception: null);

    internal static void WriteToDirectory(string directory, long maxBytes, string source, Exception ex, string? message = null)
        => Append(directory, maxBytes, source, message, ex);

    private static void Append(string directory, long maxBytes, string source, string? message, Exception? exception)
    {
        try
        {
            var entry = FormatEntry(source, message, exception);

            // Debug builds keep the debugger-output visibility that the replaced Debug.WriteLine sites provided;
            // the call is [Conditional("DEBUG")] and disappears from the shipped binary.
            Debug.Write(entry);

            lock (Gate)
            {
                Directory.CreateDirectory(directory);
                var logPath = Path.Combine(directory, LogFileName);
                RollIfAtCap(logPath, maxBytes);
                AppendUtf8(logPath, entry);
            }
        }
        catch (Exception loggingFailure)
        {
            // The sink itself is the thing that failed, so there is no channel left: routing this through
            // AppLog.Write would re-enter Append, fail on the same file for the same reason, and recurse until
            // the stack overflows -- unrecoverable, and a direct breach of the never-throw contract. Debug builds
            // still surface it in the debugger output; in Release the entry is knowingly lost.
            Debug.WriteLine($"[AppLog] entry dropped: {loggingFailure.GetType().Name}");
        }
    }

    private static void RollIfAtCap(string logPath, long maxBytes)
    {
        try
        {
            var current = new FileInfo(logPath);
            if (!current.Exists || current.Length < maxBytes) return;

            File.Move(logPath, logPath + PreviousLogSuffix, overwrite: true);
        }
        catch (Exception rollFailure)
        {
            // Keeping the entry outranks honouring the cap: a roll blocked by another process (an open tail on
            // app.log.1) must not make the append itself disappear. Recording it through AppLog.Write is not an
            // option either -- that call would roll first, fail identically, and recurse without bound. The cap
            // is re-evaluated on the next entry, so a transient block costs nothing but a slightly larger file.
            Debug.WriteLine($"[AppLog] roll skipped: {rollFailure.GetType().Name}");
        }
    }

    private static void AppendUtf8(string logPath, string entry)
    {
        using var stream = new FileStream(logPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
        using var writer = new StreamWriter(stream, Utf8NoBom);
        writer.Write(entry);
    }

    private static string FormatEntry(string source, string? message, Exception? exception)
    {
        var timestamp = DateTimeOffset.Now.ToString(TimestampFormat, CultureInfo.InvariantCulture);
        var tag = ToSingleLine(string.IsNullOrWhiteSpace(source) ? UnknownSource : source);
        var text = ToSingleLine(message ?? string.Empty);

        var header = $"[{timestamp}] [{tag}]";
        if (text.Length > 0) header += $" {text}";
        if (exception is not null) header += $" -- {exception.GetType().FullName}";

        // Exception entries keep the full ToString() (inner exceptions and stack frames included) and end with a
        // blank line so a multi-line block stays visually separable from the next entry.
        var entry = exception is null
            ? header + Environment.NewLine
            : header + Environment.NewLine + exception.ToString() + Environment.NewLine + Environment.NewLine;

        return Redact(entry);
    }

    // Control characters are folded to spaces so one entry stays one line: a message carrying a newline could
    // otherwise forge a second timestamped entry, and an escape sequence could hijack a terminal tailing the file.
    private static string ToSingleLine(string value)
    {
        if (value.Length == 0) return value;

        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            builder.Append(char.IsControl(character) ? ' ' : character);
        }

        return builder.ToString();
    }

    // Defence in depth for the "never log tokens" rule: callers pass API responses and exception text that may
    // quote a credential. Output redaction cannot use an allow-list, so this targets the two shapes that actually
    // occur here -- the Claude session key and a credential assignment in a header or query string.
    private static string Redact(string text)
    {
        var withoutSessionKeys = SessionKeyPattern().Replace(text, SessionKeyPrefix + RedactionPlaceholder);
        return CredentialAssignmentPattern().Replace(withoutSessionKeys, "$1=" + RedactionPlaceholder);
    }

    [GeneratedRegex(@"sk-ant-[A-Za-z0-9_\-]+", RegexOptions.CultureInvariant)]
    private static partial Regex SessionKeyPattern();

    [GeneratedRegex(
        @"(sessionKey|authorization|api[-_]?key|password)\s*[=:]\s*(Bearer\s+)?[^\s;,&""']+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CredentialAssignmentPattern();
}
