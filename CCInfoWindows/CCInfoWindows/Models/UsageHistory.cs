using System.Text.Json.Serialization;

namespace CCInfoWindows.Models;

/// <summary>
/// A single recorded usage data point for chart rendering.
/// Utilization is stored as 0.0-1.0 (normalized), not the API's 0-100.
/// </summary>
public class UsageHistoryPoint
{
    [JsonPropertyName("timestamp")]
    public DateTimeOffset Timestamp { get; set; }

    [JsonPropertyName("utilization")]
    public double Utilization { get; set; }
}

/// <summary>
/// Persisted collection of usage data points for the current 5-hour window.
/// Cleared when ResetsAt changes, indicating a new window has started.
/// </summary>
public class UsageHistory
{
    [JsonPropertyName("resets_at")]
    public DateTimeOffset? ResetsAt { get; set; }

    [JsonPropertyName("points")]
    public List<UsageHistoryPoint> Points { get; set; } = [];
}
