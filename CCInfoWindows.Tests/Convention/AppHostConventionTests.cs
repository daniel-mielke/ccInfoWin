namespace CCInfoWindows.Tests.Convention;

/// <summary>
/// Guards the application host — App, MainWindow and the MainView dashboard bootstrap. None of these
/// types can be instantiated in xUnit (WinUI 3 needs a XAML host and a WinRT apartment), so the
/// invariants are asserted against the source text, the same way BuildConfigurationTests asserts the
/// installer script and ResourceCoverageTests asserts the resw files.
///
/// Two ViewModel invariants are asserted the same way, deliberately: WHO owns a singleton's lifecycle
/// (finding 29) and WHICH global the language switch aligns (the localisation follow-up). Both are
/// statements about call sites rather than about observable state, and the alternative for the second —
/// a test that sets CultureInfo.DefaultThreadCurrentUICulture — would mutate process-global state that
/// a parallel test collection reads (finding 33).
///
/// Covers findings 4, 10, 17, 18, 19, 29, 30, 34 and 40 of the 2026-08-06 repo review: the bootstrap's
/// silent catch, the teardown that could outrun the setup, the navigation result that was thrown away,
/// the unreachable logout duplicate, the unguarded startup SetLanguage, the transient ViewModel owning
/// singleton lifecycles, the duplicated LOCALAPPDATA paths, the missing diagnostics and the never-closed
/// bridge WebView2.
/// </summary>
public class AppHostConventionTests
{
    private const string AppFile = "App.xaml.cs";
    private const string MainWindowFile = "MainWindow.xaml.cs";
    private const string MainViewFile = @"Views\MainView.xaml.cs";
    private const string MainViewModelFile = @"ViewModels\MainViewModel.cs";
    private const string SettingsViewModelFile = @"ViewModels\SettingsViewModel.cs";
    private const string SettingsViewFile = @"Views\SettingsView.xaml";

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

    [Fact]
    public void App_AlignsTheUiCultureWithTheLocalizerLanguage()
    {
        // WinUI3Localizer only swaps resw values. Without this the resw-supplied date patterns render
        // with the OS language's day and month names, because CountdownFormatter.FormatResetDate and
        // MainViewModel's next-window label format through CultureInfo.CurrentUICulture.
        var app = ReadAppSourceFile(AppFile);

        Assert.Contains("CultureInfo.DefaultThreadCurrentUICulture = culture;", app);
        Assert.Contains("CultureInfo.CurrentUICulture = culture;", app);

        // Deliberate: regional number and date formatting is an OS user setting that a display-language
        // choice must not override, and every numeric formatter here is InvariantCulture-pinned. If this
        // ever becomes wanted, it needs its own decision — not a silent addition.
        Assert.DoesNotContain("DefaultThreadCurrentCulture", app);
    }

    [Fact]
    public void RuntimeLanguageSwitch_AlignsTheUiCultureToo()
    {
        // The startup path and the Settings dropdown are the only two places the language changes, and
        // both have to move CurrentUICulture with it — a switch that only swaps resw values leaves the
        // localized date patterns rendering another language's day and month names.
        var settingsViewModel = ReadAppSourceFile(SettingsViewModelFile);

        Assert.Contains("ApplyUiCulture(languageCode);", settingsViewModel);
        Assert.Contains("CultureInfo.DefaultThreadCurrentUICulture = culture;", settingsViewModel);
        Assert.DoesNotContain("DefaultThreadCurrentCulture", settingsViewModel);

        // Order: the culture may only move once the localizer has accepted the language, otherwise a
        // failed switch would leave the two disagreeing in the opposite direction.
        var switchIndex = settingsViewModel.IndexOf("await LanguageSwitcher(languageCode);", StringComparison.Ordinal);
        var cultureIndex = settingsViewModel.IndexOf("ApplyUiCulture(languageCode);", StringComparison.Ordinal);
        Assert.True(switchIndex >= 0 && switchIndex < cultureIndex,
            "ApplyUiCulture must run after the localizer switch has succeeded.");
    }

    [Fact]
    public void App_StartsTheLifecycleOwningSingletonsOncePerProcess()
    {
        // Finding 29: IJsonlService (FileSystemWatcher + debounce timer) and IUpdateService (hourly
        // PeriodicTimer) are singletons whose Start/Stop used to be driven by a transient ViewModel
        // through MainView's visual-tree membership.
        var app = ReadAppSourceFile(AppFile);

        Assert.Contains("StartBackgroundServices();", app);
        Assert.Contains("jsonlService.InitializeAsync()", app);
        Assert.Contains("StartPeriodicCheck();", app);

        // Not awaited: the cold-start scan is seconds of disk work, and a scan that blocks the first
        // frame is worse than one that runs beside it.
        Assert.Contains("_ = StartJsonlWatcherAsync(", app);
    }

    [Fact]
    public void MainWindow_StopsTheLifecycleOwningSingletonsOnClose()
    {
        var mainWindow = ReadAppSourceFile(MainWindowFile);

        Assert.Contains("_jsonlService.Stop();", mainWindow);
        Assert.Contains("_updateService.StopPeriodicCheck();", mainWindow);

        // The history flush must not be skippable by a teardown failure, so it stays first.
        var flushIndex = mainWindow.IndexOf("_historyService.SaveHistory(snapshot);", StringComparison.Ordinal);
        var stopIndex = mainWindow.IndexOf("StopBackgroundServices();", StringComparison.Ordinal);
        Assert.True(flushIndex >= 0 && flushIndex < stopIndex,
            "OnClosing must flush the usage history before stopping the background services.");
    }

    [Fact]
    public void MainViewModel_DoesNotOwnTheLifecycleOfAnySingleton()
    {
        // The other half of finding 29: MainViewModel is AddTransient. It may read from these services
        // and subscribe to their events, but starting or stopping them from here tore the watcher down
        // on every Settings round-trip — and could stop services a newer MainView was already using.
        var mainViewModel = ReadAppSourceFile(MainViewModelFile);

        Assert.DoesNotContain("_jsonlService.InitializeAsync()", mainViewModel);
        Assert.DoesNotContain("_jsonlService.Stop()", mainViewModel);
        Assert.DoesNotContain("_updateService.StartPeriodicCheck()", mainViewModel);
        Assert.DoesNotContain("_updateService.StopPeriodicCheck()", mainViewModel);

        // The event subscriptions stay, and stay symmetric — a .NET event on a singleton holds a STRONG
        // reference to this transient ViewModel (finding 7).
        Assert.Contains("_jsonlService.DataUpdated += _dataUpdatedHandler;", mainViewModel);
        Assert.Contains("_jsonlService.DataUpdated -= _dataUpdatedHandler;", mainViewModel);
        Assert.Contains("_updateService.UpdateAvailable += OnUpdateAvailable;", mainViewModel);
        Assert.Contains("_updateService.UpdateAvailable -= OnUpdateAvailable;", mainViewModel);
    }

    [Fact]
    public void TheOnlyLogoutIsTheOneBoundInSettingsView()
    {
        // Finding 18: MainViewModel carried a more complete-looking Logout bound in no XAML file, so a
        // maintainer could fix a logout bug there, watch MainViewModelAuthFlowTests go green, and ship a
        // change no user could reach. IWebViewBridge was that command's only use of the dependency.
        var mainViewModel = ReadAppSourceFile(MainViewModelFile);

        Assert.DoesNotContain("IWebViewBridge", mainViewModel);
        Assert.DoesNotContain("private void Logout()", mainViewModel);

        Assert.Contains("ViewModel.LogoutCommand", ReadAppSourceFile(SettingsViewFile));
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
