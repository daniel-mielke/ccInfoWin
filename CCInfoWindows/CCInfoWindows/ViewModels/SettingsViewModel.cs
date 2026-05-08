using CCInfoWindows.Helpers;
using CCInfoWindows.Messages;
using CCInfoWindows.Services;
using CCInfoWindows.Services.Interfaces;
using CCInfoWindows.Views;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using WinUI3Localizer;

namespace CCInfoWindows.ViewModels;

/// <summary>
/// Settings page ViewModel with refresh interval selection, dark/light mode toggle, and logout.
/// </summary>
public partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsService _settingsService;
    private readonly ICredentialService _credentialService;
    private readonly INavigationService _navigationService;
    private readonly IPricingService _pricingService;
    private readonly IUsageHistoryService _historyService;

    // D-09: 1-minute UI-thread-bound timer. Owned by SettingsViewModel; lifecycle driven by SettingsView code-behind (D-10).
    private IDispatcherTimer? _aboutTimestampTimer;

    // Testability seam — overridden in unit tests to supply a fake IDispatcherTimer (avoids WinRT COM init).
    internal Func<IDispatcherTimer> TimerFactory { get; set; } = () => new WinuiDispatcherTimerAdapter();

    /// <summary>
    /// Represents a selectable refresh interval option for the ComboBox.
    /// </summary>
    public record RefreshOption(string Label, int Seconds);

    private const int DefaultRefreshSeconds = 60;

    // D-10: tab order is 0=General, 1=Updates, 2=Account, 3=About
    // Used by SettingsView code-behind to start/stop the About-tab timer on navigation.
    public const int AboutTabIndex = 3;

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

    public bool IsGeneralTabVisible => _selectedTabIndex == 0;
    public bool IsUpdatesTabVisible => _selectedTabIndex == 1;
    public bool IsAccountTabVisible => _selectedTabIndex == 2;
    public bool IsAboutTabVisible  => _selectedTabIndex == 3;

    partial void OnSelectedTabIndexChanged(int value)
    {
        OnPropertyChanged(nameof(IsGeneralTabVisible));
        OnPropertyChanged(nameof(IsUpdatesTabVisible));
        OnPropertyChanged(nameof(IsAccountTabVisible));
        OnPropertyChanged(nameof(IsAboutTabVisible));
    }

    public string AppVersionText =>
        System.Reflection.Assembly.GetExecutingAssembly()
            .GetName().Version?.ToString(3) ?? "1.0.0";

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
        PricingSource.Fallback => "Fallback (geb\u00fcndelt)",
        _ => "Unbekannt"
    };

    public string LastPricingFetchText => _pricingService.LastFetch.HasValue
        ? _pricingService.LastFetch.Value.LocalDateTime.ToString("dd.MM.yyyy HH:mm")
        : "Nie";

    /// <summary>
    /// Localized "X minutes ago" string for the About tab. Re-evaluated on each
    /// _aboutTimestampTimer Tick (D-09, D-11). v1.4 fallback: English inline literals
    /// — proper resw keys (LastFetchMinutesAgo, LastFetchNever) are deferred to a
    /// future phase per RESEARCH [A2].
    /// </summary>
    public string LastFetchRelativeTime
    {
        get
        {
            var lastFetch = _pricingService.LastFetch;
            if (!lastFetch.HasValue)
                return "Never";

            var elapsed = DateTimeOffset.Now - lastFetch.Value;
            var minutes = (int)Math.Max(0, elapsed.TotalMinutes);
            return minutes == 1 ? "1 minute ago" : $"{minutes} minutes ago";
        }
    }

    public SettingsViewModel(
        ISettingsService settingsService,
        ICredentialService credentialService,
        INavigationService navigationService,
        IPricingService pricingService,
        IUsageHistoryService historyService)
    {
        _settingsService = settingsService;
        _credentialService = credentialService;
        _navigationService = navigationService;
        _pricingService = pricingService;
        _historyService = historyService;
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
