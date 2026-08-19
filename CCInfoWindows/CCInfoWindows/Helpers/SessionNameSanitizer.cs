using System.Text;

namespace CCInfoWindows.Helpers;

/// <summary>
/// Strips control characters from session names, and owns the one definition of "control character"
/// that every surface taking text from outside the app shares (see <see cref="IsUnsafeControl"/>).
/// CVE-2021-42574 mitigation, mirroring macOS reference behavior. Bidi-control codepoints
/// (U+202A..U+202E, U+2066..U+2069) are intentionally NOT stripped — same scope as upstream.
/// </summary>
public static class SessionNameSanitizer
{
    /// <summary>
    /// The shared rule: C0 (U+0000..U+001F), DEL (U+007F) and C1 (U+0080..U+009F) are unsafe in text
    /// that came from outside the app — a JSONL record, a workflow script, an API response, a rename
    /// box. Exactly what <see cref="char.IsControl(char)"/> answers.
    ///
    /// The replacement POLICY stays with each caller, because they differ for good reasons: a display
    /// name drops the character (a name is not the place for a space nobody typed), while a log entry
    /// and the workflow tooltip fold it to a space so one entry stays one line and the words on either
    /// side of a dropped newline do not glue together. What must not differ is the SET, which used to
    /// be answered three ways — this method is the one answer.
    /// </summary>
    // ponytail: the predicate is char.IsControl and nothing more today. The value is the single
    // name: hardening the set (bidi controls, U+200B) becomes one edit instead of three.
    public static bool IsUnsafeControl(char c) => char.IsControl(c);

    public static string Strip(string? input)
    {
        if (string.IsNullOrEmpty(input)) return string.Empty;

        var sb = new StringBuilder(input.Length);
        foreach (var c in input)
        {
            if (!IsUnsafeControl(c))
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }
}
