using CCInfoWindows.Tests.Helpers;

namespace CCInfoWindows.Tests.Convention;

/// <summary>
/// The source-tree rules every scanning test shares: how to skip MSBuild's stale copies, and how to
/// read one non-C# app-project file. The build-output rule used to be spelled out at five sites in
/// three forms, and the fifth — the XAML uid scanner — was the only one without an ordinal-ignore-case
/// comparer.
/// </summary>
// ponytail: ProductionSourceFiles now calls IsBuildOutput here rather than keeping its own copy, so the
// rule already has one home. The types stay split because ProductionSourceFiles sits inside another test
// class's file; merging them would move code across six using directives for no behavioural gain.
internal static class SourceTree
{
    /// <summary>obj\ and bin\ hold source-generator output and copies of the very files being scanned.</summary>
    internal static bool IsBuildOutput(string path) =>
        path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
        || path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Text of one app-project file addressed by its path relative to the source root.
    /// <see cref="ProductionSourceFiles"/> indexes *.cs by file name only, so XAML is read through here.
    /// </summary>
    internal static string ReadRelative(string relativePath)
    {
        var path = Path.Combine(ProductionSourceFiles.Root, relativePath);

        Assert.True(File.Exists(path), $"{relativePath} not found under {ProductionSourceFiles.Root}.");

        return File.ReadAllText(path);
    }

    /// <summary>SettingsView.xaml — three suites assert against its markup, and used to locate it twice.</summary>
    internal static string ReadSettingsViewXaml() => ReadRelative(@"Views\SettingsView.xaml");
}
