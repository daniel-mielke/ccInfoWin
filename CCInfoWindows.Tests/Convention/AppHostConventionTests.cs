namespace CCInfoWindows.Tests.Convention;

/// <summary>
/// Guards the application host — App, MainWindow and the MainView dashboard bootstrap. None of these
/// types can be instantiated in xUnit (WinUI 3 needs a XAML host and a WinRT apartment), so the
/// invariants are asserted against the source text, the same way BuildConfigurationTests asserts the
/// installer script and ResourceCoverageTests asserts the resw files.
///
/// Covers findings 4, 10, 17, 19, 30, 34 and 40 of the 2026-08-06 repo review: the bootstrap's silent
/// catch, the teardown that could outrun the setup, the navigation result that was thrown away, the
/// unguarded startup SetLanguage, the duplicated LOCALAPPDATA paths, the missing diagnostics and the
/// never-closed bridge WebView2.
/// </summary>
public class AppHostConventionTests
{
    private const string AppFile = "App.xaml.cs";
    private const string MainWindowFile = "MainWindow.xaml.cs";
    private const string MainViewFile = @"Views\MainView.xaml.cs";

    private static readonly string[] AppHostFiles = [AppFile, MainWindowFile, MainViewFile];

    [Fact]
    public void AppHostFiles_ReportHandledFailuresThroughAppLog_NotDebugWriteLine()
    {
        // Debug.WriteLine carries [Conditional("DEBUG")]: in the Release build CLAUDE.md mandates, those
        // catch bodies are literally empty. AppLog is the Release-safe channel.
        foreach (var file in AppHostFiles)
        {
            Assert.DoesNotContain("Debug.WriteLine", ReadAppSourceFile(file));
        }
    }

    [Fact]
    public void AppHostFiles_DeriveTheDataRootFromAppPaths()
    {
        foreach (var file in AppHostFiles)
        {
            Assert.DoesNotContain("SpecialFolder.LocalApplicationData", ReadAppSourceFile(file));
        }

        var app = ReadAppSourceFile(AppFile);
        Assert.Contains("AppPaths.CrashLogFile", app);
        Assert.Contains("AppPaths.DataDirectory", app);
    }

    [Fact]
    public void MainWindow_DoesNotRedeclareTheWebView2UserDataFolder()
    {
        // The zero-caller copy duplicated AppPaths.WebView2UserDataFolder; LoginViewModel and MainView
        // both resolve it from AppPaths.
        Assert.DoesNotContain("WebView2UserDataFolder", ReadAppSourceFile(MainWindowFile));
    }

    [Fact]
    public void MainView_SurfacesBootstrapFailuresInTheErrorBannerAndTheLog()
    {
        var mainView = ReadAppSourceFile(MainViewFile);

        Assert.Contains("ViewModel.HasApiError = true;", mainView);
        Assert.Contains("AppLog.Write(BootstrapLogSource", mainView);

        // Generic on screen, detail in the log (CLAUDE.md): the banner text must not interpolate the
        // exception, and it must come from a single-segment localizer key.
        Assert.DoesNotContain("ApiErrorMessage = $\"", mainView);
        Assert.Contains("GetLocalizedString(BootstrapFailureMessageKey)", mainView);

        // WinUI3Localizer 2.3.0 splits resw keys at the first dot, so a multi-segment key returns empty.
        var messageKey = BootstrapFailureMessageKeyValue(mainView);
        Assert.False(messageKey.Contains('.'), $"'{messageKey}' must be a single-segment localizer key.");
    }

    [Fact]
    public void MainView_StopsWhatItStartedWhenTeardownRacesTheBootstrap()
    {
        var mainView = ReadAppSourceFile(MainViewFile);

        Assert.Contains("_bootstrapCancellation?.Cancel();", mainView);
        Assert.Contains("if (bootstrap.IsCancellationRequested) return;", mainView);

        // Twice: the normal teardown in OnUnloaded, plus the lost-race branch after InitializeAsync
        // finishes on an already-detached ViewModel.
        Assert.Equal(2, Occurrences(mainView, "ViewModel.StopTimers();"));
    }

