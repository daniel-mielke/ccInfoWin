using CCInfoWindows.Helpers;

namespace CCInfoWindows.Tests.Helpers;

/// <summary>
/// Pins the single place that derives the %LOCALAPPDATA%\CCInfoWindows layout. Finding 30 of the
/// 2026-08-06 repo review found the same root recomputed by hand at a dozen sites (one of them a
/// zero-caller duplicate on MainWindow), which is exactly how a relocation ends up half-applied.
/// </summary>
public class AppPathsTests
{
    private const string AppFolderName = "CCInfoWindows";

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
}
