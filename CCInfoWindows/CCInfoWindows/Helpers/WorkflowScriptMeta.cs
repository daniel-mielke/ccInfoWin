using System.Text;
using System.Text.RegularExpressions;
using CCInfoWindows.Models;

namespace CCInfoWindows.Helpers;

/// <summary>
/// Reads the `export const meta = { name, description, phases }` block out of a workflow run's
/// script file (`{session}/workflows/scripts/{name}-{runId}.js`).
///
/// Windows-only: workflow scripts are an artefact of the Workflow tool with no macOS counterpart.
/// Full note on <see cref="SubagentContextData.WorkflowId"/>.
///
/// WHY the script and not the completed-run JSON, which carries the same three fields already
/// parsed: the JSON is written once, at run COMPLETION. The script exists from the moment the run
/// is created. Reading the JSON first left name, description and phases blank for the entire life
/// of every live run — which is precisely when someone hovers the row.
///
/// WHY hand-rolled and not a JSON parser: the block is a JavaScript object literal, not JSON.
/// Unquoted keys, single-quoted strings and trailing commas all appear in real scripts and every
/// one of them makes <c>JsonDocument.Parse</c> throw. A real JS parser would be a dependency for
/// reading three fields.
///
/// Everything here treats the file as UNTRUSTED input (CLAUDE.md): only a bounded prefix is read,
/// the phase list is capped, and every string is control-character-stripped and length-capped
/// before it can reach the tooltip.
/// </summary>
public static partial class WorkflowScriptMeta
{
    /// <summary>
    /// How much of the script is read. `meta` is required to be the first statement of the file
    /// (the Workflow tool documents it as such), so a prefix is enough — and it bounds the work
    /// regardless of script size. Measured scripts on this machine: 8-45 KB, meta always inside the
    /// first 700 bytes.
    /// </summary>
    private const int PrefixBytes = 32 * 1024;

    private const int NameMaxLength = 60;
    private const int DescriptionMaxLength = 200;
    private const int PhaseTitleMaxLength = 40;
    private const int PhaseDetailMaxLength = 160;

    /// <summary>
    /// ponytail: a run with more phases than this shows the first <see cref="MaxPhases"/> and drops
    /// the rest. Real scripts declare 2-5. The cap exists so a malformed or hostile file cannot
    /// grow the tooltip past the screen, not because 24 is a meaningful design limit.
    /// </summary>
    private const int MaxPhases = 24;

    private const string LogSource = "WorkflowScriptMeta.Read";

    /// <summary>
    /// The three fields, all optional. <see cref="Phases"/> is empty rather than null when absent so
    /// callers never null-check a collection.
    /// </summary>
    public readonly record struct ScriptMeta(string? Name, string? Description, IReadOnlyList<WorkflowPhase> Phases)
    {
        public static readonly ScriptMeta Empty = new(null, null, []);
    }

