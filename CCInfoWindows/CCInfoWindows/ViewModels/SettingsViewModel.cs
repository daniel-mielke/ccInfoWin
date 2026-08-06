using System.Collections.ObjectModel;
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
    private readonly IWebViewBridge _bridge;                    // Finding 18 / logout must unbind the API bridge

    // D-09: 1-minute UI-thread-bound timer. Owned by SettingsViewModel; lifecycle driven by SettingsView code-behind (D-10).
    private readonly IUsageNotificationService _usageNotificationService;

    private IDispatcherTimer? _aboutTimestampTimer;

    // Testability seam — overridden in unit tests to supply a fake IDispatcherTimer (avoids WinRT COM init).
    internal Func<IDispatcherTimer> TimerFactory { get; set; } = () => new WinuiDispatcherTimerAdapter();

    /// <summary>
    /// The only call into WinUI3Localizer's language switch, kept behind a delegate so the library
    /// stays swappable and so tests can drive both the success and the failure branch — the library
    /// has no headless host, which is why the runtime-language-switch UAT was never executable.
    /// </summary>
    internal Func<string, Task> LanguageSwitcher { get; set; } = code => Localizer.Get().SetLanguage(code);

    /// <summary>
    /// Represents a selectable refresh interval option for the ComboBox.
    /// </summary>
    public record RefreshOption(string Label, int Seconds);

    // Tab order in SettingsView's Segmented control. Every dependent site — the visibility getters
    // below, the About-tab timer in the code-behind, the tests — must reference these, never the
    // literal: AboutTabIndex already shifted once when Phase 26 inserted Sessions at index 3.
    public const int GeneralTabIndex = 0;
    public const int UpdatesTabIndex = 1;
    public const int AccountTabIndex = 2;
    public const int SessionsTabIndex = 3;   // Phase 26 / RENAME-02
    public const int AboutTabIndex = 4;

    public List<RefreshOption> RefreshOptions { get; } =
    [
        new("30s", 30),
        new("1min", 60),
        new("2min", 120),
        new("5min", 300),
        new("10min", 600),
        new(Localize("RefreshIntervalManual", "Manual"), AppSettings.ManualRefreshSeconds)
    ];

    [ObservableProperty]
    private int _selectedTabIndex = GeneralTabIndex;

    public bool IsGeneralTabVisible  => _selectedTabIndex == GeneralTabIndex;
    public bool IsUpdatesTabVisible  => _selectedTabIndex == UpdatesTabIndex;
    public bool IsAccountTabVisible  => _selectedTabIndex == AccountTabIndex;
    public bool IsSessionsTabVisible => _selectedTabIndex == SessionsTabIndex;
    public bool IsAboutTabVisible    => _selectedTabIndex == AboutTabIndex;

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
    private int _selectedVisibilityWindowIndex;

    /// <summary>Message for the page-level error InfoBar. Empty while nothing has failed.</summary>
    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private bool _isErrorVisible;

    // Dropdown indices derived from the allow-lists so a reordered option list cannot desync them.
    private static readonly int DefaultVisibilityWindowIndex =
        AppSettings.SupportedSessionVisibilityWindowDays.IndexOf(AppSettings.DefaultSessionVisibilityWindowDays);

    private static readonly int DefaultThresholdIndex =
        AppSettings.SupportedSessionActivityThresholdMinutes.IndexOf(AppSettings.DefaultSessionActivityThresholdMinutes);

    private static readonly int DefaultLanguageIndex =
        AppSettings.SupportedLanguages.IndexOf(AppSettings.DefaultLanguage);

    /// <summary>Index of the language currently active in the localizer, i.e. the revert target.</summary>
    private int _appliedLanguageIndex = DefaultLanguageIndex;

    private bool _isRevertingLanguage;

    public string PricingSourceText => _pricingService.Source switch
    {
        PricingSource.Live => "Live (LiteLLM API)",
        PricingSource.Fallback => Localize("PricingSourceFallback", "Fallback (bundled)"),
        _ => Localize("PricingSourceUnknown", "Unknown")
    };

    /// <summary>
    /// Reads a resw value through the localizer, falling back to the given en-US literal. The
    /// fallback only ever renders when the key is missing from both dictionaries — WinUI3Localizer
    /// returns an empty string in that case, and an empty label is worse than an untranslated one.
    /// </summary>
    private static string Localize(string uid, string enUsFallback)
    {
        var localized = Localizer.Get().GetLocalizedString(uid);
        return string.IsNullOrWhiteSpace(localized) ? enUsFallback : localized;
    }

    /// <summary>Opens the page-level error InfoBar with a localized, non-technical message.</summary>
    private void ShowError(string uid, string enUsFallback)
    {
        ErrorMessage = Localize(uid, enUsFallback);
        IsErrorVisible = true;
    }

    private void ClearError()
    {
        IsErrorVisible = false;
        ErrorMessage = string.Empty;
    }

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
        var clearLabel = Localize("SessionsClearNameTooltip", "Remove custom name");

        SessionRenameItems.Clear();

        // Live sessions first, sorted by display name
        foreach (var s in liveSessions.OrderBy(s => s.DisplayName, StringComparer.OrdinalIgnoreCase))
        {
            SessionRenameItems.Add(new SessionRenameItem
            {
                SessionId = s.Id,
                DefaultName = s.DisplayName,
                IsOrphan = false,
                CustomName = _sessionNameStore.GetCustomName(s.Id) ?? string.Empty,
                ClearCustomNameCommand = ClearSessionCustomNameCommand,
                ClearCustomNameLabel = clearLabel
            });
        }

        // Orphan custom names (D-08): sessions whose JSONL files are gone but a custom name persists.
        foreach (var orphanId in EnumerateOrphanIds(liveIds))
        {
            var custom = _sessionNameStore.GetCustomName(orphanId);
            if (string.IsNullOrEmpty(custom)) continue;
            SessionRenameItems.Add(new SessionRenameItem
            {
                SessionId = orphanId,
                DefaultName = orphanId,   // raw projectDirName as fallback label
                IsOrphan = true,
                CustomName = custom,
                ClearCustomNameCommand = ClearSessionCustomNameCommand,
                ClearCustomNameLabel = clearLabel
            });
        }
    }

    /// <summary>
    /// Store keys with no matching live session. The store owns session-names.json and exposes its
    /// keys, so this no longer rebuilds the file path or re-parses the JSON behind the store's back.
    /// </summary>
    private IEnumerable<string> EnumerateOrphanIds(HashSet<string> liveIds)
    {
        var keys = _sessionNameStore.GetKnownSessionIds();
        foreach (var key in keys)
        {
            if (!liveIds.Contains(key)) yield return key;
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

        await PersistSessionNamesAsync(item);
    }

    [RelayCommand]
    private async Task ClearSessionCustomName(SessionRenameItem item)
    {
        if (item == null) return;
        _sessionNameStore.ClearCustomName(item.SessionId);
        item.CustomName = string.Empty;

        await PersistSessionNamesAsync(item);
    }

    /// <summary>
    /// The store mutates its map and raises NameChanged before writing, so a failed write has
    /// already rolled the map back by the time SaveAsync returns false. Re-reading the store instead
    /// of keeping the optimistic value is what stops the row from displaying a name that is not on
    /// disk; the InfoBar tells the user, and the technical detail is already in AppLog.
    /// </summary>
    private async Task PersistSessionNamesAsync(SessionRenameItem item)
    {
        if (await _sessionNameStore.SaveAsync()) return;

        item.CustomName = _sessionNameStore.GetCustomName(item.SessionId) ?? string.Empty;
        ShowError("SettingsSessionNameSaveFailed", "The session name could not be saved.");
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
        IClaudeApiService apiService,   // ORGID-01 — new parameter
        IUsageNotificationService usageNotificationService,
        IWebViewBridge bridge)          // Finding 18 — logout is the only reachable one, so it owns the bridge reset
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
        _usageNotificationService = usageNotificationService;
        _bridge = bridge;
    }

    /// <summary>
    /// Loads persisted settings and binds them to observable properties.
    /// Called on page Loaded event.
    /// </summary>
    public void Initialize()
    {
        var settings = _settingsService.LoadSettings();
        _selectedRefreshOption = RefreshOptions.FirstOrDefault(o => o.Seconds == settings.RefreshIntervalSeconds)
                                 ?? RefreshOptions.First(o => o.Seconds == AppSettings.DefaultRefreshIntervalSeconds);
        _isDarkMode = settings.ColorMode != AppSettings.LightColorMode; // default dark
        _selectedThresholdIndex = MapMinutesToThresholdIndex(settings.SessionActivityThresholdMinutes);
        _isAutostart = RegistryHelper.GetAutostart();
        _selectedLanguageIndex = MapLanguageToIndex(settings.Language);
        _appliedLanguageIndex = _selectedLanguageIndex;
        _selectedVisibilityWindowIndex = MapVisibilityDaysToIndex(settings.SessionVisibilityWindowDays);

        OnPropertyChanged(nameof(SelectedRefreshOption));
        OnPropertyChanged(nameof(IsDarkMode));
        OnPropertyChanged(nameof(SelectedThresholdIndex));
        OnPropertyChanged(nameof(IsAutostart));
        OnPropertyChanged(nameof(SelectedLanguageIndex));
        OnPropertyChanged(nameof(SelectedVisibilityWindowIndex));
    }

    partial void OnSelectedRefreshOptionChanged(RefreshOption value)
    {
        var settings = _settingsService.LoadSettings();
        settings.RefreshIntervalSeconds = value.Seconds;
        _settingsService.SaveSettings(settings);
    }

    partial void OnSelectedThresholdIndexChanged(int value)
    {
        var settings = _settingsService.LoadSettings();
        settings.SessionActivityThresholdMinutes = MapThresholdIndexToMinutes(value);
        _settingsService.SaveSettings(settings);
    }

    partial void OnIsAutostartChanged(bool value)
    {
        RegistryHelper.SetAutostart(value);
    }

    partial void OnSelectedLanguageIndexChanged(int value)
    {
        if (_isRevertingLanguage) return;   // our own revert echo, not a user choice
        if (value < 0 || value >= AppSettings.SupportedLanguages.Length) return;

        // Deliberately not wrapped in Task.Run: WinUI3Localizer 2.3.0 calls SetValue on thread-affine
        // XAML DependencyObjects and contains no dispatcher marshaling of its own, so SetLanguage must
        // run on the UI thread. This callback is only ever reached from the ComboBox's TwoWay binding
        // (Initialize assigns the backing field), so the awaited continuation resumes there too.
        //
        // The task is discarded because an [ObservableProperty] change callback cannot be async; it
        // catches everything internally, so nothing is left unobserved.
        _ = ApplyLanguageAsync(AppSettings.SupportedLanguages[value], value);
    }

    /// <summary>
    /// Applies the language, then persists it. The old order committed settings.json before the
    /// switch could report failure, so a throwing SetLanguage left the file claiming a language the
    /// screen was not showing.
    /// </summary>
    private async Task ApplyLanguageAsync(string languageCode, int languageIndex)
    {
        try
        {
            await LanguageSwitcher(languageCode);
            ApplyUiCulture(languageCode);

            var settings = _settingsService.LoadSettings();
            settings.Language = languageCode;
            _settingsService.SaveSettings(settings);

            _appliedLanguageIndex = languageIndex;
            ClearError();

            // VM-computed strings are not DependencyObjects, so the localizer cannot re-apply them.
            OnPropertyChanged(nameof(PricingSourceText));
            OnPropertyChanged(nameof(LastFetchRelativeTime));
        }
        catch (Exception ex)
        {
            AppLog.Write($"{nameof(SettingsViewModel)}.{nameof(ApplyLanguageAsync)}", ex,
                $"language switch to {languageCode} failed");
            RevertLanguageSelection();
            ShowError("SettingsLanguageChangeFailed", "The display language could not be changed.");
        }
    }

    /// <summary>
    /// Points <see cref="CultureInfo.CurrentUICulture"/> at the language the localizer just applied.
    /// WinUI3Localizer only swaps resw values, so without this the resw-supplied date patterns render
    /// with the OS language's day and month names — CountdownFormatter.FormatResetDate and
    /// MainViewModel's next-window label both format through CurrentUICulture.
    ///
    /// CurrentCulture is deliberately NOT changed: number, currency and regional date formatting is an
    /// OS user setting that a display-language choice must not override, and every numeric formatter
    /// here (CostFormatter, TokenFormatter) is already pinned to InvariantCulture. App does the same at
    /// startup. The local catch keeps a globalization failure from being reported to the user as a
    /// failed language switch — the switch itself already succeeded.
    /// </summary>
    private static void ApplyUiCulture(string languageCode)
    {
        try
        {
            var culture = CultureInfo.GetCultureInfo(languageCode);
            CultureInfo.DefaultThreadCurrentUICulture = culture;
            CultureInfo.CurrentUICulture = culture;
        }
        catch (CultureNotFoundException ex)
        {
            AppLog.Write($"{nameof(SettingsViewModel)}.{nameof(ApplyUiCulture)}", ex,
                $"'{languageCode}' is not a culture this system knows");
        }
    }

    /// <summary>
    /// Puts the dropdown back on the language that is actually active. The guard swallows the
    /// resulting change notification, which would otherwise retry the failing switch in a loop.
    /// </summary>
    private void RevertLanguageSelection()
    {
        _isRevertingLanguage = true;
        try { SelectedLanguageIndex = _appliedLanguageIndex; }
        finally { _isRevertingLanguage = false; }
    }

    private static int MapLanguageToIndex(string? language)
    {
        var index = AppSettings.SupportedLanguages.IndexOf(language ?? string.Empty);
        return index >= 0 ? index : DefaultLanguageIndex;
    }

    private static int MapThresholdIndexToMinutes(int index)
    {
        if (index >= 0 && index < AppSettings.SupportedSessionActivityThresholdMinutes.Length)
            return AppSettings.SupportedSessionActivityThresholdMinutes[index];

        return AppSettings.DefaultSessionActivityThresholdMinutes;
    }

    private static int MapMinutesToThresholdIndex(int minutes)
    {
        var index = AppSettings.SupportedSessionActivityThresholdMinutes.IndexOf(minutes);
        return index >= 0 ? index : DefaultThresholdIndex;
    }

    partial void OnSelectedVisibilityWindowIndexChanged(int value)
    {
        var settings = _settingsService.LoadSettings();
        settings.SessionVisibilityWindowDays = MapIndexToVisibilityDays(value);
        _settingsService.SaveSettings(settings);
    }

    private static int MapIndexToVisibilityDays(int index) =>
        (index >= 0 && index < AppSettings.SupportedSessionVisibilityWindowDays.Length)
            ? AppSettings.SupportedSessionVisibilityWindowDays[index]
            : AppSettings.DefaultSessionVisibilityWindowDays;

    private static int MapVisibilityDaysToIndex(int days)
    {
        var index = AppSettings.SupportedSessionVisibilityWindowDays.IndexOf(days);
        return index >= 0 ? index : DefaultVisibilityWindowIndex;
    }

    partial void OnIsDarkModeChanged(bool value)
    {
        var colorMode = value ? AppSettings.DarkColorMode : AppSettings.LightColorMode;
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

    /// <summary>
    /// The one reachable logout (bound at Views/SettingsView.xaml). Direct calls rather than a
    /// LogoutRequestedMessage round-trip: MainViewModel is AddTransient, and WeakReferenceMessenger
    /// silently drops a GC'd recipient, so the message had no live recipient in production and the
    /// user could not log out at all (D-13). Finding 18 deleted the more complete duplicate that used
    /// to live in MainViewModel and moved the bridge reset here.
    ///
    /// The order is load-bearing, not stylistic:
    ///   1. ClearHistory FIRST — the D-13 ordering trap. A save racing the credential clear
    ///      re-persists usage-history.json after deletion and leaks the previous account's usage.
    ///   2. Reset the bridge — drains in-flight fetches and unbinds the CoreWebView2, so no reply can
    ///      land later and re-create the snapshot cleared in step 4. It also has to happen before the
    ///      navigation below: LoginView's WebView2 init deletes the claude.ai cookies from the shared
    ///      user data folder, which is what actually ends the browser-side session.
    ///   3. ClearCredentials — no new authenticated request can start after this point.
    ///   4. ClearCache — usage_cache.json outlived the session and rendered the previous account's
    ///      figures on the next login (finding 18).
    ///   5. CancelAll — stops the reset countdowns and wipes notification-state.json, so the next
    ///      account does not inherit armed 80/95 % thresholds for a window it never used.
    /// </summary>
    [RelayCommand]
    private void Logout()
    {
        _historyService.ClearHistory();
        _bridge.Reset();
        _credentialService.ClearCredentials();
        _apiService.ClearCache();
        _usageNotificationService.CancelAll();
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
    /// choice, and on Primary — persists the new org-id and clears the now-stale history.
    /// The next poll picks up the new id; the session token stays valid across orgs.
    /// </summary>
    [RelayCommand]
    private async Task OpenOrgPickerAsync()
    {
        var orgs = await _apiService.ListAvailableOrganizationsAsync();

        // Populate BEFORE the dialog opens and await the dispatcher hop — an unawaited
        // TryEnqueue let the ListView realize while still empty, which is what made the
        // dialog render as just a title and two buttons.
        var populated = new TaskCompletionSource();
        _dispatcherQueue.TryEnqueue(() =>
        {
            AvailableOrganizations.Clear();
            SelectedOrgPickerItem = null;
            foreach (var o in orgs) AvailableOrganizations.Add(o);
            populated.TrySetResult();
        });
        await populated.Task;

        var request = new OrgPickerDialogRequest();
        RequestOpenOrgPickerDialog?.Invoke(this, request);
        var result = await request.CompletionSource.Task;

        if (result != ContentDialogResult.Primary || SelectedOrgPickerItem is null)
            return;

        // Persist the new org-id and drop the history — it belongs to the previous org and
        // would splice two unrelated utilization curves together. The session token stays
        // valid across orgs (only the request URL changes), so no logout is needed; the next
        // poll picks up the new id via GetOrganizationId(). The former
        // AuthStateChangedMessage(false) broadcast never arrived anyway — MainViewModel is
        // AddTransient and gets GC'd as a WeakReferenceMessenger recipient (D-13).
        _credentialService.SaveOrganizationId(SelectedOrgPickerItem.Uuid);
        _historyService.ClearHistory();
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
