using CCInfoWindows.Helpers;
using CCInfoWindows.ViewModels;
using Microsoft.UI.Xaml.Media;

namespace CCInfoWindows.Tests.ViewModels;

/// <summary>
/// Verifies initial field defaults on MainViewModel construction (G-3 convention compliance).
///
/// SolidColorBrush is a WinRT-backed type that requires COM activation — it cannot be
/// instantiated in the headless xUnit test runner. The brushFactory testability seam in
/// MainViewModel accepts a Func{string, SolidColorBrush} parameter so tests can intercept
/// the factory call without constructing a real brush.
///
/// The tests here verify the INTENT (which hexes are requested, and that every brush field goes
/// through the seam) rather than the runtime value of the brush properties.
/// </summary>
[Collection("WeakReferenceMessenger")]
public class MainViewModelInitialStateTests
{
    private static MainViewModel CreateSut(Func<string, SolidColorBrush> brushFactory)
        => MainViewModelFactory.Create(brushFactory: brushFactory);

    /// <summary>Records every hex the ViewModel asks the seam for, in request order.</summary>
    private static List<string> CaptureRequestedHexes()
    {
        var requested = new List<string>();
        _ = CreateSut(hex =>
        {
            requested.Add(hex);
            return null!;   // headless: SolidColorBrush cannot be instantiated without WinRT COM
        });
        return requested;
    }

    // CLEANUP-02 / G-3: constructor must request gray-400 as the initial badge fallback.
    // Verifies the seam is called with the correct hex — the intent of the G-3 fix.
    [Fact]
    public void ContextModelBadgeColor_AtConstruction_RequestsGray400Hex()
    {
        Assert.Equal(MainViewModel.InitialBadgeColorHex, CaptureRequestedHexes().First());
    }

    // Finding 42 extended this: the three progress-bar foregrounds moved out of a value converter
    // into [ObservableProperty] SolidColorBrush fields, and G-3 requires them to come from the same
    // seam. Four requests, not one — badge plus context/weekly/sonnet at zero utilization.
    [Fact]
    public void UtilizationBrushes_AtConstruction_ComeFromTheBrushFactory()
    {
        const int ExpectedBrushCount = 4;

        var requested = CaptureRequestedHexes();

        Assert.Equal(ExpectedBrushCount, requested.Count);
        // Zero utilization is the green zone, and the ViewModel defaults to the dark palette.
        var greenHex = ToHex(ChartColors.GetZoneColor(0, isDark: true));
        Assert.All(requested.Skip(1), hex => Assert.Equal(greenHex, hex));
    }

    [Fact]
    public void ApplyTheme_RecomputesEveryUtilizationBrush_ForTheGivenTheme()
    {
        // Finding 42: the converter this replaced returned a fixed brush instance, and x:Bind OneWay
        // cannot re-evaluate a converter on ActualThemeChanged — so the bars kept the palette of
        // whichever theme was active when the last poll landed.
        var requested = new List<string>();
        var sut = CreateSut(hex =>
        {
            requested.Add(hex);
            return null!;
        });

        sut.ContextUtilization = 0.95;   // red zone
        requested.Clear();

        sut.ApplyTheme(isDark: false);

        var lightRed = ToHex(ChartColors.GetZoneColor(0.95, isDark: false));
        var lightGreen = ToHex(ChartColors.GetZoneColor(0, isDark: false));
        Assert.Equal(new[] { lightRed, lightGreen, lightGreen }, requested);
    }

    [Fact]
    public void UtilizationChange_RepaintsOnlyItsOwnBar()
    {
        var requested = new List<string>();
        var sut = CreateSut(hex =>
        {
            requested.Add(hex);
            return null!;
        });
        requested.Clear();

        sut.WeeklyUtilization = 0.80;

        Assert.Equal(new[] { ToHex(ChartColors.GetZoneColor(0.80, isDark: true)) }, requested);
    }

    private static string ToHex(Windows.UI.Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";
}
