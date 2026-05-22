using System.Collections.ObjectModel;
using CCInfoWindows.Helpers;
using CCInfoWindows.Messages;
using CCInfoWindows.Models;
using CCInfoWindows.Services;
using CCInfoWindows.Services.Interfaces;
using CCInfoWindows.Views;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.UI.Xaml.Controls;
using WinUI3Localizer;

namespace CCInfoWindows.ViewModels;

/// <summary>
/// Settings page ViewModel with refresh interval selection, dark/light mode toggle, logout,
/// and (Phase 26) session rename management via a dedicated Sessions tab.
/// </summary>
public partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsService _settingsService;
    private readonly ICredentialService _credentialService;
    private readonly INavigationService _navigationService;
    private readonly IPricingService _pricingService;
    private readonly IUsageHistoryService _historyService;
    private readonly ISessionNameStore _sessionNameStore;       // Phase 26 / RENAME-07
    private readonly IJsonlService _jsonlService;               // Phase 26 / SessionRenameItems source
    private readonly IDispatcherQueue _dispatcherQueue;         // Phase 26 / G-1
    private readonly IClaudeApiService _apiService;             // ORGID-01 / D-OG-01

    // D-09: 1-minute UI-thread-bound timer. Owned by SettingsViewModel; lifecycle driven by SettingsView code-behind (D-10).
    private IDispatcherTimer? _aboutTimestampTimer;

    // Testability seam — overridden in unit tests to supply a fake IDispatcherTimer (avoids WinRT COM init).
    internal Func<IDispatcherTimer> TimerFactory { get; set; } = () => new WinuiDispatcherTimerAdapter();

    /// <summary>
    /// Represents a selectable refresh interval option for the ComboBox.
    /// </summary>
    public record RefreshOption(string Label, int Seconds);

    private const int DefaultRefreshSeconds = 60;

    // Tab order: 0=General, 1=Updates, 2=Account, 3=Sessions (Phase 26 / RENAME-02), 4=About
    // Used by SettingsView code-behind to start/stop the About-tab timer on navigation.
    public const int SessionsTabIndex = 3;
    public const int AboutTabIndex = 4;   // SHIFTED from 3 — Phase 26 inserts Sessions at index 3

    public List<RefreshOption> RefreshOptions { get; } =
    [
        new("30s", 30),
        new("1min", 60),
        new("2min", 120),
        new("5min", 300),
        new("10min", 600),
        new("Manuell", 0)
    ];

    [ObservableProperty]
    private int _selectedTabIndex = 0;

    public bool IsGeneralTabVisible  => _selectedTabIndex == 0;
    public bool IsUpdatesTabVisible  => _selectedTabIndex == 1;
    public bool IsAccountTabVisible  => _selectedTabIndex == 2;
    public bool IsSessionsTabVisible => _selectedTabIndex == 3;   // Phase 26 / RENAME-02
    public bool IsAboutTabVisible    => _selectedTabIndex == 4;

    partial void OnSelectedTabIndexChanged(int value)
    {
        OnPropertyChanged(nameof(IsGeneralTabVisible));
        OnPropertyChanged(nameof(IsUpdatesTabVisible));
        OnPropertyChanged(nameof(IsAccountTabVisible));
        OnPropertyChanged(nameof(IsSessionsTabVisible));   // Phase 26
        OnPropertyChanged(nameof(IsAboutTabVisible));

        // CD-03: snapshot refresh on tab activation (NOT live ObservableCollection sync).
        if (value == SessionsTabIndex)
        {
            RefreshSessionRenameItems();
        }
    }

    public string AppVersionText =>
        System.Reflection.Assembly.GetExecutingAssembly()
            .GetName().Version?.ToString(3) ?? "0.0.0";

    public bool IsTokenValid => _credentialService.HasValidToken();

    [ObservableProperty]
    private RefreshOption _selectedRefreshOption = null!;

    [ObservableProperty]
    private bool _isDarkMode;

    [ObservableProperty]
    private int _selectedThresholdIndex;

    [ObservableProperty]
    private bool _isAutostart;

    [ObservableProperty]
    private int _selectedLanguageIndex;

    [ObservableProperty]
    private int _selectedSonnetContextIndex;

    [ObservableProperty]
    private int _selectedVisibilityWindowIndex;

    private static readonly string[] LanguageCodes = ["de-DE", "en-US"];
    private static readonly int[] SonnetContextSizes = [200_000, 1_000_000];

    // DROPDOWN-04 / D-03: visibility window options. 0 == unlimited.
    private static readonly int[] VisibilityWindowDayOptions = [7, 30, 90, 0];
    private const int DefaultVisibilityWindowIndex = 1; // 30 days

    public string PricingSourceText => _pricingService.Source switch
    {
        PricingSource.Live => "Live (LiteLLM API)",
        PricingSource.Fallback => "Fallback (gebündelt)",
        _ => "Unbekannt"
    };

    public string LastPricingFetchText => _pricingService.LastFetch.HasValue
        ? _pricingService.LastFetch.Value.LocalDateTime.ToString("dd.MM.yyyy HH:mm")
        : "Nie";

    /// <summary>
    /// Localized "X minutes ago" string for the About tab. Re-evaluated on each
    /// _aboutTimestampTimer Tick (D-09, D-11). L10N-01: 5 categories backed by
    /// LastFetchRelative.* resw keys; switches DE/EN via CurrentUICulture.
    /// </summary>
    public string LastFetchRelativeTime
    {
        get
        {
            var lastFetch = _pricingService.LastFetch;
            if (!lastFetch.HasValue)
                return Localizer.Get().GetLocalizedString("LastFetchNever");

            var elapsed = DateTimeOffset.Now - lastFetch.Value;
            if (elapsed.TotalSeconds < 30)
                return Localizer.Get().GetLocalizedString("LastFetchJustNow");

            if (elapsed.TotalMinutes < 60)
            {
                var minutes = (int)Math.Max(0, elapsed.TotalMinutes);
                return string.Format(
                    Localizer.Get().GetLocalizedString("LastFetchMinutesAgo"),
                    minutes);
            }

            if (elapsed.TotalHours < 24)
            {
                var hours = (int)Math.Max(0, elapsed.TotalHours);
                return string.Format(
                    Localizer.Get().GetLocalizedString("LastFetchHoursAgo"),
                    hours);
            }

            var days = (int)Math.Max(0, elapsed.TotalDays);
            return string.Format(
                Localizer.Get().GetLocalizedString("LastFetchDaysAgo"),
                days);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Phase 26 / RENAME-02: Sessions tab — snapshot collection + commands
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Snapshot collection (CD-03) refreshed on tab activation and on ISessionNameStore.NameChanged.
    /// NOT live-synced with IJsonlService.Sessions to avoid stale-snapshot bug class (PITFALLS Cluster A).
    /// </summary>
    public ObservableCollection<SessionRenameItem> SessionRenameItems { get; } = new();

    private void RefreshSessionRenameItems()
    {
        var liveSessions = _jsonlService.Sessions;
        var liveIds = new HashSet<string>(liveSessions.Select(s => s.Id), StringComparer.Ordinal);

        SessionRenameItems.Clear();

        // Live sessions first, sorted by display name
        foreach (var s in liveSessions.OrderBy(s => s.DisplayName, StringComparer.OrdinalIgnoreCase))
        {
            SessionRenameItems.Add(new SessionRenameItem
            {
                SessionId = s.Id,
                DefaultName = s.DisplayName,
                IsOrphan = false,
                CustomName = _sessionNameStore.GetCustomName(s.Id) ?? string.Empty
            });
        }

        // Orphan custom names (D-08): sessions whose JSONL files are gone but a custom name persists.
        // Detected by enumerating store keys not present in live IJsonlService.Sessions.
        // The store does not expose enumeration; we discover orphans by reading session-names.json
        // through a best-effort helper. For Phase 26 v1.5 we keep a minimum-API approach:
        // orphans surface after a tab-activation snapshot refresh.
        // (A future v1.6+ enumeration API on ISessionNameStore is deferred per O-01.)
        foreach (var orphanId in EnumerateOrphanIds(liveIds))
        {
            var custom = _sessionNameStore.GetCustomName(orphanId);
            if (string.IsNullOrEmpty(custom)) continue;
            SessionRenameItems.Add(new SessionRenameItem
            {
                SessionId = orphanId,
                DefaultName = orphanId,   // raw projectDirName as fallback label
                IsOrphan = true,
                CustomName = custom
            });
        }
    }

    private static IEnumerable<string> EnumerateOrphanIds(HashSet<string> liveIds)
    {
        // Best-effort orphan discovery: read session-names.json directly. Failure returns empty
        // (orphans hidden until next activation). No exception propagates to the UI. (T-26-12 mitigated)
        // Note: yield-in-try/catch is not valid C#; use a separate helper that returns a snapshot list.
        var keys = TryReadSessionNamesKeys();
        foreach (var key in keys)
        {
            if (!liveIds.Contains(key)) yield return key;
        }
    }

    private static List<string> TryReadSessionNamesKeys()
    {
        try
        {
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CCInfoWindows", "session-names.json");
            if (!File.Exists(path)) return new List<string>();
            var json = File.ReadAllText(path);
            var dict = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            return dict?.Keys.ToList() ?? new List<string>();
        }
        catch
        {
            // Intentional: best-effort read — never propagate to UI (T-26-12 mitigated)
            return new List<string>();
        }
    }

    [RelayCommand]
    private async Task SaveSessionCustomName(SessionRenameItem item)
    {
        if (item == null) return;
        var sanitized = SessionNameSanitizer.Strip(item.CustomName).Trim();
        if (string.IsNullOrEmpty(sanitized))
        {
            _sessionNameStore.ClearCustomName(item.SessionId);
            item.CustomName = string.Empty;
        }
        else
        {
            _sessionNameStore.SetCustomName(item.SessionId, sanitized);
            // Reflect sanitized value back to the bound TextBox (e.g. control chars stripped):
            item.CustomName = sanitized;
        }
        await _sessionNameStore.SaveAsync();
    }

    [RelayCommand]
    private async Task ClearSessionCustomName(SessionRenameItem item)
    {
        if (item == null) return;
        _sessionNameStore.ClearCustomName(item.SessionId);
        item.CustomName = string.Empty;
        await _sessionNameStore.SaveAsync();
    }

    /// <summary>Called from SettingsView.OnLoaded — subscribe to NameChanged + initial snapshot.</summary>
    public void Activate()
    {
        _sessionNameStore.NameChanged += OnStoreNameChanged;
        if (IsSessionsTabVisible) RefreshSessionRenameItems();
    }

    /// <summary>Called from SettingsView.OnUnloaded — unsubscribe to prevent zombie handlers.</summary>
    public void Deactivate()
    {
        _sessionNameStore.NameChanged -= OnStoreNameChanged;
    }

    private void OnStoreNameChanged(object? sender, SessionNameChangedEventArgs args)
    {
        // G-1: NameChanged may arrive off-thread.
        _dispatcherQueue.TryEnqueue(RefreshSessionRenameItems);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // ORGID-01..02 (D-OG-01..03): Org picker state
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>ORGID-01: ListView ItemsSource for the OrgPicker ContentDialog.</summary>
    public ObservableCollection<OrganizationInfo> AvailableOrganizations { get; } = new();

    /// <summary>ORGID-01: selected org in the dialog ListView (TwoWay binding).</summary>
    [ObservableProperty]
    private OrganizationInfo? _selectedOrgPickerItem;

    /// <summary>
    /// ORGID-01: View subscribes to this event and calls OrgPickerDialog.ShowAsync(); the View
    /// returns the dialog result via the TaskCompletionSource on the event payload, allowing the
    /// command to await user choice without owning XAML references.
    /// </summary>
    public event EventHandler<OrgPickerDialogRequest>? RequestOpenOrgPickerDialog;

    /// <summary>Event payload — View completes the TCS with the dialog result.</summary>
    public sealed class OrgPickerDialogRequest
    {
        public TaskCompletionSource<ContentDialogResult> CompletionSource { get; } = new();
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Constructor
    // ─────────────────────────────────────────────────────────────────────────────

    public SettingsViewModel(
        ISettingsService settingsService,
        ICredentialService credentialService,
        INavigationService navigationService,
        IPricingService pricingService,
        IUsageHistoryService historyService,
        ISessionNameStore sessionNameStore,
        IJsonlService jsonlService,
        IDispatcherQueue dispatcherQueue,
        IClaudeApiService apiService)   // ORGID-01 — new parameter
    {
        _settingsService = settingsService;
        _credentialService = credentialService;
        _navigationService = navigationService;
        _pricingService = pricingService;
        _historyService = historyService;
        _sessionNameStore = sessionNameStore;
        _jsonlService = jsonlService;
        _dispatcherQueue = dispatcherQueue;
        _apiService = apiService;
    }

    private static readonly int[] ThresholdMinuteOptions = [15, 30, 60, 120];

    /// <summary>
    /// Loads persisted settings and binds them to observable properties.
    /// Called on page Loaded event.
    /// </summary>
    public void Initialize()
    {
        var settings = _settingsService.LoadSettings();
        _selectedRefreshOption = RefreshOptions.FirstOrDefault(o => o.Seconds == settings.RefreshIntervalSeconds)
                                 ?? RefreshOptions.First(o => o.Seconds == DefaultRefreshSeconds);
        _isDarkMode = settings.ColorMode != "light"; // default dark
        _selectedThresholdIndex = MapMinutesToThresholdIndex(settings.SessionActivityThresholdMinutes);
        _isAutostart = RegistryHelper.GetAutostart();
        _selectedLanguageIndex = settings.Language == "en-US" ? 1 : 0;
        _selectedSonnetContextIndex = settings.SonnetContextSize == 1_000_000 ? 1 : 0;
        _selectedVisibilityWindowIndex = MapVisibilityDaysToIndex(settings.SessionVisibilityWindowDays);

        OnPropertyChanged(nameof(SelectedRefreshOption));
        OnPropertyChanged(nameof(IsDarkMode));
        OnPropertyChanged(nameof(SelectedThresholdIndex));
        OnPropertyChanged(nameof(IsAutostart));
        OnPropertyChanged(nameof(SelectedLanguageIndex));
        OnPropertyChanged(nameof(SelectedSonnetContextIndex));
        OnPropertyChanged(nameof(SelectedVisibilityWindowIndex));
    }

    partial void OnSelectedRefreshOptionChanged(RefreshOption value)
    {
        var settings = _settingsService.LoadSettings();
        settings.RefreshIntervalSeconds = value.Seconds;
        _settingsService.SaveSettings(settings);

        WeakReferenceMessenger.Default.Send(new RefreshIntervalChangedMessage(value.Seconds));
    }

    partial void OnSelectedThresholdIndexChanged(int value)
    {
        var settings = _settingsService.LoadSettings();
        settings.SessionActivityThresholdMinutes = MapThresholdIndexToMinutes(value);
        _settingsService.SaveSettings(settings);

        // D-08: notify MainViewModel so SortedSessions tooltips update immediately
        //       (without waiting for the next 30s auto-poll).
        WeakReferenceMessenger.Default.Send(
            new SessionTimeoutChangedMessage(settings.SessionActivityThresholdMinutes));
    }

    partial void OnIsAutostartChanged(bool value)
    {
        RegistryHelper.SetAutostart(value);
    }

    partial void OnSelectedLanguageIndexChanged(int value)
    {
        if (value >= 0 && value < LanguageCodes.Length)
        {
            var code = LanguageCodes[value];
            _ = Task.Run(async () =>
            {
                try { await Localizer.Get().SetLanguage(code); }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[Settings] SetLanguage failed: {ex.Message}"); }
            });
            var settings = _settingsService.LoadSettings();
            settings.Language = code;
            _settingsService.SaveSettings(settings);
        }
    }

    partial void OnSelectedSonnetContextIndexChanged(int value)
    {
        if (value >= 0 && value < SonnetContextSizes.Length)
        {
            var settings = _settingsService.LoadSettings();
            settings.SonnetContextSize = SonnetContextSizes[value];
            _settingsService.SaveSettings(settings);
            WeakReferenceMessenger.Default.Send(new SonnetContextChangedMessage(SonnetContextSizes[value]));
        }
    }

    private static int MapThresholdIndexToMinutes(int index)
    {
        if (index >= 0 && index < ThresholdMinuteOptions.Length)
            return ThresholdMinuteOptions[index];

        return ThresholdMinuteOptions[1]; // default 30 minutes
    }

    private static int MapMinutesToThresholdIndex(int minutes)
    {
        var index = Array.IndexOf(ThresholdMinuteOptions, minutes);
        return index >= 0 ? index : 1; // default to index 1 (30 minutes)
    }

    partial void OnSelectedVisibilityWindowIndexChanged(int value)
    {
        var settings = _settingsService.LoadSettings();
        settings.SessionVisibilityWindowDays = MapIndexToVisibilityDays(value);
        _settingsService.SaveSettings(settings);

        // DROPDOWN-04 / D-03: notify MainViewModel so SortedSessions filter re-applies immediately.
        WeakReferenceMessenger.Default.Send(
            new SessionVisibilityChangedMessage(settings.SessionVisibilityWindowDays));
    }

    private static int MapIndexToVisibilityDays(int index) =>
        (index >= 0 && index < VisibilityWindowDayOptions.Length)
            ? VisibilityWindowDayOptions[index]
            : VisibilityWindowDayOptions[DefaultVisibilityWindowIndex];

    private static int MapVisibilityDaysToIndex(int days)
    {
        var index = Array.IndexOf(VisibilityWindowDayOptions, days);
        return index >= 0 ? index : DefaultVisibilityWindowIndex;
    }

    partial void OnIsDarkModeChanged(bool value)
    {
        var colorMode = value ? "dark" : "light";
        var settings = _settingsService.LoadSettings();
        settings.ColorMode = colorMode;
        _settingsService.SaveSettings(settings);
        WeakReferenceMessenger.Default.Send(new ThemeChangedMessage(colorMode));
    }

    [RelayCommand]
    private void ResetWindowSize()
    {
        WeakReferenceMessenger.Default.Send(new ResetWindowSizeMessage());
    }

    // Direct logout sequence — D-13 honored by calling ClearHistory() FIRST.
    // The MainViewModel Logout()/IRecipient<LogoutRequestedMessage> routing
    // (Plan 21-03) was reverted because MainViewModel is registered AddTransient
    // (App.xaml.cs:164); WeakReferenceMessenger silently drops the registration
    // when the MainViewModel instance is GC-collected after navigating away
    // from MainView. The unit tests passed only because of GC.KeepAlive.
    // Production behavior: the message had no live recipient and the user could
    // not log out at all. Reverting to the duplicated-but-working pattern.
    [RelayCommand]
    private void Logout()
    {
        _historyService.ClearHistory();                                              // D-13 ordering trap mitigation — must come FIRST
        _credentialService.ClearCredentials();
        WeakReferenceMessenger.Default.Send(new AuthStateChangedMessage(false));
        _navigationService.NavigateTo<LoginView>();
    }

    [RelayCommand]
    private void GoBack()
    {
        _navigationService.GoBack();
    }

    /// <summary>
    /// ORGID-01..02 (D-OG-02..03): async command that loads the org list, fires
    /// RequestOpenOrgPickerDialog so the View shows the ContentDialog, awaits the user's
    /// choice, and on Primary — persists the new org-id and triggers Logout via
    /// AuthStateChangedMessage(false) broadcast (D-13 workaround: NOT direct MainViewModel call).
    /// </summary>
    [RelayCommand]
    private async Task OpenOrgPickerAsync()
    {
        AvailableOrganizations.Clear();
        SelectedOrgPickerItem = null;

        var orgs = await _apiService.ListAvailableOrganizationsAsync();

        _dispatcherQueue.TryEnqueue(() =>
        {
            foreach (var o in orgs) AvailableOrganizations.Add(o);
        });

        var request = new OrgPickerDialogRequest();
        RequestOpenOrgPickerDialog?.Invoke(this, request);
        var result = await request.CompletionSource.Task;

        if (result != ContentDialogResult.Primary || SelectedOrgPickerItem is null)
            return;

        // ORGID-02 / PITFALLS B2: persist new org-id and trigger logout via the verified
        // AuthStateChangedMessage(false) broadcast (Phase 24 DISPATCH-04 handles cookie-jar
        // reset + nav-to-LoginView). NOT a direct MainViewModel.LogoutCommand call —
        // honors D-13 (AddTransient → wrong instance via DI resolution at call time).
        _credentialService.SaveOrganizationId(SelectedOrgPickerItem.Uuid);
        WeakReferenceMessenger.Default.Send(new AuthStateChangedMessage(false));
    }

    /// <summary>
    /// D-09: Starts the 1-minute About-tab timestamp timer. Idempotent —
    /// multiple Start calls do not create extra timers (Pitfall 7 guard).
    /// Called by SettingsView code-behind on Loaded (if About is initial tab)
    /// and on Segmented.SelectionChanged when index == AboutTabIndex.
    /// </summary>
    public void StartAboutTimestampTimer()
    {
        if (_aboutTimestampTimer != null) return;

        _aboutTimestampTimer = TimerFactory();
        _aboutTimestampTimer.Interval = TimeSpan.FromMinutes(1);
        _aboutTimestampTimer.Tick += OnAboutTimestampTimerTick;
        _aboutTimestampTimer.Start();

        // Initial refresh — show current "X minutes ago" without waiting 60s.
        OnPropertyChanged(nameof(LastFetchRelativeTime));
    }

    /// <summary>
    /// D-09: Stops and disposes the About-tab timestamp timer.
    /// Called on Segmented.SelectionChanged when leaving About, and on Page.Unloaded
    /// (belt-and-suspenders — POLISH-08).
    /// </summary>
    public void StopAboutTimestampTimer()
    {
        if (_aboutTimestampTimer == null) return;

        _aboutTimestampTimer.Tick -= OnAboutTimestampTimerTick;
        _aboutTimestampTimer.Stop();
        _aboutTimestampTimer = null;
    }

    private void OnAboutTimestampTimerTick(object? sender, object e)
    {
        // D-09 + D-11: timer drives rebinding by raising PropertyChanged;
        //              LastFetchRelativeTime is pure-computed — recomputes on read.
        OnPropertyChanged(nameof(LastFetchRelativeTime));
    }
}
