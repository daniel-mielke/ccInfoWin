using CCInfoWindows.Helpers;
using CCInfoWindows.Messages;
using CCInfoWindows.Models;
using CCInfoWindows.Services.Interfaces;
using CCInfoWindows.Views;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace CCInfoWindows.ViewModels;

/// <summary>
/// Dashboard ViewModel with API polling, usage display properties, countdown timers, and footer commands.
/// </summary>
public partial class MainViewModel : ObservableObject, IRecipient<AuthStateChangedMessage>
{
    private readonly ICredentialService _credentialService;
    private readonly INavigationService _navigationService;
    private readonly IClaudeApiService _apiService;
    private readonly ISettingsService _settingsService;
    private readonly HttpClient _httpClient;

    private DispatcherQueueTimer? _pollTimer;
    private DispatcherQueueTimer? _countdownTimer;
    private int _refreshIntervalSeconds;

    // --- Auth state ---

    [ObservableProperty]
    private bool _isSessionExpired;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    // --- 5-hour window ---

    [ObservableProperty]
    private double _fiveHourUtilization;

    [ObservableProperty]
    private double _fiveHourPercentage;

    [ObservableProperty]
    private string _fiveHourPercentageText = "--";

    [ObservableProperty]
    private string _fiveHourCountdown = "--";

    private DateTimeOffset? _fiveHourResetsAt;

    // --- Weekly quota (Opus / default) ---

    [ObservableProperty]
    private double _weeklyUtilization;

    [ObservableProperty]
    private double _weeklyPercentage;

    [ObservableProperty]
    private string _weeklyPercentageText = "--";

    [ObservableProperty]
    private string _weeklyCountdown = "--";

    [ObservableProperty]
    private string _weeklyResetDate = "--";

    private DateTimeOffset? _weeklyResetsAt;

    // --- Sonnet weekly quota ---

    [ObservableProperty]
    private double _sonnetUtilization;

    [ObservableProperty]
    private double _sonnetPercentage;

    [ObservableProperty]
    private string _sonnetPercentageText = "--";

    [ObservableProperty]
    private string _sonnetCountdown = "--";

    [ObservableProperty]
    private string _sonnetResetDate = "--";

    private DateTimeOffset? _sonnetResetsAt;

    [ObservableProperty]
    private bool _hasSonnetData;

    // --- UI state ---

    [ObservableProperty]
    private bool _isRefreshing;

    [ObservableProperty]
    private bool _hasApiError;

    [ObservableProperty]
    private bool _isUpdatingFromCache;

    public MainViewModel(
        ICredentialService credentialService,
        INavigationService navigationService,
        IClaudeApiService apiService,
        ISettingsService settingsService,
        HttpClient httpClient)
    {
        _credentialService = credentialService;
        _navigationService = navigationService;
        _apiService = apiService;
        _settingsService = settingsService;
        _httpClient = httpClient;

        WeakReferenceMessenger.Default.Register<AuthStateChangedMessage>(this);
    }

