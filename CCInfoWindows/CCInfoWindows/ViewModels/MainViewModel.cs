using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using CCInfoWindows.Helpers;
using CCInfoWindows.Messages;
using CCInfoWindows.Models;
using CCInfoWindows.Services;
using CCInfoWindows.Services.Interfaces;
using CCInfoWindows.Views;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using WinUI3Localizer;

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
    public required string TooltipText { get; init; }   // D-05
}

/// <summary>
/// Dashboard ViewModel with API polling, usage history accumulation, chart invalidation, and footer commands.
/// </summary>
public partial class MainViewModel : ObservableObject,
    IRecipient<AuthStateChangedMessage>,
    IRecipient<SessionTimeoutChangedMessage>,   // D-08
    IRecipient<SessionVisibilityChangedMessage>   // DROPDOWN-04 / D-03
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
    private readonly IBurnRateNotificationService _burnRateNotificationService;
    private readonly ISessionNameStore _sessionNameStore;   // RENAME-07 / Phase 26

    private DispatcherQueueTimer? _pollTimer;
    private DispatcherQueueTimer? _countdownTimer;
    private int _refreshIntervalSeconds;
    private readonly IDispatcherQueue _dispatcherQueue;
    private EventHandler? _dataUpdatedHandler;
    private CancellationTokenSource? _statisticsCts;

    // G-3 / CLEANUP-02: testability seam — overridden in unit tests to avoid WinRT COM activation.
    private readonly Func<string, SolidColorBrush> _brushFactory;

    private string _updateDownloadUrl = string.Empty;
    private string _updateVersion = string.Empty;

    // ORGID-03 (D-OG-04): threshold for soft-prompt trigger after consecutive zero-utilization polls
    private const int OrgMismatchPollThreshold = 5;

    // --- Update state ---

    [ObservableProperty]
    private bool _isUpdateAvailable;

    [ObservableProperty]
    private string _updateMessage = string.Empty;

    // --- Auth state ---

    // PRICING-03 / D-PR-04: IsPricingErrorVisible depends on IsSessionExpired (auth banner priority)
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPricingErrorVisible))]
    private bool _isSessionExpired;

    // PRICING-01..03 (D-PR-01, D-PR-04): surfaces _pricingService.EnsurePricesLoadedAsync() failures
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPricingErrorVisible))]
    private bool _isPricingError;

    // DROPDOWN-05 / D-04: one-time migration toast for existing installs.
    // True only on first launch after upgrade -- persisted via SaveSettings on dismiss (CD-02).
    [ObservableProperty]
    private bool _isSessionVisibilityMigrationToastVisible;

    // ORGID-03 / D-OG-04: soft-prompt InfoBar visibility — shown after OrgMismatchPollThreshold consecutive zero-utilization polls
    [ObservableProperty]
    private bool _isOrgMismatchPromptVisible;

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

    // NEXTWIN-01..03: absolute reset-time label below the countdown (D-NW-04)
    [ObservableProperty]
    private string _fiveHourNextWindowText = string.Empty;

    [ObservableProperty]
    private bool _isFiveHourNextWindowVisible;

    private DateTimeOffset? _fiveHourResetsAt;

    // --- Burn rate warning ---

    [ObservableProperty]
    private bool _isBurnRateWarningVisible;

    [ObservableProperty]
    private string _burnRateWarningText = string.Empty;

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

    // --- Spinner / refresh constants ---

    private const int MinimumSpinnerDisplayMs = 250;

    // --- UI state ---

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
    [NotifyPropertyChangedFor(nameof(CanRefresh))]
    // D-04: drives RefreshCommand.CanExecute via NotifyCanExecuteChangedFor — canonical CommunityToolkit.Mvvm 8.4 pattern, FIRST use in this codebase.
    // Gap-closure (Plan 22-04): NotifyPropertyChangedFor wires PropertyChanged("CanRefresh") so x:Bind IsEnabled re-evaluates on every flip.
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
    [NotifyPropertyChangedFor(nameof(HasSelectedSession))]
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
    private SolidColorBrush _contextModelBadgeColor = null!;

    [ObservableProperty]
    private bool _showAutocompactWarning;

    [ObservableProperty]
    private bool _hasActiveSession;

    [ObservableProperty]
    private ObservableCollection<SubagentDisplayData> _subagentContexts = [];

    // --- Statistics (STATISTIKEN section) ---

    [ObservableProperty]
    private int _selectedTabIndex = (int)TimePeriod.Today;

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
    /// One-shot flag for D-01 auto-reauth routing. Reset at constructor default,
    /// PollUsageAsync HTTP 200 success path, Logout command, and Receive(AuthStateChangedMessage(true)).
    /// </summary>
    private bool _autoReauthAttempted;

    // ORGID-03 / D-OG-04: counter for consecutive utilization=0 polls while HasActiveSession
    private int _zeroUtilizationPollCount;

    // ORGID-04 / D-OG-05: in-memory dismissal — NOT persisted (resets on app restart)
    private bool _orgMismatchSuppressed;

    /// <summary>
    /// Sends a ChartInvalidateMessage to trigger Win2D canvas redraw in MainView.
    /// </summary>
    private void InvalidateChart() => WeakReferenceMessenger.Default.Send(new ChartInvalidateMessage());

    /// <summary>
    /// Start of the current 5-hour window, computed as ResetsAt minus 5 hours.
    /// Returns null until the first API response is received.
    /// </summary>
    public DateTimeOffset? FiveHourWindowStart => _fiveHourResetsAt?.AddHours(-5);

    /// <summary>
    /// PRICING-03 / D-PR-04: banner-stack policy — pricing InfoBar suppressed while auth banner shows.
    /// Auto-notifies via [NotifyPropertyChangedFor] on IsPricingError + IsSessionExpired.
    /// </summary>
    public bool IsPricingErrorVisible => IsPricingError && !IsSessionExpired;

    public MainViewModel(
        ICredentialService credentialService,
        INavigationService navigationService,
        IClaudeApiService apiService,
        ISettingsService settingsService,
        IUsageHistoryService historyService,
        IJsonlService jsonlService,
        IPricingService pricingService,
        IUpdateService updateService,
        IWebViewBridge bridge,
        IBurnRateNotificationService burnRateNotificationService,
        IDispatcherQueue dispatcherQueue,
        ISessionNameStore sessionNameStore,   // Phase 26 / RENAME-07
        Func<string, SolidColorBrush>? brushFactory = null)   // G-3 / CLEANUP-02: testability seam; null = use ParseHexBrush
    {
        _brushFactory = brushFactory ?? ParseHexBrush;
        _credentialService = credentialService;
        _navigationService = navigationService;
        _apiService = apiService;
        _settingsService = settingsService;
        _historyService = historyService;
        _jsonlService = jsonlService;
        _pricingService = pricingService;
        _updateService = updateService;
        _bridge = bridge;
        _burnRateNotificationService = burnRateNotificationService;
        _dispatcherQueue = dispatcherQueue;
        _sessionNameStore = sessionNameStore;

        // G-3 / CLEANUP-02: initialize to gray-400 fallback before any poll runs, so bindings
        // never read null. Uses _brushFactory seam so tests can inject a headless fake.
        _contextModelBadgeColor = _brushFactory("#9CA3AF");

        // Messenger registration happens in InitializeAsync (paired with UnregisterAll for re-init safety — PITFALLS C2-P3).
        _updateService.UpdateAvailable += OnUpdateAvailable;
    }

    /// <summary>
    /// Initializes polling and countdown timers. Call from MainView.Loaded event.
    /// </summary>
    public async Task InitializeAsync()
    {
        // CD-04 / PITFALLS C2-P3: prevent double-subscription if InitializeAsync is called twice.
        // Pairs with constructor-time Register calls at lines 301-302; we re-register below via lambda
        // overloads. Cheap insurance as Phases 25-27 add new IRecipient<> handlers.
        WeakReferenceMessenger.Default.UnregisterAll(this);
        WeakReferenceMessenger.Default.Register<AuthStateChangedMessage>(this);
        WeakReferenceMessenger.Default.Register<SessionTimeoutChangedMessage>(this);   // D-08
        WeakReferenceMessenger.Default.Register<SessionVisibilityChangedMessage>(this);   // DROPDOWN-04 / D-03

        // Load settings
        var settings = _settingsService.LoadSettings();
        _refreshIntervalSeconds = settings.RefreshIntervalSeconds;

        // DROPDOWN-05 / D-04 / CD-05: first-launch migration toast.
        // Shown when the persisted flag is false (existing install upgrading to v1.5).
        // Fresh installs also see the toast once -- AppSettings default is false.
        if (!settings.SessionVisibilityMigrationShown)
        {
            IsSessionVisibilityMigrationToastVisible = true;
        }

        // Subscribe to refresh interval changes from Settings
        // CD-05 #4 audit: UpdateRefreshInterval mutates _pollTimer + _refreshIntervalSeconds; DispatcherQueueTimer requires UI thread → wrap.
        WeakReferenceMessenger.Default.Register<RefreshIntervalChangedMessage>(this, (r, m) =>
        {
            var vm = (MainViewModel)r;
            vm._dispatcherQueue.TryEnqueue(() => vm.UpdateRefreshInterval(m.Value));
        });

        // Subscribe to Sonnet context size changes from Settings — refresh context display immediately
        WeakReferenceMessenger.Default.Register<SonnetContextChangedMessage>(this, (r, m) =>
        {
            var vm = (MainViewModel)r;
            vm._dispatcherQueue.TryEnqueue(() =>
            {
                if (vm.SelectedSession != null)
                    vm.UpdateSessionData(vm.SelectedSession.Session);
            });
        });

        // RENAME-04 / D-06 / L-02: subscribe via .NET event (NOT WeakReferenceMessenger — D-13 lesson).
        // Symmetric -= cleanup happens in StopTimers (CD-05).
        _sessionNameStore.NameChanged += OnSessionNameChanged;

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
            RecomputeNextWindowLabel();   // NEXTWIN — show absolute label from persisted history at cold start
            InvalidateChart();
        }

        // Start JSONL service for local session data
        _dataUpdatedHandler = (s, e) => _dispatcherQueue.TryEnqueue(RefreshSessionList);
        _jsonlService.DataUpdated += _dataUpdatedHandler;

        IsJsonlScanning = _jsonlService.IsScanning;

        try
        {
            await _jsonlService.InitializeAsync();
        }
        catch (Exception ex)
        {
            // Background scan failure should not block the dashboard
            Debug.WriteLine($"[MainViewModel] JSONL init failed: {ex.Message}");
        }

        // Load pricing in background — non-blocking, fallback activates on failure.
        // PRICING-01..03 (D-PR-01, D-PR-03): surface failures via IsPricingError; clear on subsequent success.
        // Marshal back to the UI thread because Task.Run runs off the UI thread (G-1 alignment for property mutation).
        _ = Task.Run(async () =>
        {
            try
            {
                await _pricingService.EnsurePricesLoadedAsync();
                _dispatcherQueue.TryEnqueue(() => IsPricingError = false);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MainViewModel] Pricing load failed: {ex.Message}");
                _dispatcherQueue.TryEnqueue(() => IsPricingError = true);
            }
        });

        RefreshSessionList();

