using System.Text.Json.Serialization;

namespace CCInfoWindows.Models;

/// <summary>
/// Represents a single usage window (5-hour or 7-day) from the Claude API response.
/// </summary>
public class UsageWindow
{
    [JsonPropertyName("utilization")]
    public double Utilization { get; set; }

    [JsonPropertyName("resets_at")]
    public DateTimeOffset? ResetsAt { get; set; }
}

/// <summary>
/// Typed model for the Claude API usage response.
/// Contains rate-limit windows for 5-hour and weekly quotas.
/// </summary>
public class UsageResponse
{
    [JsonPropertyName("five_hour")]
    public UsageWindow? FiveHour { get; set; }

    [JsonPropertyName("seven_day")]
    public UsageWindow? SevenDay { get; set; }

    [JsonPropertyName("seven_day_opus")]
    public UsageWindow? SevenDayOpus { get; set; }

    [JsonPropertyName("seven_day_sonnet")]
    public UsageWindow? SevenDaySonnet { get; set; }
}
