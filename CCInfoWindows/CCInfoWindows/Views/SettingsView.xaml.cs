using CCInfoWindows.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;

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
        Loaded += (s, e) => ViewModel.Initialize();
    }
}
