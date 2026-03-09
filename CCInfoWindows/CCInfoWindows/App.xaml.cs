using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;

namespace CCInfoWindows;

/// <summary>
/// Application entry point with DI container configuration.
/// </summary>
public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    private Window? _window;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        Services = ConfigureServices();
        _window = new MainWindow();
        _window.Activate();
    }

    private static IServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();
        // Services will be registered in Task 2
        return services.BuildServiceProvider();
    }
}
