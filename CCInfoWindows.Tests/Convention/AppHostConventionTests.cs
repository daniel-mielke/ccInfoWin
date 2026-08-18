using CCInfoWindows.Tests.Helpers;

namespace CCInfoWindows.Tests.Convention;

/// <summary>
/// Guards the application host — App, MainWindow and the MainView dashboard bootstrap. None of these
/// types can be instantiated in xUnit (WinUI 3 needs a XAML host and a WinRT apartment), so the
/// invariants are asserted against the source text, the same way BuildConfigurationTests asserts the
/// installer script and ResourceCoverageTests asserts the resw files.
///
/// Two ViewModel invariants are asserted the same way, deliberately: WHO owns a singleton's lifecycle
/// (finding 29) and THAT the language switch aligns the UI culture (the localisation follow-up). Both are
/// statements about call sites rather than about observable state — for the second, a test that let the
/// real assignment run would mutate process-global culture state that a parallel test collection reads
/// (finding 33). What the alignment then DOES with the language is UiCultureTests' subject.
///
/// The source-root walk lives in Helpers/ProductionSourceFiles, shared with AppPathsTests,
/// DiagnosticChannelConventionTests, ChartColorsTests and ResourceCoverageTests; this class carried a
/// fourth private copy of it until the finding-30 wave.
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
    private const string MainViewFile = "MainView.xaml.cs";
    private const string MainViewModelFile = "MainViewModel.cs";
    private const string SettingsViewModelFile = "SettingsViewModel.cs";

    /// <summary>Not a .cs file, so it is read by relative path rather than through the by-name index.</summary>
    private const string SettingsViewXamlPath = @"Views\SettingsView.xaml";
    private const string MainViewXamlPath = @"Views\MainView.xaml";

    private static readonly string[] AppHostFiles = [AppFile, MainWindowFile, MainViewFile];

    [Fact]
    public void AppHostFiles_ReportHandledFailuresThroughAppLog_NotDebugWriteLine()
    {
        // Debug.WriteLine carries [Conditional("DEBUG")]: in the Release build CLAUDE.md mandates, those
        // catch bodies are literally empty. AppLog is the Release-safe channel.
        foreach (var file in AppHostFiles)
        {
            Assert.DoesNotContain("Debug.WriteLine", ProductionSourceFiles.Read(file));
        }
    }

    [Fact]
    public void App_WritesTheCrashLogAndTheDataRootThroughAppPaths()
    {
        // Only the positive half lives here. The negative half — "no file outside AppPaths derives the
        // LOCALAPPDATA root" — used to be a three-file loop and is now a repo-wide scan in
        // AppPathsTests, which strictly subsumes it. This half is not subsumed: App could stop writing a
        // crash log altogether and that scan would still report a clean repo.
        var app = ProductionSourceFiles.Read(AppFile);

        Assert.Contains("AppPaths.CrashLogFile", app);
        Assert.Contains("AppPaths.DataDirectory", app);
    }

    [Fact]
    public void MainWindow_DoesNotRedeclareTheWebView2UserDataFolder()
    {
        // The zero-caller copy duplicated AppPaths.WebView2UserDataFolder; LoginViewModel and MainView
        // both resolve it from AppPaths.
        Assert.DoesNotContain("WebView2UserDataFolder", ProductionSourceFiles.Read(MainWindowFile));
    }

    [Fact]
    public void MainView_SurfacesBootstrapFailuresInTheErrorBannerAndTheLog()
    {
        var mainView = ProductionSourceFiles.Read(MainViewFile);

        Assert.Contains("ViewModel.HasApiError = true;", mainView);
        Assert.Contains("AppLog.Write(BootstrapLogSource", mainView);

        // Generic on screen, detail in the log (CLAUDE.md): the banner text must not interpolate the
        // exception, and it must come from a single-segment localizer key resolved through the shared
        // rule — the private copy that used to live here accepted an echoed uid, so an unbuilt localizer
        // painted the resource key onto the InfoBar.
        Assert.DoesNotContain("ApiErrorMessage = $\"", mainView);
        Assert.Contains("LocalizedText.Resolve(", mainView);
        Assert.DoesNotContain("GetLocalizedString(BootstrapFailureMessageKey)", mainView);

        // WinUI3Localizer 2.3.0 splits resw keys at the first dot, so a multi-segment key returns empty.
        var messageKey = BootstrapFailureMessageKeyValue(mainView);
        Assert.False(messageKey.Contains('.'), $"'{messageKey}' must be a single-segment localizer key.");
    }

    [Fact]
    public void MainView_StopsWhatItStartedWhenTeardownRacesTheBootstrap()
    {
        var mainView = ProductionSourceFiles.Read(MainViewFile);

        Assert.Contains("_bootstrapCancellation?.Cancel();", mainView);
        Assert.Contains("if (bootstrap.IsCancellationRequested) return;", mainView);

        // Twice: the normal teardown in OnUnloaded, plus the lost-race branch after InitializeAsync
        // finishes on an already-detached ViewModel.
        Assert.Equal(2, Occurrences(mainView, "ViewModel.StopTimers();"));
    }

    [Fact]
    public void MainView_TreatsAFailedBridgeNavigationAsAFailureAndBoundsTheWait()
    {
        var mainView = ProductionSourceFiles.Read(MainViewFile);

        // A handler typed (object, object) discards IsSuccess, so an error page looked like a loaded page.
        Assert.Contains("CoreWebView2NavigationCompletedEventArgs args", mainView);
        Assert.Contains("args.IsSuccess", mainView);
        Assert.Contains("WaitAsync(BridgeNavigationTimeout", mainView);
        Assert.Contains("NavigationCompleted -= OnNavigationCompleted;", mainView);
    }

    [Fact]
    public void MainView_ReleasesTheHiddenBridgeWebViewOnUnload()
    {
        var mainView = ProductionSourceFiles.Read(MainViewFile);

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
        var app = ProductionSourceFiles.Read(AppFile);

        Assert.Contains("AppLog.Write(UnhandledExceptionLogSource, e.Exception);", app);
        Assert.Contains("AppLog.Write(LaunchLogSource, ex);", app);

        // Keeping the process alive is a deliberate choice for a monitor; it is only defensible while
        // every occurrence is recorded.
        Assert.Contains("e.Handled = true;", app);
    }

    [Fact]
    public void App_DegradesToTheDefaultLanguageInsteadOfRefusingToStart()
    {
        var app = ProductionSourceFiles.Read(AppFile);

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
        var app = ProductionSourceFiles.Read(AppFile);

        // Both branches: the persisted language, and the default the app degrades to when that value is
        // rejected. The second one is the easy one to forget, and forgetting it leaves the culture on the
        // OS language while the screen speaks the fallback one.
        Assert.Contains("UiCulture.Apply(requested, LocalizerLogSource);", app);
        Assert.Contains("UiCulture.Apply(DefaultLanguage, LocalizerLogSource);", app);

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
        var settingsViewModel = ProductionSourceFiles.Read(SettingsViewModelFile);

        Assert.Contains("UiCulture.Apply(languageCode", settingsViewModel);
        Assert.DoesNotContain("DefaultThreadCurrentCulture", settingsViewModel);

        // Order: the culture may only move once the localizer has accepted the language, otherwise a
        // failed switch would leave the two disagreeing in the opposite direction.
        var switchIndex = settingsViewModel.IndexOf("await LanguageSwitcher(languageCode);", StringComparison.Ordinal);
        var cultureIndex = settingsViewModel.IndexOf("UiCulture.Apply(languageCode", StringComparison.Ordinal);
        Assert.True(switchIndex >= 0 && switchIndex < cultureIndex,
            "UiCulture.Apply must run after the localizer switch has succeeded.");
    }

    [Fact]
    public void App_StartsTheLifecycleOwningSingletonsOncePerProcess()
    {
        // Finding 29: IJsonlService (FileSystemWatcher + debounce timer) and IUpdateService (hourly
        // PeriodicTimer) are singletons whose Start/Stop used to be driven by a transient ViewModel
        // through MainView's visual-tree membership.
        var app = ProductionSourceFiles.Read(AppFile);

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
        var mainWindow = ProductionSourceFiles.Read(MainWindowFile);

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
        var mainViewModel = ProductionSourceFiles.Read(MainViewModelFile);

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
        var mainViewModel = ProductionSourceFiles.Read(MainViewModelFile);

        Assert.DoesNotContain("IWebViewBridge", mainViewModel);
        Assert.DoesNotContain("private void Logout()", mainViewModel);

        Assert.Contains("ViewModel.LogoutCommand", ReadSettingsViewXaml());
    }

    /// <summary>
    /// G-2 (Windows-only): RetireStaleRows is a static pure function covered by its own tests, but
    /// nothing there proves a timer ever calls it — and unwired, the whole fix is inert. StartTimers
    /// cannot be exercised headlessly (DispatcherQueue.GetForCurrentThread() returns null), so the
    /// wiring is asserted on the source text, the same way MainView's teardown is above.
    /// </summary>
    [Fact]
    public void TheCountdownTimerDrivesTheSubagentRowRetirement()
    {
        var mainViewModel = ProductionSourceFiles.Read(MainViewModelFile);

        Assert.Contains("_countdownTimer.Tick += (s, e) => OnCountdownTick();", mainViewModel);
        Assert.Contains("RetireStaleRows(SubagentContexts, DateTimeOffset.UtcNow);", mainViewModel);
    }

    /// <summary>
    /// The hover card's keyboard path (Windows-only). None of it is reachable from a headless test —
    /// it is XAML event wiring on a template instance — so the contract is asserted on the source,
    /// the same way the countdown wiring is above.
    ///
    /// The tab stop rides on the label TextBlock rather than on the row Grid on purpose: that
    /// TextBlock is Collapsed on plain subagent rows, and a Collapsed element is not a tab stop, so
    /// only rows that actually HAVE a card can be focused into one. Moving these attributes up to
    /// the Grid would silently put every plain row in the tab order with nothing to show.
    /// </summary>
    [Fact]
    public void TheWorkflowHoverCardIsReachableByKeyboard()
    {
        var label = WorkflowLabelElement(ReadMainViewXaml());
        var codeBehind = ProductionSourceFiles.Read(MainViewFile);

        Assert.Contains("IsTabStop=\"True\"", label);
        Assert.Contains("GotFocus=\"OnWorkflowRowGotFocus\"", label);
        Assert.Contains("LostFocus=\"OnWorkflowRowLostFocus\"", label);
        Assert.Contains("KeyDown=\"OnWorkflowRowKeyDown\"", label);

        // Focus and hover must open the SAME card, or the keyboard path drifts from the mouse path.
        Assert.Contains("private void OnWorkflowRowGotFocus(object sender, RoutedEventArgs e) =>", codeBehind);
        Assert.Contains("ShowWorkflowTooltip(sender);", codeBehind);

        // Escape closes it: a card that opens and cannot be dismissed is worse than none.
        Assert.Contains("case VirtualKey.Escape:", codeBehind);

        // And Enter/Space must reopen it. Measured in the running app: Escape leaves focus ON the
        // row, so GotFocus never fires again — without this branch the card is gone for good until
        // the user tabs away and back. The branch has to sit BEFORE the visibility guard, which is
        // what the index comparison pins.
        Assert.Contains("if (e.Key is VirtualKey.Enter or VirtualKey.Space)", codeBehind);

        var keyDown = codeBehind.IndexOf("private void OnWorkflowRowKeyDown", StringComparison.Ordinal);
        var reopen = codeBehind.IndexOf("VirtualKey.Enter or VirtualKey.Space", keyDown, StringComparison.Ordinal);
        var guard = codeBehind.IndexOf("WorkflowTooltipOverlay.Visibility != Visibility.Visible", keyDown, StringComparison.Ordinal);
        Assert.True(
            reopen < guard,
            "The Enter/Space branch must precede the visibility guard, or reopening a closed card is unreachable.");
    }

    /// <summary>
    /// The card closes when the run behind it is retired, and does NOT close when a poll rebuilds the
    /// list — the second half is why this is asserted at all. Closing on Reset made the card blink
    /// away every few seconds while it was being read, so the handler has to key on Remove.
    /// </summary>
    [Fact]
    public void TheWorkflowHoverCardClosesWithTheRunItShows()
    {
        var codeBehind = ProductionSourceFiles.Read(MainViewFile);

        Assert.Contains("ViewModel.SubagentContexts.CollectionChanged += OnSubagentContextsChanged;", codeBehind);
        Assert.Contains("ViewModel.SubagentContexts.CollectionChanged -= OnSubagentContextsChanged;", codeBehind);
        Assert.Contains("e.Action != NotifyCollectionChangedAction.Remove", codeBehind);

        // The run id lives on the row, so Tag has to hand over the row and not just its tooltip.
        Assert.Contains("Tag=\"{x:Bind}\"", WorkflowLabelElement(ReadMainViewXaml()));
    }

    /// <summary>
    /// U-21 and U-6 on the row glyph: it is drawn in the brush ChartColorsTests measures against the
    /// 3:1 floor, and it stays out of the automation tree.
    /// </summary>
    [Fact]
    public void TheSubagentRowGlyphUsesTheContrastCheckedBrushAndIsNotAnnounced()
    {
        var glyph = ElementAround(ReadMainViewXaml(), "Text=\"{x:Bind Icon}\"");

        Assert.Contains("Foreground=\"{ThemeResource SecondaryTextBrush}\"", glyph);
        Assert.Contains("AutomationProperties.AccessibilityView=\"Raw\"", glyph);
    }

    /// <summary>
    /// The workflow row's label TextBlock, the element the card hangs off. Anchored on its
    /// PointerEntered handler rather than on its Text binding: {x:Bind Label} also appears in the
    /// tooltip line template, and ElementAround refuses an ambiguous marker.
    /// </summary>
    private static string WorkflowLabelElement(string mainView) =>
        ElementAround(mainView, "PointerEntered=\"OnWorkflowRowPointerEntered\"");

    /// <summary>
    /// The one XAML element carrying <paramref name="marker"/>, from its opening angle bracket to the
    /// end of its attribute list. Keeps each assertion inside a single element instead of matching an
    /// attribute that happens to exist elsewhere in the file.
    /// </summary>
    private static string ElementAround(string xaml, string marker)
    {
        var hit = xaml.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(hit >= 0, $"MainView.xaml no longer contains {marker}.");
        Assert.Equal(hit, xaml.LastIndexOf(marker, StringComparison.Ordinal));

        var start = xaml.LastIndexOf('<', hit);
        var end = xaml.IndexOf('>', hit);
        Assert.True(start >= 0 && end > start, $"Could not delimit the element carrying {marker}.");

        return xaml[start..end];
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

    /// <summary>MainView.xaml, read the same way and for the same reason as its SettingsView twin.</summary>
    private static string ReadMainViewXaml()
    {
        var path = Path.Combine(ProductionSourceFiles.Root, MainViewXamlPath);

        Assert.True(File.Exists(path), $"{MainViewXamlPath} not found under {ProductionSourceFiles.Root}.");

        return File.ReadAllText(path);
    }


    /// <summary>
    /// The one assertion target that is not C#. ProductionSourceFiles indexes *.cs by file name, so the
    /// XAML is read from the source root it already resolves — which is why the fourth private copy of
    /// that directory walk could be deleted from this class.
    /// </summary>
    private static string ReadSettingsViewXaml()
    {
        var path = Path.Combine(ProductionSourceFiles.Root, SettingsViewXamlPath);

        Assert.True(File.Exists(path), $"{SettingsViewXamlPath} not found under {ProductionSourceFiles.Root}.");

        return File.ReadAllText(path);
    }
}
