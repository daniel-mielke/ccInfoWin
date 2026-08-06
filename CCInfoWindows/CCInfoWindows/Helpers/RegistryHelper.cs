using Microsoft.Win32;

namespace CCInfoWindows.Helpers;

/// <summary>
/// Reads and writes the Windows HKCU autostart Run key for this application.
///
/// Testability: <see cref="TryRedirectToKeyPath"/> repoints both operations at a scratch HKCU subkey
/// once per process, so a test run cannot rewrite the user's real autostart entry. Without it the
/// suite wrote <c>HKCU\...\Run\CCInfoWindows = "{testhost.exe}"</c> and then deleted the value on
/// teardown — a developer who had autostart enabled lost it silently, because
/// <c>SettingsViewModel.Initialize</c> reads this key as the only source of truth for the toggle and
/// therefore never self-heals.
/// </summary>
public static class RegistryHelper
{
    private const string DefaultRunKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "CCInfoWindows";

    // Null in the shipped app. Only the test assembly installs a value, via TryRedirectToKeyPath.
    private static string? _redirectedKeyPath;

    /// <summary>The HKCU subkey the next read or write lands in.</summary>
    internal static string ActiveKeyPath => ResolveKeyPath(Volatile.Read(ref _redirectedKeyPath));

    /// <summary>The resolution rule: a redirect wins, absence of one means the real Run key.</summary>
    internal static string ResolveKeyPath(string? redirectedKeyPath) => redirectedKeyPath ?? DefaultRunKeyPath;

    /// <summary>
    /// Test-only: repoints this helper at <paramref name="keyPath"/> under HKCU so the suite exercises
    /// the real registry API without touching the real Run key. Nothing in CCInfoWindows may call it;
    /// RegistryHelperTests scans the app sources to keep it that way.
    ///
    /// One-shot by construction: an installed redirect cannot be cleared or replaced, so it cannot leak
    /// from one xUnit collection into another running in parallel and no fixture teardown is required.
    /// The price is that the fallback branch of <see cref="ResolveKeyPath"/> can only be covered
    /// directly, which is why that rule is a separate pure method.
    /// </summary>
    /// <returns>True when this call installed the redirect, false when one was already in place.</returns>
    internal static bool TryRedirectToKeyPath(string keyPath)
    {
        // A blank target would silently send the writes back to the real Run key, which is the whole
        // failure this seam exists to prevent — so it fails loudly instead.
        ArgumentException.ThrowIfNullOrWhiteSpace(keyPath);

        return Interlocked.CompareExchange(ref _redirectedKeyPath, keyPath, null) is null;
    }

    /// <summary>
    /// Returns true if the autostart Run key entry for this app exists in HKCU.
    /// </summary>
    public static bool GetAutostart()
    {
        using var key = Registry.CurrentUser.OpenSubKey(ActiveKeyPath, writable: false);
        return key?.GetValue(AppName) != null;
    }

    /// <summary>
    /// Writes or removes the autostart Run key entry for this app in HKCU.
    /// The executable path is quoted to handle paths containing spaces.
    /// </summary>
    public static void SetAutostart(bool enable)
    {
        using var key = Registry.CurrentUser.OpenSubKey(ActiveKeyPath, writable: true);
        if (key == null) return;

        if (enable)
            key.SetValue(AppName, $"\"{Environment.ProcessPath}\"");
        else
            key.DeleteValue(AppName, throwOnMissingValue: false);
    }
}
