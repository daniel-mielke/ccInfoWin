using CCInfoWindows.Models;

namespace CCInfoWindows.Services.Interfaces;

/// <summary>
/// What a toast asks for, before localization. Passing keys rather than finished strings keeps
/// the decision "which message, with which argument" testable headlessly — Localizer needs a
/// running WinUI host, and the resw content itself is covered by ResourceCoverageTests.
/// </summary>
public sealed record ToastRequest(string Tag, string TitleKey, string BodyKey, object? BodyArg = null);

/// <summary>
/// All usage-driven toasts: burn-rate prediction, 80/95% thresholds, and window reset.
///
/// One interface rather than two because MainViewModel's constructor already takes 12 arguments
/// and this way it gains one dependency instead of two.
/// </summary>
public interface IUsageNotificationService
{
    /// <summary>
    /// Burn-rate prediction toast. Fires at most once per 5-hour window: the delivered state is
    /// persisted against that window's identity, so a rotation re-arms it but an app restart does
    /// not. A null prediction re-arms it immediately.
    /// </summary>
    void CheckBurnRate(BurnRatePrediction? prediction);

    /// <summary>
    /// Evaluates thresholds and window rotation for both windows and arms the reset countdowns.
    /// Safe to call on every poll — a poll that changes nothing is a no-op. Call AFTER CheckBurnRate
    /// so the burn-rate flag is keyed against the identity of the window the prediction was made in.
    /// </summary>
    void CheckWindows(UsageWindow? fiveHour, UsageWindow? weekly);

    /// <summary>
    /// Cancels both countdowns and clears persisted state. Called on logout — this is deliberately
    /// NOT what Dispose does, which only stops the timers.
    /// </summary>
    void CancelAll();
}
