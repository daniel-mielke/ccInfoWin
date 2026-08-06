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

    internal static string DefaultLogDirectory => AppPaths.DataDirectory;

    /// <summary>
    /// Records a handled failure or state transition. <paramref name="source"/> is a short call-site tag such as
    /// "MainView.OnLoaded".
    /// </summary>
    public static void Write(string source, string message)
        => WriteToDirectory(DefaultLogDirectory, MaxLogBytes, source, message);

    /// <summary>
    /// Records a handled exception with its type and stack trace, optionally prefixed by a context message.
    /// </summary>
    public static void Write(string source, Exception ex, string? message = null)
        => WriteToDirectory(DefaultLogDirectory, MaxLogBytes, source, ex, message);

    // Test seam: an explicit target keeps AppLog free of mutable global state, so a test never redirects the log
    // of production code running concurrently in another test class.
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
            // Nowhere left to record this in Release; Debug builds still surface it in the debugger output.
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
            // app.log.1) must not make the append itself disappear.
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
