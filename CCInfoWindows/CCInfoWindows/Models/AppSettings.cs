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
}
