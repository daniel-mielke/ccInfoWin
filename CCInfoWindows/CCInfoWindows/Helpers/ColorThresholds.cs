namespace CCInfoWindows.Helpers;

/// <summary>
/// Maps utilization percentage (0.0-1.0+) to theme resource brush key names.
/// Thresholds: green (0-50%), yellow (50-75%), orange (75-90%), red (90-100%+).
/// </summary>
public static class ColorThresholds
{
    public static string GetThresholdKey(double utilization) => utilization switch
    {
        < 0.50 => "ProgressGreenBrush",
        < 0.75 => "ProgressYellowBrush",
        < 0.90 => "ProgressOrangeBrush",
        _ => "ProgressRedBrush"
    };
}
