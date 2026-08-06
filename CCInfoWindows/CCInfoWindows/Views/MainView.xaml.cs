using System.ComponentModel;
using CCInfoWindows.Helpers;
using CCInfoWindows.Messages;
using CCInfoWindows.Services;
using CCInfoWindows.Services.Interfaces;
using CCInfoWindows.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.Web.WebView2.Core;
using CommunityToolkit.Mvvm.Messaging;
using WinUI3Localizer;

namespace CCInfoWindows.Views;

/// <summary>
/// Dashboard view with usage chart, countdowns, and footer toolbar.
/// Hosts a hidden WebView2 for API calls when the bridge isn't already initialized (cold start with saved token).
/// </summary>
public sealed partial class MainView : Page
{
    private const string BootstrapLogSource = "MainView.OnLoaded";
    private const string TeardownLogSource = "MainView.OnUnloaded";
    private const string BridgeLogSource = "MainView.InitializeBridgeAsync";

    /// <summary>Single-segment key — WinUI3Localizer 2.3.0 splits resw keys at the first dot.</summary>
    private const string BootstrapFailureMessageKey = "DashboardStartupFailedMessage";

    /// <summary>
    /// Last resort for the banner text: the localizer host is itself part of the startup that may have
    /// failed, and an error InfoBar with an empty message tells the user nothing.
    /// </summary>
    private const string BootstrapFailureFallbackMessage =
        "The dashboard could not be started. Please restart the app.";

    /// <summary>
    /// Bounds the wait for the bridge WebView2 to finish loading claude.ai. Matches WebViewBridge's
    /// per-request timeout; without it an offline or captive-portal start gates the dashboard forever.
    /// </summary>
    private static readonly TimeSpan BridgeNavigationTimeout = TimeSpan.FromSeconds(30);

    private static readonly TimeSpan ShimmerPulseDuration = TimeSpan.FromSeconds(0.8);
    private const double ShimmerDimOpacity = 0.4;

    private Storyboard? _shimmerStoryboard;
    private bool _stopOnComplete;

    /// <summary>
    /// Cancelled by <see cref="OnUnloaded"/>. The bootstrap below is multi-second on a cold start while the
    /// Settings gear stays enabled, so teardown can win the race; without this an in-flight bootstrap would
    /// finish on a detached ViewModel and start timers nothing can ever stop again.
    /// </summary>
    private CancellationTokenSource? _bootstrapCancellation;

    public MainViewModel ViewModel { get; }

    /// <summary>
    /// Returns Collapsed when value is true, Visible when false.
    /// Used by x:Bind to toggle visibility inverse of IsAggregating.
    /// </summary>
    public static Visibility InvertBool(bool value) =>
        value ? Visibility.Collapsed : Visibility.Visible;

