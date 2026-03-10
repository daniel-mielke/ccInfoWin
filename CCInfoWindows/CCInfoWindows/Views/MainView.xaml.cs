using System.ComponentModel;
using CCInfoWindows.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CCInfoWindows.Views;

/// <summary>
/// Dashboard view with usage sections, progress bars, countdown timers, and footer toolbar.
/// </summary>
public sealed partial class MainView : Page
{
    public MainViewModel ViewModel { get; }

    public MainView()
    {
        ViewModel = App.Services.GetRequiredService<MainViewModel>();
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        await ViewModel.InitializeAsync();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        ViewModel.StopTimers();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.IsRefreshing))
        {
            if (ViewModel.IsRefreshing)
            {
                SpinnerStoryboard.Begin();
            }
            else
            {
                SpinnerStoryboard.Stop();
            }
        }
    }
}
