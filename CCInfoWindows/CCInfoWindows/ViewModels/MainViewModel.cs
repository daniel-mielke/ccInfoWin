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
///
/// Cross-VM settings changes are NOT received here. MainView carries no NavigationCacheMode, so this
/// ViewModel does not exist while SettingsView is on screen — which is exactly when a settings change
/// happens. Every setting is therefore re-read from disk in <see cref="InitializeAsync"/> and
/// <see cref="RefreshSessionList"/>; see CLAUDE.md's cross-VM priority rule (direct DI >
/// singleton-service event > WeakReferenceMessenger) before adding a channel here.
/// </summary>
public partial class MainViewModel : ObservableObject,
    IRecipient<AuthStateChangedMessage>
{
    private const string PollLogSource = "MainViewModel.PollUsage";
    private const string StartupLogSource = "MainViewModel.InitializeAsync";
    private const string StatisticsLogSource = "MainViewModel.AggregateStatistics";
    private const string SessionNameLogSource = "MainViewModel.SaveCustomName";
    private const string NextWindowLogSource = "MainViewModel.NextWindowLabel";
    private const string ExportLogSource = "MainViewModel.ExportChart";
    private const string ContextWindowLogSource = "MainViewModel.UpdateSessionData";

    // Single-segment resw uids, plus the text used when the dictionary cannot answer. The rename
    // failure shares SettingsViewModel's key deliberately: same failure, same sentence, one
    // translation to keep correct.
    private const string SessionNameSaveFailedUid = "SettingsSessionNameSaveFailed";
    private const string SessionNameSaveFailedFallback = "The session name could not be saved.";
    internal const string ChartExportFailedUid = "ChartExportFailed";
    private const string ChartExportFailedFallback = "The chart could not be exported.";

    private readonly ICredentialService _credentialService;
    private readonly INavigationService _navigationService;
    private readonly IClaudeApiService _apiService;
    private readonly ISettingsService _settingsService;
    private readonly IUsageHistoryService _historyService;
    private readonly IJsonlService _jsonlService;
    private readonly IPricingService _pricingService;
    private readonly IUpdateService _updateService;
    private readonly IUsageNotificationService _usageNotificationService;
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

    // PRICING-01..03 (D-PR-01, D-PR-04): mirrors _pricingService.Source == Unknown. Set only by
    // ApplyPricingSource — never from a catch, since EnsurePricesLoadedAsync cannot throw.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPricingErrorVisible))]
    private bool _isPricingError;

    // DROPDOWN-05 / D-04: one-time migration toast for existing installs.
    // True only on first launch after upgrade -- persisted via SaveSettings on dismiss (CD-02).
    [ObservableProperty]
    private bool _isSessionVisibilityMigrationToastVisible;

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

    // --- Threshold brushes for the three progress bars ---

    // Finding 42: these used to be an IValueConverter reading Application.Current.Resources, which
    // resolves ThemeDictionaries against Application.RequestedTheme -- the OS theme, which this app
    // never sets -- while every {ThemeResource} beside it followed the element theme the app does
    // set. A converter also cannot re-run on ActualThemeChanged, so the bars kept the palette of
    // whichever theme was active when the last poll landed. Computing them here fixes both: the
    // source is ChartColors (the same table the chart draws from) and ApplyTheme recomputes on toggle.

    [ObservableProperty]
    private SolidColorBrush _contextUtilizationBrush;

    [ObservableProperty]
    private SolidColorBrush _weeklyUtilizationBrush;

    [ObservableProperty]
    private SolidColorBrush _sonnetUtilizationBrush;

    /// <summary>
    /// Element theme MainView last reported. Dark is the app's default for every ColorMode other
    /// than "light" (App.ApplyPersistedTheme), so a ViewModel nobody tells stays consistent with it.
    /// </summary>
    private bool _isDarkTheme = true;

    /// <summary>Gray-400: the model badge colour before any model has been identified.</summary>
    internal const string InitialBadgeColorHex = "#9CA3AF";

    // --- Spinner / refresh constants ---

    private const int MinimumSpinnerDisplayMs = 250;

    /// <summary>Countdown labels have minute resolution, so a faster tick would only burn CPU.</summary>
    private static readonly TimeSpan CountdownTickInterval = TimeSpan.FromMinutes(1);

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

    // Findings 24 + 25: one banner for "the button you just pressed did not do what it said".
    // Rename persistence and chart export both report failure through a bool and log the technical
    // detail themselves, so all the user needs here is a generic localized sentence.

    [ObservableProperty]
    private bool _hasActionError;

    [ObservableProperty]
    private string _actionErrorMessage = string.Empty;

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
    /// Monotonic id of the newest context-window read. Only the newest read may paint the panels; see
    /// <see cref="ApplyContextWindow"/>.
    /// </summary>
    private int _contextWindowRequest;

    /// <summary>
    /// One-shot flag for D-01 auto-reauth routing. Reset at constructor default and on the
    /// PollUsageAsync HTTP 200 success path. Neither logout nor a successful login needs to reset it:
    /// both navigate away, and the next dashboard is a brand-new transient ViewModel.
    /// </summary>
    private bool _autoReauthAttempted;

    /// <summary>Which API field the weekly notification window is currently pinned to.</summary>
    private enum WeeklyWindowSource
    {
        None,
        SevenDayOpus,
        SevenDay
    }

    /// <summary>
    /// Number of consecutive polls the pinned source may be missing before the other one is adopted.
    /// One tolerated miss covers a single truncated response; a source that is gone for two polls in
    /// a row is gone for real.
    /// </summary>
    private const int MaxPinnedWeeklySourceMisses = 1;

    private WeeklyWindowSource _pinnedWeeklySource = WeeklyWindowSource.None;
    private DateTimeOffset? _pinnedWeeklyResetsAt;
    private int _pinnedWeeklySourceMisses;

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
        IUsageNotificationService usageNotificationService,
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
        _usageNotificationService = usageNotificationService;
        _dispatcherQueue = dispatcherQueue;
        _sessionNameStore = sessionNameStore;

        // G-3 / CLEANUP-02: initialize to gray-400 fallback before any poll runs, so bindings
        // never read null. Uses _brushFactory seam so tests can inject a headless fake.
        _contextModelBadgeColor = _brushFactory(InitialBadgeColorHex);

        // G-3: same rule for the three progress-bar foregrounds. Zero utilization is the green zone,
        // which is what the bars show before the first poll anyway.
        _contextUtilizationBrush = ZoneBrush(0);
        _weeklyUtilizationBrush = ZoneBrush(0);
        _sonnetUtilizationBrush = ZoneBrush(0);

        // Messenger registration happens in InitializeAsync (paired with UnregisterAll for re-init safety — PITFALLS C2-P3).
        _updateService.UpdateAvailable += OnUpdateAvailable;
    }

    /// <summary>
    /// Initializes polling and countdown timers. Call from MainView.Loaded event.
    /// </summary>
    public async Task InitializeAsync()
    {
        // CD-04 / PITFALLS C2-P3: prevent double-subscription if InitializeAsync is called twice
        // (a re-login runs MainView.Loaded again on the same instance).
        WeakReferenceMessenger.Default.UnregisterAll(this);
        WeakReferenceMessenger.Default.Register<AuthStateChangedMessage>(this);

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

        // Attach to the JSONL service the app host already started (finding 29). IJsonlService is a
        // singleton owning a FileSystemWatcher and a debounce timer, and this ViewModel is transient:
        // driving its lifecycle from MainView's visual-tree membership tore the watcher down on every
        // Settings round-trip and paid a full forced re-scan on the way back.
        //
        // Subscribe BEFORE sampling IsScanning so a scan that finishes in between cannot be missed,
        // and note that no scan is requested here: RefreshSessionList below reads whatever snapshot
        // the host's scan has already published, and DataUpdated delivers the rest.
        _dataUpdatedHandler = (s, e) => _dispatcherQueue.TryEnqueue(RefreshSessionList);
        _jsonlService.DataUpdated += _dataUpdatedHandler;

        IsJsonlScanning = _jsonlService.IsScanning;

        // Load pricing in background — non-blocking, fallback activates on failure.
        // PRICING-01..03 (D-PR-01, D-PR-03): surface failures via IsPricingError; clear on subsequent success.
        // Marshal back to the UI thread because Task.Run runs off the UI thread (G-1 alignment for property mutation).
        _ = Task.Run(async () =>
        {
            try
            {
                await _pricingService.EnsurePricesLoadedAsync();
                _dispatcherQueue.TryEnqueue(() =>
                {
                    ApplyPricingSource();
                    // Statistics rendered before prices arrived were priced with whatever was
                    // seeded from the bundled table; recompute once the live data lands. Much
                    // smaller than upstream's generation/invalidation machinery because there is
                    // no parse cache here — CalculateEntryCost calls GetPrice fresh every time.
                    RecomputeStatisticsForCurrentTab();
                });
            }
            catch (Exception ex)
            {
                // EnsurePricesLoadedAsync is documented as non-throwing, so this only fires if the
                // Task.Run machinery itself fails. IsPricingError is not set from here: the service's
                // own Source is the authority (finding 34).
                AppLog.Write(StartupLogSource, ex, "pricing load failed");
            }
        });

        RefreshSessionList();

#if !MOCK_CHART
        // Timers FIRST (finding 4): anything that throws in the cache-hydration or first-poll block
        // below used to leave a fully rendered dashboard with no poller at all, recoverable only by
        // restarting the app.
        StartTimers();

        await RenderCachedUsageAsync();

        // Immediate first poll
        await PollUsageAsync();
        IsUpdatingFromCache = false;
#endif

        // One stateless check so the banner can appear on the dashboard the user just opened. The
        // hourly PeriodicTimer is started once by the app host (finding 29): restarting it from a
        // transient ViewModel meant a user who opened Settings more often than hourly never
        // completed an update check.
        await _updateService.CheckForUpdateAsync();
    }

    /// <summary>
    /// Creates and starts the poll and countdown timers. Runs before any data is rendered so a
    /// failure further into the bootstrap cannot leave the dashboard without a poller (finding 4).
    /// </summary>
    private void StartTimers()
    {
        // WinRT DispatcherQueue required for CreateTimer() — not part of the IDispatcherQueue
        // abstraction. InitializeAsync runs on the UI thread (called from MainView.Loaded), so
        // GetForCurrentThread() is safe.
        var winuiDispatcherQueue = DispatcherQueue.GetForCurrentThread();

        _pollTimer = winuiDispatcherQueue.CreateTimer();
        _pollTimer.Tick += async (s, e) => await PollUsageAsync();
        ApplyRefreshInterval();

        _countdownTimer = winuiDispatcherQueue.CreateTimer();
        _countdownTimer.Interval = CountdownTickInterval;
        _countdownTimer.Tick += (s, e) => UpdateCountdowns();
        _countdownTimer.Start();
    }

    /// <summary>
    /// Applies the current refresh interval to the poll timer, or stops it when the user chose
    /// "Manual". A zero interval is not a fast poll — DispatcherQueueTimer would either reject it
    /// or tick continuously — so the sentinel has to be handled here rather than converted.
    /// </summary>
    private void ApplyRefreshInterval()
    {
        if (_pollTimer is null) return;

        if (!ShouldPollAutomatically(_refreshIntervalSeconds))
        {
            _pollTimer.Stop();
            return;
        }

        _pollTimer.Interval = TimeSpan.FromSeconds(_refreshIntervalSeconds);
        _pollTimer.Start();
    }

    /// <summary>
    /// True when the persisted interval asks for automatic polling. AppSettings.ManualRefreshSeconds
    /// means "manual only"; anything below it can only come from a settings.json SettingsService
    /// could not clamp, and is treated the same way.
    /// </summary>
    internal static bool ShouldPollAutomatically(int refreshIntervalSeconds) =>
        refreshIntervalSeconds > AppSettings.ManualRefreshSeconds;

    /// <summary>
    /// Paints the last persisted API snapshot so the dashboard is populated before the first live
    /// poll returns. Best-effort: an unreadable cache must not stop the poll that would replace it.
    /// IsUpdatingFromCache stays set until that poll lands — it drives the "updating" hint.
    /// </summary>
    private async Task RenderCachedUsageAsync()
    {
        try
        {
            var cached = await _apiService.LoadCacheAsync();
            if (cached == null) return;

            IsUpdatingFromCache = true;
            await UpdateUsagePropertiesAsync(cached);
        }
        catch (Exception ex)
        {
            AppLog.Write(StartupLogSource, ex, "rendering the cached usage snapshot failed");
        }
    }

    /// <summary>
    /// PRICING-01..03 (D-PR-01): the pricing InfoBar is driven by the service's own Source, because
    /// EnsurePricesLoadedAsync handles every failure internally and never throws — deriving the flag
    /// from a caught exception left the banner permanently unreachable (finding 34).
    /// </summary>
    private void ApplyPricingSource()
        => IsPricingError = _pricingService.Source == PricingSource.Unknown;

    /// <summary>
    /// Builds the threshold brush for a utilization value in the app's current element theme, via
    /// the same G-3 factory seam the model badge uses.
    /// </summary>
    private SolidColorBrush ZoneBrush(double utilization)
        => _brushFactory(ToHexColor(ChartColors.GetZoneColor(utilization, _isDarkTheme)));

    private static string ToHexColor(Windows.UI.Color color)
        => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    /// <summary>
    /// Called by MainView on load and on ActualThemeChanged. x:Bind OneWay cannot re-evaluate on a
    /// theme toggle, so the brushes are recomputed here instead of by a converter (finding 42).
    /// </summary>
    internal void ApplyTheme(bool isDark)
    {
        _isDarkTheme = isDark;
        ContextUtilizationBrush = ZoneBrush(ContextUtilization);
        WeeklyUtilizationBrush = ZoneBrush(WeeklyUtilization);
        SonnetUtilizationBrush = ZoneBrush(SonnetUtilization);
    }

    partial void OnContextUtilizationChanged(double value) => ContextUtilizationBrush = ZoneBrush(value);

    partial void OnWeeklyUtilizationChanged(double value) => WeeklyUtilizationBrush = ZoneBrush(value);

    partial void OnSonnetUtilizationChanged(double value) => SonnetUtilizationBrush = ZoneBrush(value);

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
            AppLog.Write(PollLogSource, ex, "usage fetch returned an HTTP error");
        }
        catch (Exception ex)
        {
            HasApiError = true;
            // Exception type only, never ex.Message: a failing cache write put the full
            // %LOCALAPPDATA% path of the user's profile on screen and into every pasted
            // screenshot. The type still distinguishes a bridge failure from a network one,
            // and app.log has the message plus the stack.
            ApiErrorMessage = $"API request failed ({ex.GetType().Name}).";
            AppLog.Write(PollLogSource, ex, "usage fetch failed");
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
            _usageNotificationService.CheckBurnRate(prediction);
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
            _usageNotificationService.CheckBurnRate(null);
        }

        // WOCHENLIMIT = SevenDayOpus (fallback to SevenDay).
        // The DISPLAY deliberately keeps the plain fallback: the panel must show what the API just
        // said on every poll, and freezing it at a stale percentage to protect a notification would
        // trade a visible lie for an invisible one. Notifications get the pinned window below —
        // they are one-shot and irreversible, so they must not fire on a source flip.
        ApplyWeeklyWindow(data.SevenDayOpus ?? data.SevenDay,
            v => WeeklyUtilization = v, v => WeeklyPercentage = v, v => WeeklyPercentageText = v,
            v => WeeklyCountdown = v, v => WeeklyResetDate = v, v => _weeklyResetsAt = v);

        // SONNET WOCHENLIMIT = SevenDaySonnet
        HasSonnetData = data.SevenDaySonnet != null;
        ApplyWeeklyWindow(data.SevenDaySonnet,
            v => SonnetUtilization = v, v => SonnetPercentage = v, v => SonnetPercentageText = v,
            v => SonnetCountdown = v, v => SonnetResetDate = v, v => _sonnetResetsAt = v);

        // Threshold + window-reset toasts. One call after both windows are applied rather than
        // one per branch: a weekly rotation has to be evaluated even in a poll without FiveHour.
        // SevenDaySonnet gets no notification (upstream scope: 5h + primary weekly window).
        _usageNotificationService.CheckWindows(data.FiveHour, PinWeeklyNotificationWindow(data));
    }

    /// <summary>
    /// Picks the weekly window the notification service is allowed to see, pinned to one API field
    /// for the lifetime of a window (finding 20a).
    ///
    /// `SevenDayOpus ?? SevenDay` are two windows with independent resets_at, so a poll that
    /// transiently omits seven_day_opus changes the window identity without anything having rotated
    /// — which announced a bogus "weekly window reset" and re-armed the 80/95 toasts. The pinned
    /// source is kept while it is reported; when it goes missing, the window is skipped for this
    /// poll (returning null makes CheckWindows a no-op, leaving the armed countdown untouched) and
    /// the other source is only adopted once the pinned window's own reset time has passed or the
    /// pinned source has been absent for more than <see cref="MaxPinnedWeeklySourceMisses"/> polls.
    /// </summary>
    internal UsageWindow? PinWeeklyNotificationWindow(UsageResponse data)
    {
        // Nothing pinned yet, or the pinned window has run out: both are clean points to re-apply the
        // preference, so one poll's omission does not pin the fallback for the rest of the process.
        if (_pinnedWeeklySource == WeeklyWindowSource.None || IsPinnedWeeklyWindowOver())
        {
            return RepinWeeklySource(data);
        }

        var pinned = ReadPinnedWeeklyWindow(data);
        if (pinned != null)
        {
            _pinnedWeeklySourceMisses = 0;
            _pinnedWeeklyResetsAt = pinned.ResetsAt;
            return pinned;
        }

        // Missing for one poll: report nothing rather than the other window. A null window makes
        // CheckWindows a no-op, so the armed reset countdown and the 80/95 flags stay as they are.
        if (++_pinnedWeeklySourceMisses <= MaxPinnedWeeklySourceMisses) return null;

        return RepinWeeklySource(data);
    }

    /// <summary>
    /// True once the pinned window's own reset time has passed, or when no reset time was ever
    /// reported — a window without resets_at has no identity for the notification state to track.
    /// </summary>
    private bool IsPinnedWeeklyWindowOver()
        => _pinnedWeeklyResetsAt is null || _pinnedWeeklyResetsAt.Value <= DateTimeOffset.UtcNow;

    private UsageWindow? ReadPinnedWeeklyWindow(UsageResponse data) => _pinnedWeeklySource switch
    {
        WeeklyWindowSource.SevenDayOpus => data.SevenDayOpus,
        WeeklyWindowSource.SevenDay => data.SevenDay,
        _ => null
    };

    /// <summary>
    /// Adopts whichever weekly field the response actually carries, preferring seven_day_opus —
    /// the same precedence the display uses. Clears the pin when neither is present.
    /// </summary>
    private UsageWindow? RepinWeeklySource(UsageResponse data)
    {
        _pinnedWeeklySource = data.SevenDayOpus is not null ? WeeklyWindowSource.SevenDayOpus
            : data.SevenDay is not null ? WeeklyWindowSource.SevenDay
            : WeeklyWindowSource.None;

        var window = ReadPinnedWeeklyWindow(data);
        _pinnedWeeklyResetsAt = window?.ResetsAt;
        _pinnedWeeklySourceMisses = 0;
        return window;
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

        // Cutoff is the start of the CURRENT 5h window (apiResetsAt - 5h), not now - 5h.
        // Falls back to now - 5h only if the API never delivered a resetsAt. This prevents
        // points from the prior window leaking in when IsWindowReset misses (e.g. on cold
        // start where stored ResetsAt is null and the persisted history still holds samples
        // from a window that ended minutes ago).
        var cutoff = apiResetsAt.HasValue
            ? apiResetsAt.Value - windowDuration
            : now - windowDuration;
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
    /// Single-segment resw key carrying the next-window label pattern of the active language.
    /// WinUI3Localizer 2.3.0 keys its dictionary on the text before the FIRST '.', so a dotted uid
    /// resolves to nothing.
    /// </summary>
    internal const string NextWindowPatternUid = "NextWindowLabelPattern";

    /// <summary>
    /// NEXTWIN-01..03 (D-NW-02..04): recomputes the absolute next-window label from
    /// _fiveHourResetsAt, with the field order taken from the active language's
    /// <see cref="NextWindowPatternUid"/> entry. Hides the label (Visibility=Collapsed) when ResetsAt
    /// is null OR IsSessionExpired is true (auth banner takes priority — banner-stack alignment with
    /// PRICING).
    /// </summary>
    private void RecomputeNextWindowLabel()
    {
        if (_fiveHourResetsAt is null || IsSessionExpired)
        {
            IsFiveHourNextWindowVisible = false;
            FiveHourNextWindowText = string.Empty;
            return;
        }

        FiveHourNextWindowText = FormatNextWindowLabel(
            _fiveHourResetsAt.Value,
            LocalizedText.ResolveOrNull(NextWindowPatternUid, NextWindowLogSource),
            CultureInfo.CurrentUICulture);
        IsFiveHourNextWindowVisible = true;
    }

    /// <summary>
    /// Names this label's pattern uid for the shared formatter, and stays internal so the formatting
    /// can be asserted without a WinUI3Localizer host. The layout comes from the active language's resw
    /// entry rather than from a `culture.Name.StartsWith("de")` branch, which silently gave every third
    /// language the English field order; a missing or malformed pattern degrades to a culture-derived
    /// one instead of throwing into the caller's UI update.
    ///
    /// The body used to be a second copy of CountdownFormatter's — same try/ToString/catch, same
    /// culture-derived fallback — so a hardening applied to the weekly reset date silently skipped this
    /// label (finding 30).
    /// </summary>
    internal static string FormatNextWindowLabel(DateTimeOffset resetsAt, string? pattern, CultureInfo culture)
        => CountdownFormatter.FormatWithPattern(resetsAt, pattern, NextWindowPatternUid, culture);

    /// <summary>
    /// Releases everything this ViewModel owns: its two timers and its event subscriptions. Call from
    /// MainView.Unloaded.
    ///
    /// Deliberately does NOT stop IJsonlService or IUpdateService (finding 29). Both are singletons
    /// owning process-wide resources; stopping them here disposed the file watcher and the hourly
    /// update timer every time the user opened Settings, and — while a bootstrap was still in flight
    /// — could stop services a newer MainView was already using. The app host starts them at launch
    /// and stops them in MainWindow.OnClosing.
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
        // IUpdateService is a singleton and a .NET event holds a STRONG reference, so without this
        // every Settings round-trip left a whole transient ViewModel rooted for the process lifetime,
        // pinning its Sessions / SortedSessions / SubagentContexts / UsageHistoryPoints (finding 7).
        _updateService.UpdateAvailable -= OnUpdateAvailable;
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

        var template = Localizer.Get().GetLocalizedString("InactiveSessionTooltip");
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
        await PersistSessionNamesAsync();
    }

    /// <summary>Persists "no custom name" (Reset button in rename dialog).</summary>
    public async Task ClearCustomNameAsync(string sessionId)
    {
        if (string.IsNullOrEmpty(sessionId)) return;
        _sessionNameStore.ClearCustomName(sessionId);
        await PersistSessionNamesAsync();
    }

    /// <summary>
    /// Finding 25: the store's bool is its only error channel. On failure it has already rolled the
    /// in-memory map back and re-raised NameChanged, so the displayed name self-corrects and the
    /// technical detail is already in app.log — the one thing missing was telling the user, which is
    /// why nothing is re-read or re-set here.
    /// </summary>
    private async Task PersistSessionNamesAsync()
    {
        ClearActionError();
        if (await _sessionNameStore.SaveAsync()) return;

        ReportActionError(SessionNameSaveFailedUid, SessionNameSaveFailedFallback, SessionNameLogSource);
    }

    /// <summary>
    /// Raises the generic action-failure banner. The technical detail belongs in app.log, which the
    /// failing operation has already written — this only resolves the sentence the user reads.
    /// </summary>
    private void ReportActionError(string uid, string fallback, string logSource)
    {
        ActionErrorMessage = LocalizedText.Resolve(uid, fallback, logSource);
        HasActionError = true;
    }

    private void ClearActionError()
    {
        HasActionError = false;
        ActionErrorMessage = string.Empty;
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

        // Rebuild internal sessions collection
        Sessions.Clear();
        foreach (var session in latestSessions)
        {
            Sessions.Add(session);
        }

        // SESS-04: capture current selection BEFORE rebuilding the collection
        var previousSessionId = SelectedSession?.Session.Id;

        // Guard: suppress OnSelectedSessionChanged while the collection and the selection are both
        // in flux — the ComboBox writes null back through its TwoWay binding on every ItemsSource
        // swap, and that null must not be mistaken for the user clearing the selection.
        _isRefreshingSessionList = true;
        SortedSessions = BuildSessionDisplayItems(latestSessions, settings);
        var retainedItem = previousSessionId == null
            ? null
            : SortedSessions.FirstOrDefault(d => d.Session.Id == previousSessionId);
        SelectedSession = retainedItem;
        _isRefreshingSessionList = false;

        if (retainedItem != null)
        {
            UpdateSessionData(retainedItem.Session);
            return;
        }

        if (previousSessionId != null)
        {
            // Finding 6: the selected session vanished (visibility window narrowed in Settings, or
            // its project directory was deleted). This branch used to just drop the guard and return,
            // so KONTEXTFENSTER kept rendering the gone session's percentage, model badge and
            // autocompact warning — and STATISTIKEN its token counts — beside an empty ComboBox.
            ClearSessionData();
        }

        SelectSessionFromSettingsOrActivity(settings);
    }

    /// <summary>
    /// Projects the raw session list onto the ComboBox's display items: newest first, custom names
    /// applied, and cut off at the configured visibility window.
    /// </summary>
    private ObservableCollection<SessionDisplayItem> BuildSessionDisplayItems(
        IReadOnlyList<SessionInfo> sessions, AppSettings settings)
    {
        // D-06: no .Where(s => s.IsActive(threshold)) filter — inactive sessions are visible in the
        //       ComboBox to support POLISH-04 (two-line tooltip), with per-item IsActive replacing
        //       the previous hardcoded `IsActive = true` (correct only because of that filter).
        var thresholdMinutes = settings.SessionActivityThresholdMinutes;
        var threshold = TimeSpan.FromMinutes(thresholdMinutes);

        // DROPDOWN-01 / DROPDOWN-04 / D-03: display-layer visibility cutoff.
        // JsonlService keeps aggregating ALL sessions (cost / quota totals must NOT lose data) —
        // we only filter the user-visible ComboBox source here.
        var visibilityCutoff = settings.SessionVisibilityWindowDays > AppSettings.UnlimitedSessionVisibilityWindowDays
            ? DateTimeOffset.UtcNow.AddDays(-settings.SessionVisibilityWindowDays)
            : DateTimeOffset.MinValue;

        var displayItems = sessions
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
            });

        return new ObservableCollection<SessionDisplayItem>(displayItems);
    }

    /// <summary>
    /// Picks a session when none is selected: the persisted one while it is still visible, otherwise
    /// the most recently active one. Leaves the selection empty when neither exists.
    /// </summary>
    private void SelectSessionFromSettingsOrActivity(AppSettings settings)
    {
        if (!string.IsNullOrEmpty(settings.LastSelectedSessionId))
        {
            var restoredItem = SortedSessions.FirstOrDefault(d => d.Session.Id == settings.LastSelectedSessionId);
            if (restoredItem != null)
            {
                SelectedSession = restoredItem;
                return;
            }
        }

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
        // Invalidates any context-window read still in flight — otherwise its apply would repaint the
        // panels this method just cleared.
        _contextWindowRequest++;

        ContextUtilization = 0;
        ContextPercentage = 0;
        ContextPercentageText = "--";
        ContextModelBadge = string.Empty;
        ContextModelBadgeColor = _brushFactory(ModelContextLimits.GetBadgeColorHex(null));
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
            // PRICING-02 / CD-04: manual refresh + auto-poll BOTH re-evaluate the pricing banner.
            // This site runs inside the existing dispatcher chain (no extra TryEnqueue needed — G-1).
            await _pricingService.EnsurePricesLoadedAsync();
            ApplyPricingSource();
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
            // Not a pricing failure: EnsurePricesLoadedAsync never throws, so anything landing here
            // came from the JSONL aggregation. IsPricingError stays owned by ApplyPricingSource.
            AppLog.Write(StatisticsLogSource, ex, $"aggregating statistics for {period} failed");
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
            .Select(ModelContextLimits.GetDisplayName)
            // Distinct: several raw ids collapse onto one display name (claude-opus-5-2026xxxx
            // and claude-opus-5 both render "Opus 5"), which showed up as "Opus 5, Opus 5".
            // Ordered because JsonlService returns a HashSet.ToList() — otherwise the row
            // reshuffles between polls for no reason.
            .Distinct(StringComparer.Ordinal)
            .OrderBy(m => m, StringComparer.CurrentCulture)
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

    /// <summary>
    /// Refreshes the KONTEXTFENSTER panels for a session. The read itself runs off the UI thread:
    /// GetContextWindow takes no lock since finding 28, but it is still a tail read of up to 1 MB plus
    /// a subagent directory glob plus JSON deserialization — tens of milliseconds on a large corpus,
    /// on every DataUpdated batch. Same shape as <see cref="AggregateStatisticsAsync"/>: read in
    /// Task.Run, apply through the dispatcher.
    /// </summary>
    private void UpdateSessionData(SessionInfo session)
    {
        PendingContextWindowRead = ReadAndApplyContextWindowAsync(session.Id, ++_contextWindowRequest);
    }

    /// <summary>
    /// Testability seam — the in-flight context-window read, so a test can await the apply instead of
    /// waiting on the clock. <see cref="Task.CompletedTask"/> until the first session is selected.
    /// </summary>
    internal Task PendingContextWindowRead { get; private set; } = Task.CompletedTask;

    private async Task ReadAndApplyContextWindowAsync(string sessionId, int request)
    {
        ContextWindowData context;
        try
        {
            context = await Task.Run(() => _jsonlService.GetContextWindow(sessionId));
        }
        catch (Exception ex)
        {
            // GetContextWindow degrades to Empty for the file races it expects, so anything arriving
            // here is unexpected — and this task is not awaited by the UI, so without the catch the
            // failure would surface as an unobserved task exception with no context.
            AppLog.Write(ContextWindowLogSource, ex, "reading the session context window failed");
            context = ContextWindowData.Empty;
        }

        _dispatcherQueue.TryEnqueue(() => ApplyContextWindow(request, context));
    }

    /// <summary>
    /// Paints a context-window snapshot unless a newer request has superseded it: two DataUpdated
    /// batches can be in flight at once and complete out of order, and panels cleared by
    /// <see cref="ClearSessionData"/> must not be repainted by a read that started before the session
    /// disappeared (finding 6). The counter is only ever incremented on the UI thread.
    /// </summary>
    private void ApplyContextWindow(int request, ContextWindowData context)
    {
        if (request != _contextWindowRequest) return;

        ContextUtilization = context.Utilization;
        ContextPercentage = Math.Min(context.Utilization * 100, 100);
        ContextPercentageText = $"{Math.Min(context.Utilization * 100, 100):0}%";
        ContextModelBadge = ModelContextLimits.GetDisplayName(context.ModelName);
        ContextModelBadgeColor = _brushFactory(ModelContextLimits.GetBadgeColorHex(context.ModelName));
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
                BadgeColor = _brushFactory(ModelContextLimits.GetBadgeColorHex(subagent.ModelName))
            });
        }

        RecomputeStatisticsForCurrentTab();
    }

    /// <summary>
    /// Recomputes the statistics panel for whichever tab is active, without the loading spinner.
    /// </summary>
    private void RecomputeStatisticsForCurrentTab()
    {
        if (SelectedTabIndex == (int)TimePeriod.Session)
        {
            UpdateStatisticsFromSession();
            return;
        }

        _statisticsCts?.Cancel();
        var cts = new CancellationTokenSource();
        _statisticsCts = cts;
        _ = AggregateStatisticsAsync((TimePeriod)SelectedTabIndex, cts.Token, showLoading: false);
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

    // Finding 18: the Logout command that used to live here was bound in no XAML file. The only
    // reachable logout is SettingsViewModel.Logout (Views/SettingsView.xaml), and keeping a
    // more-complete-looking duplicate here meant a maintainer could fix a logout bug, watch
    // MainViewModelAuthFlowTests go green, and ship a change no user could ever reach. The API bridge
    // dependency went with it — the deleted command was this ViewModel's only use of it.

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
        if (appWindow == null)
        {
            AppLog.Write(ExportLogSource, "no AppWindow to parent the save picker to");
            ReportActionError(ChartExportFailedUid, ChartExportFailedFallback, ExportLogSource);
            return;
        }

        ClearActionError();
        var exported = await ExportHelper.ExportChartAsPngAsync(
            appWindow, UsageHistoryPoints, FiveHourWindowStart, FiveHourPercentageText, FiveHourCountdown, FiveHourUtilization);

        if (!exported)
        {
            ReportActionError(ChartExportFailedUid, ChartExportFailedFallback, ExportLogSource);
        }
    }

    [RelayCommand]
    private async Task CopyChartToClipboard()
    {
        // ExportHelper.CopyChartToClipboardAsync requires the WinRT DispatcherQueue type for
        // Clipboard.SetContent marshaling. Obtain it here on the UI thread (command executes on UI thread).
        var winuiDispatcherQueue = DispatcherQueue.GetForCurrentThread();

        ClearActionError();
        var copied = await ExportHelper.CopyChartToClipboardAsync(
            winuiDispatcherQueue, UsageHistoryPoints, FiveHourWindowStart, FiveHourPercentageText, FiveHourCountdown, FiveHourUtilization);

        if (!copied)
        {
            ReportActionError(ChartExportFailedUid, ChartExportFailedFallback, ExportLogSource);
        }
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
        // Finding 37: only the "signed out" broadcast can reach a live MainViewModel. LoginViewModel
        // navigates to MainView right after signalling success, so the post-login dashboard is a
        // brand-new transient ViewModel — default flags, and InitializeAsync polls immediately. The
        // guard stays so a future `true` sender cannot be mistaken for a 401 and bounce the user to
        // the login page.
        if (message.Value) return;

        // D-01: first 401 in a session → auto-navigate to LoginView, do NOT open InfoBar.
        // NOTE: ClaudeApiService has two send sites for AuthStateChangedMessage(false). The
        // stacked-401 edge case is accepted — a stale IsSessionExpired dies with this ViewModel when
        // the login navigation unloads MainView.
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

    // NEXTWIN-02 (D-NW-02): hide the next-window label when auth banner appears.
    partial void OnIsSessionExpiredChanged(bool value) => RecomputeNextWindowLabel();
}