    public MainView()
    {
        ViewModel = App.Services.GetRequiredService<MainViewModel>();
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    /// <summary>
    /// Pulses the statistics skeleton placeholders while a tab aggregates.
    /// </summary>
    private void StartShimmerAnimation()
    {
        StopShimmerAnimation();

        var storyboard = new Storyboard();
        foreach (var placeholder in ShimmerPlaceholders())
        {
            storyboard.Children.Add(CreateShimmerPulse(placeholder));
        }

        _shimmerStoryboard = storyboard;
        storyboard.Begin();
    }

    private void StopShimmerAnimation()
    {
        _shimmerStoryboard?.Stop();
        _shimmerStoryboard = null;
    }

    /// <summary>
    /// The skeleton rectangles of the statistics table, in row order.
    /// </summary>
    private IEnumerable<Border> ShimmerPlaceholders()
    {
        yield return ModelsShimmer;
        yield return InputShimmer;
        yield return OutputShimmer;
        yield return CacheWriteShimmer;
        yield return CacheReadShimmer;
        yield return TotalShimmer;
        yield return CostShimmer;
    }

    /// <summary>
    /// Fades one placeholder in and out forever. Opacity is composition-backed, so the pulse runs off the
    /// UI thread and needs no EnableDependentAnimation — unlike animating the shared ShimmerBaseBrush colour,
    /// which would also freeze on a theme switch and hard-code one theme's palette.
    /// </summary>
    private static DoubleAnimation CreateShimmerPulse(Border placeholder)
    {
        var animation = new DoubleAnimation
        {
            From = 1.0,
            To = ShimmerDimOpacity,
            Duration = new Duration(ShimmerPulseDuration),
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever
        };

        Storyboard.SetTarget(animation, placeholder);
        Storyboard.SetTargetProperty(animation, "Opacity");

        return animation;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Disposed only after the finally below has cleared the field, so OnUnloaded can never
        // cancel through a disposed source.
        using var bootstrap = new CancellationTokenSource();
        _bootstrapCancellation = bootstrap;

        try
        {
            ViewModel.PropertyChanged += OnViewModelPropertyChanged;
            SpinnerStoryboard.Completed += OnSpinnerCompleted;
            ActualThemeChanged += OnActualThemeChanged;
            ViewModel.ApplyTheme(ActualTheme == ElementTheme.Dark);
            WeakReferenceMessenger.Default.Register<ChartInvalidateMessage>(this, (r, m) =>
            {
                ((MainView)r).UsageChart.Invalidate();
            });

            // Always re-bind the bridge to MainView's own long-lived ApiBridgeWebView.
            // After a re-login the bridge still points at LoginView's CoreWebView2, which is
            // destroyed on navigation — IsInitialized stays true (null-check only) while every
            // fetch() times out, surfacing as "API request failed" + an empty org picker.
            var bridge = App.Services.GetRequiredService<IWebViewBridge>();
            var bridgeReady = await InitializeBridgeAsync(bridge, bootstrap.Token);

            if (bootstrap.IsCancellationRequested) return;

            if (!bridgeReady)
            {
                // The dashboard still initializes: local JSONL statistics and the cached usage snapshot
                // remain useful, and the banner tells the user that the live data will not arrive.
                ReportBootstrapFailure("the API bridge never reached claude.ai");
            }

            await ViewModel.InitializeAsync();

            if (bootstrap.IsCancellationRequested)
            {
                // Teardown ran while InitializeAsync was still going, so its StopTimers() call found
                // nothing to stop. Everything InitializeAsync just started would otherwise poll,
                // persist history and raise threshold toasts for the rest of the process lifetime.
                // StopTimers also stops the singleton JSONL and update services, which a newer MainView
                // may already be using — the permanent zombie poller is the worse of the two, and the
                // overlap disappears once service startup moves out of the transient ViewModel.
                ViewModel.StopTimers();
            }
        }
        catch (OperationCanceledException)
        {
            // Teardown won the race before anything was started; OnUnloaded already released the page.
        }
        catch (Exception ex)
        {
            ReportBootstrapFailure("dashboard bootstrap failed", ex);
        }
        finally
        {
            if (ReferenceEquals(_bootstrapCancellation, bootstrap))
            {
                _bootstrapCancellation = null;
            }
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        // Unblocks a bootstrap still waiting for the bridge navigation and makes every later
        // checkpoint in OnLoaded bail out.
        _bootstrapCancellation?.Cancel();

        UsageChart.RemoveFromVisualTree();
        ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        SpinnerStoryboard.Completed -= OnSpinnerCompleted;
        ActualThemeChanged -= OnActualThemeChanged;
        WeakReferenceMessenger.Default.Unregister<ChartInvalidateMessage>(this);
        StopShimmerAnimation();
        ViewModel.StopTimers();

        ReleaseBridgeWebView();
    }

    /// <summary>
    /// Records a bootstrap failure in app.log and raises the dashboard's error banner. The banner text stays
    /// generic per CLAUDE.md — the exception, including its stack, goes to the log only.
    /// </summary>
    private void ReportBootstrapFailure(string reason, Exception? exception = null)
    {
        if (exception is null)
        {
            AppLog.Write(BootstrapLogSource, reason);
        }
        else
        {
            AppLog.Write(BootstrapLogSource, exception, reason);
        }

        ViewModel.ApiErrorMessage = BootstrapFailureMessage();
        ViewModel.HasApiError = true;
    }

    private static string BootstrapFailureMessage()
    {
        try
        {
            var localized = Localizer.Get().GetLocalizedString(BootstrapFailureMessageKey);
            return string.IsNullOrWhiteSpace(localized) ? BootstrapFailureFallbackMessage : localized;
        }
        catch (Exception ex)
        {
            // Localizer.Get() throws when the localizer host never built — which is one of the startup
            // failures this banner exists to report.
            AppLog.Write(BootstrapLogSource, ex, "localized bootstrap-failure text unavailable");
            return BootstrapFailureFallbackMessage;
        }
    }

    /// <summary>
    /// Closes the native browser instance behind the hidden bridge WebView2. Without this every navigation
    /// away orphans a claude.ai renderer until finalization. The bridge is unbound first so it can never be
    /// left holding a closed CoreWebView2.
    /// </summary>
    private void ReleaseBridgeWebView()
    {
        // A null CoreWebView2 means this page never bound the bridge. During the login flow the bridge
        // belongs to LoginView, and resetting it here would break the session it just established.
        if (ApiBridgeWebView.CoreWebView2 is null) return;

        try
        {
            App.Services.GetRequiredService<IWebViewBridge>().Reset();
            ApiBridgeWebView.Close();
        }
        catch (Exception ex)
        {
            // Teardown runs from a framework event: an escaping exception would become an unhandled one.
            AppLog.Write(TeardownLogSource, ex, "releasing the bridge WebView2 failed");
        }
    }

    /// <summary>
    /// Initializes the hidden WebView2, navigates to claude.ai to acquire Cloudflare cookies, then binds it
    /// to the WebViewBridge for API fetch calls. Returns false when the WebView never reached a loaded
    /// claude.ai page; the bridge is then left unbound so requests fail immediately instead of paying a
    /// 30 s timeout each from a Chromium error page.
    /// </summary>
    private async Task<bool> InitializeBridgeAsync(IWebViewBridge bridge, CancellationToken cancellationToken)
    {
        var udfPath = AppPaths.WebView2UserDataFolder;
        Directory.CreateDirectory(udfPath);

        // Idempotent: on a re-login OnLoaded runs again on the same MainView instance, so
        // CoreWebView2 already exists and is already on claude.ai. Re-navigating would leave
        // us awaiting a NavigationCompleted that never fires.
        var alreadyLive = ApiBridgeWebView.CoreWebView2 is not null;
        if (!alreadyLive)
        {
            var env = await CoreWebView2Environment.CreateWithOptionsAsync(
                browserExecutableFolder: null,
                userDataFolder: udfPath,
                options: null);
            await ApiBridgeWebView.EnsureCoreWebView2Async(env);

            if (!await NavigateBridgeToClaudeAsync(cancellationToken))
            {
                return false;
            }
        }

        var coreWebView = ApiBridgeWebView.CoreWebView2;
        if (coreWebView is null)
        {
            AppLog.Write(BridgeLogSource, "CoreWebView2 unavailable after initialization -- bridge left unbound");
            return false;
        }

        bridge.Initialize(coreWebView, DispatcherQueue.GetForCurrentThread());
        return true;
    }

    /// <summary>
    /// Navigates the bridge WebView2 to claude.ai and reports whether the page actually loaded.
    /// </summary>
    private async Task<bool> NavigateBridgeToClaudeAsync(CancellationToken cancellationToken)
    {
        var navigation = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        void OnNavigationCompleted(WebView2 sender, CoreWebView2NavigationCompletedEventArgs args)
        {
            if (!args.IsSuccess)
            {
                AppLog.Write(BridgeLogSource, $"navigation failed: {args.WebErrorStatus}");
            }

            navigation.TrySetResult(args.IsSuccess);
        }

        ApiBridgeWebView.NavigationCompleted += OnNavigationCompleted;
        try
        {
            ApiBridgeWebView.CoreWebView2!.Navigate(ClaudeAiUrlPolicy.Origin);
            return await navigation.Task.WaitAsync(BridgeNavigationTimeout, cancellationToken);
        }
        catch (TimeoutException)
        {
            AppLog.Write(
                BridgeLogSource,
                $"navigation did not complete within {BridgeNavigationTimeout.TotalSeconds:F0} s");
            return false;
        }
        finally
        {
            ApiBridgeWebView.NavigationCompleted -= OnNavigationCompleted;
        }
    }

    /// <summary>
    /// The three progress-bar foregrounds are theme-dependent brushes computed in the ViewModel: an
    /// x:Bind converter could not re-run on a theme toggle, so the bars kept the palette of whichever
    /// theme was active when the last poll landed. The chart reads ActualTheme in its draw handler,
    /// so it only needs the invalidate.
    /// </summary>
    private void OnActualThemeChanged(FrameworkElement sender, object args)
    {
        ViewModel.ApplyTheme(ActualTheme == ElementTheme.Dark);
        UsageChart.Invalidate();
    }

    private void UsageChart_Draw(CanvasControl sender, CanvasDrawEventArgs args)
    {
        var session = args.DrawingSession;
        var width = (float)sender.ActualWidth;
        var height = (float)sender.ActualHeight;
        var plotWidth = width - ChartRenderer.LeftMargin;
        var plotHeight = height - ChartRenderer.BottomMargin - ChartRenderer.TopMargin;
        var isDark = ActualTheme == ElementTheme.Dark;

        ChartDrawing.DrawAxesAndLabels(session, plotWidth, plotHeight, isDark);

        var points = ViewModel.UsageHistoryPoints;
        if (points.Count == 0) return;

        var windowStart = ViewModel.FiveHourWindowStart;
        if (windowStart == null) return;

        ChartDrawing.DrawChart(session, sender, points, windowStart.Value, plotWidth, plotHeight, isDark);
    }

    private void OnUpdateInfoBarClosing(InfoBar sender, InfoBarClosingEventArgs args)
    {
        ViewModel.DismissUpdate();
    }

    /// <summary>
    /// DROPDOWN-05 / D-04 / CD-02: when the user dismisses the migration toast,
    /// invoke the VM command which persists SessionVisibilityMigrationShown = true synchronously.
    /// Closed (not Closing) fires AFTER the InfoBar collapses; TwoWay binding on IsOpen
    /// keeps the VM in sync, but persistence requires the explicit command call.
    /// </summary>
    private void OnMigrationToastClosed(InfoBar sender, InfoBarClosedEventArgs args)
    {
        if (ViewModel.DismissMigrationToastCommand.CanExecute(null))
        {
            ViewModel.DismissMigrationToastCommand.Execute(null);
        }
    }

    private void OnSpinnerCompleted(object? sender, object e)
    {
        if (_stopOnComplete)
        {
            _stopOnComplete = false;
        }
        else
        {
            SpinnerStoryboard.Begin();
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.Equals(e.PropertyName, nameof(MainViewModel.IsRefreshing), StringComparison.Ordinal))
        {
            if (ViewModel.IsRefreshing)
            {
                _stopOnComplete = false;
                SpinnerStoryboard.Begin();
            }
            else
            {
                _stopOnComplete = true;
            }
        }
        else if (string.Equals(e.PropertyName, nameof(MainViewModel.IsAggregating), StringComparison.Ordinal))
        {
            if (ViewModel.IsAggregating)
            {
                StartShimmerAnimation();
            }
            else
            {
                StopShimmerAnimation();
            }
        }
    }

    /// <summary>
    /// RENAME-01 / D-03: Opens the session rename ContentDialog.
    /// Pure view-side concern: ContentDialog requires XamlRoot which is only available here.
    /// All persistence logic delegates to ViewModel methods.
    /// </summary>
    private async void OnRenamePencilClicked(object sender, RoutedEventArgs e)
    {
        var selected = ViewModel.SelectedSession;
        if (selected == null) return;

        var sessionId = selected.Session.Id;
        var currentDisplayName = selected.DisplayName;
        var hasCustomName = ViewModel.HasCustomName(sessionId);

        var textBox = new TextBox
        {
            Text = currentDisplayName,
            MaxLength = 100,
            AcceptsReturn = false
        };

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = Localizer.Get().GetLocalizedString("RenameSessionDialogTitle"),
            PrimaryButtonText = Localizer.Get().GetLocalizedString("RenameSessionDialogSaveButton"),
            SecondaryButtonText = Localizer.Get().GetLocalizedString("RenameSessionDialogCancelButton"),
            CloseButtonText = hasCustomName
                ? Localizer.Get().GetLocalizedString("RenameSessionDialogResetButton")
                : string.Empty,
            DefaultButton = ContentDialogButton.Primary,
            Content = textBox
        };

        // Disable Save when TextBox contains only whitespace
        textBox.TextChanged += (_, _) =>
        {
            dialog.IsPrimaryButtonEnabled = !string.IsNullOrWhiteSpace(textBox.Text);
        };
        dialog.IsPrimaryButtonEnabled = !string.IsNullOrWhiteSpace(textBox.Text);

        var result = await dialog.ShowAsync();

        if (result == ContentDialogResult.Primary)
        {
            await ViewModel.SaveCustomNameAsync(sessionId, textBox.Text);
        }
        else if (result == ContentDialogResult.None && hasCustomName)
        {
            // CloseButton acts as Reset (only shown when a custom name exists)
            await ViewModel.ClearCustomNameAsync(sessionId);
        }
        // Secondary (Cancel) — no-op
    }
}
