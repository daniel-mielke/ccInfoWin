using System.ComponentModel;
using CCInfoWindows.Helpers;
using CCInfoWindows.Models;
using CCInfoWindows.Services;
using CCInfoWindows.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Brushes;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using Windows.UI;

namespace CCInfoWindows.Views;

/// <summary>
/// Dashboard view with usage chart, countdowns, and footer toolbar.
/// Hosts a hidden WebView2 for API calls when the bridge isn't already initialized (cold start with saved token).
/// </summary>
public sealed partial class MainView : Page
{
    private static readonly CanvasStrokeStyle DashStrokeStyle = new()
    {
        CustomDashStyle = [4f, 4f]
    };

    private static readonly CanvasTextFormat AxisLabelFormat = new()
    {
        FontFamily = "Segoe UI Variable",
        FontSize = 10f
    };

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
        ViewModel.ChartInvalidateCallback = () => UsageChart.Invalidate();

        var bridge = App.Services.GetRequiredService<WebViewBridge>();
        if (!bridge.IsInitialized)
        {
            await InitializeBridgeAsync(bridge);
        }

        await ViewModel.InitializeAsync();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        UsageChart.RemoveFromVisualTree();
        ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        ViewModel.StopTimers();
    }

    /// <summary>
    /// Initializes a hidden WebView2, navigates to claude.ai to acquire Cloudflare cookies,
    /// then binds it to the WebViewBridge for API fetch calls.
    /// </summary>
    private async Task InitializeBridgeAsync(WebViewBridge bridge)
    {
        var udfPath = LoginViewModel.UserDataFolderPath;
        Directory.CreateDirectory(udfPath);

        var env = await CoreWebView2Environment.CreateWithOptionsAsync(
            browserExecutableFolder: null,
            userDataFolder: udfPath,
            options: null);
        await ApiBridgeWebView.EnsureCoreWebView2Async(env);

        var tcs = new TaskCompletionSource();
        ApiBridgeWebView.NavigationCompleted += (s, args) => tcs.TrySetResult();
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

        DrawAxesAndLabels(session, plotWidth, plotHeight, isDark);

        var points = ViewModel.UsageHistoryPoints;
        if (points.Count == 0) return;

        var windowStart = ViewModel.FiveHourWindowStart;
        if (windowStart == null) return;

        DrawChartFills(session, sender, points, windowStart.Value, plotWidth, plotHeight, isDark);
        DrawChartTopLine(session, sender, points, windowStart.Value, plotWidth, plotHeight, isDark);
        DrawGlowIndicator(session, sender, points, windowStart.Value, plotWidth, plotHeight, isDark);
    }

    private void DrawAxesAndLabels(CanvasDrawingSession session, float plotWidth, float plotHeight, bool isDark)
    {
        var thresholdColor = ChartColors.GetColor("ThresholdBrush", isDark);
        var labelColor = ChartColors.GetColor("AxisLabelBrush", isDark);
        var lineStart = ChartRenderer.LeftMargin;
        var lineEnd = ChartRenderer.LeftMargin + plotWidth;

        // Dashed threshold lines at 0%, 50%, and 100%
        var y0 = ChartRenderer.ToY(0.0, plotHeight);
        var y50 = ChartRenderer.ToY(0.5, plotHeight);
        var y100 = ChartRenderer.ToY(1.0, plotHeight);
        session.DrawLine(lineStart, y0, lineEnd, y0, thresholdColor, 1f, DashStrokeStyle);
        session.DrawLine(lineStart, y50, lineEnd, y50, thresholdColor, 1f, DashStrokeStyle);
        session.DrawLine(lineStart, y100, lineEnd, y100, thresholdColor, 1f, DashStrokeStyle);

        // Y-axis labels (offset upward so text baseline aligns with line)
        session.DrawText("100%", 0, y100 - 6f, labelColor, AxisLabelFormat);
        session.DrawText("50%", 0, y50 - 6f, labelColor, AxisLabelFormat);
        session.DrawText("0%", 0, y0 - 6f, labelColor, AxisLabelFormat);

        // X-axis labels: 0h through 5h
        for (var hour = 0; hour <= 5; hour++)
        {
            var xRatio = hour / 5f;
            var x = ChartRenderer.LeftMargin + (xRatio * plotWidth);
            // Shift last label left so it doesn't clip beyond the canvas edge
            if (hour == 5) x -= 14f;
            session.DrawText($"{hour}h", x, y0 + 2f, labelColor, AxisLabelFormat);
        }
    }

    private static void DrawChartFills(
        CanvasDrawingSession session,
        ICanvasResourceCreator resourceCreator,
        IReadOnlyList<UsageHistoryPoint> points,
        DateTimeOffset windowStart,
        float plotWidth,
        float plotHeight,
        bool isDark)
    {
        var baselineY = ChartRenderer.ToY(0.0, plotHeight);
        var segments = ChartRenderer.GetZoneSegments(points);
        foreach (var (startIndex, endIndex, brushKey) in segments)
        {
            var zoneColor = ChartColors.GetColor(brushKey, isDark);
            var fillColor = Color.FromArgb(102, zoneColor.R, zoneColor.G, zoneColor.B);
            var rightEdgeX = ChartRenderer.GetRightEdgeAbsoluteX(points, endIndex, windowStart, plotWidth);

            using var pathBuilder = new CanvasPathBuilder(resourceCreator);
            var firstX = ChartRenderer.LeftMargin + ChartRenderer.ToX(points[startIndex].Timestamp, windowStart, plotWidth);
            pathBuilder.BeginFigure(firstX, baselineY);

            for (var i = startIndex; i <= endIndex; i++)
            {
                var x = ChartRenderer.LeftMargin + ChartRenderer.ToX(points[i].Timestamp, windowStart, plotWidth);
                var y = ChartRenderer.ToY(points[i].Utilization, plotHeight);

                if (i == startIndex)
                {
                    pathBuilder.AddLine(x, y);
                }
                else
                {
                    var prevY = ChartRenderer.ToY(points[i - 1].Utilization, plotHeight);
                    pathBuilder.AddLine(x, prevY);
                    pathBuilder.AddLine(x, y);
                }
            }

            var lastY = ChartRenderer.ToY(points[endIndex].Utilization, plotHeight);
            pathBuilder.AddLine(rightEdgeX, lastY);
            pathBuilder.AddLine(rightEdgeX, baselineY);
            pathBuilder.EndFigure(CanvasFigureLoop.Closed);

            using var geometry = CanvasGeometry.CreatePath(pathBuilder);
            session.FillGeometry(geometry, fillColor);
        }
    }

    private static void DrawChartTopLine(
        CanvasDrawingSession session,
        ICanvasResourceCreator resourceCreator,
        IReadOnlyList<UsageHistoryPoint> points,
        DateTimeOffset windowStart,
        float plotWidth,
        float plotHeight,
        bool isDark)
    {
        var segments = ChartRenderer.GetZoneSegments(points);
        foreach (var (startIndex, endIndex, brushKey) in segments)
        {
            var zoneColor = ChartColors.GetColor(brushKey, isDark);
            var rightEdgeX = ChartRenderer.GetRightEdgeAbsoluteX(points, endIndex, windowStart, plotWidth);

            using var pathBuilder = new CanvasPathBuilder(resourceCreator);
            var firstX = ChartRenderer.LeftMargin + ChartRenderer.ToX(points[startIndex].Timestamp, windowStart, plotWidth);
            var firstY = ChartRenderer.ToY(points[startIndex].Utilization, plotHeight);
            pathBuilder.BeginFigure(firstX, firstY);

            for (var i = startIndex + 1; i <= endIndex; i++)
            {
                var x = ChartRenderer.LeftMargin + ChartRenderer.ToX(points[i].Timestamp, windowStart, plotWidth);
                var y = ChartRenderer.ToY(points[i].Utilization, plotHeight);
                var prevY = ChartRenderer.ToY(points[i - 1].Utilization, plotHeight);
                pathBuilder.AddLine(x, prevY);
                pathBuilder.AddLine(x, y);
            }

            var lastY = ChartRenderer.ToY(points[endIndex].Utilization, plotHeight);
            pathBuilder.AddLine(rightEdgeX, lastY);
            pathBuilder.EndFigure(CanvasFigureLoop.Open);

            using var geometry = CanvasGeometry.CreatePath(pathBuilder);
            session.DrawGeometry(geometry, zoneColor, 2f);
        }
    }

    private static void DrawGlowIndicator(
        CanvasDrawingSession session,
        ICanvasResourceCreator resourceCreator,
        IReadOnlyList<UsageHistoryPoint> points,
        DateTimeOffset windowStart,
        float plotWidth,
        float plotHeight,
        bool isDark)
    {
        var lastPoint = points[^1];
        var x = ChartRenderer.GetRightEdgeAbsoluteX(points, points.Count - 1, windowStart, plotWidth);
        var y = ChartRenderer.ToY(lastPoint.Utilization, plotHeight);
        var zoneColor = ChartColors.GetZoneColor(lastPoint.Utilization, isDark);
        var glowColor = Color.FromArgb(115, zoneColor.R, zoneColor.G, zoneColor.B);

        // Draw glow halo via command list + blur
        using var commandList = new CanvasCommandList(resourceCreator);
        using (var clSession = commandList.CreateDrawingSession())
        {
            clSession.FillCircle(x, y, 8f, glowColor);
        }

        using var blurEffect = new GaussianBlurEffect
        {
            Source = commandList,
            BlurAmount = 3.0f
        };
        session.DrawImage(blurEffect);

        // Solid dot on top
        session.FillCircle(x, y, 4f, zoneColor);
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
