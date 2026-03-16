using System.Net.Http;
using CCInfoWindows.Services;
using CCInfoWindows.Services.Interfaces;
using CCInfoWindows.ViewModels;
using CCInfoWindows.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;

namespace CCInfoWindows;

/// <summary>
/// Application entry point with DI container configuration and startup token routing.
/// </summary>
public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    private Window? _window;

    public App()
    {
        InitializeComponent();
        UnhandledException += OnUnhandledException;
    }

    private static void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        var logPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CCInfoWindows", "crash.log");
        Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
        File.AppendAllText(logPath, $"[{DateTime.Now:O}] {e.Exception}\n\n");
        e.Handled = true;
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        Services = ConfigureServices();
        _window = new MainWindow();
        _window.Activate();

        ApplyPersistedTheme();
        await RouteOnStartupAsync();
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
    private static async Task RouteOnStartupAsync()
    {
        var navigationService = Services.GetRequiredService<INavigationService>();
        var mainViewModel = Services.GetRequiredService<MainViewModel>();

        var isTokenValid = await mainViewModel.ValidateTokenAsync();

        if (isTokenValid)
        {
            navigationService.NavigateTo<MainView>();
        }
        else
        {
            navigationService.NavigateTo<LoginView>();
        }
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
            new JsonlService(pricingService: sp.GetRequiredService<IPricingService>()));

        // ViewModels
        services.AddTransient<LoginViewModel>();
        services.AddTransient<MainViewModel>();
        services.AddTransient<SettingsViewModel>();

        return services.BuildServiceProvider();
    }
}