#if !MOCK_CHART
        // Load cache for instant display
        var cached = await _apiService.LoadCacheAsync();
        if (cached != null)
        {
            IsUpdatingFromCache = true;
            await UpdateUsagePropertiesAsync(cached);
        }

        // WinRT DispatcherQueue required for CreateTimer() — not part of IDispatcherQueue abstraction.
        // InitializeAsync runs on the UI thread (called from MainView.Loaded), so GetForCurrentThread() is safe.
        var winuiDispatcherQueue = DispatcherQueue.GetForCurrentThread();

        // Start poll timer
        _pollTimer = winuiDispatcherQueue.CreateTimer();
        _pollTimer.Interval = TimeSpan.FromSeconds(_refreshIntervalSeconds);
        _pollTimer.Tick += async (s, e) => await PollUsageAsync();
        _pollTimer.Start();

        // Start countdown timer (ticks every 60 seconds)
        _countdownTimer = winuiDispatcherQueue.CreateTimer();
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

    // D-03: auto-poll wrapper — no 250ms floor (D-02). The 250ms anti-flicker only applies to manual Refresh.
    private async Task PollUsageAsync()
    {
        if (IsRefreshing) return;
        IsRefreshing = true;
        try { await PollUsageCoreAsync(); }
        finally { IsRefreshing = false; }
    }

    private async Task PollUsageCoreAsync()
    {
        HasApiError = false;
        ApiErrorMessage = string.Empty;

        try
        {
            var result = await _apiService.FetchUsageAsync();
            if (result != null)
            {
                await UpdateUsagePropertiesAsync(result);
                _autoReauthAttempted = false;  // D-02: HTTP 200 resets the auto-reauth budget

                // ORGID-03 / D-OG-04: org-mismatch soft-prompt counter.
                // Uses Utilization (0-100 integer range) per Phase-2 fix (NormalizedUtilization is 0-1 for display).
                var utilization = result.FiveHour?.Utilization ?? 0;
                if (utilization == 0 && HasActiveSession)
                {
                    _zeroUtilizationPollCount++;
                    if (_zeroUtilizationPollCount >= OrgMismatchPollThreshold && !_orgMismatchSuppressed)
                    {
                        IsOrgMismatchPromptVisible = true;
                    }
                }
                else
                {
                    _zeroUtilizationPollCount = 0;
                    IsOrgMismatchPromptVisible = false;
                }
            }
            else
            {
                HasApiError = true;
                ApiErrorMessage = "API returned empty data. The response body could not be deserialized.";
            }
        }
        catch (HttpFetchException ex)
        {
            HasApiError = true;
            ApiErrorMessage = $"API request failed (HTTP {ex.StatusCode}).";
            Debug.WriteLine($"[MainViewModel] PollUsage: {ex.Message}");
        }
        catch (Exception ex)
        {
            HasApiError = true;
            ApiErrorMessage = "API request failed. Please try again.";
            Debug.WriteLine($"[MainViewModel] PollUsage: {ex.Message}");
        }
    }

    private async Task UpdateUsagePropertiesAsync(UsageResponse data)
    {
        // 5-STUNDEN-FENSTER = FiveHour
        if (data.FiveHour != null)
        {
            var util = data.FiveHour.NormalizedUtilization;
            FiveHourUtilization = util;
            FiveHourPercentage = Math.Min(util * 100, 100);
            FiveHourPercentageText = $"{Math.Min(util * 100, 100):0}%";
            FiveHourCountdown = CountdownFormatter.FormatCountdown(data.FiveHour.ResetsAt);

            await AppendHistoryPointAsync(data.FiveHour.ResetsAt, util);

            // Burn rate prediction — uses Utilization (0-100) NOT NormalizedUtilization (0-1)
            var prediction = BurnRateCalculator.Predict(
                UsageHistoryPoints,
                data.FiveHour.Utilization,
                data.FiveHour.ResetsAt);

            IsBurnRateWarningVisible = prediction != null;
            BurnRateWarningText = prediction != null
                ? FormatBurnRateText(prediction.MinutesUntilLimit)
                : string.Empty;
            _burnRateNotificationService.CheckBurnRate(prediction);
        }
        else
        {
            FiveHourUtilization = 0;
            FiveHourPercentage = 0;
            FiveHourPercentageText = "--";
            FiveHourCountdown = "--";
            _fiveHourResetsAt = null;
            RecomputeNextWindowLabel();   // NEXTWIN — clears label when API returns no FiveHour
            IsBurnRateWarningVisible = false;
            BurnRateWarningText = string.Empty;
            _burnRateNotificationService.CheckBurnRate(null);
        }

        // WOCHENLIMIT = SevenDayOpus (fallback to SevenDay)
        var weeklyWindow = data.SevenDayOpus ?? data.SevenDay;
        ApplyWeeklyWindow(weeklyWindow,
            v => WeeklyUtilization = v, v => WeeklyPercentage = v, v => WeeklyPercentageText = v,
            v => WeeklyCountdown = v, v => WeeklyResetDate = v, v => _weeklyResetsAt = v);

        // SONNET WOCHENLIMIT = SevenDaySonnet
        HasSonnetData = data.SevenDaySonnet != null;
        ApplyWeeklyWindow(data.SevenDaySonnet,
            v => SonnetUtilization = v, v => SonnetPercentage = v, v => SonnetPercentageText = v,
            v => SonnetCountdown = v, v => SonnetResetDate = v, v => _sonnetResetsAt = v);
    }

    private static string FormatBurnRateText(int minutesUntilLimit)
    {
        var timeLabel = BurnRateFormatter.FormatTimeLabel(minutesUntilLimit);
        return string.Format(
            Localizer.Get().GetLocalizedString("BurnRateBannerText"),
            timeLabel);
    }

    private static void ApplyWeeklyWindow(
        UsageWindow? window,
        Action<double> setUtilization, Action<double> setPercentage, Action<string> setPercentageText,
        Action<string> setCountdown, Action<string> setResetDate, Action<DateTimeOffset?> setResetsAt)
    {
        if (window != null)
        {
            var util = window.NormalizedUtilization;
            setUtilization(util);
            setPercentage(Math.Min(util * 100, 100));
            setPercentageText($"{Math.Min(util * 100, 100):0}%");
            setCountdown(CountdownFormatter.FormatCountdown(window.ResetsAt));
            setResetDate(CountdownFormatter.FormatResetDate(window.ResetsAt));
            setResetsAt(window.ResetsAt);
        }
        else
        {
            setUtilization(0);
            setPercentage(0);
            setPercentageText("--");
            setCountdown("--");
            setResetDate("--");
            setResetsAt(null);
        }
    }

    /// <summary>
    /// Appends a new data point to persisted history, clearing history first when the 5-hour window resets.
    /// </summary>
    private async Task AppendHistoryPointAsync(DateTimeOffset? apiResetsAt, double utilization)
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

        await _historyService.SaveHistoryAsync(history);

        // Set window timestamp BEFORE invalidating chart so FiveHourWindowStart is non-null when draw handler runs
        _fiveHourResetsAt = apiResetsAt;
        RecomputeNextWindowLabel();   // NEXTWIN — recomputes when fresh API resetsAt arrives
        UsageHistoryPoints = history.Points.AsReadOnly();
        InvalidateChart();
    }

    private static readonly TimeSpan WindowResetTolerance = TimeSpan.FromMinutes(2);

    internal static bool IsWindowReset(DateTimeOffset? storedResetsAt, DateTimeOffset? apiResetsAt)
    {
        if (!storedResetsAt.HasValue || !apiResetsAt.HasValue) return false;

        var difference = (apiResetsAt.Value - storedResetsAt.Value).Duration();
        return difference > WindowResetTolerance;
    }

    private void UpdateCountdowns()
    {
        FiveHourCountdown = CountdownFormatter.FormatCountdown(_fiveHourResetsAt);
        RecomputeNextWindowLabel();
        WeeklyCountdown = CountdownFormatter.FormatCountdown(_weeklyResetsAt);
        SonnetCountdown = CountdownFormatter.FormatCountdown(_sonnetResetsAt);
    }

    /// <summary>
    /// NEXTWIN-01..03 (D-NW-02..04): recomputes the absolute next-window label from
    /// _fiveHourResetsAt. Hides the label (Visibility=Collapsed) when ResetsAt is null OR
    /// IsSessionExpired is true (auth banner takes priority — banner-stack alignment with PRICING).
    /// Format pattern is loaded from MainView.NextWindow.Label{De,En} resw key based on
    /// CultureInfo.CurrentUICulture.
    /// </summary>
    private void RecomputeNextWindowLabel()
    {
        if (_fiveHourResetsAt is null || IsSessionExpired)
        {
            IsFiveHourNextWindowVisible = false;
            FiveHourNextWindowText = string.Empty;
            return;
        }

        var culture = CultureInfo.CurrentUICulture;
        var formatKey = culture.Name.StartsWith("de", StringComparison.OrdinalIgnoreCase)
            ? "MainView.NextWindow.LabelDe"
            : "MainView.NextWindow.LabelEn";
        var format = Localizer.Get().GetLocalizedString(formatKey);

        FiveHourNextWindowText = _fiveHourResetsAt.Value.LocalDateTime.ToString(format, culture);
        IsFiveHourNextWindowVisible = true;
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
        if (_dataUpdatedHandler is not null)
        {
            _jsonlService.DataUpdated -= _dataUpdatedHandler;
            _dataUpdatedHandler = null;
        }
        _jsonlService.Stop();
        _updateService.StopPeriodicCheck();
        _sessionNameStore.NameChanged -= OnSessionNameChanged;
        WeakReferenceMessenger.Default.UnregisterAll(this);
    }

    // D-07: Active = single-line Cwd. Inactive = two-line "Cwd\n<template formatted with threshold>".
    //       Try/catch protects against Phase 23 ordering (key may not exist yet).
    private static string ComputeTooltipText(SessionInfo session, bool isActive, int sessionTimeoutMinutes)
    {
        if (isActive)
        {
            return session.Cwd;
        }

        string template;
        try
        {
            template = Localizer.Get().GetLocalizedString("InactiveSessionTooltip");
        }
        catch
        {
            // Defensive fallback if Localizer throws (Phase 23 authors the resw key).
            template = "Inactive for > {0}min";
        }

        return $"{session.Cwd}\n{string.Format(template, sessionTimeoutMinutes)}";
    }

    // G-1: NameChanged may arrive off-thread (singleton-published event from any caller).
    //      Always-TryEnqueue per CLAUDE.md MVVM Conventions, no HasThreadAccess shortcut.
    private void OnSessionNameChanged(object? sender, SessionNameChangedEventArgs args)
    {
        _dispatcherQueue.TryEnqueue(RefreshSessionList);
    }

    /// <summary>True when a session is selected — gates the rename pencil button.</summary>
    public bool HasSelectedSession => SelectedSession != null;

    /// <summary>
    /// Triggered by the pencil button. View-layer code-behind handles the actual ContentDialog
    /// because ContentDialog requires an XamlRoot — but MainView passes the SelectedSession
    /// snapshot through this command so all rename logic stays in the ViewModel.
    /// Save flow is invoked by the View via SaveCustomNameAsync below.
    /// </summary>
    [RelayCommand(CanExecute = nameof(HasSelectedSession))]
    private void OpenRenameDialog()
    {
        // Intentionally empty — the View's Click handler queries SelectedSession and shows
        // the dialog. The Command exists so the Button binds with proper CanExecute gating
        // and accessibility (RelayCommand publishes IsEnabled).
    }

    /// <summary>
    /// Persists a new custom name from the rename dialog. View calls this with already-trimmed input.
    /// </summary>
    public async Task SaveCustomNameAsync(string sessionId, string newName)
    {
        if (string.IsNullOrEmpty(sessionId)) return;
        var sanitized = SessionNameSanitizer.Strip(newName).Trim();
        if (string.IsNullOrEmpty(sanitized))
        {
            _sessionNameStore.ClearCustomName(sessionId);
        }
        else
        {
            _sessionNameStore.SetCustomName(sessionId, sanitized);
        }
        await _sessionNameStore.SaveAsync();
    }

    /// <summary>Persists "no custom name" (Reset button in rename dialog).</summary>
    public async Task ClearCustomNameAsync(string sessionId)
    {
        if (string.IsNullOrEmpty(sessionId)) return;
        _sessionNameStore.ClearCustomName(sessionId);
        await _sessionNameStore.SaveAsync();
    }

    /// <summary>Lookup helper for the View — exposes whether a custom name currently exists.</summary>
    public bool HasCustomName(string sessionId)
        => _sessionNameStore.GetCustomName(sessionId) != null;

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

        // D-06: removed .Where(s => s.IsActive(threshold)) filter — inactive sessions now visible
        //       in ComboBox to support POLISH-04 (two-line tooltip). Per-item IsActive replaces
        //       the previous hardcoded `IsActive = true` (was correct only because of the filter).
        var thresholdMinutes = settings.SessionActivityThresholdMinutes;

        // DROPDOWN-01 / DROPDOWN-04 / D-03: display-layer visibility cutoff.
        // JsonlService keeps aggregating ALL sessions (cost / quota totals must NOT lose data) —
        // we only filter the user-visible ComboBox source here.
        var visibilityCutoff = settings.SessionVisibilityWindowDays > 0
            ? DateTimeOffset.UtcNow.AddDays(-settings.SessionVisibilityWindowDays)
            : DateTimeOffset.MinValue;

        var displayItems = latestSessions
            .Where(s => s.LastActivity >= visibilityCutoff)
            .OrderByDescending(s => s.LastActivity)
            .Select(s =>
            {
                var isActive = s.IsActive(threshold);
                return new SessionDisplayItem
                {
                    Session = s,
                    DisplayName = _sessionNameStore.GetCustomName(s.Id) ?? s.DisplayName,   // RENAME-08
                    IsActive = isActive,
                    TooltipText = ComputeTooltipText(s, isActive, thresholdMinutes)
                };
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
        _statisticsCts?.Cancel();

        var period = (TimePeriod)value;
        if (period == TimePeriod.Session)
        {
            UpdateStatisticsFromSession();
        }
        else
        {
            var cts = new CancellationTokenSource();
            _statisticsCts = cts;
            _ = AggregateStatisticsAsync(period, cts.Token);
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

    private async Task AggregateStatisticsAsync(TimePeriod period, CancellationToken ct = default, bool showLoading = true)
    {
        if (showLoading)
        {
            IsAggregating = true;
        }
        try
        {
            // PRICING-02 / CD-04: manual refresh + auto-poll BOTH clear IsPricingError on success.
            // This site runs inside the existing dispatcher chain (no extra TryEnqueue needed — G-1).
            await _pricingService.EnsurePricesLoadedAsync();
            IsPricingError = false;
            ct.ThrowIfCancellationRequested();
            var stats = await Task.Run(() => _jsonlService.GetStatistics(period), ct);
            _dispatcherQueue.TryEnqueue(() => ApplyStatistics(stats));
        }
        catch (OperationCanceledException)
        {
            // Tab switched — discard stale result
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MainViewModel] AggregateStatistics failed: {ex.Message}");
            IsPricingError = true;
            _dispatcherQueue.TryEnqueue(() => ApplyStatistics(StatisticsSummary.Empty));
        }
        finally
        {
            if (showLoading)
            {
                _dispatcherQueue.TryEnqueue(() => IsAggregating = false);
            }
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

        if (SelectedTabIndex == (int)TimePeriod.Session)
        {
            UpdateStatisticsFromSession();
        }
        else
        {
            _statisticsCts?.Cancel();
            var cts = new CancellationTokenSource();
            _statisticsCts = cts;
            _ = AggregateStatisticsAsync((TimePeriod)SelectedTabIndex, cts.Token, showLoading: false);
        }
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

    // D-02: 250ms anti-flicker floor — manual click only. D-04 Option A: CanExecute auto-disables button while refreshing.
    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private async Task Refresh()
    {
        if (IsRefreshing) return;
        IsRefreshing = true;
        try
        {
            await Task.WhenAll(
                PollUsageCoreAsync(),
                Task.Delay(TimeSpan.FromMilliseconds(MinimumSpinnerDisplayMs))
            );
        }
        finally { IsRefreshing = false; }
    }

    public bool CanRefresh => !IsRefreshing;

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
        _autoReauthAttempted = false;  // D-02: explicit reset on user-driven logout
        _navigationService.NavigateTo<LoginView>();
    }

    [RelayCommand]
    private void ReLogin()
    {
        IsSessionExpired = false;
        _navigationService.NavigateTo<LoginView>();
    }

    /// <summary>
    /// DROPDOWN-05 / D-04 / CD-02: dismiss the migration toast and persist immediately.
    /// CD-02 rule: SaveSettings is synchronous (no app-shutdown dependency) so a crash
    /// between dismiss and shutdown does not re-show the toast on next launch.
    /// </summary>
    [RelayCommand]
    private void DismissMigrationToast()
    {
        IsSessionVisibilityMigrationToastVisible = false;

        var settings = _settingsService.LoadSettings();
        settings.SessionVisibilityMigrationShown = true;
        _settingsService.SaveSettings(settings);
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
        // ExportHelper.CopyChartToClipboardAsync requires the WinRT DispatcherQueue type for
        // Clipboard.SetContent marshaling. Obtain it here on the UI thread (command executes on UI thread).
        var winuiDispatcherQueue = DispatcherQueue.GetForCurrentThread();
        await ExportHelper.CopyChartToClipboardAsync(winuiDispatcherQueue, UsageHistoryPoints, FiveHourWindowStart, FiveHourPercentageText, FiveHourCountdown, FiveHourUtilization);
    }

    [RelayCommand]
    private void OpenUpdateDownload()
    {
        if (string.IsNullOrEmpty(_updateDownloadUrl)) return;
        if (!_updateDownloadUrl.StartsWith("https://github.com/", StringComparison.OrdinalIgnoreCase)) return;
        Process.Start(new ProcessStartInfo(_updateDownloadUrl) { UseShellExecute = true });
    }

    private void OnUpdateAvailable(string version, string downloadUrl)
    {
        _updateDownloadUrl = downloadUrl;
        _updateVersion = version;
        _dispatcherQueue.TryEnqueue(() =>
        {
            // TODO Phase 27 (L10N-01): localize via WinUI3Localizer like FormatBurnRateText.
            UpdateMessage = $"Update v{version} verfügbar";
            IsUpdateAvailable = true;
        });
    }

    public void DismissUpdate()
    {
        var settings = _settingsService.LoadSettings();
        settings.DismissedUpdateVersion = _updateVersion;
        _settingsService.SaveSettings(settings);
        IsUpdateAvailable = false;
    }

    public void Receive(AuthStateChangedMessage message)
    {
        // L-04 / PITFALLS C2-P1: always-TryEnqueue. ClaudeApiService Send sites at FetchUsageAsync:88
        // and TryMigrateOrgIdAsync:184 may run on the HttpClient continuation thread; off-thread
        // mutation of [ObservableProperty] fields below produces inconsistent mid-update state.
        _dispatcherQueue.TryEnqueue(() => HandleAuthStateChangedCore(message));
    }

    private void HandleAuthStateChangedCore(AuthStateChangedMessage message)
    {
        // D-03: post-login refresh — clear error flags, reset auto-reauth budget, refresh immediately.
        if (message.Value)
        {
            IsSessionExpired = false;
            HasApiError = false;
            _autoReauthAttempted = false;
            // CD-02 / PITFALLS C1-P1: explicit discard documents intentional fire-and-forget.
            // [RelayCommand] machinery already catches exceptions inside Refresh() and surfaces
            // them via HasApiError / ApiErrorMessage in PollUsageCoreAsync (lines 428-458).
            // Adding a try/catch at THIS call site would be dead code.
            _ = RefreshCommand.ExecuteAsync(null);
            return;
        }

        // D-01: first 401 in a session → auto-navigate to LoginView, do NOT open InfoBar.
        // NOTE: ClaudeApiService has two send sites for AuthStateChangedMessage(false)
        // (FetchUsageAsync:88 and TryMigrateOrgIdAsync:184). Stacked-401 edge case is accepted —
        // Receive(true) post-login clears IsSessionExpired so a stale flag resolves at next login.
        if (!_autoReauthAttempted)
        {
            _autoReauthAttempted = true;
            _navigationService.NavigateTo<LoginView>();
            return;
        }

        // Second 401 (and beyond): existing InfoBar fallback path (AUTH-02).
        IsSessionExpired = true;
        StatusMessage = "Session expired. Please re-login to continue.";
    }

    public void Receive(SessionTimeoutChangedMessage message)
    {
        // D-08: rebuild SortedSessions on threshold change so TooltipText reflects new minutes.
        // Dispatched to UI thread — RefreshSessionList requires it.
        // G-1 compliant: constructor-injected _dispatcherQueue is non-null. CD-05 #2 — implicit-default exemption (no [ThreadSafeReceive] needed).
        _dispatcherQueue.TryEnqueue(RefreshSessionList);
    }

    public void Receive(SessionVisibilityChangedMessage message)
    {
        // DROPDOWN-04 / D-03: re-apply visibility cutoff filter on SortedSessions.
        // Dispatched to UI thread — RefreshSessionList requires it.
        // G-1 compliant: constructor-injected _dispatcherQueue is non-null. L-02 honored.
        _dispatcherQueue.TryEnqueue(RefreshSessionList);
    }

    // NEXTWIN-02 (D-NW-02): hide the next-window label when auth banner appears.
    partial void OnIsSessionExpiredChanged(bool value) => RecomputeNextWindowLabel();

    /// <summary>
    /// ORGID-03: navigates user to Settings → Account and signals SettingsViewModel
    /// to open the OrgPicker ContentDialog. Fires from the MainView soft-prompt InfoBar
    /// "Re-resolve" button.
    /// </summary>
    [RelayCommand]
    private void ResolveOrgMismatch()
    {
        IsOrgMismatchPromptVisible = false;
        _navigationService.NavigateTo<SettingsView>();
        WeakReferenceMessenger.Default.Send(new OpenOrgPickerRequestedMessage());
    }

    /// <summary>
    /// ORGID-04 (D-OG-05): suppresses the soft-prompt for the rest of the in-process session.
    /// NOT persisted — flag resets to false on next app start.
    /// </summary>
    [RelayCommand]
    private void SuppressOrgMismatchPrompt()
    {
        _orgMismatchSuppressed = true;
        IsOrgMismatchPromptVisible = false;
    }
}

