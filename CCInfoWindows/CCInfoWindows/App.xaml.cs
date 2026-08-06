using System.Globalization;
using System.Net.Http;
using CCInfoWindows.Helpers;
using CCInfoWindows.Services;
using CCInfoWindows.Services.Interfaces;
using CCInfoWindows.ViewModels;
using CCInfoWindows.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppNotifications;
using WinUI3Localizer;

namespace CCInfoWindows;

/// <summary>
/// Application entry point with DI container configuration and startup token routing.
/// </summary>
public partial class App : Application
{
    private const string LaunchLogSource = "App.OnLaunched";
    private const string UnhandledExceptionLogSource = "App.OnUnhandledException";
    private const string LocalizerLogSource = "App.InitializeLocalizerAsync";
    private const string BackgroundServiceLogSource = "App.StartBackgroundServices";
    private const string DefaultLanguage = "en-US";

    public static IServiceProvider Services { get; private set; } = null!;
    public static Window? MainWindow { get; private set; }

    private Window? _window;

    public App()
    {
        InitializeComponent();
        UnhandledException += OnUnhandledException;
    }

    private static void OnNotificationInvoked(AppNotificationManager sender, AppNotificationActivatedEventArgs args)
    {
        // No action needed -- toast click brings app to foreground automatically
    }

    private static void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        AppLog.Write(UnhandledExceptionLogSource, e.Exception);
        AppendToCrashLog($"{e.Exception.GetType().Name}: {e.Exception.Message}\n{e.Exception.StackTrace}\n");

