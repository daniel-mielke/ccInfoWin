using CCInfoWindows.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using WinUI3Localizer;

namespace CCInfoWindows.Views;

/// <summary>
/// Settings page with refresh interval, theme toggle, and logout.
/// </summary>
public sealed partial class SettingsView : Page
{
    public SettingsViewModel ViewModel { get; }

    public SettingsView()
    {
        ViewModel = App.Services.GetRequiredService<SettingsViewModel>();
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        ViewModel.Initialize();
        ApplyTabTooltips();
    }

    private void ApplyTabTooltips()
    {
        var localizer = Localizer.Get();
        ToolTipService.SetToolTip(TabGeneral, localizer.GetLocalizedString("SettingsTabGeneral"));
        ToolTipService.SetToolTip(TabUpdates, localizer.GetLocalizedString("SettingsTabUpdates"));
        ToolTipService.SetToolTip(TabAccount, localizer.GetLocalizedString("SettingsTabAccount"));
        ToolTipService.SetToolTip(TabAbout, localizer.GetLocalizedString("SettingsTabAbout"));
    }
}
