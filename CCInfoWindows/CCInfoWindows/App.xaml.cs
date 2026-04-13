using System.Net.Http;
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
        var logPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CCInfoWindows", "crash.log");
        Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
        File.AppendAllText(logPath, $"[{DateTime.Now:O}] {e.Exception.GetType().Name}: {e.Exception.Message}\n{e.Exception.StackTrace}\n\n");
        e.Handled = true;
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
            await RouteOnStartupAsync();
        }
        catch (Exception ex)
        {
            var logPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CCInfoWindows", "crash.log");
            Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
            File.AppendAllText(logPath, $"[{DateTime.Now:O}] OnLaunched failed: {ex.GetType().Name}: {ex.Message}\n\n");
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
                options.DefaultLanguage = "en-US";
            })
            .Build();

        var settingsService = Services.GetRequiredService<ISettingsService>();
        var appSettings = settingsService.LoadSettings();

        if (!string.IsNullOrEmpty(appSettings.Language))
        {
            await Localizer.Get().SetLanguage(appSettings.Language);
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

        // Singleton services
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<IUsageHistoryService, UsageHistoryService>();
        services.AddSingleton<INavigationService, NavigationService>();
        services.AddSingleton<ICredentialService, CredentialService>();
        services.AddSingleton<WebViewBridge>();
        services.AddSingleton<IWebViewBridge>(sp => sp.GetRequiredService<WebViewBridge>());
        services.AddSingleton<IClaudeApiService, ClaudeApiService>();
        services.AddSingleton<IPricingService>(sp =>
            new LiteLLMPricingService(sp.GetRequiredService<HttpClient>()));
        services.AddSingleton<IJsonlService>(sp =>
            new JsonlService(
                pricingService: sp.GetRequiredService<IPricingService>(),
                settingsService: sp.GetRequiredService<ISettingsService>()));
        services.AddSingleton<IUpdateService>(sp =>
            new UpdateService(sp.GetRequiredService<HttpClient>(), sp.GetRequiredService<ISettingsService>()));
        services.AddSingleton<IBurnRateNotificationService, BurnRateNotificationService>();

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
            sp.GetRequiredService<IWebViewBridge>(),
            sp.GetRequiredService<IBurnRateNotificationService>()));
        services.AddTransient<SettingsViewModel>();

        return services.BuildServiceProvider();
    }
}
