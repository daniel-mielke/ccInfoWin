using System.Text.Json.Serialization;

namespace CCInfoWindows.Models;

/// <summary>
/// Persisted window state (position and size).
/// </summary>
public record WindowState(int X, int Y, int Width, int Height);

/// <summary>
/// Application settings persisted to settings.json.
/// </summary>
public class AppSettings
{
    [JsonPropertyName("windowState")]
    public WindowState? WindowState { get; set; }

    [JsonPropertyName("refreshIntervalSeconds")]
    public int RefreshIntervalSeconds { get; set; } = 60;

    [JsonPropertyName("colorMode")]
    public string ColorMode { get; set; } = "dark";
}