    /// <summary>
    /// Locates and reads the script of one run. <paramref name="sessionDirectory"/> is the session
    /// folder (the one holding both `subagents/` and `workflows/`), <paramref name="runId"/> the
    /// directory name of the run, e.g. "wf_d5bf6469-814".
    ///
    /// The file name is `{workflowName}-{runId}.js` — the name half is unknown here, so the run id
    /// is matched as a suffix. Returns <see cref="ScriptMeta.Empty"/> for every failure: no session
    /// directory, no scripts folder, no matching file, an unreadable file, or a file with no
    /// recognisable meta block. A missing script is the normal case for older runs, not an error.
    /// </summary>
    public static ScriptMeta Read(string? sessionDirectory, string? runId)
    {
        if (string.IsNullOrEmpty(sessionDirectory) || string.IsNullOrEmpty(runId))
            return ScriptMeta.Empty;

        try
        {
            var scriptsDirectory = Path.Combine(sessionDirectory, "workflows", "scripts");
            if (!Directory.Exists(scriptsDirectory))
                return ScriptMeta.Empty;

            // "*{runId}.js" and not "*.js" + filter: the glob is done by the OS, and a session can
            // hold one script per run ever executed in it.
            var matches = Directory.GetFiles(scriptsDirectory, "*" + runId + ".js");
            if (matches.Length == 0)
                return ScriptMeta.Empty;

            return Parse(ReadPrefix(matches[0]));
        }
        // One clause: five unrelated failure types, one identical response — no metadata, tooltip
        // drops the lines. ArgumentException covers invalid characters reaching Path.Combine from a
        // run id read off disk.
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                       or ArgumentException or NotSupportedException or RegexMatchTimeoutException)
        {
            AppLog.Write(LogSource, ex, $"Failed to read workflow script for {runId}.");
            return ScriptMeta.Empty;
        }
    }

    private static string ReadPrefix(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var buffer = new byte[PrefixBytes];
        var read = stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
        return Encoding.UTF8.GetString(buffer, 0, read);
    }

    /// <summary>
    /// Parses the meta block out of already-read script text. Internal rather than private so the
    /// parser can be tested without touching the filesystem.
    /// </summary>
    internal static ScriptMeta Parse(string script)
    {
        var block = ExtractMetaBlock(script);
        if (block is null)
            return ScriptMeta.Empty;

        return new ScriptMeta(
            Sanitize(MatchStringProperty(block, "name"), NameMaxLength),
            Sanitize(MatchStringProperty(block, "description"), DescriptionMaxLength),
            ParsePhases(block));
    }

    /// <summary>
    /// The object literal after `export const meta =`, brace-matched.
    ///
    /// Brace COUNTING rather than "read to the next line starting with }": both formattings occur,
    /// and a one-line meta block would defeat the line-based version. Counting is string-aware,
    /// because a description like 'use {0} here' otherwise closes the block early — and cutting the
    /// block short is not a harmless bug, it silently drops the phases that follow.
    ///
    /// Bounding the block matters for a second reason: the scripts that follow it are full of
    /// `title:` and `description:` keys inside JSON schemas. Searching the whole file instead of
    /// this block would pull phase titles out of a completely unrelated schema definition.
    /// </summary>
    private static string? ExtractMetaBlock(string script)
    {
        var header = MetaHeaderPattern().Match(script);
        if (!header.Success)
            return null;

        var start = header.Index + header.Length - 1;   // the '{' itself
        var end = FindMatchingClose(script, start, '{', '}');
        return end < 0 ? null : script[start..(end + 1)];
    }

    /// <summary>
    /// Index of the bracket closing the one at <paramref name="open"/>, or -1 if the prefix ends
    /// first (a script larger than <see cref="PrefixBytes"/> truncated mid-block, or an unbalanced
    /// file). Skips over string literals — single, double and template quotes — and honours
    /// backslash escapes inside them.
    ///
    /// ponytail: no comment handling. A `//` or `/* */` containing an unbalanced brace inside the
    /// meta literal would break this. Meta blocks are generated data, not hand-commented code; add
    /// comment skipping if that ever shows up in a real script.
    /// </summary>
    private static int FindMatchingClose(string text, int open, char openChar, char closeChar)
    {
        var depth = 0;
        var quote = '\0';

        for (var i = open; i < text.Length; i++)
        {
            var c = text[i];

            if (quote != '\0')
            {
                if (c == '\\') i++;                 // escaped char, whatever it is
                else if (c == quote) quote = '\0';
                continue;
            }

            if (c is '\'' or '"' or '`') quote = c;
            else if (c == openChar) depth++;
            else if (c == closeChar && --depth == 0) return i;
        }

        return -1;
    }

    private static List<WorkflowPhase> ParsePhases(string block)
    {
        var header = PhasesHeaderPattern().Match(block);
        if (!header.Success)
            return [];

        var start = header.Index + header.Length - 1;   // the '['
        var end = FindMatchingClose(block, start, '[', ']');
        if (end < 0)
            return [];

        var phases = new List<WorkflowPhase>();
        var array = block[start..(end + 1)];

        // Each entry is an object literal; brace-matching walks them one by one so a detail string
        // containing '{' or '}' cannot split an entry in two.
        var cursor = 0;
        while (phases.Count < MaxPhases)
        {
            var entryStart = array.IndexOf('{', cursor);
            if (entryStart < 0) break;

            var entryEnd = FindMatchingClose(array, entryStart, '{', '}');
            if (entryEnd < 0) break;

            var entry = array[entryStart..(entryEnd + 1)];
            cursor = entryEnd + 1;

            // A phase without a title is not displayable — the title is the row's identity.
            var title = Sanitize(MatchStringProperty(entry, "title"), PhaseTitleMaxLength);
            if (title is not null)
                phases.Add(new WorkflowPhase(title, Sanitize(MatchStringProperty(entry, "detail"), PhaseDetailMaxLength)));
        }

        return phases;
    }

    /// <summary>
    /// The value of `key: '...'` in one literal, with JS escapes resolved. Returns null when the
    /// key is absent or its value is not a string literal (a computed value, a variable reference).
    /// </summary>
    private static string? MatchStringProperty(string literal, string key)
    {
        var match = Regex.Match(
            literal,
            @"(?<![\w$])" + key + @"\s*:\s*(?<q>['""`])(?<v>(?:\\.|(?!\k<q>)[\s\S])*)\k<q>",
            RegexOptions.None,
            TimeSpan.FromSeconds(1));

        return match.Success ? Unescape(match.Groups["v"].Value) : null;
    }

    /// <summary>
    /// Resolves the JS escapes that occur in real meta strings. \n and \t become the characters they
    /// name and are then turned into spaces by <see cref="Sanitize"/> — going straight to a space
    /// here would be equivalent, but this keeps the two concerns separate. Unknown escapes drop the
    /// backslash, which is what JS itself does.
    /// </summary>
    private static string Unescape(string value)
    {
        if (!value.Contains('\\'))
            return value;

        var result = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] != '\\' || i + 1 >= value.Length)
            {
                result.Append(value[i]);
                continue;
            }

            var next = value[++i];
            result.Append(next switch
            {
                'n' => '\n',
                't' => '\t',
                'r' => '\r',
                _ => next
            });
        }

        return result.ToString();
    }

    /// <summary>
    /// Untrusted text on its way into the UI: control characters become spaces rather than
    /// vanishing — dropping a newline glues the words on either side of it together — runs of
    /// whitespace collapse, and the length is capped so one long value cannot stretch the tooltip.
    ///
    /// The cap never splits a surrogate pair: cutting between the two halves of an astral character
    /// leaves a lone surrogate that renders as a replacement box.
    ///
    /// Which characters count as control characters is <see cref="SessionNameSanitizer.IsUnsafeControl"/>,
    /// shared with the log sink and the session-name box; only the folding policy and the cap are local.
    /// </summary>
    internal static string? Sanitize(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var cleaned = WhitespaceRunPattern().Replace(
            string.Concat(value.Select(c => SessionNameSanitizer.IsUnsafeControl(c) ? ' ' : c)), " ").Trim();

        if (cleaned.Length == 0)
            return null;
        if (cleaned.Length <= maxLength)
            return cleaned;

        var cut = maxLength;
        if (char.IsHighSurrogate(cleaned[cut - 1]))
            cut--;

        return cleaned[..cut];
    }

    [GeneratedRegex(@"export\s+const\s+meta\s*=\s*\{", RegexOptions.None, 1000)]
    private static partial Regex MetaHeaderPattern();

    [GeneratedRegex(@"(?<![\w$])phases\s*:\s*\[", RegexOptions.None, 1000)]
    private static partial Regex PhasesHeaderPattern();

    [GeneratedRegex(@"\s+", RegexOptions.None, 1000)]
    private static partial Regex WhitespaceRunPattern();
}
