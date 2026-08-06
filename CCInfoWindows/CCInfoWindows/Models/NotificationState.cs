using System.Text.Json.Serialization;

namespace CCInfoWindows.Models;

/// <summary>
/// Which rate-limit window a notification refers to.
/// </summary>
public enum UsageWindowKind
{
    FiveHour,
    Weekly
}

/// <summary>
/// Per-window notification bookkeeping. Machine state, not user configuration — deliberately
/// kept out of AppSettings (see NotificationStateStore).
/// </summary>
public class WindowNotificationState
{
    /// <summary>
    /// Canonical identity of the window: resets_at in UTC, truncated to whole minutes, "O" format.
    ///
    /// A string rather than a DateTimeOffset because it has to survive a JSON round-trip and be
    /// found again after a restart. Minute granularity sits 60x above the measured sub-second
    /// noise in the API (two fields of the SAME response differ by .102078 vs .102162) and 300x
    /// below the shortest window.
    /// </summary>
    [JsonPropertyName("windowId")]
    public string? WindowId { get; set; }

    /// <summary>
    /// Un-truncated reset time of the tracked window; truncation is identity only.
    ///
    /// Also the rotation deadline: an identity change is only treated as a real reset once this
    /// moment has passed (UsageNotificationService.IsRealRotation), which is what keeps a weekly
    /// source flip from being announced as a reset.
    /// </summary>
    [JsonPropertyName("resetsAt")]
    public DateTimeOffset? ResetsAt { get; set; }

    [JsonPropertyName("notified80")]
    public bool Notified80 { get; set; }

    [JsonPropertyName("notified95")]
    public bool Notified95 { get; set; }

    [JsonPropertyName("notifiedReset")]
    public bool NotifiedReset { get; set; }

    /// <summary>
    /// Highest utilization seen within this identity, not the last one.
    ///
    /// The API sometimes reports a rotated window as 0%/unused in the very poll where resets_at
    /// jumps. Gating the reset notification on the LAST value would suppress it exactly when it
    /// matters. A maximum inside an identity is monotone and is only cleared when the identity
    /// changes.
    /// </summary>
    [JsonPropertyName("peakUtilization")]
    public double PeakUtilization { get; set; }
}

/// <summary>
/// Persisted notification state for both tracked windows.
/// </summary>
public class NotificationState
{
    [JsonPropertyName("fiveHour")]
    public WindowNotificationState FiveHour { get; set; } = new();

    [JsonPropertyName("weekly")]
    public WindowNotificationState Weekly { get; set; } = new();

    /// <summary>
    /// Identity of the 5-hour window whose burn-rate toast has already been delivered, or null when
    /// the toast is armed.
    ///
    /// Keyed by the window rather than stored as a bare bool so a window rotation re-arms it while a
    /// process RESTART does not: history is rehydrated from disk, so the first poll after a restart
    /// already has the >= 3 points BurnRateCalculator.Predict needs and would re-fire the same toast.
    /// Absent from files written before v1.6, which deserializes to null — one re-arm on upgrade.
    /// </summary>
    [JsonPropertyName("burnRateNotifiedWindowId")]
    public string? BurnRateNotifiedWindowId { get; set; }

    public WindowNotificationState For(UsageWindowKind kind) =>
        kind == UsageWindowKind.FiveHour ? FiveHour : Weekly;
}
