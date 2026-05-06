using CCInfoWindows.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinUI3Localizer;

namespace CCInfoWindows.Views;

/// <summary>
/// Settings page with refresh interval, theme toggle, and logout.
/// </summary>
public sealed partial class SettingsView : Page
{
    // D-10: tab order defined in SettingsViewModel.AboutTabIndex (shared constant)
    private const int AboutTabIndex = SettingsViewModel.AboutTabIndex;

    public SettingsViewModel ViewModel { get; }

    public SettingsView()
    {
        ViewModel = App.Services.GetRequiredService<SettingsViewModel>();
        InitializeComponent();
        Loaded += OnLoaded;
        // Unloaded is wired via XAML attribute (Page.Unloaded="OnUnloaded")
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ViewModel.Initialize();
        ApplyTabTooltips();
        // D-10: if Settings opens with About already selected (rare — persistence),
        // start the timer immediately so "X minutes ago" is live from t=0.
        if (TabsSegmented.SelectedIndex == AboutTabIndex)
            ViewModel.StartAboutTimestampTimer();
    }

    private void OnSegmentedSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // D-10: route Segmented.SelectionChanged to ViewModel timer lifecycle.
        // ViewModel may be null during early initialization; guard.
        if (ViewModel == null) return;

        if (TabsSegmented.SelectedIndex == AboutTabIndex)
            ViewModel.StartAboutTimestampTimer();
        else
            ViewModel.StopAboutTimestampTimer();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        // D-10: belt-and-suspenders — always stop on Page.Unloaded (POLISH-08).
        ViewModel?.StopAboutTimestampTimer();
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
