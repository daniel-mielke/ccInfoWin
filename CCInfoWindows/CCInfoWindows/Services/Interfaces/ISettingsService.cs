using CCInfoWindows.Models;

namespace CCInfoWindows.Services.Interfaces;

/// <summary>
/// JSON-based settings persistence to %LOCALAPPDATA%\CCInfoWindows\settings.json.
/// </summary>
public interface ISettingsService
{
    AppSettings LoadSettings();

    void SaveSettings(AppSettings settings);

    WindowState? LoadWindowState();

    void SaveWindowState(WindowState state);
}
