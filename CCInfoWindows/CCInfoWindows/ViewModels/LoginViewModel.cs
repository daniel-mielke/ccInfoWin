using CCInfoWindows.Messages;
using CCInfoWindows.Services.Interfaces;
using CCInfoWindows.Views;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Web.WebView2.Core;
using Microsoft.UI.Xaml.Controls;

namespace CCInfoWindows.ViewModels;

/// <summary>
/// Orchestrates WebView2 initialization, cookie extraction, and navigation after login.
/// </summary>
public partial class LoginViewModel : ObservableObject
{
    private readonly ICredentialService _credentialService;
    private readonly INavigationService _navigationService;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasErrorMessage))]
    private string? _errorMessage;

    public bool HasErrorMessage => !string.IsNullOrEmpty(ErrorMessage);

    /// <summary>
    /// User Data Folder path for WebView2 isolation (%LOCALAPPDATA%\CCInfoWindows\WebView2).
    /// </summary>
    public static string UserDataFolderPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CCInfoWindows", "WebView2");

    public LoginViewModel(ICredentialService credentialService, INavigationService navigationService)
    {
        _credentialService = credentialService;
        _navigationService = navigationService;
    }

    /// <summary>
    /// Initializes WebView2 with explicit UDF path and navigates to claude.ai login.
    /// Includes retry logic for corrupted User Data Folder.
    /// </summary>
    public async Task InitializeWebViewAsync(WebView2 webView)
    {
        IsLoading = true;
        ErrorMessage = null;

        var udfPath = UserDataFolderPath;

        try
        {
            await InitializeCoreWebView2(webView, udfPath);
        }
        catch (Exception)
        {
            // Retry once after deleting corrupted UDF (Pitfall 1 from research)
            try
            {
                if (Directory.Exists(udfPath))
                {
                    Directory.Delete(udfPath, recursive: true);
                }

                await InitializeCoreWebView2(webView, udfPath);
            }
            catch (Exception retryEx)
            {
                ErrorMessage = $"WebView2 initialization failed: {retryEx.Message}";
                IsLoading = false;
                return;
            }
        }

        webView.CoreWebView2.Navigate("https://claude.ai/login");
        IsLoading = false;
    }

    /// <summary>
    /// Handles navigation completion by checking for sessionKey cookie.
    /// CRITICAL: Cookie .Name and .Value accessed on UI thread only (Pitfall 2 from research).
    /// </summary>
    public async void HandleNavigationCompleted(WebView2 sender, CoreWebView2NavigationCompletedEventArgs args)
    {
        if (sender.CoreWebView2 is null)
        {
            return;
        }

        var cookies = await sender.CoreWebView2.CookieManager
            .GetCookiesAsync("https://claude.ai");

        // Access cookie properties on UI thread (NavigationCompleted fires on UI thread)
        var sessionCookie = cookies.FirstOrDefault(c => c.Name == "sessionKey");

        if (sessionCookie is not null)
        {
            _credentialService.SaveSessionToken(sessionCookie.Value);
            WeakReferenceMessenger.Default.Send(new AuthStateChangedMessage(true));
            _navigationService.NavigateTo<MainView>();
        }
    }

    private static async Task InitializeCoreWebView2(WebView2 webView, string udfPath)
    {
        Directory.CreateDirectory(udfPath);

        var env = await CoreWebView2Environment.CreateWithOptionsAsync(
            browserExecutableFolder: null,
            userDataFolder: udfPath,
            options: null);
        await webView.EnsureCoreWebView2Async(env);
    }
}
