namespace CCInfoWindows.Helpers;

/// <summary>
/// Centralized application paths for data and cache directories.
/// </summary>
public static class AppPaths
{
    private static readonly string AppDataRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CCInfoWindows");

    /// <summary>
    /// WebView2 User Data Folder for browser isolation.
    /// </summary>
    public static string WebView2UserDataFolder => Path.Combine(AppDataRoot, "WebView2");

    /// <summary>
    /// Root directory for application-local data (caches, logs, settings).
    /// </summary>
    public static string DataDirectory => AppDataRoot;
}
