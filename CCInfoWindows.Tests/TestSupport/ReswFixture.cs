using System.Xml.Linq;

namespace CCInfoWindows.Tests.TestSupport;

/// <summary>
/// A localize delegate backed by the SHIPPED resw file of one locale, for the static ViewModel
/// formatters that take the localizer as a delegate. Testing against the real templates is the point:
/// an injected test template cannot catch a translation whose placeholders are swapped or missing.
/// </summary>
internal static class ReswFixture
{
    public static Func<string, string> Load(string locale)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Strings", locale, "Resources.resw");
        Assert.True(File.Exists(path), $"Resw file not found at: {path}");

        var values = XDocument.Load(path).Root!.Elements("data")
            .Where(data => data.Attribute("name") is not null && data.Element("value") is not null)
            .ToDictionary(data => data.Attribute("name")!.Value, data => data.Element("value")!.Value, StringComparer.Ordinal);

        return key => values[key];
    }
}
