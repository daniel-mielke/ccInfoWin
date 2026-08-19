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
/// Display model for one row in the KONTEXTFENSTER section: either a single subagent spawned by
/// the Agent tool, or the whole set of agents belonging to one workflow run collapsed into one row.
/// </summary>
public class SubagentDisplayData
{
    public required string AgentId { get; init; }
    public double Percentage { get; init; }
    public required string PercentageText { get; init; }
    public required string ModelBadge { get; init; }
    /// <summary>Null on workflow rows, where the template collapses the badge — the agents of one
    /// run can be on different models, so there is no single badge to show.</summary>
    public required SolidColorBrush? BadgeColor { get; init; }

    /// <summary>Leading glyph: arrow for a single subagent, gear for a workflow run.</summary>
    public required string Icon { get; init; }

    /// <summary>Run id plus agent count for workflow rows; empty for single subagents.</summary>
    public string Label { get; init; } = string.Empty;

    /// <summary>
    /// Swaps the model badge for the label in the template. Workflow rows carry no badge because
    /// the agents of one run can be on different models (D-3).
    /// </summary>
    public bool IsWorkflow { get; init; }

    /// <summary>
    /// Hover content for workflow rows: everything the one-line, trimmed row has no space for. Null
    /// on plain rows, where the label TextBlock carrying it is collapsed anyway.
    /// </summary>
    public WorkflowTooltipData? Tooltip { get; init; }

    /// <summary>
    /// Newest agent write behind this row, as the service already measured it from the file mtime
    /// (<c>SubagentContextData.LastActivity</c>). Carried onto the row so the countdown tick can
    /// retire a run that stopped writing without going back to disk to rediscover it — see
    /// <see cref="MainViewModel.RetireStaleRows"/>. Windows-only.
    /// </summary>
    public DateTimeOffset LastActivity { get; init; }
}

/// <summary>
/// One rendered row of the tooltip's phase table: the phase name and its optional one-line summary.
/// The phases carry no visible numbering — the table is read as a sequence, and a number column
/// spent width on information the row order already carries.
/// </summary>
public record WorkflowPhaseRow(string Title, string? Detail);

/// <summary>
/// One labelled line of the tooltip, split so the two halves can be coloured independently: the
/// label is the quiet part, the value the one being read.
///
/// <see cref="Label"/> keeps its trailing space ("Name: "), because the template renders the two as
/// adjacent Runs inside a single wrapping TextBlock — concatenation, not a layout with a gap.
/// <see cref="Value"/> is empty for a line that is nothing but a label, like the leading type line.
/// </summary>
public record WorkflowTooltipLine(string Label, string Value);

/// <summary>
/// The workflow tooltip in the two pieces the template lays out: one flat run of labelled lines,
/// then the phase TABLE.
///
/// One list and not two, so no line can end up with more air around it than its neighbours — the
/// earlier header/footer split put a gap in the middle of what reads as a single block of facts.
///
/// The phases stay separate because their details wrap. A <c>TextBlock</c> with
/// <c>TextWrapping="Wrap"</c> has no hanging indent — the second line of a wrapped, indented list
/// item jumps back to the left margin and the list structure collapses. A Grid per phase row keeps
/// the wrapped remainder under its own column.
/// </summary>
public record WorkflowTooltipData(
    IReadOnlyList<WorkflowTooltipLine> Lines,
    string PhasesCaption,
    IReadOnlyList<WorkflowPhaseRow> Phases)
{
    /// <summary>Drives one Visibility binding over the caption and the table together.</summary>
    public bool HasPhases => Phases.Count > 0;
}