    /// <summary>
    /// Initializes polling and countdown timers. Call from MainView.Loaded event.
    /// </summary>
    public async Task InitializeAsync()
    {
        var dispatcherQueue = DispatcherQueue.GetForCurrentThread();

        // Load settings
        var settings = _settingsService.LoadSettings();
        _refreshIntervalSeconds = settings.RefreshIntervalSeconds;

        // Subscribe to refresh interval changes from Settings
        WeakReferenceMessenger.Default.Register<RefreshIntervalChangedMessage>(this, (r, m) =>
        {
            ((MainViewModel)r).UpdateRefreshInterval(m.Value);
        });

        // Load cache for instant display
        var cached = await _apiService.LoadCacheAsync();
        if (cached != null)
        {
            IsUpdatingFromCache = true;
            UpdateUsageProperties(cached);
        }

        // Start poll timer
        _pollTimer = dispatcherQueue.CreateTimer();
        _pollTimer.Interval = TimeSpan.FromSeconds(_refreshIntervalSeconds);
        _pollTimer.Tick += async (s, e) => await PollUsageAsync();
        _pollTimer.Start();

        // Start countdown timer (ticks every 60 seconds)
        _countdownTimer = dispatcherQueue.CreateTimer();
        _countdownTimer.Interval = TimeSpan.FromMinutes(1);
        _countdownTimer.Tick += (s, e) => UpdateCountdowns();
        _countdownTimer.Start();

        // Immediate first poll
        await PollUsageAsync();
        IsUpdatingFromCache = false;
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

    private async Task PollUsageAsync()
    {
        if (IsRefreshing) return;
        IsRefreshing = true;
        HasApiError = false;

        try
        {
            var result = await _apiService.FetchUsageAsync();
            if (result != null)
            {
                UpdateUsageProperties(result);
            }
            else
            {
                HasApiError = true;
            }
        }
        catch
        {
            HasApiError = true;
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    private void UpdateUsageProperties(UsageResponse data)
    {
        // 5-STUNDEN-FENSTER = FiveHour
        if (data.FiveHour != null)
        {
            var util = data.FiveHour.Utilization;
            FiveHourUtilization = util;
            FiveHourPercentage = Math.Min(util, 1.0) * 100;
            FiveHourPercentageText = $"{Math.Min(util, 1.0) * 100:0}%";
            FiveHourCountdown = CountdownFormatter.FormatCountdown(data.FiveHour.ResetsAt);
            _fiveHourResetsAt = data.FiveHour.ResetsAt;
        }
        else
        {
            FiveHourUtilization = 0;
            FiveHourPercentage = 0;
            FiveHourPercentageText = "--";
            FiveHourCountdown = "--";
            _fiveHourResetsAt = null;
        }

        // WOCHENLIMIT = SevenDayOpus (fallback to SevenDay)
        var weeklyWindow = data.SevenDayOpus ?? data.SevenDay;
        if (weeklyWindow != null)
        {
            var util = weeklyWindow.Utilization;
            WeeklyUtilization = util;
            WeeklyPercentage = Math.Min(util, 1.0) * 100;
            WeeklyPercentageText = $"{Math.Min(util, 1.0) * 100:0}%";
            WeeklyCountdown = CountdownFormatter.FormatCountdown(weeklyWindow.ResetsAt);
            WeeklyResetDate = CountdownFormatter.FormatResetDate(weeklyWindow.ResetsAt);
            _weeklyResetsAt = weeklyWindow.ResetsAt;
        }
        else
        {
            WeeklyUtilization = 0;
            WeeklyPercentage = 0;
            WeeklyPercentageText = "--";
            WeeklyCountdown = "--";
            WeeklyResetDate = "--";
            _weeklyResetsAt = null;
        }

        // SONNET WOCHENLIMIT = SevenDaySonnet
        HasSonnetData = data.SevenDaySonnet != null;
        if (data.SevenDaySonnet != null)
        {
            var util = data.SevenDaySonnet.Utilization;
            SonnetUtilization = util;
            SonnetPercentage = Math.Min(util, 1.0) * 100;
            SonnetPercentageText = $"{Math.Min(util, 1.0) * 100:0}%";
            SonnetCountdown = CountdownFormatter.FormatCountdown(data.SevenDaySonnet.ResetsAt);
            SonnetResetDate = CountdownFormatter.FormatResetDate(data.SevenDaySonnet.ResetsAt);
            _sonnetResetsAt = data.SevenDaySonnet.ResetsAt;
        }
        else
        {
            SonnetUtilization = 0;
            SonnetPercentage = 0;
            SonnetPercentageText = "--";
            SonnetCountdown = "--";
            SonnetResetDate = "--";
            _sonnetResetsAt = null;
        }
    }

    private void UpdateCountdowns()
    {
        FiveHourCountdown = CountdownFormatter.FormatCountdown(_fiveHourResetsAt);
        WeeklyCountdown = CountdownFormatter.FormatCountdown(_weeklyResetsAt);
        SonnetCountdown = CountdownFormatter.FormatCountdown(_sonnetResetsAt);
    }

    /// <summary>
    /// Updates the polling interval when settings change at runtime.
    /// </summary>
    public void UpdateRefreshInterval(int seconds)
    {
        _refreshIntervalSeconds = seconds;
        if (_pollTimer != null)
        {
            _pollTimer.Interval = TimeSpan.FromSeconds(seconds);
        }
    }

    /// <summary>
    /// Stops polling and countdown timers. Call from MainView.Unloaded event.
    /// </summary>
    public void StopTimers()
    {
        _pollTimer?.Stop();
        _countdownTimer?.Stop();
        WeakReferenceMessenger.Default.UnregisterAll(this);
    }

    [RelayCommand]
    private async Task Refresh()
    {
        await PollUsageAsync();
    }

    [RelayCommand]
    private void OpenSettings()
    {
        _navigationService.NavigateTo<SettingsView>();
    }

    [RelayCommand]
    private void ExitApp()
    {
        Application.Current.Exit();
    }

    [RelayCommand]
    private void Logout()
    {
        _credentialService.ClearCredentials();
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
        if (!message.Value)
        {
            IsSessionExpired = true;
            StatusMessage = "Session expired. Please re-login to continue.";
        }
    }
}
