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
    /// Initializes WebView2 with explicit UDF path, registers NavigationCompleted handler,
    /// and navigates to claude.ai login.
    /// Handler is registered BEFORE Navigate() to avoid race condition with cached sessions.
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

        // Clear session cookies (e.g., after logout) while preserving UDF cache/service workers.
        // This ensures claude.ai shows the login page, not a cached authenticated session.
        var cookieManager = webView.CoreWebView2.CookieManager;
        var existingCookies = await cookieManager.GetCookiesAsync("https://claude.ai");
        foreach (var cookie in existingCookies)
        {
            cookieManager.DeleteCookie(cookie);
        }

        // Reset login state for re-entry (e.g., after logout)
        _loginHandled = false;

        // Register SourceChanged on CoreWebView2 — fires on SPA pushState navigation too.
        // NavigationCompleted only fires on full page loads, which misses SPA route changes.
        webView.CoreWebView2.SourceChanged += HandleSourceChanged;
        webView.CoreWebView2.HistoryChanged += HandleHistoryChanged;
        webView.NavigationCompleted += HandleNavigationCompleted;
        webView.CoreWebView2.Navigate("https://claude.ai/login");
        IsLoading = false;
    }

    private async void HandleSourceChanged(CoreWebView2 sender, CoreWebView2SourceChangedEventArgs args)
    {
        if (_loginHandled) return;
        await TryExtractSessionCookieAsync(sender, sender.Source ?? "");
    }

    private async void HandleHistoryChanged(CoreWebView2 sender, object args)
    {
        if (_loginHandled) return;
        await TryExtractSessionCookieAsync(sender, sender.Source ?? "");
    }

    private bool _loginHandled;

    /// <summary>
    /// Handles full page navigation completion.
    /// </summary>
    public async void HandleNavigationCompleted(WebView2 sender, CoreWebView2NavigationCompletedEventArgs args)
    {
        if (_loginHandled || sender.CoreWebView2 is null) return;
        await TryExtractSessionCookieAsync(sender.CoreWebView2, sender.CoreWebView2.Source ?? "");
    }

    /// <summary>
    /// Shared cookie extraction logic used by all navigation event handlers.
    /// Checks URL for post-login state and extracts sessionKey cookie.
    /// CRITICAL: Cookie .Name and .Value accessed on UI thread only (Pitfall 2 from research).
    /// </summary>
    private async Task TryExtractSessionCookieAsync(CoreWebView2 coreWebView, string currentUrl)
    {
        if (_loginHandled) return;

        if (!IsPostLoginUrl(currentUrl))
        {
            return;
        }

        var cookies = await coreWebView.CookieManager
            .GetCookiesAsync("https://claude.ai");

        var sessionCookie = cookies.FirstOrDefault(c =>
            string.Equals(c.Name, "sessionKey", StringComparison.Ordinal));

        if (sessionCookie is not null)
        {
            _loginHandled = true;
            _credentialService.SaveSessionToken(sessionCookie.Value);

            // Extract lastActiveOrg cookie for API URL construction.
            // If missing, org ID will be fetched via /api/organizations on first poll.
            var orgCookie = cookies.FirstOrDefault(c =>
                string.Equals(c.Name, "lastActiveOrg", StringComparison.Ordinal));
            if (orgCookie is not null)
            {
                _credentialService.SaveOrganizationId(orgCookie.Value);
            }

            WeakReferenceMessenger.Default.Send(new AuthStateChangedMessage(true));
            _navigationService.NavigateTo<MainView>();
        }
    }

    /// <summary>
    /// Determines if the current URL indicates a successful post-login state.
    /// Returns true for claude.ai pages that are NOT the login/signup flow.
    /// </summary>
    private static bool IsPostLoginUrl(string url)
    {
        if (!url.StartsWith("https://claude.ai", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // Login/signup flow URLs — not yet authenticated
        var loginPaths = new[] { "/login", "/signup", "/oauth", "/auth" };
        var uri = new Uri(url);
        return !loginPaths.Any(p => uri.AbsolutePath.StartsWith(p, StringComparison.OrdinalIgnoreCase));
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
