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
    /// Represents a selectable refresh interval option for the ComboBox. Observable rather than a
    /// record because the manual entry's label is localized: a runtime language switch has to
    /// re-resolve it, and replacing the item would clear ComboBox.SelectedItem and write null back
    /// into SelectedRefreshOption through the TwoWay binding.
    /// </summary>
    public partial class RefreshOption : ObservableObject
    {
        public RefreshOption(string label, int seconds)
        {
            Label = label;
            Seconds = seconds;
        }

        [ObservableProperty]
        private string _label = string.Empty;

        public int Seconds { get; }
    }

    // Tab order in SettingsView's Segmented control. Every dependent site — the visibility getters
    // below, the About-tab timer in the code-behind, the tests — must reference these, never the
    // literal: AboutTabIndex already shifted once when Phase 26 inserted Sessions at index 3.
    public const int GeneralTabIndex = 0;
    public const int UpdatesTabIndex = 1;
    public const int AccountTabIndex = 2;
    public const int SessionsTabIndex = 3;   // Phase 26 / RENAME-02
    public const int AboutTabIndex = 4;

    /// <summary>
    /// The only entry with a translated label, held by reference so a language switch can refresh it
    /// in place without disturbing the collection or the current selection.
    /// </summary>
    private readonly RefreshOption _manualRefreshOption =
        new(ManualRefreshLabel(), AppSettings.ManualRefreshSeconds);

    public List<RefreshOption> RefreshOptions { get; }

    private static string ManualRefreshLabel() => Localize("RefreshIntervalManual", "Manual");

    /// <summary>
    /// The resw key and English fallback of every dropdown entry that is addressed by position.
    /// Order is load-bearing: SelectedThresholdIndex and SelectedVisibilityWindowIndex index into it.
    /// </summary>
    private static readonly (string Uid, string Fallback)[] SessionTimeoutCaptions =
    [
        ("SessionTimeout15Label", "15 minutes"),
        ("SessionTimeout30Label", "30 minutes"),
        ("SessionTimeout60Label", "60 minutes"),
        ("SessionTimeout120Label", "120 minutes"),
    ];

    private static readonly (string Uid, string Fallback)[] VisibilityWindowCaptions =
    [
        ("VisibilityWindow7dLabel", "7 days"),
        ("VisibilityWindow30dLabel", "30 days"),
        ("VisibilityWindow90dLabel", "90 days"),
        ("VisibilityWindowUnlimitedLabel", "Unlimited"),
    ];

    public List<LabeledOption> SessionTimeoutOptions { get; } = BuildOptions(SessionTimeoutCaptions);

    public List<LabeledOption> VisibilityWindowOptions { get; } = BuildOptions(VisibilityWindowCaptions);

    private static List<LabeledOption> BuildOptions((string Uid, string Fallback)[] captions) =>
        [.. captions.Select(caption => new LabeledOption(Localize(caption.Uid, caption.Fallback)))];

    /// <summary>
    /// Re-reads every positional dropdown caption. Assigning Label rather than replacing the entry is
    /// what keeps the selection — and therefore the persisted setting — untouched.
    /// </summary>
    private static void RefreshOptionLabels(
        List<LabeledOption> options, (string Uid, string Fallback)[] captions)
    {
        for (var index = 0; index < options.Count && index < captions.Length; index++)
        {
            options[index].Label = Localize(captions[index].Uid, captions[index].Fallback);
        }
    }

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

    private const string LocalizeLogSource = "SettingsViewModel.Localize";

    /// <summary>
    /// Reads a resw value through the localizer, falling back to the given en-US literal.
    ///
    /// The rule itself now lives in Helpers/LocalizedText, which is what stops this file from growing a
    /// second answer to "is this dictionary answer usable?". Only the echoed-uid clause is opted out
    /// of: the echo is what SettingsViewModelTests and SettingsViewModelTimerTests read to assert WHICH
    /// key a label reached for (see HeadlessLocalizerContractTests), and it cannot reach a shipped
    /// caption — App awaits the localizer build before the first window is constructed. What the
    /// shared rule contributes here is the guarded lookup: the callers below are property getters, so a
    /// throwing Localizer.Get() used to escape into binding evaluation.
    /// </summary>
    private static string Localize(string uid, string enUsFallback) =>
        LocalizedText.ResolveKeepingEcho(uid, enUsFallback, LocalizeLogSource);

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
    /// _aboutTimestampTimer Tick (D-09, D-11). L10N-01: 5 categories backed by the single-segment resw
    /// keys LastFetchNever / LastFetchJustNow / LastFetchMinutesAgo / LastFetchHoursAgo /
    /// LastFetchDaysAgo, at the 30 s, 60 min and 24 h boundaries.
    ///
    /// Single-segment on purpose, and the doc said "LastFetchRelative.*" until the 2026-08-06 review:
    /// WinUI3Localizer 2.3.0 keys its dictionary on the text before the FIRST '.', so every dotted key
    /// the old comment named would resolve to an empty label.
    /// </summary>
    public string LastFetchRelativeTime
    {
        get
        {
            var lastFetch = _pricingService.LastFetch;
            if (!lastFetch.HasValue)
                return Localize("LastFetchNever", "Never");

            var elapsed = DateTimeOffset.Now - lastFetch.Value;
            if (elapsed.TotalSeconds < 30)
                return Localize("LastFetchJustNow", "just now");

            if (elapsed.TotalMinutes < 60)
                return Ago("LastFetchMinutesAgo", "{0} minutes ago", elapsed.TotalMinutes);

            if (elapsed.TotalHours < 24)
                return Ago("LastFetchHoursAgo", "{0} hours ago", elapsed.TotalHours);

            return Ago("LastFetchDaysAgo", "{0} days ago", elapsed.TotalDays);
        }
    }

    /// <summary>
    /// One counted band of the relative-time label. The clamp is what keeps a clock correction from
    /// rendering a negative count, and having it in one place is what keeps plural handling or a format
    /// provider from ever being applied to the minutes band alone — a bug that would only show at
    /// one age.
    /// </summary>
    private static string Ago(string uid, string enUsFallback, double elapsedUnits) =>
        string.Format(Localize(uid, enUsFallback), (int)Math.Max(0, elapsedUnits));

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

    /// <summary>
    /// Commits what the row currently holds; an empty box is the intended way to drop a custom name.
    /// Depends on the TextBox binding writing back per keystroke (UpdateSourceTrigger=PropertyChanged
    /// in SettingsView.xaml): with the WinUI default of LostFocus the write-back lands after the
    /// commit handlers, so every rename read an empty CustomName and cleared the name instead.
    /// </summary>
    [RelayCommand]
    private async Task SaveSessionCustomName(SessionRenameItem item)
    {
        if (item == null) return;
        var sanitized = SessionNameSanitizer.Strip(item.CustomName).Trim();
        if (string.IsNullOrEmpty(sanitized))
        {
            // The same gesture as the X button, so it runs the same command rather than a second copy
            // of it: an emptied box and a click must not be able to drift apart.
            await ClearSessionCustomName(item);
            return;
        }

        _sessionNameStore.SetCustomName(item.SessionId, sanitized);
        // Reflect sanitized value back to the bound TextBox (e.g. control chars stripped):
        item.CustomName = sanitized;

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

        // Built here rather than in an initializer: a field initializer cannot reference another
        // instance field, and the manual entry has to be the very instance the language switch
        // updates in place.
        RefreshOptions =
        [
            new("30s", 30),
            new("1min", 60),
            new("2min", 120),
            new("5min", 300),
            new("10min", 600),
            _manualRefreshOption
        ];
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

    /// <summary>
    /// The read-modify-write every settings change callback performs. The [ObservableProperty] callback
    /// shape is framework-imposed, so the load/mutate/save around it is the only part that can be
    /// shared.
    ///
    /// ponytail: last write wins. Two callbacks firing before the first save completes can still drop
    /// each other's field, and a failed SaveSettings is not surfaced — both predate this helper and
    /// hold identically with one copy or five.
    /// </summary>
    private void UpdateSettings(Action<AppSettings> mutate)
    {
        var settings = _settingsService.LoadSettings();
        mutate(settings);
        _settingsService.SaveSettings(settings);
    }

    partial void OnSelectedRefreshOptionChanged(RefreshOption value) =>
        UpdateSettings(settings => settings.RefreshIntervalSeconds = value.Seconds);

    partial void OnSelectedThresholdIndexChanged(int value) =>
        UpdateSettings(settings => settings.SessionActivityThresholdMinutes = MapThresholdIndexToMinutes(value));

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
    /// Raised on the UI thread after a language switch succeeded. The View re-applies the labels the
    /// localizer cannot reach on its own — tab tooltips it set in code, and the ComboBoxItems whose
    /// popup is not part of the visual tree the localizer walks. A .NET event on the ViewModel the
    /// View already owns, per the CLAUDE.md G-1 priority (direct wiring over WeakReferenceMessenger).
    /// </summary>
    public event EventHandler? LanguageApplied;

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

            // After the switch, never before: a failed switch must not leave the culture claiming a
            // language the screen is not showing. UiCulture logs and ignores a name the system cannot
            // resolve, so a globalization failure is not reported to the user as a failed switch.
            UiCulture.Apply(languageCode, $"{nameof(SettingsViewModel)}.{nameof(ApplyLanguageAsync)}");

            UpdateSettings(settings => settings.Language = languageCode);

            _appliedLanguageIndex = languageIndex;
            ClearError();

            // VM-computed strings are not DependencyObjects, so the localizer cannot re-apply them.
            OnPropertyChanged(nameof(PricingSourceText));
            OnPropertyChanged(nameof(LastFetchRelativeTime));

            // Same reason, one level deeper: this label is VM-owned text behind DisplayMemberPath.
            // Mutating the instance keeps ComboBox.SelectedItem pointing at it.
            _manualRefreshOption.Label = ManualRefreshLabel();
            RefreshOptionLabels(SessionTimeoutOptions, SessionTimeoutCaptions);
            RefreshOptionLabels(VisibilityWindowOptions, VisibilityWindowCaptions);

            LanguageApplied?.Invoke(this, EventArgs.Empty);
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

    partial void OnSelectedVisibilityWindowIndexChanged(int value) =>
        UpdateSettings(settings => settings.SessionVisibilityWindowDays = MapIndexToVisibilityDays(value));

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
        UpdateSettings(settings => settings.ColorMode = colorMode);
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
