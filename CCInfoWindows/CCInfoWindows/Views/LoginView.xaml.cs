using CCInfoWindows.Helpers;
using CCInfoWindows.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CCInfoWindows.Views;

/// <summary>
/// Full-window WebView2 login page for claude.ai authentication.
/// Code-behind wires WebView2 control to LoginViewModel (WebView2 requires direct control reference).
/// </summary>
public sealed partial class LoginView : Page
{
    public LoginViewModel ViewModel { get; }

    public LoginView()
    {
        ViewModel = App.Services.GetRequiredService<LoginViewModel>();
        DataContext = ViewModel;
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            // NavigationCompleted handler is registered inside InitializeWebViewAsync
            // BEFORE Navigate() is called, to avoid race condition with cached sessions
            await ViewModel.InitializeWebViewAsync(LoginWebView);
        }
        catch (Exception ex)
        {
            AppLog.Write("LoginView.OnLoaded", ex, "login WebView2 initialization failed");
        }
    }

    /// <summary>
    /// D-06: Manual reload of the login page. Double null guard handles
    /// the early-click case where CoreWebView2 has not yet been initialized
    /// by EnsureCoreWebView2Async (Pitfall 1). One-shot — no retry, no busy state.
    /// </summary>
    private void OnReloadLoginClicked(object sender, RoutedEventArgs e)
    {
        LoginWebView?.CoreWebView2?.Reload();
    }
}
