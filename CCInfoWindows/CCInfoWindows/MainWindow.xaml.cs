using CCInfoWindows.Helpers;
using CCInfoWindows.Messages;
using CCInfoWindows.Models;
using CCInfoWindows.Services.Interfaces;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;

namespace CCInfoWindows;

/// <summary>
/// Main application window with Frame-based navigation shell.
/// Sets initial size, minimum constraints, persists window state,
/// and applies theme changes via ThemeChangedMessage.
/// </summary>
public sealed partial class MainWindow : Window, IRecipient<ThemeChangedMessage>, IRecipient<ResetWindowSizeMessage>
{
    /// <summary>
    /// WebView2 User Data Folder path for cookie/cache isolation.
    /// Used by Plan 02 (LoginView) for WebView2 initialization.
    /// </summary>
    public static readonly string WebView2UserDataFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CCInfoWindows", "WebView2");

    private readonly ISettingsService _settingsService;
    private readonly INavigationService _navigationService;

    public MainWindow()
    {
        InitializeComponent();

        _settingsService = App.Services.GetRequiredService<ISettingsService>();
        _navigationService = App.Services.GetRequiredService<INavigationService>();

        ConfigureWindow();
        RestoreWindowState();
        InitializeNavigation();

        AppWindow.Closing += OnClosing;

        WeakReferenceMessenger.Default.Register<ThemeChangedMessage>(this);
        WeakReferenceMessenger.Default.Register<ResetWindowSizeMessage>(this);
    }

    /// <summary>
    /// Applies theme change immediately by setting RequestedTheme on the root FrameworkElement.
    /// </summary>
    public void Receive(ThemeChangedMessage message)
    {
        if (Content is FrameworkElement fe)
        {
            fe.RequestedTheme = message.Value == "light"
                ? ElementTheme.Light
                : ElementTheme.Dark;
        }
    }

    /// <summary>
    /// Resets the window to the default size when triggered via settings.
    /// </summary>
    public void Receive(ResetWindowSizeMessage message)
    {
        AppWindow.Resize(WindowHelper.GetDefaultWindowSize(GetDpiScale()));
    }

    private void ConfigureWindow()
    {
        Title = "ccInfoWin";
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico"));

        var defaultSize = WindowHelper.GetDefaultWindowSize(GetDpiScale());
        AppWindow.Resize(defaultSize);

        // Set minimum size via OverlappedPresenter
        var presenter = OverlappedPresenter.Create();
        presenter.PreferredMinimumWidth = 300;
        presenter.PreferredMinimumHeight = 300;
        AppWindow.SetPresenter(presenter);
    }

    private double GetDpiScale()
    {
        return Content is FrameworkElement fe && fe.XamlRoot != null
            ? fe.XamlRoot.RasterizationScale
            : 1.0;
    }

    private void RestoreWindowState()
    {
        var savedState = _settingsService.LoadWindowState();
        if (savedState != null && WindowHelper.IsPositionOnScreen(savedState))
        {
            AppWindow.MoveAndResize(new RectInt32(
                savedState.X, savedState.Y,
                savedState.Width, savedState.Height));
        }
    }

    private void InitializeNavigation()
    {
        _navigationService.Initialize(RootFrame);
    }

    private void OnClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        var state = new WindowState(
            AppWindow.Position.X,
            AppWindow.Position.Y,
            AppWindow.Size.Width,
            AppWindow.Size.Height);

        _settingsService.SaveWindowState(state);
    }
}
