using CCInfoWindows.Helpers;

namespace CCInfoWindows.Tests.Helpers;

/// <summary>
/// Pins the single place that derives the %LOCALAPPDATA%\CCInfoWindows layout. Finding 30 of the
/// 2026-08-06 repo review found the same root recomputed by hand at a dozen sites (one of them a
/// zero-caller duplicate on MainWindow), which is exactly how a relocation ends up half-applied.
///
/// The value assertions pin the layout itself; the source scan pins the "derived in exactly one
/// place" property, which no amount of value assertions can express — it is the only guard that
/// still fires when someone adds a twelfth hand-rolled copy next year.
/// </summary>
public class AppPathsTests
{
    private const string AppFolderName = "CCInfoWindows";
    private const string AppPathsFileName = "AppPaths.cs";

    private const string LocalAppDataFolderApi = "SpecialFolder.LocalApplicationData";
    private const string LocalAppDataEnvironmentApi = "GetEnvironmentVariable(\"LOCALAPPDATA\")";

    /// <summary>
    /// The two APIs that yield the LOCALAPPDATA root. A hand-rolled second copy of the layout needs
    /// one of them, so scanning for both catches the shape regardless of which one is reached for.
    /// </summary>
    private static readonly string[] LocalAppDataApis = [LocalAppDataFolderApi, LocalAppDataEnvironmentApi];

    /// <summary>
    /// AppPaths and nothing else. JsonlService held a temporary exemption while its cache-directory
    /// default still rebuilt the root by hand; it now takes AppPaths.DataDirectory, so the list is
    /// closed — a file joining it is the defect, not the exemption.
    /// </summary>
    private static readonly string[] AllowedToDeriveTheRoot = [AppPathsFileName];

    private static string LocalAppData =>
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    [Fact]
    public void DataDirectory_IsTheAppFolderUnderLocalAppData()
    {
        Assert.Equal(Path.Combine(LocalAppData, AppFolderName), AppPaths.DataDirectory);
    }

    [Fact]
    public void WebView2UserDataFolder_LivesInsideTheDataDirectory()
    {
        Assert.Equal(Path.Combine(AppPaths.DataDirectory, "WebView2"), AppPaths.WebView2UserDataFolder);
    }

    [Fact]
    public void CrashLogFile_LivesInsideTheDataDirectory()
    {
        Assert.Equal(Path.Combine(AppPaths.DataDirectory, "crash.log"), AppPaths.CrashLogFile);
    }

    [Fact]
    public void AllPaths_AreRootedAndStable()
    {
        foreach (var path in new[] { AppPaths.DataDirectory, AppPaths.WebView2UserDataFolder, AppPaths.CrashLogFile })
        {
            Assert.True(Path.IsPathRooted(path), $"'{path}' is not an absolute path.");
        }

        // Property getters, so a caller comparing two reads must still get the same string.
        Assert.Equal(AppPaths.DataDirectory, AppPaths.DataDirectory);
        Assert.Equal(AppPaths.CrashLogFile, AppPaths.CrashLogFile);
    }

    [Fact]
    public void AppPaths_IsTheOnlyProductionFileThatDerivesTheLocalAppDataRoot()
    {
        var offenders = ProductionSourceFiles.All()
            .Where(file => !AllowedToDeriveTheRoot.Contains(file.Name, StringComparer.OrdinalIgnoreCase))
            .Where(file => LocalAppDataApis.Any(api => file.Text.Contains(api, StringComparison.Ordinal)))
            .Select(file => file.Name)
            .ToList();

        Assert.True(
            offenders.Count == 0,
            $"These files rebuild the data root instead of using AppPaths.DataDirectory: {string.Join(", ", offenders)}");
    }

    [Fact]
    public void AppPaths_StillDerivesTheRootItself()
    {
        // Guards the scan above against passing vacuously: a typo in the needle, or a relocation of
        // the derivation out of AppPaths, would otherwise leave a broken scan looking like a clean repo.
        var appPaths = ProductionSourceFiles.Read(AppPathsFileName);

        Assert.Contains(LocalAppDataFolderApi, appPaths);
    }
}

/// <summary>
/// Reads the app project's C# sources for the convention scans. Some invariants — "this root is
/// derived in exactly one place", "this test-only member has no production caller" — are properties
/// of the source text and cannot be asserted against the compiled assembly.
/// </summary>
internal static class ProductionSourceFiles
{
    private const string AnchorFileName = "App.xaml.cs";

    /// <summary>Lower bound that makes a broken locator fail loudly instead of scanning nothing.</summary>
    private const int MinimumExpectedFileCount = 20;

    private static readonly Lazy<string> LazySourceRoot = new(LocateSourceRoot);

    // Read once per test run: the sources cannot change mid-run, and several scans share them.
    private static readonly Lazy<IReadOnlyList<SourceFile>> LazyFiles = new(ReadAll);

    internal readonly record struct SourceFile(string Name, string Text);

    internal static IReadOnlyList<SourceFile> All() => LazyFiles.Value;

    /// <summary>
    /// The app project's source root, for scans that read something other than C# — the XAML uid
    /// scanner and the AppTheme.xaml palette mirror both walk up to the same directory.
    /// </summary>
    internal static string Root => LazySourceRoot.Value;

    /// <summary>Text of the one production file with this name, wherever it sits in the tree.</summary>
    internal static string Read(string fileName)
    {
        var matches = All()
            .Where(file => string.Equals(file.Name, fileName, StringComparison.OrdinalIgnoreCase))
            .ToList();

        return Assert.Single(matches).Text;
    }

    private static IReadOnlyList<SourceFile> ReadAll()
    {
        var files = Directory
            .EnumerateFiles(LazySourceRoot.Value, "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(path))
            .Select(path => new SourceFile(Path.GetFileName(path), File.ReadAllText(path)))
            .ToList();

        Assert.True(
            files.Count >= MinimumExpectedFileCount,
            $"Only {files.Count} production sources found under {LazySourceRoot.Value} -- the locator is broken.");

        return files;
    }

    // obj\ and bin\ hold source-generator output and copies of the very files being scanned.
    private static bool IsBuildOutput(string path) =>
        path.Contains(@"\obj\", StringComparison.OrdinalIgnoreCase)
        || path.Contains(@"\bin\", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Walks up from the test output directory to the app's source root — the compiled assembly carries
    /// no source. The one copy: AppPathsTests, DiagnosticChannelConventionTests, ResourceCoverageTests,
    /// ChartColorsTests and AppHostConventionTests all read through here.
    /// </summary>
    private static string LocateSourceRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            var candidate = Path.Combine(directory.FullName, "CCInfoWindows", "CCInfoWindows");
            if (File.Exists(Path.Combine(candidate, AnchorFileName)))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            $"Could not locate the app source directory ({AnchorFileName}) from {AppContext.BaseDirectory}.");
    }
}
