using System.Collections.Specialized;
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
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.Web.WebView2.Core;
using CommunityToolkit.Mvvm.Messaging;
using Windows.Foundation;
using VirtualKey = Windows.System.VirtualKey;
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
    private const string IconButtonLabelLogSource = "MainView.ApplyIconButtonLabels";

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

        // handledEventsToo: the ScrollViewer inside the card handles pointer events itself, so
        // XAML attribute handlers on the card would never see them. See
        // OnWorkflowTooltipPointerPressed.
        WorkflowTooltipOverlay.AddHandler(
            PointerPressedEvent, new PointerEventHandler(OnWorkflowTooltipPointerPressed), true);
        WorkflowTooltipOverlay.AddHandler(
            PointerMovedEvent, new PointerEventHandler(OnWorkflowTooltipPointerMoved), true);
        WorkflowTooltipOverlay.AddHandler(
            PointerReleasedEvent, new PointerEventHandler(OnWorkflowTooltipPointerReleased), true);
        WorkflowTooltipOverlay.AddHandler(
            PointerCaptureLostEvent, new PointerEventHandler(OnWorkflowTooltipPointerReleased), true);
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

    /// <summary>
    /// Names the icon-only buttons, which carry a glyph and no text of their own. The rule itself
    /// lives in <see cref="IconLabel"/>, shared with SettingsView's tab strip — the two
    /// hand-written copies had drifted, and only this one set an accessible name.
    ///
    /// Runs before the bootstrap: a dashboard that failed to load still needs a reachable
    /// Settings button.
    ///
    /// No language-switch re-apply hook, unlike SettingsView: MainView carries no
    /// NavigationCacheMode, so the only place a language can change — the Settings page — has
    /// replaced this page by the time the switch happens, and OnLoaded runs again on the way back.
    /// </summary>
    private void ApplyIconButtonLabels()
    {
        SetIconButtonLabel(RenameSessionButton, "MainViewRenameLabel", "Rename session");
        SetIconButtonLabel(ExportChartButton, "MainViewExportLabel", "Export chart");
        SetIconButtonLabel(FooterRefreshButton, "MainViewRefreshLabel", "Refresh");
        SetIconButtonLabel(FooterSettingsButton, "MainViewSettingsLabel", "Settings");
        SetIconButtonLabel(FooterQuitButton, "MainViewQuitLabel", "Quit");
    }

    /// <summary>
    /// Kept as a named local step rather than inlining <see cref="IconLabel.Apply"/> at the five call
    /// sites: FooterLocalizationTests pins each (button, key) pair by this call's source text.
    /// </summary>
    private static void SetIconButtonLabel(DependencyObject button, string uid, string fallback) =>
        IconLabel.Apply(button, uid, fallback, IconButtonLabelLogSource);

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        ApplyIconButtonLabels();

        // Disposed only after the finally below has cleared the field, so OnUnloaded can never
        // cancel through a disposed source.
        using var bootstrap = new CancellationTokenSource();
        _bootstrapCancellation = bootstrap;

        try
        {
            ViewModel.PropertyChanged += OnViewModelPropertyChanged;
            ViewModel.SubagentContexts.CollectionChanged += OnSubagentContextsChanged;
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
                // Safe unconditionally since finding 29: StopTimers now only releases what this
                // ViewModel owns, so a newer MainView's JSONL watcher and update schedule — started
                // once by the app host — cannot be torn down by this instance's late teardown.
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
        ViewModel.SubagentContexts.CollectionChanged -= OnSubagentContextsChanged;
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

        // The shared rule also rejects an echoed uid, which this copy accepted: an unbuilt localizer —
        // one of the very startup failures this banner reports — hands back the key it was asked for,
        // so the InfoBar would have read "DashboardStartupFailedMessage" (finding 30).
        ViewModel.ApiErrorMessage = LocalizedText.Resolve(
            BootstrapFailureMessageKey, BootstrapFailureFallbackMessage, BootstrapLogSource);
        ViewModel.HasApiError = true;
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
        // Idempotent: on a re-login OnLoaded runs again on the same MainView instance, so
        // CoreWebView2 already exists and is already on claude.ai. Re-navigating would leave
        // us awaiting a NavigationCompleted that never fires.
        var alreadyLive = ApiBridgeWebView.CoreWebView2 is not null;
        if (!alreadyLive)
        {
            // Shared with LoginViewModel: same user data folder, same options, and now the same
            // delete-and-retry recovery — a corrupted profile used to heal on the login path and
            // fail forever on a cold start with a saved token.
            await WebView2Bootstrap.EnsureAsync(ApiBridgeWebView, BridgeLogSource);

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

    // -------------------------------------------------------------------------
    // Workflow hover card
    //
    // Windows-only, like the rest of the workflow display — full note on
    // SubagentContextData.WorkflowId. This is positioning code, not application logic:
    // it converts a row's position in the visual tree into a canvas offset, which is
    // exactly the kind of work that cannot move to a ViewModel (CLAUDE.md's no-logic-in-
    // code-behind rule is about behaviour, and TransformToVisual has no ViewModel-side
    // equivalent). Everything the card DISPLAYS is composed in MainViewModel.
    // -------------------------------------------------------------------------

    /// <summary>Constant x offset from the window's left edge — the card never moves sideways.</summary>
    private const double TooltipLeftMargin = 10;

    /// <summary>Narrowest the card may render; below this its content scrolls instead of shrinking.</summary>
    private const double TooltipMinWidth = 340;

    private const double TooltipMaxWidth = 360;

    /// <summary>
    /// Padding (14 + 14) and border (2 + 2) between the card's edge and its content. Must be kept in
    /// step with the card's Padding and BorderThickness in MainView.xaml — it is what turns the
    /// card's outer width into the width available to the content, and so decides both the wrap
    /// point and when the scrollbar appears.
    /// </summary>
    private const double TooltipChromeWidth = 32;

    /// <summary>Where the card wants to sit, before containment clamps it.</summary>
    /// <summary>One arrow-key press worth of horizontal scroll across the hover card.</summary>
    private const double TooltipKeyboardScrollStep = 40;


    private double _tooltipDesiredTop;

    /// <summary>
    /// Run id of the card currently on screen, or null when it is closed. Compared as a STRING and
    /// not by row identity: every poll replaces the row objects, so an identity check would stop
    /// matching the moment a poll ran between opening the card and retiring its run — which is the
    /// normal case, not the rare one.
    /// </summary>
    private string? _openTooltipRunId;


    private bool _isPanningTooltip;
    private double _panStartX;
    private double _panStartOffset;

    private void OnWorkflowRowPointerEntered(object sender, PointerRoutedEventArgs e) =>
        ShowWorkflowTooltip(sender);

    /// <summary>
    /// Keyboard counterpart of the hover. Nothing extra to do: the opener never read the pointer
    /// position — the card is placed off the ROW's own geometry — so focus and hover open the same
    /// card in the same place.
    /// </summary>
    private void OnWorkflowRowGotFocus(object sender, RoutedEventArgs e) =>
        ShowWorkflowTooltip(sender);

    private void OnWorkflowRowLostFocus(object sender, RoutedEventArgs e) =>
        HideWorkflowTooltip();

    /// <summary>
    /// Enter and Space open the card, Escape closes it, Left/Right scroll it.
    ///
    /// The scrolling is not a nicety: a card wider than the window is read by scrolling it, and the
    /// mouse gets two ways to do that (wheel, middle-button pan) while the keyboard had none — the
    /// run name and description would stay cut off for exactly the users this change is for.
    /// Handled is set so the arrows do not also move focus out of the row.
    /// </summary>
    private void OnWorkflowRowKeyDown(object sender, KeyRoutedEventArgs e)
    {
        // Enter and Space (re)open it, and this branch sits BEFORE the visibility guard because it is
        // the one key that has to work while the card is closed. Measured in the running app: Escape
        // closes the card while the row KEEPS focus, so no further GotFocus is ever raised for it —
        // without a reopen key a keyboard user would have to tab away and back to see it again.
        if (e.Key is VirtualKey.Enter or VirtualKey.Space)
        {
            ShowWorkflowTooltip(sender);
            e.Handled = true;
            return;
        }

        if (WorkflowTooltipOverlay.Visibility != Visibility.Visible)
            return;

        switch (e.Key)
        {
            case VirtualKey.Escape:
                HideWorkflowTooltip();
                e.Handled = true;
                break;

            case VirtualKey.Left:
            case VirtualKey.Right:
                var delta = e.Key == VirtualKey.Left ? -TooltipKeyboardScrollStep : TooltipKeyboardScrollStep;
                WorkflowTooltipScroll.ChangeView(
                    WorkflowTooltipScroll.HorizontalOffset + delta, null, null, disableAnimation: true);
                e.Handled = true;
                break;
        }
    }

    /// <summary>
    /// Closes the card when the run behind it is retired.
    ///
    /// Only on Remove, and this is the whole point: a poll clears and refills the list, which raises
    /// Reset — closing on that made the card blink away every few seconds while it was being read.
    /// MainViewModel.RetireStaleRows uses RemoveAt, so a retired run arrives here as a Remove with
    /// the row in OldItems, and the two cases finally tell themselves apart.
    /// </summary>
    private void OnSubagentContextsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action != NotifyCollectionChangedAction.Remove || _openTooltipRunId is null || e.OldItems is null)
            return;

        foreach (var removed in e.OldItems)
        {
            if (removed is SubagentDisplayData row
                && string.Equals(row.AgentId, _openTooltipRunId, StringComparison.Ordinal))
            {
                HideWorkflowTooltip();
                return;
            }
        }
    }

    /// <summary>
    /// Opens the card under a workflow row, for pointer and keyboard alike.
    ///
    /// The row hands over its content through Tag rather than DataContext: ItemsRepeater's
    /// generated containers are bound with x:Bind, so relying on an inherited DataContext here
    /// would be relying on a detail of the repeater rather than on something the template states.
    /// </summary>
    private void ShowWorkflowTooltip(object sender)
    {
        if (sender is not FrameworkElement row
            || row.Tag is not SubagentDisplayData data
            || data.Tooltip is not { } tooltip)
            return;

        WorkflowTooltipOverlay.DataContext = tooltip;
        _openTooltipRunId = data.AgentId;

        // Symmetric margins: the card is contained by construction (the Canvas is clipped to the
        // window), but a card flush against the right edge reads as clipped even when it is not.
        var cardWidth = Math.Clamp(TooltipLayer.ActualWidth - (2 * TooltipLeftMargin), 0, TooltipMaxWidth);
        WorkflowTooltipOverlay.Width = cardWidth;

        // The content never goes below the width a 340-wide card would give it. When the window
        // cannot supply that, the card shrinks with the window but the content does not — which is
        // what puts the horizontal scrollbar on screen instead of quietly cutting the text off.
        WorkflowTooltipContent.Width = Math.Max(TooltipMinWidth, cardWidth) - TooltipChromeWidth;

        // Measured in the overlay's own coordinate space, so the value already accounts for the
        // scroll position of the panel the row lives in. Flush against the row's bottom edge, with
        // no gap: the pointer has to be able to travel from row to card without crossing a strip
        // that belongs to neither, or the card would close on its way to its own scrollbar.
        _tooltipDesiredTop = row.TransformToVisual(TooltipLayer)
            .TransformPoint(new Point(0, row.ActualHeight)).Y;

        // ActualHeight is still last hover's until this one lays out; OnWorkflowTooltipSizeChanged
        // corrects it. Setting it here too keeps a repeat hover of the same-sized card — which
        // raises no SizeChanged at all — from opening at the previous row's offset.
        Canvas.SetTop(WorkflowTooltipOverlay, ClampTooltipTop(WorkflowTooltipOverlay.ActualHeight));
        WorkflowTooltipOverlay.Visibility = Visibility.Visible;
    }

    private void OnWorkflowTooltipSizeChanged(object sender, SizeChangedEventArgs e)
    {
        Canvas.SetTop(WorkflowTooltipOverlay, ClampTooltipTop(e.NewSize.Height));
        UpdateTooltipScrollbar();
    }

    private void OnWorkflowTooltipViewChanged(object? sender, ScrollViewerViewChangedEventArgs e) =>
        UpdateTooltipScrollbar();

    /// <summary>
    /// Sizes and places the hand-built scroll thumb, and hides the whole track when the content
    /// fits. See the XAML for why the framework's own scrollbar is not used.
    /// </summary>
    private void UpdateTooltipScrollbar()
    {
        var scrollable = WorkflowTooltipScroll.ScrollableWidth;

        // Sub-pixel overflow is rounding noise, not something to offer a scrollbar for.
        if (scrollable <= 1)
        {
            TooltipScrollTrack.Visibility = Visibility.Collapsed;
            return;
        }

        TooltipScrollTrack.Visibility = Visibility.Visible;

        var trackWidth = TooltipScrollTrack.ActualWidth;
        var extent = WorkflowTooltipScroll.ExtentWidth;
        if (trackWidth <= 0 || extent <= 0)
            return;

        // Floor of 24 px: proportional sizing alone makes the thumb a few pixels wide once the
        // content is several times the viewport, at which point it is neither visible nor grabbable.
        var thumbWidth = Math.Clamp(trackWidth * WorkflowTooltipScroll.ViewportWidth / extent, 24, trackWidth);
        TooltipScrollThumb.Width = thumbWidth;
        TooltipScrollThumb.Margin = new Thickness(
            (trackWidth - thumbWidth) * (WorkflowTooltipScroll.HorizontalOffset / scrollable), 0, 0, 0);
    }

    private void OnTooltipScrollTrackPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        TooltipScrollTrack.CapturePointer(e.Pointer);
        ScrollToTrackPosition(e);
    }

    private void OnTooltipScrollTrackPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (e.Pointer.IsInContact)
            ScrollToTrackPosition(e);
    }

    private void OnTooltipScrollTrackPointerReleased(object sender, PointerRoutedEventArgs e) =>
        TooltipScrollTrack.ReleasePointerCapture(e.Pointer);

    /// <summary>
    /// Jump-and-drag in one: pressing anywhere on the track centres the thumb there, and holding
    /// keeps following the pointer. Cheaper than a separate thumb-drag path, and a click on the
    /// track scrolling to that spot is what a user expects anyway.
    /// </summary>
    private void ScrollToTrackPosition(PointerRoutedEventArgs e)
    {
        var travel = TooltipScrollTrack.ActualWidth - TooltipScrollThumb.ActualWidth;
        if (travel <= 0)
            return;

        var x = e.GetCurrentPoint(TooltipScrollTrack).Position.X - (TooltipScrollThumb.ActualWidth / 2);
        var fraction = Math.Clamp(x / travel, 0, 1);

        WorkflowTooltipScroll.ChangeView(
            fraction * WorkflowTooltipScroll.ScrollableWidth, null, null, disableAnimation: true);
    }

    /// <summary>
    /// "Below the row" holds until the card would run past the bottom of the window, at which point
    /// containment wins and it slides up by just enough to fit. A card taller than the window is
    /// pinned to the top — its own scrollbar is horizontal only, so there is nothing better to do
    /// than show the beginning of it.
    /// </summary>
    private double ClampTooltipTop(double cardHeight)
    {
        var lowestFittingTop = TooltipLayer.ActualHeight - cardHeight;
        return lowestFittingTop <= 0 ? 0 : Math.Clamp(_tooltipDesiredTop, 0, lowestFittingTop);
    }

    /// <summary>
    /// The card sits flush under the row, so a pointer leaving the row downwards is usually
    /// entering the card — and the card has to stay reachable, because its horizontal scrollbar is
    /// the only way to read a card wider than the window. Hit-testing the new position beats a
    /// close-and-reopen timer: no delay, no flicker, no state to unwind.
    /// </summary>
    private void OnWorkflowRowPointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (IsPointerOverTooltip(e))
            return;

        HideWorkflowTooltip();
    }

    private void OnWorkflowTooltipPointerExited(object sender, PointerRoutedEventArgs e)
    {
        // A drag that runs past the card's edge must not close the thing being dragged.
        if (_isPanningTooltip)
            return;

        HideWorkflowTooltip();
    }

    /// <summary>
    /// Middle-button drag-to-pan across the card.
    ///
    /// Registered with AddHandler(..., handledEventsToo: true) rather than as XAML attributes: the
    /// ScrollViewer between the card and the pointer marks pointer events handled as part of its own
    /// input processing, and a plain PointerPressed="..." attribute on the card never fires.
    ///
    /// The content follows the pointer — dragging left pulls the text left and reveals what is off
    /// to the right, the same direction as grabbing a sheet of paper. The framework offers nothing
    /// here: ScrollViewer has no middle-button panning, and its touch panning modes do not apply to
    /// a mouse.
    /// </summary>
    private void OnWorkflowTooltipPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(WorkflowTooltipOverlay);
        if (!point.Properties.IsMiddleButtonPressed || WorkflowTooltipScroll.ScrollableWidth <= 0)
            return;

        _isPanningTooltip = true;
        _panStartX = point.Position.X;
        _panStartOffset = WorkflowTooltipScroll.HorizontalOffset;
        WorkflowTooltipOverlay.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void OnWorkflowTooltipPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_isPanningTooltip)
            return;

        var x = e.GetCurrentPoint(WorkflowTooltipOverlay).Position.X;
        WorkflowTooltipScroll.ChangeView(
            _panStartOffset - (x - _panStartX), null, null, disableAnimation: true);
        e.Handled = true;
    }

    private void OnWorkflowTooltipPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_isPanningTooltip)
            return;

        _isPanningTooltip = false;
        WorkflowTooltipOverlay.ReleasePointerCapture(e.Pointer);
        e.Handled = true;
    }

    private bool IsPointerOverTooltip(PointerRoutedEventArgs e)
    {
        if (WorkflowTooltipOverlay.Visibility != Visibility.Visible)
            return false;

        var position = e.GetCurrentPoint(TooltipLayer).Position;
        var left = Canvas.GetLeft(WorkflowTooltipOverlay);
        var top = Canvas.GetTop(WorkflowTooltipOverlay);

        return position.X >= left
               && position.X <= left + WorkflowTooltipOverlay.ActualWidth
               && position.Y >= top
               && position.Y <= top + WorkflowTooltipOverlay.ActualHeight;
    }

    /// <summary>
    /// Deliberately NOT wired to the subagent list being rebuilt, though the temptation is real:
    /// this card outlives its row, where a ToolTip died with the element it hung off. But every
    /// poll clears and refills that list, so closing on the rebuild made the card blink away every
    /// few seconds while it was being read — worse than the case it guarded against.
    ///
    /// A run going stale under a resting pointer used to leave the card standing with the dead
    /// run's snapshot, because no PointerExited is ever raised for a row that vanished. That is
    /// what OnSubagentContextsChanged closes: MainViewModel.RetireStaleRows removes the row, the
    /// Remove carries it, and the card recognises its own run by id.
    /// </summary>
    private void HideWorkflowTooltip()
    {
        _isPanningTooltip = false;
        WorkflowTooltipOverlay.Visibility = Visibility.Collapsed;
        WorkflowTooltipOverlay.DataContext = null;
        _openTooltipRunId = null;
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
