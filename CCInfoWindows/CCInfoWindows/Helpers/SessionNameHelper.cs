namespace CCInfoWindows.Helpers;

/// <summary>
/// Extracts human-readable session names from working directory paths and encoded directory names.
/// Fallback chain mirrors the Tauri reference: cwd last segment → decoded dir name → raw dir name.
/// </summary>
public static class SessionNameHelper
{
    /// <summary>
    /// Returns a human-readable display name using the same fallback chain as the Tauri reference app:
    /// 1. Last path segment of cwd (when available)
    /// 2. Part after the first "--" in the encoded directory name (Claude CLI encodes paths with dashes)
    /// 3. Raw fallbackDirName as last resort
    /// Returns null when no usable name can be derived (caller decides whether to show or skip).
    /// </summary>
    public static string? GetDisplayName(string? cwd, string? fallbackDirName = null)
    {
        // 1. Try cwd last path segment
        if (!string.IsNullOrEmpty(cwd))
        {
            var lastSeparator = cwd.LastIndexOfAny(['/', '\\']);
            var segment = lastSeparator >= 0 ? cwd[(lastSeparator + 1)..] : cwd;
            if (!string.IsNullOrEmpty(segment))
                return segment;
        }

        // 2. Try decoding the encoded directory name (e.g. "D--myProjects-ccInfoWin" → "ccInfoWin")
        if (!string.IsNullOrEmpty(fallbackDirName))
        {
            var decoded = DecodeProjectDirectory(fallbackDirName);
            if (decoded != null)
                return decoded;
        }

        // 3. No usable name — return null so caller can decide to skip this entry
        return null;
    }

    /// <summary>
    /// Decodes an encoded project directory name (e.g. "D--myProjects-ccInfoWin") to its last segment.
    /// Returns null for null/empty input or when no non-empty segment can be found.
    /// </summary>
    public static string? DecodeProjectDirectory(string? encodedName)
    {
        if (string.IsNullOrEmpty(encodedName))
            return null;

        // Try double-dash convention first: everything after the first "--"
        var doubleDashIndex = encodedName.IndexOf("--", StringComparison.Ordinal);
        if (doubleDashIndex >= 0)
        {
            var afterDash = encodedName[(doubleDashIndex + 2)..];
            // Take the last dash-separated segment
            var parts = afterDash.Split('-');
            for (var i = parts.Length - 1; i >= 0; i--)
            {
                if (!string.IsNullOrEmpty(parts[i]))
                    return parts[i];
            }
        }

        // Fallback: last non-empty segment from single-dash split
        var segments = encodedName.Split('-');
        for (var i = segments.Length - 1; i >= 0; i--)
        {
            if (!string.IsNullOrEmpty(segments[i]))
                return segments[i];
        }

        return null;
    }
}
