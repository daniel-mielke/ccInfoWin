using System.Diagnostics;
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
            Debug.WriteLine($"[LoginView] OnLoaded failed: {ex.Message}");
        }
    }
}
