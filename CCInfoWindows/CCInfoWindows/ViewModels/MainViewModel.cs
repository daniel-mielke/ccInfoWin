using CCInfoWindows.Messages;
using CCInfoWindows.Services.Interfaces;
using CCInfoWindows.Views;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;

namespace CCInfoWindows.ViewModels;

/// <summary>
/// Post-login dashboard ViewModel with token validation, logout, and session expiry handling.
/// </summary>
public partial class MainViewModel : ObservableObject, IRecipient<AuthStateChangedMessage>
{
    private readonly ICredentialService _credentialService;
    private readonly INavigationService _navigationService;
    private readonly HttpClient _httpClient;

    [ObservableProperty]
    private bool _isSessionExpired;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    public MainViewModel(
        ICredentialService credentialService,
        INavigationService navigationService,
        HttpClient httpClient)
    {
        _credentialService = credentialService;
        _navigationService = navigationService;
        _httpClient = httpClient;

        WeakReferenceMessenger.Default.Register<AuthStateChangedMessage>(this);
    }

    /// <summary>
    /// Validates the stored session token by calling claude.ai API.
    /// Returns true if token is valid or if offline (assume valid to not block user).
    /// </summary>
    public async Task<bool> ValidateTokenAsync()
    {
        var token = _credentialService.GetSessionToken();
        if (string.IsNullOrEmpty(token))
        {
            return false;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "https://claude.ai/api/organizations");
            request.Headers.Add("Cookie", $"sessionKey={token}");

            var response = await _httpClient.SendAsync(request);

            if (response.IsSuccessStatusCode)
            {
                return true;
            }

            var statusCode = (int)response.StatusCode;
            if (statusCode == 401 || statusCode == 403)
            {
                return false;
            }

            // Other HTTP errors: assume valid (don't block user)
            return true;
        }
        catch (HttpRequestException)
        {
            // Network error: assume valid (user might be offline)
            return true;
        }
        catch (TaskCanceledException)
        {
            // Timeout: assume valid
            return true;
        }
    }

    [RelayCommand]
    private void Logout()
    {
        _credentialService.ClearCredentials();

        // Don't delete WebView2 UDF — it contains cache/service workers needed for rendering.
        // WebView2 session cookies are cleared in LoginView via CookieManager API
        // after CoreWebView2 is initialized (requires active WebView2 instance).
        WeakReferenceMessenger.Default.Send(new AuthStateChangedMessage(false));
        IsSessionExpired = false;
        _navigationService.NavigateTo<LoginView>();
    }

    [RelayCommand]
    private void ReLogin()
    {
        IsSessionExpired = false;
        _navigationService.NavigateTo<LoginView>();
    }

    public void Receive(AuthStateChangedMessage message)
    {
        // If message indicates logged out (e.g., 401 detected during API calls)
        if (!message.Value)
        {
            IsSessionExpired = true;
            StatusMessage = "Session expired. Please re-login to continue.";
        }
    }

}
