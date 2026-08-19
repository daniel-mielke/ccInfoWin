using System.Xml.Linq;

namespace CCInfoWindows.Tests.Localization;

/// <summary>
/// The two resw locales, read from the copies MSBuild puts beside the test assembly. xUnit cannot
/// initialize the WinUI3Localizer host (RESEARCH Pitfall 1), so every localization assertion in the
/// suite parses the dictionaries itself — and parses them here, so a third locale is one edit rather
/// than a two-file edit where the file that is missed keeps scanning en-US/de-DE and reports green.
/// </summary>
internal static class ReswFiles
{
    internal const string EnUsRelativePath = "Strings/en-US/Resources.resw";
    internal const string DeDeRelativePath = "Strings/de-DE/Resources.resw";

    internal static IEnumerable<(string Locale, string Path)> Locales()
    {
        yield return ("en-US", EnUsRelativePath);
        yield return ("de-DE", DeDeRelativePath);
    }

    internal static string FullPath(string relativePath) =>
        Path.Combine(AppContext.BaseDirectory, relativePath);

    /// <summary>
    /// Key-to-value map for one locale. Ordinal by choice: the localizer's lookups are case-sensitive,
    /// so a test must not match a key the running app would miss.
    /// </summary>
    internal static Dictionary<string, string> Load(string relativePath)
    {
        var fullPath = FullPath(relativePath);
        Assert.True(File.Exists(fullPath), $"Resw file not found at: {fullPath}");

        var doc = XDocument.Load(fullPath);
        var dataElements = doc.Root?.Elements("data") ?? Enumerable.Empty<XElement>();

        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var data in dataElements)
        {
            var name = data.Attribute("name")?.Value;
            var value = data.Element("value")?.Value;
            if (name != null && value != null)
            {
                result[name] = value;
            }
        }

        return result;
    }
}
