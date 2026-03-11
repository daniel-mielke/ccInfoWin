using System.ComponentModel;
using CCInfoWindows.Services;
using CCInfoWindows.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;

namespace CCInfoWindows.Views;

/// <summary>
/// Dashboard view with usage sections, progress bars, countdown timers, and footer toolbar.
/// Hosts a hidden WebView2 for API calls when the bridge isn't already initialized (cold start with saved token).
/// </summary>
public sealed partial class MainView : Page
{
    public MainViewModel ViewModel { get; }

    public MainView()
    {
        ViewModel = App.Services.GetRequiredService<MainViewModel>();
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;

        // If bridge isn't initialized (cold start with persisted token, no login flow),
        // initialize it with the hidden WebView2 before starting API polling.
        var bridge = App.Services.GetRequiredService<WebViewBridge>();
        if (!bridge.IsInitialized)
        {
            await InitializeBridgeAsync(bridge);
        }

        await ViewModel.InitializeAsync();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        ViewModel.StopTimers();
    }

    /// <summary>
    /// Initializes a hidden WebView2, navigates to claude.ai to acquire Cloudflare cookies,
    /// then binds it to the WebViewBridge for API fetch calls.
    /// </summary>
    private async Task InitializeBridgeAsync(WebViewBridge bridge)
    {
        var udfPath = LoginViewModel.UserDataFolderPath;
        Directory.CreateDirectory(udfPath);

        var env = await CoreWebView2Environment.CreateWithOptionsAsync(
            browserExecutableFolder: null,
            userDataFolder: udfPath,
            options: null);
        await ApiBridgeWebView.EnsureCoreWebView2Async(env);

        // Navigate to claude.ai so the WebView2 picks up Cloudflare cookies (cf_clearance)
        // and the sessionKey cookie from the shared User Data Folder.
        var tcs = new TaskCompletionSource();
        ApiBridgeWebView.NavigationCompleted += (s, args) => tcs.TrySetResult();
        ApiBridgeWebView.CoreWebView2.Navigate("https://claude.ai");
        await tcs.Task;

        bridge.Initialize(ApiBridgeWebView.CoreWebView2, DispatcherQueue.GetForCurrentThread());
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.IsRefreshing))
        {
            if (ViewModel.IsRefreshing)
            {
                SpinnerStoryboard.Begin();
            }
            else
            {
                SpinnerStoryboard.Stop();
            }
        }
    }
}
