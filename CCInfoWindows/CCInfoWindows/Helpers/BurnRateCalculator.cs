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
    /// Upper bound for a usable ETA — one whole rate-limit window has already elapsed by then. A
    /// derived bound, not the window length itself: it is defined as "one window" only because
    /// nothing beyond the current window is worth warning about, so it tracks
    /// <see cref="RateLimitWindow.DurationSeconds"/> rather than restating it.
    /// </summary>
    private const double MaxSecondsToLimit = RateLimitWindow.DurationSeconds;

    /// <summary>
    /// Steepest utilization change treated as real, in percentage points per second.
    /// 0.5 means 15 points inside one 30-second poll — far above anything sustained usage
    /// produces (typically well under 0.1 points/s), so only genuine jumps are rejected.
    ///
    /// Upstream calls its own limit "a first guess" and so is this: it is a plausibility bound,
    /// not a measured constant. Tune it here if real sessions start tripping it.
    /// </summary>
    public const double MaxPlausiblePointsPerSecond = 0.5;

    private const double RateComparisonEpsilon = 1e-9;

    private const string LogSource = nameof(BurnRateCalculator);

    /// <summary>
    /// Appended to both rejection entries, because a maintainer reading a single line would otherwise
    /// conclude the condition occurred exactly once.
    /// </summary>
    private const string RepeatsSuppressed =
        " Further occurrences in this process are not reported.";

    // One entry per condition per process. Predict re-filters the whole 15-minute lookback on every
    // 30-second poll, so one implausible sample would be re-rejected ~30 times before it ages out and
    // a permanently duplicated timestamp would be re-rejected for as long as the app runs. That is
    // enough volume to push every other entry out of a 1 MiB app.log — and the repeats carry no
    // information the first one did not. The bound on MaxPlausiblePointsPerSecond is documented as "a
    // first guess", so whether it ever trips in the field is exactly what has to survive in Release.
    private static int _nonAdvancingTimestampReported;
    private static int _implausibleRateReported;

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
        var recentPoints = FilterImplausibleJumps(history
            .Where(p => p.Timestamp >= lookbackCutoff)
            .OrderBy(p => p.Timestamp)
            .ToList());

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

        // A near-flat slope surviving the checks above (float noise on identical samples)
        // yields an astronomic ETA that overflows AddSeconds. Beyond the window there is
        // nothing to warn about anyway.
        if (!double.IsFinite(secondsToLimit) || secondsToLimit > MaxSecondsToLimit)
            return null;

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

    /// <summary>
    /// Drops regression points whose change since the last accepted point is too steep to be
    /// real usage. A single bogus jump drags the slope up and produces a false
    /// "exhausted in 2 minutes" alarm.
    ///
    /// Complementary to the near-flat guard further down, not a duplicate of it: that one
    /// protects against slope -> 0 (ETA -> infinity, overflow), this one against slope ->
    /// infinity. Together Predict now has both plausibility bounds, where upstream only has the
    /// upper one.
    ///
    /// Points are compared against the last ACCEPTED point, so a rejected outlier cannot become
    /// the baseline that makes the following (normal) point look like a jump in the other
    /// direction.
    /// </summary>
    public static List<UsageHistoryPoint> FilterImplausibleJumps(IReadOnlyList<UsageHistoryPoint> ordered)
    {
        var accepted = new List<UsageHistoryPoint>(ordered.Count);
        if (ordered.Count == 0) return accepted;

        accepted.Add(ordered[0]);

        for (var i = 1; i < ordered.Count; i++)
        {
            var previous = accepted[^1];
            var current = ordered[i];

            var elapsedSeconds = (current.Timestamp - previous.Timestamp).TotalSeconds;
            if (elapsedSeconds <= 0.0)
            {
                if (TryClaimFirstReport(ref _nonAdvancingTimestampReported))
                {
                    AppLog.Write(
                        LogSource,
                        $"rejected the sample at {current.Timestamp:O}: its timestamp does not advance "
                        + $"past the last accepted one ({previous.Timestamp:O})." + RepeatsSuppressed);
                }

                continue;
            }

            var pointsPerSecond = Math.Abs((current.Utilization - previous.Utilization) * 100.0) / elapsedSeconds;

            // Epsilon because the bound is a round number that real data hits exactly: 15 points
            // over 30s computes to 0.5000000000000001 and would otherwise be rejected by noise.
            if (pointsPerSecond > MaxPlausiblePointsPerSecond + RateComparisonEpsilon)
            {
                if (TryClaimFirstReport(ref _implausibleRateReported))
                {
                    AppLog.Write(
                        LogSource,
                        $"rejected the sample at {current.Timestamp:O}: {pointsPerSecond:F3} points/s "
                        + $"exceeds the plausibility bound of {MaxPlausiblePointsPerSecond} "
                        + $"({previous.Utilization * 100:F1}% -> {current.Utilization * 100:F1}% "
                        + $"in {elapsedSeconds:F0}s)." + RepeatsSuppressed);
                }

                continue;
            }

            accepted.Add(current);
        }

        return accepted;
    }

    /// <summary>
    /// True on the first call for a given latch and false forever after. A parameter rather than a
    /// captured field so the once-only rule is deterministic under xUnit, where process-wide state
    /// would make the outcome depend on which test ran first.
    /// </summary>
    internal static bool TryClaimFirstReport(ref int latch) => Interlocked.Exchange(ref latch, 1) == 0;
}
