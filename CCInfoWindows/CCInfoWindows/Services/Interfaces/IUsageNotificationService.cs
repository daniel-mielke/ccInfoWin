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
    /// <summary>Burn-rate prediction toast; fires once per warning cycle.</summary>
    void CheckBurnRate(BurnRatePrediction? prediction);

    /// <summary>
    /// Evaluates thresholds and window rotation for both windows and arms the reset countdowns.
    /// Safe to call on every poll — a poll that changes nothing is a no-op.
    /// </summary>
    void CheckWindows(UsageWindow? fiveHour, UsageWindow? weekly);

    /// <summary>Cancels both countdowns and clears persisted state. Called on logout.</summary>
    void CancelAll();
}
