using System.Collections.ObjectModel;
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
using Microsoft.UI.Xaml.Data;

namespace CCInfoWindows.ViewModels;

/// <summary>
/// Display model for a single subagent context bar in the KONTEXTFENSTER section.
/// </summary>
public class SubagentDisplayData
{
    public required string AgentId { get; init; }
    public double Utilization { get; init; }
    public double Percentage { get; init; }
    public required string PercentageText { get; init; }
    public required string ModelBadge { get; init; }
}

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
    private readonly IJsonlService _jsonlService;

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

    // --- Session management ---

    [ObservableProperty]
    private ObservableCollection<SessionInfo> _sessions = [];

    [ObservableProperty]
    private SessionInfo? _selectedSession;

    [ObservableProperty]
    private bool _isJsonlScanning;

    [ObservableProperty]
    private bool _hasActiveSessions;

    // --- Context window ---

    [ObservableProperty]
    private double _contextUtilization;

    [ObservableProperty]
    private double _contextPercentage;

    [ObservableProperty]
    private string _contextPercentageText = "--";

    [ObservableProperty]
    private string _contextModelBadge = string.Empty;

    [ObservableProperty]
    private bool _showAutocompactWarning;

    [ObservableProperty]
    private bool _hasActiveSession;

    [ObservableProperty]
    private ObservableCollection<SubagentDisplayData> _subagentContexts = [];

    // --- Token counters ---

    [ObservableProperty]
    private string _inputTokensText = "--";

    [ObservableProperty]
    private string _outputTokensText = "--";

    // --- Grouped sessions for CollectionViewSource ---

    /// <summary>
    /// Sessions grouped by active status for the ComboBox CollectionViewSource.
    /// Groups: "Aktiv" (active sessions first), "Inaktiv" (inactive sessions below).
    /// </summary>
    public IEnumerable<object> GroupedSessions
    {
        get
        {
            var threshold = TimeSpan.FromMinutes(
                _settingsService.LoadSettings().SessionActivityThresholdMinutes);

            var active = Sessions
                .Where(s => s.IsActive(threshold))
                .OrderByDescending(s => s.LastActivity)
                .ToList();

            var inactive = Sessions
                .Where(s => !s.IsActive(threshold))
                .OrderByDescending(s => s.LastActivity)
                .ToList();

            var groups = new List<SessionGroup>();
            if (active.Count > 0)
                groups.Add(new SessionGroup("Aktiv", active));
            if (inactive.Count > 0)
                groups.Add(new SessionGroup("Inaktiv", inactive));

            return groups;
        }
    }

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
        IUsageHistoryService historyService,
        IJsonlService jsonlService)
    {
        _credentialService = credentialService;
        _navigationService = navigationService;
        _apiService = apiService;
        _settingsService = settingsService;
        _historyService = historyService;
        _jsonlService = jsonlService;

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

        // Clear stale data when the persisted window has already expired
        if (history.ResetsAt.HasValue && history.ResetsAt.Value < DateTimeOffset.UtcNow)
        {
            _historyService.ClearHistory();
            history = new UsageHistory();
        }

        if (history.Points.Count > 0)
        {
            UsageHistoryPoints = history.Points.AsReadOnly();
            if (history.ResetsAt.HasValue)
            {
                _fiveHourResetsAt = history.ResetsAt;
            }
            ChartInvalidateCallback?.Invoke();
        }

        // Start JSONL service for local session data
        _jsonlService.DataUpdated += (s, e) =>
        {
            dispatcherQueue.TryEnqueue(RefreshSessionList);
        };

        IsJsonlScanning = _jsonlService.IsScanning;

        try
        {
            await _jsonlService.InitializeAsync();
        }
        catch
        {
            // Background scan failure should not block the dashboard
        }

        RefreshSessionList();

#if !MOCK_CHART
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
#endif
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

        // Set window timestamp BEFORE invalidating chart so FiveHourWindowStart is non-null when draw handler runs
        _fiveHourResetsAt = apiResetsAt;
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
    /// Stops polling and countdown timers and the JSONL service. Call from MainView.Unloaded event.
    /// </summary>
    public void StopTimers()
    {
        _pollTimer?.Stop();
        _countdownTimer?.Stop();
        _jsonlService.Stop();
        WeakReferenceMessenger.Default.UnregisterAll(this);
    }

    /// <summary>
    /// Rebuilds the Sessions collection from the JSONL service and restores/retains the selected session.
    /// Called on the UI thread.
    /// </summary>
    private void RefreshSessionList()
    {
        IsJsonlScanning = _jsonlService.IsScanning;

        var latestSessions = _jsonlService.Sessions;
        HasActiveSessions = latestSessions.Count > 0;

        // Rebuild collection in place to minimize UI churn
        Sessions.Clear();
        foreach (var session in latestSessions)
        {
            Sessions.Add(session);
        }

        OnPropertyChanged(nameof(GroupedSessions));

        var currentSelection = SelectedSession;

        if (currentSelection != null)
        {
            // SESS-04: do not auto-switch; re-find the same session object by ID
            var updated = latestSessions.FirstOrDefault(s => s.Id == currentSelection.Id);
            if (updated != null && !ReferenceEquals(SelectedSession, updated))
            {
                // Update to fresh object but keep the same logical selection
                _selectedSession = updated;
                OnPropertyChanged(nameof(SelectedSession));
                UpdateSessionData(updated);
            }
            return;
        }

        // No current selection — try to restore from persisted setting
        var settings = _settingsService.LoadSettings();
        if (!string.IsNullOrEmpty(settings.LastSelectedSessionId))
        {
            var restored = latestSessions.FirstOrDefault(s => s.Id == settings.LastSelectedSessionId);
            if (restored != null)
            {
                SelectedSession = restored;
                return;
            }
        }

        // Fall back to first active session
        var threshold = TimeSpan.FromMinutes(settings.SessionActivityThresholdMinutes);
        var firstActive = latestSessions.FirstOrDefault(s => s.IsActive(threshold));
        if (firstActive != null)
        {
            SelectedSession = firstActive;
        }
    }

    partial void OnSelectedSessionChanged(SessionInfo? value)
    {
        if (value == null)
        {
            ClearSessionData();
            return;
        }

        UpdateSessionData(value);
        PersistSelectedSessionId(value.Id);
    }

    private void ClearSessionData()
    {
        ContextUtilization = 0;
        ContextPercentage = 0;
        ContextPercentageText = "--";
        ContextModelBadge = string.Empty;
        ShowAutocompactWarning = false;
        HasActiveSession = false;
        SubagentContexts.Clear();
        InputTokensText = "--";
        OutputTokensText = "--";
    }

    private void UpdateSessionData(SessionInfo session)
    {
        var context = _jsonlService.GetContextWindow(session.Id);

        ContextUtilization = context.Utilization;
        ContextPercentage = Math.Min(context.Utilization * 100, 100);
        ContextPercentageText = $"{Math.Min(context.Utilization * 100, 100):0}%";
        ContextModelBadge = ModelContextLimits.GetDisplayName(context.ModelName);
        ShowAutocompactWarning = context.ShouldWarnAutocompact;
        HasActiveSession = true;

        SubagentContexts.Clear();
        foreach (var subagent in context.Subagents)
        {
            var subUtil = subagent.Utilization;
            SubagentContexts.Add(new SubagentDisplayData
            {
                AgentId = subagent.AgentId,
                Utilization = subUtil,
                Percentage = Math.Min(subUtil * 100, 100),
                PercentageText = $"{Math.Min(subUtil * 100, 100):0}%",
                ModelBadge = ModelContextLimits.GetDisplayName(subagent.ModelName)
            });
        }

        var tokens = _jsonlService.GetTokenSummary(session.Id);
        InputTokensText = TokenFormatter.FormatTokenCount(tokens.InputTokens);
        OutputTokensText = TokenFormatter.FormatTokenCount(tokens.OutputTokens);
    }

    private void PersistSelectedSessionId(string sessionId)
    {
        var settings = _settingsService.LoadSettings();
        settings.LastSelectedSessionId = sessionId;
        _settingsService.SaveSettings(settings);
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

/// <summary>
/// A named group of sessions for the grouped ComboBox CollectionViewSource.
/// Implements IGrouping to work with CollectionViewSource IsSourceGrouped=true.
/// </summary>
public class SessionGroup : List<SessionInfo>, IGrouping<string, SessionInfo>
{
    public string Key { get; }

    public SessionGroup(string key, IEnumerable<SessionInfo> sessions) : base(sessions)
    {
        Key = key;
    }
}
