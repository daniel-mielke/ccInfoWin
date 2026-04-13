using CCInfoWindows.Models;

namespace CCInfoWindows.Helpers;

/// <summary>
/// Predicts when the 5-hour token limit will be reached using linear regression
/// over recent usage history.
/// </summary>
public static class BurnRateCalculator
{
    private const double MinimumUtilization = 20.0;
    private const int LookbackWindowMinutes = 15;
    private const int MinimumDataPoints = 3;
    private const double MaxUtilization = 100.0;
    private const double NearZeroThreshold = 1e-10;

    /// <summary>
    /// Predicts the burn rate based on recent usage history.
    /// Returns null when no warning should be shown.
    /// </summary>
    /// <param name="history">Recent usage data points (utilization stored as 0.0-1.0).</param>
    /// <param name="currentUtilization">Current utilization on 0-100 scale (from API).</param>
    /// <param name="resetsAt">When the current 5-hour window resets.</param>
    public static BurnRatePrediction? Predict(
        IReadOnlyList<UsageHistoryPoint> history,
        double currentUtilization,
        DateTimeOffset? resetsAt)
    {
        if (resetsAt is null || resetsAt.Value <= DateTimeOffset.UtcNow)
            return null;

        if (currentUtilization < MinimumUtilization)
            return null;

        if (currentUtilization >= MaxUtilization)
            return null;

        var lookbackCutoff = DateTimeOffset.UtcNow - TimeSpan.FromMinutes(LookbackWindowMinutes);
        var recentPoints = history
            .Where(p => p.Timestamp >= lookbackCutoff)
            .OrderBy(p => p.Timestamp)
            .ToList();

        if (recentPoints.Count < MinimumDataPoints)
            return null;

        var referenceTime = recentPoints[0].Timestamp;
        var n = recentPoints.Count;
        var sumX = 0.0;
        var sumY = 0.0;
        var sumXY = 0.0;
        var sumX2 = 0.0;

        foreach (var p in recentPoints)
        {
            var x = (p.Timestamp - referenceTime).TotalSeconds;
            var y = p.Utilization * 100.0;

            sumX += x;
            sumY += y;
            sumXY += x * y;
            sumX2 += x * x;
        }

        var denominator = (n * sumX2) - (sumX * sumX);

        if (Math.Abs(denominator) < NearZeroThreshold)
            return null;

        var slope = ((n * sumXY) - (sumX * sumY)) / denominator;

        if (slope <= 0)
            return null;

        var secondsToLimit = (MaxUtilization - currentUtilization) / slope;
        var hitsLimitAt = DateTimeOffset.UtcNow.AddSeconds(secondsToLimit);

        if (hitsLimitAt >= resetsAt.Value)
            return null;

        var minutesUntilLimit = Math.Max(1, (int)Math.Floor(secondsToLimit / 60.0));

        return new BurnRatePrediction
        {
            HitsLimitAt = hitsLimitAt,
            MinutesUntilLimit = minutesUntilLimit,
        };
    }
}
