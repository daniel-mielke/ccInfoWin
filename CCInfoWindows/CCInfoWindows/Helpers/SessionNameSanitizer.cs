using System.Text;

namespace CCInfoWindows.Helpers;

/// <summary>
/// Strips C0 control characters (U+0000..U+001F) and DEL (U+007F) from session names.
/// CVE-2021-42574 mitigation, mirroring macOS reference behavior. Bidi-control codepoints
/// (U+202A..U+202E, U+2066..U+2069) are intentionally NOT stripped — same scope as upstream.
/// </summary>
public static class SessionNameSanitizer
{
    public static string Strip(string? input)
    {
        if (string.IsNullOrEmpty(input)) return string.Empty;

        var sb = new StringBuilder(input.Length);
        foreach (var c in input)
        {
            if (c >= 0x20 && c != 0x7F)
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }
}
