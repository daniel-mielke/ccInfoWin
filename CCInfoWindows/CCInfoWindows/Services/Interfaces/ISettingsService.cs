using CCInfoWindows.Models;

namespace CCInfoWindows.Services.Interfaces;

/// <summary>
/// JSON-based settings persistence to %LOCALAPPDATA%\CCInfoWindows\settings.json.
/// </summary>
public interface ISettingsService
{
    /// <summary>
    /// Returns the persisted settings with every field validated against the allow-lists on
    /// <see cref="AppSettings"/>. Consumers may use the values directly: an unsupported language
    /// tag, refresh interval, colour mode or window geometry has already been replaced by the
    /// corresponding default. A missing or corrupt file yields defaults.
    /// </summary>
    AppSettings LoadSettings();

    /// <summary>
    /// Persists the settings atomically (tmp file + File.Move). Failures are logged, never thrown —
    /// a settings write must not take down the caller's flow.
    /// </summary>
    void SaveSettings(AppSettings settings);

    WindowState? LoadWindowState();

    void SaveWindowState(WindowState state);
}
