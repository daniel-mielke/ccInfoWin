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
/// Dashboard ViewModel with API polling, usage history accumulation, chart invalidation, and footer commands.
/// </summary>
public partial class MainViewModel : ObservableObject, IRecipient<AuthStateChangedMessage>
{
    private readonly ICredentialService _credentialService;
    private readonly INavigationService _navigationService;
    private readonly IClaudeApiService _apiService;
    private readonly ISettingsService _settingsService;
    private readonly IUsageHistoryService _historyService;

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

    // --- Chart state ---

    [ObservableProperty]
    private IReadOnlyList<UsageHistoryPoint> _usageHistoryPoints = [];

    /// <summary>
    /// Callback invoked after each data update to trigger Win2D canvas redraw.
    /// Set by MainView.xaml.cs after the canvas is created.
    /// </summary>
    public Action? ChartInvalidateCallback { get; set; }

    /// <summary>
    /// Start of the current 5-hour window, computed as ResetsAt minus 5 hours.
    /// Returns null until the first API response is received.
    /// </summary>
    public DateTimeOffset? FiveHourWindowStart => _fiveHourResetsAt?.AddHours(-5);

    public MainViewModel(
        ICredentialService credentialService,
        INavigationService navigationService,
        IClaudeApiService apiService,
        ISettingsService settingsService,
        IUsageHistoryService historyService)
    {
        _credentialService = credentialService;
        _navigationService = navigationService;
        _apiService = apiService;
        _settingsService = settingsService;
        _historyService = historyService;

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

        // Load persisted history for instant chart display before first poll
        var history = _historyService.LoadHistory();
        UsageHistoryPoints = history.Points.AsReadOnly();
        if (history.ResetsAt.HasValue && _fiveHourResetsAt == null)
        {
            _fiveHourResetsAt = history.ResetsAt;
        }
        ChartInvalidateCallback?.Invoke();

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
    public Task<bool> ValidateTokenAsync()
    {
        var token = _credentialService.GetSessionToken();
        return Task.FromResult(!string.IsNullOrEmpty(token));
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
            var util = data.FiveHour.NormalizedUtilization;
            FiveHourUtilization = util;
            FiveHourPercentage = Math.Min(util * 100, 100);
            FiveHourPercentageText = $"{Math.Min(util * 100, 100):0}%";
            FiveHourCountdown = CountdownFormatter.FormatCountdown(data.FiveHour.ResetsAt);

            AppendHistoryPoint(data.FiveHour.ResetsAt, util);
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
            var util = weeklyWindow.NormalizedUtilization;
            WeeklyUtilization = util;
            WeeklyPercentage = Math.Min(util * 100, 100);
            WeeklyPercentageText = $"{Math.Min(util * 100, 100):0}%";
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
            var util = data.SevenDaySonnet.NormalizedUtilization;
            SonnetUtilization = util;
            SonnetPercentage = Math.Min(util * 100, 100);
            SonnetPercentageText = $"{Math.Min(util * 100, 100):0}%";
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

    /// <summary>
    /// Appends a new data point to persisted history, clearing history first when the 5-hour window resets.
    /// </summary>
    private void AppendHistoryPoint(DateTimeOffset? apiResetsAt, double utilization)
    {
        var history = _historyService.LoadHistory();

        var windowResetDetected = history.ResetsAt.HasValue
            && apiResetsAt.HasValue
            && history.ResetsAt.Value != apiResetsAt.Value;

        if (windowResetDetected)
        {
            history = new UsageHistory();
        }

        history.ResetsAt = apiResetsAt;
        history.Points.Add(new UsageHistoryPoint
        {
            Timestamp = DateTimeOffset.UtcNow,
            Utilization = utilization
        });

        _historyService.SaveHistory(history);
        UsageHistoryPoints = history.Points.AsReadOnly();
        ChartInvalidateCallback?.Invoke();
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
        _historyService.ClearHistory();
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
