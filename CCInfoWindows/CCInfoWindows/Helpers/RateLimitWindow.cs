namespace CCInfoWindows.Helpers;

/// <summary>
/// The one definition of the Claude usage rate-limit window length. The chart's X axis, the burn-rate
/// ETA bound and the window-start label each used to carry their own spelling of "5 hours" — two as a
/// seconds product, two as hours — so the length was a four-site edit that could land half-applied.
///
/// The three members are the same length in the three shapes the callers need. Only
/// <see cref="DurationHours"/> holds the number.
/// </summary>
public static class RateLimitWindow
{
    /// <summary>Length of one window in hours — the "5-hour window" the UI names.</summary>
    public const int DurationHours = 5;

    /// <summary>The same length in seconds, for the chart's coordinate math.</summary>
    public const double DurationSeconds = DurationHours * 60 * 60;

    /// <summary>The same length as a <see cref="TimeSpan"/>, for date arithmetic.</summary>
    public static readonly TimeSpan Duration = TimeSpan.FromHours(DurationHours);
}
