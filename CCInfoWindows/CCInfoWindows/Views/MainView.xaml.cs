using System.ComponentModel;
using System.Diagnostics;
using CCInfoWindows.Helpers;
using CCInfoWindows.Messages;
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
using Windows.UI;

namespace CCInfoWindows.Views;

/// <summary>
/// Dashboard view with usage chart, countdowns, and footer toolbar.
/// Hosts a hidden WebView2 for API calls when the bridge isn't already initialized (cold start with saved token).
/// </summary>
public sealed partial class MainView : Page
{
    private static readonly Color ShimmerBaseColor = Color.FromArgb(0xFF, 0x38, 0x38, 0x3A);
    private static readonly Color ShimmerHighlightColor = Color.FromArgb(0xFF, 0x55, 0x55, 0x58);

    private Storyboard? _shimmerStoryboard;
    private bool _stopOnComplete;

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

    private void StartShimmerAnimation()
    {
        _shimmerStoryboard?.Stop();

        var shimmerBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(ShimmerBaseColor);

        var animation = new ColorAnimation
        {
            From = ShimmerBaseColor,
            To = ShimmerHighlightColor,
            Duration = new Duration(TimeSpan.FromSeconds(0.8)),
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EnableDependentAnimation = true
        };

        Storyboard.SetTarget(animation, shimmerBrush);
        Storyboard.SetTargetProperty(animation, "Color");

        _shimmerStoryboard = new Storyboard();
        _shimmerStoryboard.Children.Add(animation);
        _shimmerStoryboard.Begin();
    }

    private void StopShimmerAnimation()
    {
        _shimmerStoryboard?.Stop();
        _shimmerStoryboard = null;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            ViewModel.PropertyChanged += OnViewModelPropertyChanged;
            SpinnerStoryboard.Completed += OnSpinnerCompleted;
            WeakReferenceMessenger.Default.Register<ChartInvalidateMessage>(this, (r, m) =>
            {
                ((MainView)r).UsageChart.Invalidate();
            });

            var bridge = App.Services.GetRequiredService<IWebViewBridge>();
            if (!bridge.IsInitialized)
            {
                await InitializeBridgeAsync(bridge);
            }

            await ViewModel.InitializeAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MainView] OnLoaded failed: {ex.Message}");
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        UsageChart.RemoveFromVisualTree();
        ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        SpinnerStoryboard.Completed -= OnSpinnerCompleted;
        WeakReferenceMessenger.Default.Unregister<ChartInvalidateMessage>(this);
        ViewModel.StopTimers();
    }

    /// <summary>
    /// Initializes a hidden WebView2, navigates to claude.ai to acquire Cloudflare cookies,
    /// then binds it to the WebViewBridge for API fetch calls.
    /// </summary>
    private async Task InitializeBridgeAsync(IWebViewBridge bridge)
    {
        var udfPath = Helpers.AppPaths.WebView2UserDataFolder;
        Directory.CreateDirectory(udfPath);

        var env = await CoreWebView2Environment.CreateWithOptionsAsync(
            browserExecutableFolder: null,
            userDataFolder: udfPath,
            options: null);
        await ApiBridgeWebView.EnsureCoreWebView2Async(env);

        var tcs = new TaskCompletionSource();
        void handler(object s, object args)
        {
            tcs.TrySetResult();
            ApiBridgeWebView.NavigationCompleted -= handler;
        }
        ApiBridgeWebView.NavigationCompleted += handler;
        ApiBridgeWebView.CoreWebView2.Navigate("https://claude.ai");
        await tcs.Task;

        bridge.Initialize(ApiBridgeWebView.CoreWebView2, DispatcherQueue.GetForCurrentThread());
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

        ChartDrawing.DrawChartFills(session, sender, points, windowStart.Value, plotWidth, plotHeight, isDark);
        ChartDrawing.DrawChartTopLine(session, sender, points, windowStart.Value, plotWidth, plotHeight, isDark);
        ChartDrawing.DrawGlowIndicator(session, sender, points, windowStart.Value, plotWidth, plotHeight, isDark);
    }

    private void OnUpdateInfoBarClosing(InfoBar sender, InfoBarClosingEventArgs args)
    {
        ViewModel.DismissUpdate();
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
}
