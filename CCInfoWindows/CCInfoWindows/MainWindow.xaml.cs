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
    private const string ShutdownLogSource = "MainWindow.StopBackgroundServices";

    private readonly ISettingsService _settingsService;
    private readonly INavigationService _navigationService;
    private readonly IUsageHistoryService _historyService;
    private readonly IJsonlService _jsonlService;
    private readonly IUpdateService _updateService;

    public MainWindow()
    {
        InitializeComponent();

        _settingsService = App.Services.GetRequiredService<ISettingsService>();
        _navigationService = App.Services.GetRequiredService<INavigationService>();
        _historyService = App.Services.GetRequiredService<IUsageHistoryService>();
        _jsonlService = App.Services.GetRequiredService<IJsonlService>();
        _updateService = App.Services.GetRequiredService<IUpdateService>();

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
    [ThreadSafeReceive("Window receivers run on the UI thread that hosts the window — WinUI 3 Window construction and access is by-design UI-thread-only.")]
    public void Receive(ThemeChangedMessage message)
    {
        if (Content is FrameworkElement fe)
        {
            // The constant, not the literal: SettingsViewModel persists and reads
            // AppSettings.LightColorMode, so a bare "light" here silently falls through to Dark the
            // day that value is renamed or recased. App.ApplyPersistedTheme carries the same rule and
            // still spells it out as a literal.
            fe.RequestedTheme = message.Value == AppSettings.LightColorMode
                ? ElementTheme.Light
                : ElementTheme.Dark;
        }
    }

    /// <summary>
    /// Resets the window to the default size when triggered via settings.
    /// </summary>
    [ThreadSafeReceive("Window receivers run on the UI thread that hosts the window — WinUI 3 Window construction and access is by-design UI-thread-only.")]
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

        // HIST-01: synchronous flush of in-memory history snapshot before window teardown (D-01, D-02, D-09, D-14).
        // Snapshot is null when the user has logged out (D-13) or no successful poll has occurred yet -- skip in those cases.
        var snapshot = _historyService.PeekLastSnapshot();
        if (snapshot != null)
        {
            _historyService.SaveHistory(snapshot);
        }

        StopBackgroundServices();
    }

    /// <summary>
    /// Counterpart to App.StartBackgroundServices (finding 29): the window that owns the app's only UI
    /// is the last place that can stop the two singletons owning process-wide resources. Runs after the
    /// history flush so a failure here cannot cost the user their usage curve.
    ///
    /// The ServiceProvider is deliberately NOT disposed instead. Closing runs before the window is torn
    /// down, and MainView's own teardown still resolves IWebViewBridge from the container, so disposing
    /// it here would turn ordinary shutdown into ObjectDisposedException. Of the remaining disposable
    /// singletons only UsageNotificationService does anything on Dispose — it stops its countdown
    /// timers, which the process exit ends anyway.
    /// </summary>
    private void StopBackgroundServices()
    {
        try
        {
            _jsonlService.Stop();
            _updateService.StopPeriodicCheck();
        }
        catch (Exception ex)
        {
            // Closing is a framework event: an escaping exception here would become an unhandled one
            // on the way out of the process.
            AppLog.Write(ShutdownLogSource, ex, "stopping the background services failed");
        }
    }
}
