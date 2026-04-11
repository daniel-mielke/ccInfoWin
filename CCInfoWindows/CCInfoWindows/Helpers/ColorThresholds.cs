namespace CCInfoWindows.Helpers;

/// <summary>
/// Maps utilization percentage (0.0-1.0+) to theme resource brush key names.
/// Thresholds: green (0-50%), yellow (50-75%), orange (75-90%), red (90-100%+).
/// </summary>
public static class ColorThresholds
{
    private const double GreenYellowThreshold = 0.50;
    private const double YellowOrangeThreshold = 0.75;
    private const double OrangeRedThreshold = 0.90;

    public static string GetThresholdKey(double utilization) => utilization switch
    {
        < GreenYellowThreshold => "ProgressGreenBrush",
        < YellowOrangeThreshold => "ProgressYellowBrush",
        < OrangeRedThreshold => "ProgressOrangeBrush",
        _ => "ProgressRedBrush"
    };
}
