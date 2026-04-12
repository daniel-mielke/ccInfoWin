using CCInfoWindows.Helpers;
using CCInfoWindows.Messages;
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

    /// <summary>
    /// Represents a selectable refresh interval option for the ComboBox.
    /// </summary>
    public record RefreshOption(string Label, int Seconds);

    private const int DefaultRefreshSeconds = 60;

    public List<RefreshOption> RefreshOptions { get; } =
    [
        new("30 Sekunden", 30),
        new("1 Minute", 60),
        new("2 Minuten", 120),
        new("5 Minuten", 300),
        new("10 Minuten", 600),
        new("Manuell", 0)
    ];

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

    private static readonly string[] LanguageCodes = ["de-DE", "en-US"];
    private static readonly int[] SonnetContextSizes = [200_000, 1_000_000];

    public string PricingSourceText => _pricingService.Source switch
    {
        PricingSource.Live => "Live (LiteLLM API)",
        PricingSource.Fallback => "Fallback (geb\u00fcndelt)",
        _ => "Unbekannt"
    };

    public string LastPricingFetchText => _pricingService.LastFetch.HasValue
        ? _pricingService.LastFetch.Value.LocalDateTime.ToString("dd.MM.yyyy HH:mm")
        : "Nie";

    public SettingsViewModel(
        ISettingsService settingsService,
        ICredentialService credentialService,
        INavigationService navigationService,
        IPricingService pricingService)
    {
        _settingsService = settingsService;
        _credentialService = credentialService;
        _navigationService = navigationService;
        _pricingService = pricingService;
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

        OnPropertyChanged(nameof(SelectedRefreshOption));
        OnPropertyChanged(nameof(IsDarkMode));
        OnPropertyChanged(nameof(SelectedThresholdIndex));
        OnPropertyChanged(nameof(IsAutostart));
        OnPropertyChanged(nameof(SelectedLanguageIndex));
        OnPropertyChanged(nameof(SelectedSonnetContextIndex));
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

    [RelayCommand]
    private void Logout()
    {
        _credentialService.ClearCredentials();
        WeakReferenceMessenger.Default.Send(new AuthStateChangedMessage(false));
        _navigationService.NavigateTo<LoginView>();
    }

    [RelayCommand]
    private void GoBack()
    {
        _navigationService.GoBack();
    }
}