/// <summary>
/// The run-level facts one workflow row is built from, aggregated out of its agents. A record
/// instead of yet more delegate type arguments: label and tooltip are formatted from the same eight
/// values, so they take the same parameter and stay in step.
/// </summary>
internal record WorkflowRowFacts(
    string RunId,
    int AgentsDone,
    int AgentsStarted,
    long TotalTokens,
    DateTimeOffset StartedUtc,
    string? Name,
    string? Description,
    IReadOnlyList<WorkflowPhase> Phases);

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
    // Escapes, not literals: U+2699 resolves through DWrite fallback to Segoe UI Emoji — a COLOUR
    // font, which silently ignores Foreground and made the gear the brightest element in its row at
    // ~7.8:1 where the declared #636366 gives 2.84:1 (D-11). The template pins FontFamily to
    // "Segoe UI Symbol" so both glyphs stay monochrome. Keeping the source pure ASCII also puts
    // them out of reach of the PowerShell bulk edit that has already corrupted non-ASCII here once.
    private const string PlainSubagentIcon = "\u21B3";     // downwards arrow with tip rightwards
    private const string WorkflowSubagentIcon = "\u2699";  // gear
    private const string WorkflowSubagentLabelKey = "WorkflowSubagentLabel";
    private const string WorkflowSubagentLabelTokensOnlyKey = "WorkflowSubagentLabelTokensOnly";
    private const string WorkflowTooltipKindKey = "WorkflowTooltipKind";
    private const string WorkflowTooltipNameKey = "WorkflowTooltipName";
    private const string WorkflowTooltipDescriptionKey = "WorkflowTooltipDescription";
    private const string WorkflowTooltipIdKey = "WorkflowTooltipId";
    private const string WorkflowTooltipAgentsKey = "WorkflowTooltipAgents";
    private const string WorkflowTooltipStartKey = "WorkflowTooltipStart";
    private const string WorkflowTooltipContextKey = "WorkflowTooltipContext";
    private const string WorkflowTooltipPhasesKey = "WorkflowTooltipPhases";
    private const string WorkflowTooltipLogSource = "MainViewModel.WorkflowTooltip";

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

    /// <summary>
    /// No-data text of the utilization panels (5-hour, weekly, Sonnet, context).
    ///
    /// The statistics panel deliberately writes <see cref="NoStatisticsDataText"/> instead. Those are
    /// two display conventions, not one rule spelled twice — so each is single-sourced on its own and
    /// the two are NOT unified.
    /// </summary>
    private const string NoDataText = "--";

    /// <summary>
    /// No-data text of the statistics panel: an en dash. Spelled as an escape, like every other
    /// non-ASCII string literal in this file — a PowerShell bulk edit has already corrupted one once.
    /// </summary>
    private const string NoStatisticsDataText = "\u2013";

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
    private string _fiveHourPercentageText = NoDataText;

    [ObservableProperty]
    private string _fiveHourCountdown = NoDataText;

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
    private string _weeklyPercentageText = NoDataText;

    [ObservableProperty]
    private string _weeklyCountdown = NoDataText;

    [ObservableProperty]
    private string _weeklyResetDate = NoDataText;

    private DateTimeOffset? _weeklyResetsAt;

    // --- Sonnet weekly quota ---

    [ObservableProperty]
    private double _sonnetUtilization;

    [ObservableProperty]
    private double _sonnetPercentage;

    [ObservableProperty]
    private string _sonnetPercentageText = NoDataText;

    [ObservableProperty]
    private string _sonnetCountdown = NoDataText;

    [ObservableProperty]
    private string _sonnetResetDate = NoDataText;

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

    /// <summary>
    /// How long a subagent row outlives the newest write behind it before the countdown tick drops
    /// it (G-2). Windows-only.
    ///
    /// Deliberately NOT the service's 30 s activity window. That value is calibrated for
    /// WRITE-triggered sampling, where a repaint happens only because some agent just wrote, so
    /// "nothing fresh" reliably means "over". Sampled on a clock instead, the same 30 s deletes the
    /// row of a run that is still going: measured against the real 43-agent run, 26 % of its
    /// runtime had no agent fresh within 30 s, and one agent went 474 s without a write inside a
    /// single model call (.planning/reviews/2026-08-09_v16-v17-review.md). Ten minutes clears that
    /// measured gap with margin while still bounding a finished run's row to minutes, not hours.
    /// </summary>
    internal static readonly TimeSpan SubagentRetirementWindow = TimeSpan.FromMinutes(10);

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
    private string _contextPercentageText = NoDataText;

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
    private string _statisticsModels = NoStatisticsDataText;

    [ObservableProperty]
    private string _statisticsInput = NoStatisticsDataText;

    [ObservableProperty]
    private string _statisticsOutput = NoStatisticsDataText;

    [ObservableProperty]
    private string _statisticsCacheCreation = NoStatisticsDataText;

    [ObservableProperty]
    private string _statisticsCacheRead = NoStatisticsDataText;

    [ObservableProperty]
    private string _statisticsTotal = NoStatisticsDataText;

    [ObservableProperty]
    private string _statisticsCost = NoStatisticsDataText;

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
    /// Start of the current rate-limit window, computed as ResetsAt minus the window length.
    /// Returns null until the first API response is received.
    /// </summary>
    public DateTimeOffset? FiveHourWindowStart => _fiveHourResetsAt?.Subtract(RateLimitWindow.Duration);

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
        _countdownTimer.Tick += (s, e) => OnCountdownTick();
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
    /// The percentage rule every panel and every subagent row renders: a 0..1 utilization becomes a
    /// 0..100 percentage clamped at the length of the bar's track, plus the text for it.
    ///
    /// Both halves in one call because they have to agree — the clamp used to be spelled at four
    /// sites, so dropping it (or moving to one decimal, or to a culture-aware percent format) at one
    /// of them left one bar rendering past its rail while the others saturated, or two panels showing
    /// "97.4 %" and "97 %" for the same number.
    /// </summary>
    private static (double Value, string Text) ToPercentage(double utilization)
    {
        var value = Math.Min(utilization * 100, 100);
        return (value, $"{value:0}%");
    }

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
    private Task PollUsageAsync() => RunGuardedRefreshAsync(PollUsageCoreAsync);

    /// <summary>
    /// The reentrancy guard both refresh routes share: at most one poll in flight, with IsRefreshing
    /// driving the spinner and RefreshCommand.CanExecute (D-04). The routes differ only in what they
    /// await — the 250 ms anti-flicker floor belongs to the manual click alone (D-02/D-03) — so
    /// anything added to the guard now reaches the auto-poll and the button alike.
    /// </summary>
    private async Task RunGuardedRefreshAsync(Func<Task> work)
    {
        if (IsRefreshing) return;
        IsRefreshing = true;
        try { await work(); }
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
        // 5-STUNDEN-FENSTER = FiveHour. Numbers, text and countdown come from the same reader the two
        // weekly panels use, so the no-data placeholder and the countdown call cannot be hardened on
        // the weekly panels while silently skipping the most prominent panel on the dashboard.
        var fiveHour = ReadPanelValues(data.FiveHour);
        FiveHourUtilization = fiveHour.Utilization;
        FiveHourPercentage = fiveHour.Percentage;
        FiveHourPercentageText = fiveHour.PercentageText;
        FiveHourCountdown = fiveHour.Countdown;

        // The tail is 5-hour-only: history plus burn rate, or clearing both. Not routed through
        // ApplyWeeklyWindow, whose extra reset-date and resets-at delegates this panel has no use for
        // — _fiveHourResetsAt is written by AppendHistoryPointAsync, deliberately before the chart is
        // invalidated.
        if (data.FiveHour != null)
        {
            await AppendHistoryPointAsync(data.FiveHour.ResetsAt, fiveHour.Utilization);

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

    /// <summary>
    /// The four values every utilization panel derives from one API window, and the one place both the
    /// no-data placeholder and the countdown call live for all three of them — the 5-hour panel
    /// included, which used to spell this out by hand.
    /// </summary>
    private static (double Utilization, double Percentage, string PercentageText, string Countdown)
        ReadPanelValues(UsageWindow? window)
    {
        if (window is null) return (0, 0, NoDataText, NoDataText);

        var util = window.NormalizedUtilization;
        var percentage = ToPercentage(util);
        return (util, percentage.Value, percentage.Text, CountdownFormatter.FormatCountdown(window.ResetsAt));
    }

    private static void ApplyWeeklyWindow(
        UsageWindow? window,
        Action<double> setUtilization, Action<double> setPercentage, Action<string> setPercentageText,
        Action<string> setCountdown, Action<string> setResetDate, Action<DateTimeOffset?> setResetsAt)
    {
        var panel = ReadPanelValues(window);
        setUtilization(panel.Utilization);
        setPercentage(panel.Percentage);
        setPercentageText(panel.PercentageText);
        setCountdown(panel.Countdown);

        // The reset date and the tracked reset time are the weekly panels' own: the 5-hour panel has
        // no date label, and its reset time is written by AppendHistoryPointAsync.
        setResetDate(window is null ? NoDataText : CountdownFormatter.FormatResetDate(window.ResetsAt));
        setResetsAt(window?.ResetsAt);
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
        var windowDuration = RateLimitWindow.Duration;

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

    /// <summary>
    /// Decides whether the rate-limit window rotated, and therefore whether the persisted chart
    /// history belongs to a window that is gone.
    ///
    /// The clock-skew allowance is <see cref="UsageNotificationService.RotationClockSkewTolerance"/>
    /// rather than a second copy of the same two minutes: the notification service answers the same
    /// question for the reset toast and the re-armed 80/95 % toasts, and at a boundary where the two
    /// disagreed the chart would splice a new window onto the old curve while the toast announced a
    /// reset, or the history would be cleared with no toast at all.
    /// </summary>
    internal static bool IsWindowReset(DateTimeOffset? storedResetsAt, DateTimeOffset? apiResetsAt)
    {
        if (!storedResetsAt.HasValue || !apiResetsAt.HasValue) return false;

        var difference = (apiResetsAt.Value - storedResetsAt.Value).Duration();
        return difference > UsageNotificationService.RotationClockSkewTolerance;
    }

    private void UpdateCountdowns()
    {
        FiveHourCountdown = CountdownFormatter.FormatCountdown(_fiveHourResetsAt);
        RecomputeNextWindowLabel();
        WeeklyCountdown = CountdownFormatter.FormatCountdown(_weeklyResetsAt);
        SonnetCountdown = CountdownFormatter.FormatCountdown(_sonnetResetsAt);
    }

    /// <summary>
    /// Everything the one-minute timer does. A named method rather than a multi-statement Tick
    /// lambda because <see cref="StartTimers"/> is unreachable from tests —
    /// DispatcherQueue.GetForCurrentThread() returns null in a headless host — so anything left
    /// inside the lambda could never be exercised.
    /// </summary>
    internal void OnCountdownTick()
    {
        UpdateCountdowns();
        RetireStaleRows(SubagentContexts, DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// G-2 (Windows-only): drops subagent rows whose run has stopped writing.
    ///
    /// SubagentContexts is only ever rebuilt by <see cref="ApplyContextWindow"/>, which is reachable
    /// only from a DataUpdated batch — and the last write of a workflow run comes from the run's own
    /// agents. So nothing repaints after a run ends, the service's staleness gate never gets a pass
    /// at which the run is already stale, and a finished run keeps a row in a section that claims to
    /// show live agents for hours.
    ///
    /// Expired against <see cref="SubagentDisplayData.LastActivity"/>, which the service already
    /// measured and shipped on every row: re-reading the session to rediscover it would mean a tail
    /// read plus a recursive glob per tick, and would drag the whole KONTEXTFENSTER repaint —
    /// statistics, pricing, badge brushes — onto the clock with it.
    ///
    /// RemoveAt rather than Clear+refill so the repeater raises Remove instead of Reset and the
    /// surviving rows keep their realized containers.
    /// </summary>
    internal static void RetireStaleRows(IList<SubagentDisplayData> rows, DateTimeOffset nowUtc)
    {
        var cutoff = nowUtc - SubagentRetirementWindow;

        for (var i = rows.Count - 1; i >= 0; i--)
        {
            if (rows[i].LastActivity < cutoff)
                rows.RemoveAt(i);
        }
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

        PaintContextWindow(ContextWindowData.Empty, hasSession: false);
        // Do NOT reset SelectedTabIndex — user's tab choice must survive session refreshes
        ApplyStatistics(StatisticsSummary.Empty);
    }

    partial void OnSelectedTabIndexChanged(int value)
        => DispatchStatistics((TimePeriod)value, showLoading: true);

    /// <summary>
    /// Dispatches the statistics panel for one period: the Session tab reads synchronously, every
    /// other tab aggregates off the UI thread under a fresh cancellation token. The loading spinner is
    /// the only difference between the two callers — a tab switch shows it, a repaint triggered by a
    /// context-window read does not.
    ///
    /// The cancel sits above the Session branch, so switching to Session also drops an aggregate still
    /// in flight instead of letting it repaint over the session numbers.
    /// </summary>
    private void DispatchStatistics(TimePeriod period, bool showLoading)
    {
        _statisticsCts?.Cancel();

        if (period == TimePeriod.Session)
        {
            UpdateStatisticsFromSession();
            return;
        }

        var cts = new CancellationTokenSource();
        _statisticsCts = cts;
        _ = AggregateStatisticsAsync(period, cts.Token, showLoading);
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
            // The synthetic markers come from JsonlService, which owns that vocabulary — a new marker
            // taught to the service must not leak a raw id into this row. "unknown" stays local: it is
            // a display placeholder, not a marker Claude Code writes.
            .Where(m => !JsonlService.IsSyntheticModel(m)
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
            : NoStatisticsDataText;

        // One no-data rule for the five token rows: five copies of it made a wrong-member slip
        // invisible: reading CacheCreationTokens under the CacheReadTokens guard compiles and reads
        // plausibly.
        StatisticsInput = FormatTokenRow(stats.InputTokens);
        StatisticsOutput = FormatTokenRow(stats.OutputTokens);
        StatisticsCacheCreation = FormatTokenRow(stats.CacheCreationTokens);
        StatisticsCacheRead = FormatTokenRow(stats.CacheReadTokens);
        StatisticsTotal = FormatTokenRow(stats.TotalTokens);
        StatisticsCost = CostFormatter.FormatCost(stats.TotalCostUsd, stats.HasEstimatedCosts);

        static string FormatTokenRow(long value)
            => value > 0 ? TokenFormatter.FormatTokenCount(value) : NoStatisticsDataText;
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

        PaintContextWindow(context, hasSession: true);
        RecomputeStatisticsForCurrentTab();
    }

    /// <summary>
    /// The one enumeration of the KONTEXTFENSTER properties, used by the apply path and the clear
    /// path alike. The two lists were coupled by convention only, so a property added to the apply
    /// path and forgotten in the clear path left the gone session's value on screen next to an empty
    /// ComboBox — which is exactly the regression recorded on <see cref="ClearSessionData"/>.
    ///
    /// <paramref name="hasSession"/> false is the cleared state, and it is NOT the same as painting a
    /// zeroed window: the panel then shows the no-data placeholder rather than a computed "0%", and an
    /// empty badge rather than GetDisplayName's "unknown" label for a model nobody reported.
    /// </summary>
    private void PaintContextWindow(ContextWindowData context, bool hasSession)
    {
        var percentage = ToPercentage(context.Utilization);

        ContextUtilization = context.Utilization;
        ContextPercentage = percentage.Value;
        ContextPercentageText = hasSession ? percentage.Text : NoDataText;
        ContextModelBadge = hasSession ? ModelContextLimits.GetDisplayName(context.ModelName) : string.Empty;
        ContextModelBadgeColor = _brushFactory(ModelContextLimits.GetBadgeColorHex(context.ModelName));
        ShowAutocompactWarning = context.ShouldWarnAutocompact;
        HasActiveSession = hasSession;

        SubagentContexts.Clear();
        foreach (var row in BuildSubagentRows(context.Subagents, _brushFactory, FormatWorkflowRow))
            SubagentContexts.Add(row);
    }

    /// <summary>
    /// Maps subagents to display rows. Agents of one workflow run collapse into a single row from
    /// the first agent on (D-1) — a single run reached 44 agents on this machine, which would be
    /// roughly 1230 px of bars in a ~600 px window.
    ///
    /// A workflow row carries NO bar and NO percentage. Utilization is a ratio against a PER-AGENT
    /// ceiling (context / MaxTokens); over a group that ceiling does not exist — 43 agents have 43
    /// windows — so maximum, mean and sum alike yield a number with no reference quantity. The row
    /// reports extensive quantities instead, which do add up over a group: agents finished out of
    /// agents started, and the summed context of the run. Same reason the CLI shows those two.
    ///
    /// Static so it can be exercised without a ViewModel instance; the two delegates keep the WinRT
    /// brush type and the localizer out of the test path.
    ///
    /// Windows-only: the whole workflow branch of this method — grouping, the gear row, the label —
    /// has no macOS counterpart. The plain-subagent branch does. Full note on
    /// SubagentContextData.WorkflowId; decisions D-1…D-27 in
    /// `.planning/milestones/v1.7-ROADMAP.md`.
    /// </summary>
    internal static List<SubagentDisplayData> BuildSubagentRows(
        IReadOnlyList<SubagentContextData> subagents,
        Func<string, SolidColorBrush> brushFactory,
        Func<WorkflowRowFacts, (string Label, WorkflowTooltipData Tooltip)> workflowFormatter)
    {
        // Plain subagents keep their incoming AgentId order, workflow rows follow sorted by run id.
        // Deterministic ordering keeps rows from swapping places between two polls.
        var rows = subagents
            .Where(s => s.WorkflowId is null)
            .Select(s => CreatePlainRow(
                s.AgentId,
                s.Utilization,
                ModelContextLimits.GetDisplayName(s.ModelName),
                brushFactory(ModelContextLimits.GetBadgeColorHex(s.ModelName)),
                s.LastActivity))
            .ToList();

        // Max, not First: every agent of a run carries the same run-level counts, and Max keeps a
        // hand-built list whose members disagree from reporting the lowest of them. Same reasoning
        // for the start time (Min = the earliest claimed start) and for the two text fields, where
        // the first non-null wins over a member that happened to read the file before it existed.
        var workflowRows = subagents
            .Where(s => s.WorkflowId is not null)
            .GroupBy(s => s.WorkflowId!)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => CreateWorkflowRow(
                g.Key,
                workflowFormatter(new WorkflowRowFacts(
                    g.Key,
                    g.Max(s => s.RunAgentsDone),
                    g.Max(s => s.RunAgentsStarted),
                    g.Sum(s => s.TotalTokens),
                    g.Min(s => s.RunStartedUtc),
                    g.Select(s => s.RunName).FirstOrDefault(n => n is not null),
                    g.Select(s => s.RunDescription).FirstOrDefault(d => d is not null),
                    g.Select(s => s.RunPhases).FirstOrDefault(p => p.Count > 0) ?? [])),
                // Max, like the service's own per-run gate (JsonlService.BuildSubagentContext):
                // one agent still writing keeps the whole run on screen, however many of its
                // siblings have already finished and gone quiet.
                g.Max(s => s.LastActivity)));

        rows.AddRange(workflowRows);
        return rows;
    }

    private static SubagentDisplayData CreatePlainRow(
        string agentId,
        double utilization,
        string modelBadge,
        SolidColorBrush badgeColor,
        DateTimeOffset lastActivity)
    {
        var percentage = ToPercentage(utilization);
        return new SubagentDisplayData
        {
            AgentId = agentId,
            Percentage = percentage.Value,
            PercentageText = percentage.Text,
            ModelBadge = modelBadge,
            BadgeColor = badgeColor,
            Icon = PlainSubagentIcon,
            Label = string.Empty,
            IsWorkflow = false,
            Tooltip = null,
            LastActivity = lastActivity
        };
    }

    /// <summary>
    /// A workflow row is label-only: the template collapses the bar, the percentage and the model
    /// badge for it, so the zeroed numeric members are never read. The badge is absent because the
    /// agents of one run can be on different models.
    /// </summary>
    private static SubagentDisplayData CreateWorkflowRow(
        string workflowId,
        (string Label, WorkflowTooltipData Tooltip) text,
        DateTimeOffset lastActivity) =>
        new()
        {
            AgentId = workflowId,
            Percentage = 0,
            PercentageText = string.Empty,
            ModelBadge = string.Empty,
            BadgeColor = null,
            Icon = WorkflowSubagentIcon,
            Label = text.Label,
            IsWorkflow = true,
            Tooltip = text.Tooltip,
            LastActivity = lastActivity
        };

    /// <summary>
    /// Composes the workflow row label in the ViewModel rather than via l:Uids.Uid in the
    /// DataTemplate: WinUI3Localizer does not apply attached-property uids to template instances
    /// created at runtime, which would leave the text blank. The run id is inserted verbatim so it
    /// stays comparable to /workflows output and to the directory name on disk (D-6).
    ///
    /// A run with no journal.jsonl (older runs, other harness versions) reports zero started agents;
    /// the label then drops the count rather than showing a fabricated "0/0".
    ///
    /// Takes the localizer as a delegate for the same reason <see cref="FormatWorkflowTooltip"/>
    /// does: <c>Localizer.Get()</c> needs a WinUI3Localizer host, so a direct call would leave the
    /// only production composition of the row text reachable through the UI alone.
    /// </summary>
    internal static string FormatWorkflowLabel(WorkflowRowFacts facts, Func<string, string> localize)
    {
        var tokens = TokenFormatter.FormatTokenCount(facts.TotalTokens);

        return facts.AgentsStarted <= 0
            ? string.Format(
                CultureInfo.CurrentCulture,
                localize(WorkflowSubagentLabelTokensOnlyKey),
                facts.RunId,
                tokens)
            : string.Format(
                CultureInfo.CurrentCulture,
                localize(WorkflowSubagentLabelKey),
                facts.RunId,
                facts.AgentsDone,
                facts.AgentsStarted,
                tokens);
    }

    /// <summary>
    /// The row's two texts from one set of facts, so they can never disagree about the same run.
    /// This is the seam the localizer lives behind: <see cref="BuildSubagentRows"/> takes it as a
    /// delegate and stays testable without a WinUI3Localizer host.
    /// </summary>
    private static (string Label, WorkflowTooltipData Tooltip) FormatWorkflowRow(WorkflowRowFacts facts)
    {
        static string Localize(string key) => Localizer.Get().GetLocalizedString(key);

        return (FormatWorkflowLabel(facts, Localize),
                FormatWorkflowTooltip(
                    facts,
                    Localize,
                    LocalizedText.ResolveOrNull(WorkflowTooltipStartPatternUid, WorkflowTooltipLogSource),
                    CultureInfo.CurrentUICulture));
    }

    internal const string WorkflowTooltipStartPatternUid = "WorkflowTooltipStartPattern";

    /// <summary>
    /// Hover text for a workflow row: what the one-line, trimmed row has no space for. Every value
    /// carries a label (D-17) — the row can be terse because it sits under a gear next to its
    /// neighbours, a tooltip is read in isolation and has to explain itself.
    ///
    /// Windows-only, no macOS counterpart. Full note on SubagentContextData.WorkflowId.
    ///
    /// Three lines are conditional. Name and description are missing for runs whose script cannot be
    /// read; the agent count is missing for a run with no journal. All three are dropped rather than
    /// shown as an empty placeholder.
    ///
    /// The token line is labelled "context", never "usage": it is the summed FINAL context of the
    /// run's agents, measured at 3.25 M against 112.65 M actually consumed for the same run — a
    /// factor of 34.6, so "usage" would not be imprecise but wrong by an order of magnitude.
    ///
    /// Line structure is decided here rather than in the resw, exactly as ComputeTooltipText does for
    /// the session ComboBox: it is layout, not translation. One line per list entry, with no \n
    /// anywhere — the template renders each entry as its own TextBlock so the wrapping description
    /// cannot drag its neighbours around.
    ///
    /// The phases are NOT part of that list. They are a table — see
    /// <see cref="WorkflowTooltipData"/> for why wrapping defeats an indented text list.
    /// They come from the run's script file, so unlike name and description they are available for
    /// the whole life of a live run.
    /// </summary>
    internal static WorkflowTooltipData FormatWorkflowTooltip(
        WorkflowRowFacts facts,
        Func<string, string> localize,
        string? startPattern,
        CultureInfo culture)
    {
        // Order: identity and measurements first, free text after. The run id, counts, start and
        // context are one line each and always present, so they form a stable block whose lines do
        // not move when a run has no name. Name and description are the only two that wrap to
        // several lines and the only two that can be missing entirely — putting them last keeps
        // everything above them at a fixed position from one run to the next.
        List<WorkflowTooltipLine> lines =
        [
            Line(localize(WorkflowTooltipKindKey), culture),
            Line(localize(WorkflowTooltipIdKey), culture, facts.RunId)
        ];

        if (facts.AgentsStarted > 0)
            lines.Add(Line(localize(WorkflowTooltipAgentsKey), culture, facts.AgentsDone, facts.AgentsStarted));

        lines.Add(Line(
            localize(WorkflowTooltipStartKey),
            culture,
            CountdownFormatter.FormatWithPattern(facts.StartedUtc, startPattern, WorkflowTooltipStartPatternUid, culture)));

        lines.Add(Line(
            localize(WorkflowTooltipContextKey),
            culture,
            TokenFormatter.FormatTokenCount(facts.TotalTokens)));

        if (facts.Name is not null)
            lines.Add(Line(localize(WorkflowTooltipNameKey), culture, facts.Name));

        if (facts.Description is not null)
            lines.Add(Line(localize(WorkflowTooltipDescriptionKey), culture, facts.Description));

        var phases = facts.Phases.Select(p => new WorkflowPhaseRow(p.Title, p.Detail)).ToList();

        // The count stays on the caption even though the numbers left the rows: "how many phases"
        // is not something a numberless table can be counted for at a glance.
        return new WorkflowTooltipData(
            lines,
            phases.Count > 0 ? string.Format(culture, localize(WorkflowTooltipPhasesKey), phases.Count) : string.Empty,
            phases);
    }

    /// <summary>
    /// Splits one resw template into its label half and its formatted value half, cutting at the
    /// first placeholder: "Name: {0}" becomes ("Name: ", "code-clone-review").
    ///
    /// Done here rather than by adding a second resw key per line, because the label is not a
    /// separate string — it is the part of the sentence in front of the value, and a translation
    /// that moves the value ("Agents: {0}/{1} fertig") keeps working: everything up to the first
    /// placeholder is the label, everything from it on is formatted with the original indices
    /// intact.
    ///
    /// A template with no placeholder is all label and no value, which is exactly right for the
    /// leading type line. One with a leading placeholder degrades to all value, no label — it loses
    /// the grey, not the text.
    ///
    /// ponytail: does not understand "{{" escapes. No tooltip template contains one; if one ever
    /// does, the split lands inside the escape and that line renders wrong. Use a dedicated label
    /// key for that line if it happens.
    /// </summary>
    private static WorkflowTooltipLine Line(string template, CultureInfo culture, params object[] values)
    {
        var placeholder = template.IndexOf('{', StringComparison.Ordinal);

        return placeholder < 0
            ? new WorkflowTooltipLine(template, string.Empty)
            : new WorkflowTooltipLine(
                template[..placeholder],
                string.Format(culture, template[placeholder..], values));
    }

    /// <summary>
    /// Recomputes the statistics panel for whichever tab is active, without the loading spinner.
    /// </summary>
    private void RecomputeStatisticsForCurrentTab()
        => DispatchStatistics((TimePeriod)SelectedTabIndex, showLoading: false);

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
    private Task Refresh() => RunGuardedRefreshAsync(() => Task.WhenAll(
        PollUsageCoreAsync(),
        Task.Delay(TimeSpan.FromMilliseconds(MinimumSpinnerDisplayMs))));

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
        // Pattern-matched rather than null-checked so the captured window is non-nullable inside the
        // renderer lambda below.
        if (App.MainWindow?.AppWindow is not { } appWindow)
        {
            AppLog.Write(ExportLogSource, "no AppWindow to parent the save picker to");
            ReportActionError(ChartExportFailedUid, ChartExportFailedFallback, ExportLogSource);
            return;
        }

        await RunChartExportAsync((points, windowStart, percentageText, countdown, utilization) =>
            ExportHelper.ExportChartAsPngAsync(
                appWindow, points, windowStart, percentageText, countdown, utilization));
    }

    [RelayCommand]
    private async Task CopyChartToClipboard()
    {
        // ExportHelper.CopyChartToClipboardAsync requires the WinRT DispatcherQueue type for
        // Clipboard.SetContent marshaling. Obtain it here on the UI thread (command executes on UI thread).
        var winuiDispatcherQueue = DispatcherQueue.GetForCurrentThread();

        await RunChartExportAsync((points, windowStart, percentageText, countdown, utilization) =>
            ExportHelper.CopyChartToClipboardAsync(
                winuiDispatcherQueue, points, windowStart, percentageText, countdown, utilization));
    }

    /// <summary>
    /// Hands the chart snapshot to one of the two <see cref="ExportHelper"/> renderers and reports the
    /// shared failure.
    ///
    /// The snapshot is read here and nowhere else: the argument list is positional and both renderers
    /// take the same five values in the same order, so spelling it at each command let the saved PNG
    /// and the clipboard image drift apart — a sixth value added at one site, or the two strings
    /// swapped, compiles either way.
    /// </summary>
    private async Task RunChartExportAsync(
        Func<IReadOnlyList<UsageHistoryPoint>, DateTimeOffset?, string, string, double, Task<bool>> render)
    {
        ClearActionError();

        var succeeded = await render(
            UsageHistoryPoints, FiveHourWindowStart, FiveHourPercentageText, FiveHourCountdown, FiveHourUtilization);

        if (!succeeded)
        {
            ReportActionError(ChartExportFailedUid, ChartExportFailedFallback, ExportLogSource);
        }
    }

    [RelayCommand]
    private void OpenUpdateDownload()
    {
        if (string.IsNullOrEmpty(_updateDownloadUrl)) return;
        // Review finding I4: one allow-list for the github.com egress. UpdateService already
        // enforces it for the release URL it hands over, and its parse-then-compare host check
        // is stricter than the prefix test this replaces.
        if (!UpdateService.IsReleasePageUrl(_updateDownloadUrl)) return;
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

