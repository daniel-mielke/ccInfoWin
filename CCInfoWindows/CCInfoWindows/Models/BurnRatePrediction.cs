namespace CCInfoWindows.Models;

/// <summary>
/// Result of a burn rate prediction. Null from BurnRateCalculator.Predict means no warning.
/// </summary>
public class BurnRatePrediction
{
    /// <summary>Projected timestamp when 100% utilization is reached.</summary>
    public DateTimeOffset HitsLimitAt { get; set; }

    /// <summary>Minutes until the limit is hit. Minimum value is 1.</summary>
    public int MinutesUntilLimit { get; set; }
}
