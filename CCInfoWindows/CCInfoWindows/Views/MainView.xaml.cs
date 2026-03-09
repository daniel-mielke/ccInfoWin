using CCInfoWindows.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;

namespace CCInfoWindows.Views;

/// <summary>
/// Post-login dashboard view with session expiry InfoBar and logout button.
/// Full dashboard implementation in Phase 2+.
/// </summary>
public sealed partial class MainView : Page
{
    public MainViewModel ViewModel { get; }

    public MainView()
    {
        ViewModel = App.Services.GetRequiredService<MainViewModel>();
        InitializeComponent();
    }
}
