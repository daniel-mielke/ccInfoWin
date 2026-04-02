using System.Collections.ObjectModel;
using System.Diagnostics;
using CCInfoWindows.Helpers;
using CCInfoWindows.Messages;
using CCInfoWindows.Models;
using CCInfoWindows.Services.Interfaces;
using CCInfoWindows.Views;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

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
    public required SolidColorBrush BadgeColor { get; init; }
}

/// <summary>
/// Flat display item for the session ComboBox.
/// Wraps a SessionInfo and exposes display name and activity state.
/// </summary>
public class SessionDisplayItem
{
    public required SessionInfo Session { get; init; }
    public required string DisplayName { get; init; }
    public required bool IsActive { get; init; }
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
    private readonly IPricingService _pricingService;
    private readonly IUpdateService _updateService;
    private readonly IWebViewBridge _bridge;

    private DispatcherQueueTimer? _pollTimer;
    private DispatcherQueueTimer? _countdownTimer;
    private int _refreshIntervalSeconds;
    private DispatcherQueue? _dispatcherQueue;

    private string _updateDownloadUrl = string.Empty;

    // --- Update state ---

    [ObservableProperty]
    private bool _isUpdateAvailable;

    [ObservableProperty]
    private string _updateMessage = string.Empty;

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
    private string _apiErrorMessage = string.Empty;

    [ObservableProperty]
    private bool _isUpdatingFromCache;

    // --- Chart state ---

    [ObservableProperty]
    private IReadOnlyList<UsageHistoryPoint> _usageHistoryPoints = [];

    // --- Session management ---

    [ObservableProperty]
    private ObservableCollection<SessionInfo> _sessions = [];

    [ObservableProperty]
    private SessionDisplayItem? _selectedSession;

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
    private SolidColorBrush _contextModelBadgeColor = new(Microsoft.UI.Colors.Gray);

    [ObservableProperty]
    private bool _showAutocompactWarning;

    [ObservableProperty]
    private bool _hasActiveSession;

    [ObservableProperty]
    private ObservableCollection<SubagentDisplayData> _subagentContexts = [];

    // --- Statistics (STATISTIKEN section) ---

    [ObservableProperty]
    private int _selectedTabIndex;

    [ObservableProperty]
    private bool _isAggregating;

    [ObservableProperty]
    private string _statisticsModels = "\u2013";

    [ObservableProperty]
    private string _statisticsInput = "\u2013";

    [ObservableProperty]
    private string _statisticsOutput = "\u2013";

    [ObservableProperty]
    private string _statisticsCacheCreation = "\u2013";

    [ObservableProperty]
    private string _statisticsCacheRead = "\u2013";

    [ObservableProperty]
    private string _statisticsTotal = "\u2013";

    [ObservableProperty]
    private string _statisticsCost = "\u2013";

    // --- Sorted session display items ---

    [ObservableProperty]
    private ObservableCollection<SessionDisplayItem> _sortedSessions = [];

