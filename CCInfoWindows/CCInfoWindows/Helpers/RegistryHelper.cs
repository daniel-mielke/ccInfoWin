using Microsoft.Win32;

namespace CCInfoWindows.Helpers;

/// <summary>
/// Reads and writes the Windows HKCU autostart Run key for this application.
/// </summary>
public static class RegistryHelper
{
    private const string RunKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "CCInfoWindows";

    /// <summary>
    /// Returns true if the autostart Run key entry for this app exists in HKCU.
    /// </summary>
    public static bool GetAutostart()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        return key?.GetValue(AppName) != null;
    }

    /// <summary>
    /// Writes or removes the autostart Run key entry for this app in HKCU.
    /// The executable path is quoted to handle paths containing spaces.
    /// </summary>
    public static void SetAutostart(bool enable)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        if (key == null) return;

        if (enable)
            key.SetValue(AppName, $"\"{Environment.ProcessPath}\"");
        else
            key.DeleteValue(AppName, throwOnMissingValue: false);
    }
}
