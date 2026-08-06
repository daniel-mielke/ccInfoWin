using CCInfoWindows.Helpers;
using CCInfoWindows.Services;
using CCInfoWindows.Services.Interfaces;
using CCInfoWindows.Views;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Dispatching;
using Microsoft.Web.WebView2.Core;
using Microsoft.UI.Xaml.Controls;

namespace CCInfoWindows.ViewModels;

/// <summary>
/// Orchestrates WebView2 initialization, cookie extraction, and navigation after login.
/// </summary>
public partial class LoginViewModel : ObservableObject
{
    private const string SessionCookieName = "sessionKey";
    private const string OrganizationCookieName = "lastActiveOrg";
    private const string LoginPath = "/login";
    private const int LoginUnclaimed = 0;
    private const int LoginClaimed = 1;

    /// <summary>Paths of the pre-authentication flow — reaching one of these is not a login.</summary>
    private static readonly string[] LoginFlowPaths = [LoginPath, "/signup", "/oauth", "/auth"];

    private readonly ICredentialService _credentialService;
    private readonly INavigationService _navigationService;
    private readonly IWebViewBridge _bridge;

    /// <summary>
    /// One-shot claim guarding the login tail. An <see cref="int"/> and not a <see cref="bool"/>
    /// because all three navigation handlers suspend at the cookie await: a plain check-then-set
    /// let two WebView2 events for the same navigation both complete the login, which navigated
    /// to MainView twice.
    /// </summary>
    private int _loginHandled = LoginUnclaimed;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasErrorMessage))]
    private string? _errorMessage;

    public bool HasErrorMessage => !string.IsNullOrEmpty(ErrorMessage);

    /// <summary>
    /// User Data Folder path for WebView2 isolation (%LOCALAPPDATA%\CCInfoWindows\WebView2).
    /// </summary>
    public static string UserDataFolderPath => AppPaths.WebView2UserDataFolder;

    public LoginViewModel(
        ICredentialService credentialService,
        INavigationService navigationService,
        IWebViewBridge bridge)
    {
        _credentialService = credentialService;
        _navigationService = navigationService;
        _bridge = bridge;
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
        catch (Exception firstAttemptEx)
        {
            AppLog.Write("LoginViewModel.InitializeWebView", firstAttemptEx, "retrying with a fresh UDF");

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
                ErrorMessage = "WebView2 initialization failed. Please restart the application.";
                AppLog.Write("LoginViewModel.InitializeWebView", retryEx, "UDF recreation failed");
                IsLoading = false;
                return;
            }
        }

        // Clear session cookies (e.g., after logout) while preserving UDF cache/service workers.
        // This ensures claude.ai shows the login page, not a cached authenticated session.
        var cookieManager = webView.CoreWebView2.CookieManager;
        var existingCookies = await cookieManager.GetCookiesAsync(ClaudeAiUrlPolicy.Origin);
        foreach (var cookie in existingCookies)
        {
            cookieManager.DeleteCookie(cookie);
        }

        // Reset login state for re-entry (e.g., after logout)
        ReleaseLoginClaim();

        // Register SourceChanged on CoreWebView2 — fires on SPA pushState navigation too.
        // NavigationCompleted only fires on full page loads, which misses SPA route changes.
        webView.CoreWebView2.SourceChanged += HandleSourceChanged;
        webView.CoreWebView2.HistoryChanged += HandleHistoryChanged;
        webView.NavigationCompleted += HandleNavigationCompleted;
        webView.CoreWebView2.Navigate($"{ClaudeAiUrlPolicy.Origin}{LoginPath}");
        // D-08: IsLoading stays true here. HandleNavigationCompleted flips it to false
        // ONLY when args.IsSuccess && Source is the claude.ai login page. This keeps
        // LoginWebView (bound to inverse of IsLoading) Collapsed and the loading
        // overlay (bound to IsLoading) Visible — preventing AUTH-07 flash of cached chat URL.
    }

    private async void HandleSourceChanged(CoreWebView2 sender, CoreWebView2SourceChangedEventArgs args)
    {
        try
        {
            if (IsLoginClaimed) return;
            await TryExtractSessionCookieAsync(sender, sender.Source ?? "");
        }
        catch (Exception ex)
        {
            ErrorMessage = "Login processing failed.";
            AppLog.Write("LoginViewModel.HandleSourceChanged", ex);
        }
    }

    private async void HandleHistoryChanged(CoreWebView2 sender, object args)
    {
        try
        {
            if (IsLoginClaimed) return;
            await TryExtractSessionCookieAsync(sender, sender.Source ?? "");
        }
        catch (Exception ex)
        {
            ErrorMessage = "Login processing failed.";
            AppLog.Write("LoginViewModel.HandleHistoryChanged", ex);
        }
    }

    /// <summary>
    /// Handles full page navigation completion.
    /// D-08: extend IsLoading semantics — keep IsLoading=true (loading overlay visible,
    /// LoginWebView Collapsed via inverse binding in LoginView.xaml) until the login URL
    /// itself has loaded successfully. Single source of truth, no second visibility flag.
    /// args.IsSuccess guards against offline/error completions (Pitfall 4).
    /// </summary>
    public async void HandleNavigationCompleted(WebView2 sender, CoreWebView2NavigationCompletedEventArgs args)
    {
        try
        {
            if (IsLoginClaimed || sender.CoreWebView2 is null) return;

            var source = sender.CoreWebView2.Source ?? string.Empty;

            // D-08: reveal the WebView2 (and hide the loading overlay) only when the login URL
            // has finished loading successfully — prevents AUTH-07 flash of any cached chat URL.
            if (args.IsSuccess && IsLoginPageUrl(source))
            {
                IsLoading = false;
            }

            await TryExtractSessionCookieAsync(sender.CoreWebView2, source);
        }
        catch (Exception ex)
        {
            ErrorMessage = "Login processing failed.";
            AppLog.Write("LoginViewModel.HandleNavigationCompleted", ex);
        }
    }

    /// <summary>
    /// Shared cookie extraction logic used by all navigation event handlers.
    /// Checks URL for post-login state and extracts sessionKey cookie.
    /// CRITICAL: Cookie .Name and .Value accessed on UI thread only (Pitfall 2 from research).
    /// </summary>
    private async Task TryExtractSessionCookieAsync(CoreWebView2 coreWebView, string currentUrl)
    {
        if (!IsPostLoginUrl(currentUrl)) return;

        // Claim BEFORE the first await — the message pump can dispatch a second WebView2 event
        // for the same navigation while this one is parked at the cookie await.
        if (!TryClaimLogin()) return;

        try
        {
            var cookies = await coreWebView.CookieManager.GetCookiesAsync(ClaudeAiUrlPolicy.Origin);

            var sessionToken = FindCookieValue(cookies, SessionCookieName);
            if (string.IsNullOrEmpty(sessionToken))
            {
                // The SPA has not written the cookie yet — hand the claim back so a later
                // navigation event can complete the login.
                ReleaseLoginClaim();
                return;
            }

            PersistSession(sessionToken, FindCookieValue(cookies, OrganizationCookieName));
            ActivateSession(coreWebView);
        }
        catch (Exception)
        {
            // Release before rethrowing: the caller surfaces the error but the user stays on the
            // login page, where a following navigation event must be able to try again.
            ReleaseLoginClaim();
            throw;
        }
    }

    private void PersistSession(string sessionToken, string? organizationId)
    {
        _credentialService.SaveSessionToken(sessionToken);

        if (organizationId is not null)
        {
            _credentialService.SaveOrganizationId(organizationId);
        }
        else
        {
            // The cookie is often not set yet at this point (the SPA resolves the org after
            // the first redirect). Dropping the stale id forces re-resolution via
            // /api/organizations on the first poll — keeping the previous account's org id
            // here is what made re-login fail with "API request failed".
            _credentialService.ClearOrganizationId();
        }
    }

    private void ActivateSession(CoreWebView2 coreWebView)
    {
        // Initialize WebView2 bridge for API calls — Chromium context has
        // all Cloudflare cookies and proper TLS fingerprint at this point.
        _bridge.Initialize(coreWebView, DispatcherQueue.GetForCurrentThread());

        // No AuthStateChangedMessage(true) broadcast: the navigation below builds a fresh transient
        // MainViewModel that polls from InitializeAsync, so nothing was ever listening (finding 37).
        _navigationService.NavigateTo<MainView>();
    }

    private static string? FindCookieValue(IEnumerable<CoreWebView2Cookie> cookies, string cookieName) =>
        cookies.FirstOrDefault(c => string.Equals(c.Name, cookieName, StringComparison.Ordinal))?.Value;

    private bool IsLoginClaimed => Volatile.Read(ref _loginHandled) == LoginClaimed;

    private bool TryClaimLogin() =>
        Interlocked.Exchange(ref _loginHandled, LoginClaimed) == LoginUnclaimed;

    private void ReleaseLoginClaim() => Volatile.Write(ref _loginHandled, LoginUnclaimed);

    /// <summary>
    /// Determines if the current URL indicates a successful post-login state.
    /// Returns true for claude.ai pages that are NOT the login/signup flow.
    /// The host is compared after parsing: a prefix test also accepts lookalike authorities
    /// such as https://claude.ai.evil.example/, and this method gates the bridge handover.
    /// </summary>
    internal static bool IsPostLoginUrl(string url)
    {
        if (!ClaudeAiUrlPolicy.TryGetAllowedUri(url, out var uri)) return false;

        return !LoginFlowPaths.Any(p => uri.AbsolutePath.StartsWith(p, StringComparison.OrdinalIgnoreCase));
    }

    internal static bool IsLoginPageUrl(string url) =>
        ClaudeAiUrlPolicy.TryGetAllowedUri(url, out var uri) &&
        uri.AbsolutePath.StartsWith(LoginPath, StringComparison.OrdinalIgnoreCase);

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
