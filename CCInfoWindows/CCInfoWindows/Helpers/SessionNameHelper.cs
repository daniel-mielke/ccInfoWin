namespace CCInfoWindows.Helpers;

/// <summary>
/// Extracts human-readable session names from working directory paths and encoded directory names.
/// </summary>
public static class SessionNameHelper
{
    private const string UnknownProjectName = "Unbekanntes Projekt";

    /// <summary>
    /// Returns the last path segment of the given working directory path.
    /// Returns "Unbekanntes Projekt" for null or empty input.
    /// </summary>
    public static string GetDisplayName(string? cwd)
    {
        if (string.IsNullOrEmpty(cwd))
            return UnknownProjectName;

        var lastSeparator = cwd.LastIndexOfAny(['/', '\\']);
        if (lastSeparator < 0)
            return string.IsNullOrEmpty(cwd) ? UnknownProjectName : cwd;

        var segment = cwd[(lastSeparator + 1)..];
        return string.IsNullOrEmpty(segment) ? UnknownProjectName : segment;
    }

    /// <summary>
    /// Decodes an encoded project directory name (e.g. "D--myProjects-ccInfoWin") to its last segment.
    /// Returns "Unbekanntes Projekt" for null or empty input.
    /// </summary>
    public static string DecodeProjectDirectory(string? encodedName)
    {
        if (string.IsNullOrEmpty(encodedName))
            return UnknownProjectName;

        var parts = encodedName.Split('-');
        for (var i = parts.Length - 1; i >= 0; i--)
        {
            if (!string.IsNullOrEmpty(parts[i]))
                return parts[i];
        }

        return UnknownProjectName;
    }
}
