using System.Diagnostics;
using System.Text.Json;
using CCInfoWindows.Models;
using CCInfoWindows.Services.Interfaces;

namespace CCInfoWindows.Services;

/// <summary>
/// Reads/writes settings.json in %LOCALAPPDATA%\CCInfoWindows\.
/// Handles missing or corrupt files gracefully by returning defaults.
/// </summary>
public class SettingsService : ISettingsService
{
    private static readonly string SettingsDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CCInfoWindows");

    private static readonly string SettingsFilePath = Path.Combine(
        SettingsDirectory, "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public AppSettings LoadSettings()
    {
        try
        {
            if (!File.Exists(SettingsFilePath))
            {
                return new AppSettings();
            }

            var json = File.ReadAllText(SettingsFilePath);
            return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SettingsService] LoadSettings failed: {ex.Message}");
            return new AppSettings();
        }
    }

    public void SaveSettings(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(SettingsDirectory);
            var json = JsonSerializer.Serialize(settings, JsonOptions);
            File.WriteAllText(SettingsFilePath, json);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SettingsService] SaveSettings failed: {ex.Message}");
        }
    }

    public WindowState? LoadWindowState()
    {
        return LoadSettings().WindowState;
    }

    public void SaveWindowState(WindowState state)
    {
        var settings = LoadSettings();
        settings.WindowState = state;
        SaveSettings(settings);
    }
}