    private bool _isRefreshingSessionList;

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
        IJsonlService jsonlService,
        IPricingService pricingService,
        IUpdateService updateService,
        IWebViewBridge bridge)
    {
        _credentialService = credentialService;
        _navigationService = navigationService;
        _apiService = apiService;
        _settingsService = settingsService;
        _historyService = historyService;
        _jsonlService = jsonlService;
        _pricingService = pricingService;
        _updateService = updateService;
        _bridge = bridge;

        _updateService.UpdateAvailable += OnUpdateAvailable;
        WeakReferenceMessenger.Default.Register<AuthStateChangedMessage>(this);
    }

    /// <summary>
    /// Initializes polling and countdown timers. Call from MainView.Loaded event.
    /// </summary>
    public async Task InitializeAsync()
    {
        var dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        _dispatcherQueue = dispatcherQueue;

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

        // Load pricing in background — non-blocking, fallback activates on failure
        _ = _pricingService.EnsurePricesLoadedAsync();

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

        await _updateService.CheckForUpdateAsync();
        _updateService.StartPeriodicCheck();
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
        ApiErrorMessage = string.Empty;

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
                ApiErrorMessage = "API returned empty data. The response body could not be deserialized.";
            }
        }
        catch (Exception ex)
        {
            HasApiError = true;
            ApiErrorMessage = ex.Message;
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

        var windowResetDetected = IsWindowReset(history.ResetsAt, apiResetsAt);

        if (windowResetDetected)
        {
            history = new UsageHistory();
        }

        history.ResetsAt = apiResetsAt;

        var now = DateTimeOffset.UtcNow;
        var windowDuration = TimeSpan.FromHours(5);
        var cutoff = now - windowDuration;
        history.Points.RemoveAll(p => p.Timestamp < cutoff);

        history.Points.Add(new UsageHistoryPoint
        {
            Timestamp = now,
            Utilization = utilization
        });

        _historyService.SaveHistory(history);

        // Set window timestamp BEFORE invalidating chart so FiveHourWindowStart is non-null when draw handler runs
        _fiveHourResetsAt = apiResetsAt;
        UsageHistoryPoints = history.Points.AsReadOnly();
        ChartInvalidateCallback?.Invoke();
    }

    private static readonly TimeSpan WindowResetTolerance = TimeSpan.FromMinutes(2);

    private static bool IsWindowReset(DateTimeOffset? storedResetsAt, DateTimeOffset? apiResetsAt)
    {
        if (!storedResetsAt.HasValue || !apiResetsAt.HasValue) return false;

        var difference = (apiResetsAt.Value - storedResetsAt.Value).Duration();
        return difference > WindowResetTolerance;
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
        _updateService.StopPeriodicCheck();
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

        var settings = _settingsService.LoadSettings();
        var threshold = TimeSpan.FromMinutes(settings.SessionActivityThresholdMinutes);

        // Rebuild internal sessions collection
        Sessions.Clear();
        foreach (var session in latestSessions)
        {
            Sessions.Add(session);
        }

        // SESS-04: capture current selection BEFORE rebuilding the collection
        var previousSessionId = SelectedSession?.Session.Id;

        // Guard: suppress OnSelectedSessionChanged while rebuilding
        _isRefreshingSessionList = true;

        // Only show sessions with recent activity; sort by last activity descending
        var displayItems = latestSessions
            .Where(s => s.IsActive(threshold))
            .OrderByDescending(s => s.LastActivity)
            .Select(s => new SessionDisplayItem
            {
                Session = s,
                DisplayName = s.DisplayName,
                IsActive = true
            })
            .ToList();

        SortedSessions = new ObservableCollection<SessionDisplayItem>(displayItems);

        // Restore previous selection without triggering ClearSessionData
        if (previousSessionId != null)
        {
            var updatedItem = SortedSessions.FirstOrDefault(d => d.Session.Id == previousSessionId);
            if (updatedItem != null)
            {
                SelectedSession = updatedItem;
                _isRefreshingSessionList = false;
                UpdateSessionData(updatedItem.Session);
            }
            else
            {
                _isRefreshingSessionList = false;
            }
            return;
        }

        _isRefreshingSessionList = false;

        // No current selection — try to restore from persisted setting
        if (!string.IsNullOrEmpty(settings.LastSelectedSessionId))
        {
            var restoredItem = SortedSessions.FirstOrDefault(d => d.Session.Id == settings.LastSelectedSessionId);
            if (restoredItem != null)
            {
                SelectedSession = restoredItem;
                return;
            }
        }

        // Fall back to first active session
        var firstActiveItem = SortedSessions.FirstOrDefault(d => d.IsActive);
        if (firstActiveItem != null)
        {
            SelectedSession = firstActiveItem;
        }
    }

    partial void OnSelectedSessionChanged(SessionDisplayItem? value)
    {
        // Suppress spurious null transitions during session list rebuild
        if (_isRefreshingSessionList) return;

        if (value == null)
        {
            ClearSessionData();
            return;
        }

        UpdateSessionData(value.Session);
        PersistSelectedSessionId(value.Session.Id);
    }

    private void ClearSessionData()
    {
        ContextUtilization = 0;
        ContextPercentage = 0;
        ContextPercentageText = "--";
        ContextModelBadge = string.Empty;
        ContextModelBadgeColor = ParseHexBrush(ModelContextLimits.GetBadgeColorHex(null));
        ShowAutocompactWarning = false;
        HasActiveSession = false;
        SubagentContexts.Clear();
        // Do NOT reset SelectedTabIndex — user's tab choice must survive session refreshes
        ApplyStatistics(StatisticsSummary.Empty);
    }

    partial void OnSelectedTabIndexChanged(int value)
    {
        var period = (TimePeriod)value;
        if (period == TimePeriod.Session)
        {
            UpdateStatisticsFromSession();
        }
        else
        {
            _ = AggregateStatisticsAsync(period);
        }
    }

    private void UpdateStatisticsFromSession()
    {
        if (SelectedSession == null)
        {
            ApplyStatistics(StatisticsSummary.Empty);
            return;
        }
        var stats = _jsonlService.GetStatistics(TimePeriod.Session, SelectedSession.Session.Id);
        ApplyStatistics(stats);
    }

    private async Task AggregateStatisticsAsync(TimePeriod period)
    {
        IsAggregating = true;
        try
        {
            await _pricingService.EnsurePricesLoadedAsync();
            var stats = await Task.Run(() => _jsonlService.GetStatistics(period));
            DispatcherQueue.GetForCurrentThread()?.TryEnqueue(() => ApplyStatistics(stats));
        }
        catch
        {
            DispatcherQueue.GetForCurrentThread()?.TryEnqueue(() => ApplyStatistics(StatisticsSummary.Empty));
        }
        finally
        {
            DispatcherQueue.GetForCurrentThread()?.TryEnqueue(() => IsAggregating = false);
        }
    }

    internal void ApplyStatistics(StatisticsSummary stats)
    {
        var displayModels = stats.Models
            .Where(m => !string.Equals(m, "<synthetic>", StringComparison.OrdinalIgnoreCase)
                     && !string.Equals(m, "synthetic", StringComparison.OrdinalIgnoreCase)
                     && !string.Equals(m, "unknown", StringComparison.OrdinalIgnoreCase))
            .Select(m => ModelContextLimits.GetDisplayName(m))
            .ToList();
        StatisticsModels = displayModels.Count > 0
            ? string.Join(", ", displayModels)
            : "\u2013";
        StatisticsInput = stats.InputTokens > 0 ? TokenFormatter.FormatTokenCount(stats.InputTokens) : "\u2013";
        StatisticsOutput = stats.OutputTokens > 0 ? TokenFormatter.FormatTokenCount(stats.OutputTokens) : "\u2013";
        StatisticsCacheCreation = stats.CacheCreationTokens > 0 ? TokenFormatter.FormatTokenCount(stats.CacheCreationTokens) : "\u2013";
        StatisticsCacheRead = stats.CacheReadTokens > 0 ? TokenFormatter.FormatTokenCount(stats.CacheReadTokens) : "\u2013";
        StatisticsTotal = stats.TotalTokens > 0 ? TokenFormatter.FormatTokenCount(stats.TotalTokens) : "\u2013";
        StatisticsCost = CostFormatter.FormatCost(stats.TotalCostUsd, stats.HasEstimatedCosts);
    }

    private void UpdateSessionData(SessionInfo session)
    {
        var context = _jsonlService.GetContextWindow(session.Id);

        ContextUtilization = context.Utilization;
        ContextPercentage = Math.Min(context.Utilization * 100, 100);
        ContextPercentageText = $"{Math.Min(context.Utilization * 100, 100):0}%";
        ContextModelBadge = ModelContextLimits.GetDisplayName(context.ModelName);
        ContextModelBadgeColor = ParseHexBrush(ModelContextLimits.GetBadgeColorHex(context.ModelName));
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
                ModelBadge = ModelContextLimits.GetDisplayName(subagent.ModelName),
                BadgeColor = ParseHexBrush(ModelContextLimits.GetBadgeColorHex(subagent.ModelName))
            });
        }

        if (SelectedTabIndex == 0) // Session tab
            UpdateStatisticsFromSession();
    }

    private static SolidColorBrush ParseHexBrush(string hex)
    {
        var value = hex.TrimStart('#');
        var r = byte.Parse(value[..2], System.Globalization.NumberStyles.HexNumber);
        var g = byte.Parse(value[2..4], System.Globalization.NumberStyles.HexNumber);
        var b = byte.Parse(value[4..6], System.Globalization.NumberStyles.HexNumber);
        return new SolidColorBrush(Windows.UI.Color.FromArgb(255, r, g, b));
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
        _bridge.Reset();
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

    [RelayCommand]
    private async Task ExportChartAsPng()
    {
        var appWindow = App.MainWindow?.AppWindow;
        if (appWindow == null) return;
        await ExportHelper.ExportChartAsPngAsync(appWindow, UsageHistoryPoints, FiveHourWindowStart, FiveHourPercentageText, FiveHourCountdown, FiveHourUtilization);
    }

    [RelayCommand]
    private async Task CopyChartToClipboard()
    {
        if (_dispatcherQueue == null) return;
        await ExportHelper.CopyChartToClipboardAsync(_dispatcherQueue, UsageHistoryPoints, FiveHourWindowStart, FiveHourPercentageText, FiveHourCountdown, FiveHourUtilization);
    }

    [RelayCommand]
    private void OpenUpdateDownload()
    {
        if (string.IsNullOrEmpty(_updateDownloadUrl)) return;
        Process.Start(new ProcessStartInfo(_updateDownloadUrl) { UseShellExecute = true });
    }

    private void OnUpdateAvailable(string version, string downloadUrl)
    {
        _updateDownloadUrl = downloadUrl;
        var dispatcherQueue = _dispatcherQueue ?? DispatcherQueue.GetForCurrentThread();
        dispatcherQueue?.TryEnqueue(() =>
        {
            UpdateMessage = $"Update v{version} verfügbar";
            IsUpdateAvailable = true;
        });
    }

    public void DismissUpdate()
    {
        var settings = _settingsService.LoadSettings();
        var version = UpdateMessage.Replace("Update v", "").Replace(" verfügbar", "");
        settings.DismissedUpdateVersion = version;
        _settingsService.SaveSettings(settings);
        IsUpdateAvailable = false;
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

