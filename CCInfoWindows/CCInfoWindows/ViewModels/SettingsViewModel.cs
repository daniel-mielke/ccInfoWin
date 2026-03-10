using CCInfoWindows.Messages;
using CCInfoWindows.Services.Interfaces;
using CCInfoWindows.Views;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;

namespace CCInfoWindows.ViewModels;

/// <summary>
/// Settings page ViewModel with refresh interval selection, dark/light mode toggle, and logout.
/// </summary>
public partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsService _settingsService;
    private readonly ICredentialService _credentialService;
    private readonly INavigationService _navigationService;

    /// <summary>
    /// Represents a selectable refresh interval option for the ComboBox.
    /// </summary>
    public record RefreshOption(string Label, int Seconds);

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

    public SettingsViewModel(
        ISettingsService settingsService,
        ICredentialService credentialService,
        INavigationService navigationService)
    {
        _settingsService = settingsService;
        _credentialService = credentialService;
        _navigationService = navigationService;
    }

    /// <summary>
    /// Loads persisted settings and binds them to observable properties.
    /// Called on page Loaded event.
    /// </summary>
    public void Initialize()
    {
        var settings = _settingsService.LoadSettings();
        _selectedRefreshOption = RefreshOptions.FirstOrDefault(o => o.Seconds == settings.RefreshIntervalSeconds)
                                 ?? RefreshOptions[1]; // default 60s
        _isDarkMode = settings.ColorMode != "light"; // default dark

        OnPropertyChanged(nameof(SelectedRefreshOption));
        OnPropertyChanged(nameof(IsDarkMode));
    }

    partial void OnSelectedRefreshOptionChanged(RefreshOption value)
    {
        var settings = _settingsService.LoadSettings();
        settings.RefreshIntervalSeconds = value.Seconds;
        _settingsService.SaveSettings(settings);

        WeakReferenceMessenger.Default.Send(new RefreshIntervalChangedMessage(value.Seconds));
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