    [Fact]
    public void MainView_TreatsAFailedBridgeNavigationAsAFailureAndBoundsTheWait()
    {
        var mainView = ReadAppSourceFile(MainViewFile);

        // A handler typed (object, object) discards IsSuccess, so an error page looked like a loaded page.
        Assert.Contains("CoreWebView2NavigationCompletedEventArgs args", mainView);
        Assert.Contains("args.IsSuccess", mainView);
        Assert.Contains("WaitAsync(BridgeNavigationTimeout", mainView);
        Assert.Contains("NavigationCompleted -= OnNavigationCompleted;", mainView);
    }

    [Fact]
    public void MainView_ReleasesTheHiddenBridgeWebViewOnUnload()
    {
        var mainView = ReadAppSourceFile(MainViewFile);

        Assert.Contains("ApiBridgeWebView.Close();", mainView);

        // Reset() must precede Close(), otherwise the bridge keeps a closed CoreWebView2.
        var resetIndex = mainView.IndexOf(".Reset();", StringComparison.Ordinal);
        var closeIndex = mainView.IndexOf("ApiBridgeWebView.Close();", StringComparison.Ordinal);
        Assert.True(resetIndex >= 0 && resetIndex < closeIndex,
            "WebViewBridge.Reset() must run before ApiBridgeWebView.Close().");
    }

    [Fact]
    public void App_RecordsEveryUnhandledExceptionBeforeSwallowingIt()
    {
        var app = ReadAppSourceFile(AppFile);

        Assert.Contains("AppLog.Write(UnhandledExceptionLogSource, e.Exception);", app);
        Assert.Contains("AppLog.Write(LaunchLogSource, ex);", app);

        // Keeping the process alive is a deliberate choice for a monitor; it is only defensible while
        // every occurrence is recorded.
        Assert.Contains("e.Handled = true;", app);
    }

    [Fact]
    public void App_DegradesToTheDefaultLanguageInsteadOfRefusingToStart()
    {
        var app = ReadAppSourceFile(AppFile);

        Assert.Contains("await ApplyPersistedLanguageAsync(appSettings.Language);", app);
        Assert.DoesNotContain("await Localizer.Get().SetLanguage(appSettings.Language);", app);
        Assert.Contains("AppLog.Write(", SetLanguageMethodBody(app));
    }

    /// <summary>Extracts the ApplyPersistedLanguageAsync body so the guard cannot be asserted from elsewhere.</summary>
    private static string SetLanguageMethodBody(string app)
    {
        const string signature = "private static async Task ApplyPersistedLanguageAsync";

        var start = app.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"{AppFile} no longer declares {signature}.");

        var body = app[start..];
        Assert.Contains("try", body[..body.IndexOf("SetLanguage", StringComparison.Ordinal)]);

        return body;
    }

    private static string BootstrapFailureMessageKeyValue(string mainView)
    {
        const string declaration = "BootstrapFailureMessageKey = \"";

        var start = mainView.IndexOf(declaration, StringComparison.Ordinal);
        Assert.True(start >= 0, $"{MainViewFile} no longer declares BootstrapFailureMessageKey.");

        var valueStart = start + declaration.Length;
        var valueEnd = mainView.IndexOf('"', valueStart);
        Assert.True(valueEnd > valueStart, "BootstrapFailureMessageKey is not a string literal.");

        return mainView[valueStart..valueEnd];
    }

    private static int Occurrences(string text, string value)
    {
        var count = 0;
        var index = text.IndexOf(value, StringComparison.Ordinal);
        while (index >= 0)
        {
            count++;
            index = text.IndexOf(value, index + value.Length, StringComparison.Ordinal);
        }

        return count;
    }

    private static string ReadAppSourceFile(string relativePath) =>
        File.ReadAllText(Path.Combine(FindAppSourceDirectory(), relativePath));

    /// <summary>
    /// Walks up from the test output directory to the app's source root (mirrors the locator in
    /// ResourceCoverageTests — the compiled assembly carries no source).
    /// </summary>
    private static string FindAppSourceDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            var candidate = Path.Combine(directory.FullName, "CCInfoWindows", "CCInfoWindows");
            if (File.Exists(Path.Combine(candidate, AppFile)))
            {
                return candidate;
            }
            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            $"Could not locate the app source directory ({AppFile}) from {AppContext.BaseDirectory}.");
    }
}