        // Deliberately kept: WinUI 3 offers no signal for whether an exception is fatal, and for a usage
        // monitor a process that vanishes mid-session is worse than one that survives a failed background
        // tick. What made recurring failures invisible was the missing log, not this flag — every occurrence
        // now also lands in the size-capped app.log.
        e.Handled = true;
    }

    /// <summary>
    /// Appends one entry to the crash log. Guarded because it runs inside the last-chance exception handler:
    /// a failing write here (full disk, locked file) would turn a handled exception into a process kill.
    /// </summary>
    private static void AppendToCrashLog(string message)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.DataDirectory);
            File.AppendAllText(AppPaths.CrashLogFile, $"[{DateTime.Now:O}] {message}\n");
        }
        catch (Exception ex)
        {
            // The original failure is already in app.log; losing its crash.log copy is not worth a crash.
            AppLog.Write("App.AppendToCrashLog", ex);
        }
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            Services = ConfigureServices();

            await InitializeLocalizerAsync();

            if (AppNotificationManager.IsSupported())
            {
                var notificationManager = AppNotificationManager.Default;
                notificationManager.NotificationInvoked += OnNotificationInvoked;
                notificationManager.Register();
            }

            _window = new MainWindow();
            MainWindow = _window;
            _window.Activate();

            ApplyPersistedTheme();
            StartBackgroundServices();
            await RouteOnStartupAsync();
        }
        catch (Exception ex)
        {
            AppLog.Write(LaunchLogSource, ex);
            AppendToCrashLog($"OnLaunched failed: {ex.GetType().Name}: {ex.Message}");
            Exit();
        }
    }

    /// <summary>
    /// Initializes WinUI3Localizer with the Strings folder and applies the persisted language preference.
    /// Must be called before any Window is created.
    /// </summary>
    private async Task InitializeLocalizerAsync()
    {
        var stringsFolderPath = Path.Combine(AppContext.BaseDirectory, "Strings");

        await new LocalizerBuilder()
            .AddStringResourcesFolderForLanguageDictionaries(stringsFolderPath)
            .SetOptions(options =>
            {
                options.DefaultLanguage = DefaultLanguage;
            })
            .Build();

        var settingsService = Services.GetRequiredService<ISettingsService>();
        var appSettings = settingsService.LoadSettings();

        // Called even for an empty persisted value: the language decides CurrentUICulture too, and
        // leaving that on the OS language rendered the localizer's default-language date patterns with
        // another language's day and month names.
        await ApplyPersistedLanguageAsync(appSettings.Language);
    }

    /// <summary>
    /// Applies a persisted language and degrades to the localizer default when the value is not one the app
    /// ships. settings.json lives in user-writable %LOCALAPPDATA%, so it is untrusted input: WinUI3Localizer
    /// throws for an unknown language, and an escaping throw here reaches OnLaunched's catch, which calls
    /// Exit() — one bad string would make the app refuse to start on every launch with no UI way back.
    /// This is the second layer behind SettingsService's allow-list, for callers that bypass it.
    /// </summary>
    private static async Task ApplyPersistedLanguageAsync(string? language)
    {
        var requested = string.IsNullOrWhiteSpace(language) ? DefaultLanguage : language;

        try
        {
            await Localizer.Get().SetLanguage(requested);
            ApplyUiCulture(requested);
        }
        catch (Exception ex)
        {
            // The rejected value is logged so the user can repair settings.json; AppLog folds control
            // characters, so a hand-edited value cannot forge a second log entry.
            AppLog.Write(
                LocalizerLogSource,
                ex,
                $"unsupported persisted language '{requested}', falling back to {DefaultLanguage}");
            ApplyUiCulture(DefaultLanguage);
        }
    }

    /// <summary>
    /// Points <see cref="CultureInfo.CurrentUICulture"/> at the language the localizer is showing.
    /// WinUI3Localizer only swaps resw values, so without this the resw-supplied date patterns render
    /// with the OS language's day and month names — CountdownFormatter.FormatResetDate and
    /// MainViewModel's next-window label both format through CurrentUICulture.
    ///
    /// CurrentCulture is deliberately left on the OS regional setting: which language the UI speaks and
    /// how the user wants numbers and dates formatted are independent Windows settings, and every
    /// numeric formatter in this app is pinned to InvariantCulture anyway. SettingsViewModel repeats
    /// this for the runtime language switch, which is the only other place the language changes.
    /// </summary>
    private static void ApplyUiCulture(string language)
    {
        try
        {
            var culture = CultureInfo.GetCultureInfo(language);
            CultureInfo.DefaultThreadCurrentUICulture = culture;
            CultureInfo.CurrentUICulture = culture;
        }
        catch (CultureNotFoundException ex)
        {
            AppLog.Write(LocalizerLogSource, ex, $"'{language}' is not a culture this system knows");
        }
    }

    /// <summary>
    /// Starts the two singletons that own process-wide resources — the JSONL file watcher and the
    /// hourly update timer — exactly once per process (finding 29). They used to be started and
    /// stopped by MainViewModel, a transient ViewModel whose lifetime is MainView's visual-tree
    /// membership, so every Settings round-trip disposed the watcher and paid a full forced re-scan of
    /// ~/.claude/projects on the way back, and a user who opened Settings more often than hourly never
    /// completed an update check. MainWindow.OnClosing stops them again.
    ///
    /// Called after the window is activated and deliberately not awaited: the cold-start scan is
    /// seconds of disk work on a large corpus, and a scan that blocks the first frame is worse than
    /// one that runs beside it. The dashboard needs no scan of its own — it subscribes to DataUpdated
    /// and reads the published snapshot.
    /// </summary>
    private static void StartBackgroundServices()
    {
        _ = StartJsonlWatcherAsync(Services.GetRequiredService<IJsonlService>());

        // The first PeriodicTimer tick is an hour away, so this starts the schedule only; the
        // dashboard runs its own one-shot check when it loads.
        Services.GetRequiredService<IUpdateService>().StartPeriodicCheck();
    }

    /// <summary>
    /// Runs the initial JSONL scan. Nothing awaits the returned task, so every failure has to be
    /// caught here: an escaping exception would be an unobserved task exception, and "the session
    /// dropdown is empty and the chart has nothing in it" is undiagnosable without it on disk.
    /// </summary>
    private static async Task StartJsonlWatcherAsync(IJsonlService jsonlService)
    {
        try
        {
            await jsonlService.InitializeAsync();
        }
        catch (Exception ex)
        {
            AppLog.Write(BackgroundServiceLogSource, ex, "the initial JSONL scan failed");
        }
    }

    /// <summary>
    /// Loads persisted color mode setting and applies it to the root FrameworkElement.
    /// </summary>
    private void ApplyPersistedTheme()
    {
        var settings = Services.GetRequiredService<ISettingsService>().LoadSettings();

        if (_window?.Content is FrameworkElement fe)
        {
            fe.RequestedTheme = settings.ColorMode == "light"
                ? ElementTheme.Light
                : ElementTheme.Dark;
        }
    }

    /// <summary>
    /// Checks stored token validity and navigates to MainView or LoginView accordingly.
    /// </summary>
    private static Task RouteOnStartupAsync()
    {
        var navigationService = Services.GetRequiredService<INavigationService>();
        var credentialService = Services.GetRequiredService<ICredentialService>();

        if (credentialService.HasValidToken())
        {
            navigationService.NavigateTo<MainView>();
        }
        else
        {
            navigationService.NavigateTo<LoginView>();
        }

        return Task.CompletedTask;
    }

    private static IServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();

        // Infrastructure
        services.AddSingleton<HttpClient>();
        services.AddSingleton<IDispatcherQueue, WinuiDispatcherQueueAdapter>();   // DISPATCH-02 (Phase 24, L-02)

        // Singleton services
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<IUsageHistoryService, UsageHistoryService>();
        services.AddSingleton<ISessionNameStore, SessionNameStore>();   // RENAME-07 (Phase 26 Plan 01)
        services.AddSingleton<INavigationService, NavigationService>();
        services.AddSingleton<ICredentialService, CredentialService>();
        services.AddSingleton<WebViewBridge>();
        services.AddSingleton<IWebViewBridge>(sp => sp.GetRequiredService<WebViewBridge>());
        services.AddSingleton<IClaudeApiService, ClaudeApiService>();
        services.AddSingleton<IPricingService>(sp =>
            new LiteLLMPricingService(sp.GetRequiredService<HttpClient>()));
        services.AddSingleton<IJsonlService>(sp =>
            new JsonlService(
                pricingService: sp.GetRequiredService<IPricingService>()));
        services.AddSingleton<IUpdateService>(sp =>
            new UpdateService(sp.GetRequiredService<HttpClient>(), sp.GetRequiredService<ISettingsService>()));
        services.AddSingleton<INotificationStateStore, NotificationStateStore>();
        services.AddSingleton<IUsageNotificationService>(sp =>
            new UsageNotificationService(sp.GetRequiredService<INotificationStateStore>()));

        // ViewModels
        services.AddTransient<LoginViewModel>();
        services.AddTransient<MainViewModel>(sp => new MainViewModel(
            sp.GetRequiredService<ICredentialService>(),
            sp.GetRequiredService<INavigationService>(),
            sp.GetRequiredService<IClaudeApiService>(),
            sp.GetRequiredService<ISettingsService>(),
            sp.GetRequiredService<IUsageHistoryService>(),
            sp.GetRequiredService<IJsonlService>(),
            sp.GetRequiredService<IPricingService>(),
            sp.GetRequiredService<IUpdateService>(),
            sp.GetRequiredService<IUsageNotificationService>(),
            sp.GetRequiredService<IDispatcherQueue>(),
            sp.GetRequiredService<ISessionNameStore>()));   // Phase 26 / RENAME-07
        services.AddTransient<SettingsViewModel>(sp => new SettingsViewModel(
            sp.GetRequiredService<ISettingsService>(),
            sp.GetRequiredService<ICredentialService>(),
            sp.GetRequiredService<INavigationService>(),
            sp.GetRequiredService<IPricingService>(),
            sp.GetRequiredService<IUsageHistoryService>(),
            sp.GetRequiredService<ISessionNameStore>(),
            sp.GetRequiredService<IJsonlService>(),
            sp.GetRequiredService<IDispatcherQueue>(),
            sp.GetRequiredService<IClaudeApiService>(),     // ORGID-01
            sp.GetRequiredService<IUsageNotificationService>(),
            sp.GetRequiredService<IWebViewBridge>()));     // Finding 18 — logout resets the bridge here now

        return services.BuildServiceProvider();
    }
}
