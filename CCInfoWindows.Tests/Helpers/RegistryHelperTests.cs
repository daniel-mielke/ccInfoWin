using System.Runtime.CompilerServices;
using CCInfoWindows.Helpers;
using Microsoft.Win32;

namespace CCInfoWindows.Tests.Helpers;

/// <summary>
/// Keeps the whole test assembly out of the user's real autostart entry.
///
/// Why a module initializer and not a per-class fixture: <c>SettingsViewModel</c> writes the Run key
/// from an <c>[ObservableProperty]</c> change callback, so any future test that assigns
/// <c>IsAutostart</c> reaches <see cref="RegistryHelper.SetAutostart"/> through production code that
/// knows nothing about a fixture. Installed once, before any test body runs, no test class can write
/// the real key by accident — and because the redirect is one-shot, no teardown can clear it while
/// another xUnit collection is still running in parallel.
/// </summary>
internal static class TestAutostartKeyRedirect
{
    /// <summary>Root of the scratch tree, deleted wholesale by the test teardown.</summary>
    internal const string ScratchRootKeyPath = @"SOFTWARE\CCInfoWindowsTests";

    /// <summary>
    /// Stands in for the Run key. A stable path rather than a per-run GUID: a killed runner then leaves
    /// at most one dead scratch key behind instead of one per run, and neither carries an autostart
    /// entry, so a leftover cannot launch anything at the next logon.
    /// </summary>
    internal const string ScratchRunKeyPath = ScratchRootKeyPath + @"\Run";

    [ModuleInitializer]
    internal static void Install() => RegistryHelper.TryRedirectToKeyPath(ScratchRunKeyPath);
}

/// <summary>
/// Covers the autostart Run key contract: presence reporting, the quoted value the shipped app writes,
/// the two missing-key paths, and the one-shot redirect that keeps all of it off the real key.
///
/// Every write here lands in the scratch subkey, which the teardown removes. The suite previously wrote
/// <c>HKCU\...\Run\CCInfoWindows = "{testhost.exe}"</c> and deleted the value afterwards instead of
/// restoring it, so running the tests silently disabled a developer's autostart and a killed runner left
/// a Run entry pointing at a test host.
/// </summary>
public class RegistryHelperTests : IDisposable
{
    /// <summary>
    /// The real per-user Run key, spelled out independently of the production constant on purpose: a
    /// typo there would break autostart for every user and a shared constant could not detect it.
    /// </summary>
    private const string RealRunKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";

    /// <summary>The Run value name, likewise independent — it must match installer/setup.iss.</summary>
    private const string AutostartValueName = "CCInfoWindows";

    private const string RegistryHelperFileName = "RegistryHelper.cs";

    public RegistryHelperTests()
    {
        // SetAutostart opens the key rather than creating it (the real Run key always exists), so the
        // stand-in has to be created here. Per test, because two of the cases delete it.
        using var scratchKey = Registry.CurrentUser.CreateSubKey(TestAutostartKeyRedirect.ScratchRunKeyPath);
    }

    public void Dispose()
    {
        Registry.CurrentUser.DeleteSubKeyTree(
            TestAutostartKeyRedirect.ScratchRootKeyPath,
            throwOnMissingSubKey: false);
    }

    // --- The redirect (F.I.R.S.T. Independent: the suite must not mutate the machine) ---

    [Fact]
    public void ActiveKeyPath_IsRedirectedAwayFromTheRealRunKeyForTheWholeRun()
    {
        Assert.Equal(TestAutostartKeyRedirect.ScratchRunKeyPath, RegistryHelper.ActiveKeyPath);
        Assert.NotEqual(RealRunKeyPath, RegistryHelper.ActiveKeyPath);
    }

    [Fact]
    public void SetAutostart_LeavesTheRealRunKeyValueUntouched()
    {
        var before = ReadRealRunValue();

        RegistryHelper.SetAutostart(true);
        var afterEnable = ReadRealRunValue();

        RegistryHelper.SetAutostart(false);
        var afterDisable = ReadRealRunValue();

        // Both halves are needed. Checking only the end state would pass on a machine with autostart
        // off even if the redirect were bypassed, because enable-then-disable round-trips back to
        // "no value" on the real key too; the mid-point is what catches the write.
        //
        // Deliberately detect-and-fail rather than capture-and-restore: a restore path would be
        // untested code whose only job is writing to the real Run key, which is the one thing this
        // whole seam exists to stop.
        Assert.Equal(before, afterEnable);
        Assert.Equal(before, afterDisable);
    }

    [Fact]
    public void ResolveKeyPath_WithoutARedirect_IsTheRealPerUserRunKey()
    {
        // The fallback branch is unreachable through ActiveKeyPath once the module initializer has
        // installed the redirect, which is why the rule is a separate pure method.
        Assert.Equal(RealRunKeyPath, RegistryHelper.ResolveKeyPath(null));
    }

    [Fact]
    public void ResolveKeyPath_WithARedirect_PrefersTheRedirect()
    {
        Assert.Equal(
            TestAutostartKeyRedirect.ScratchRunKeyPath,
            RegistryHelper.ResolveKeyPath(TestAutostartKeyRedirect.ScratchRunKeyPath));
    }

    [Fact]
    public void TryRedirectToKeyPath_WhenOneIsAlreadyInstalled_IsRejected()
    {
        var secondTarget = TestAutostartKeyRedirect.ScratchRootKeyPath + @"\Rejected";

        Assert.False(RegistryHelper.TryRedirectToKeyPath(secondTarget));
        Assert.Equal(TestAutostartKeyRedirect.ScratchRunKeyPath, RegistryHelper.ActiveKeyPath);
    }

    [Fact]
    public void TryRedirectToKeyPath_WithABlankTarget_FailsLoudly()
    {
        Assert.Throws<ArgumentException>(() => RegistryHelper.TryRedirectToKeyPath("   "));
    }

    [Fact]
    public void RedirectSeam_HasNoProductionCaller()
    {
        // internal + InternalsVisibleTo makes the seam visible to the tests, but it does not stop the
        // app assembly from calling it. Only a source scan can.
        var seamName = nameof(RegistryHelper.TryRedirectToKeyPath);

        var callers = ProductionSourceFiles.All()
            .Where(file => !string.Equals(file.Name, RegistryHelperFileName, StringComparison.OrdinalIgnoreCase))
            .Where(file => file.Text.Contains(seamName, StringComparison.Ordinal))
            .Select(file => file.Name)
            .ToList();

        Assert.True(
            callers.Count == 0,
            $"{seamName} is a test-only seam but is referenced by: {string.Join(", ", callers)}");
    }

    // --- The autostart contract ---

    [Fact]
    public void SetAutostart_True_ThenGetAutostart_ReturnsTrue()
    {
        RegistryHelper.SetAutostart(true);

        Assert.True(RegistryHelper.GetAutostart());
    }

    [Fact]
    public void SetAutostart_False_ThenGetAutostart_ReturnsFalse()
    {
        RegistryHelper.SetAutostart(true);

        RegistryHelper.SetAutostart(false);

        Assert.False(RegistryHelper.GetAutostart());
    }

    [Fact]
    public void SetAutostart_True_WritesTheQuotedExecutablePathUnderTheAppValueName()
    {
        RegistryHelper.SetAutostart(true);

        // The quotes are the point: Windows splits an unquoted Run value at the first space, so a
        // launcher path under "C:\Program Files\..." would never start. Nothing else asserted this.
        Assert.Equal($"\"{Environment.ProcessPath}\"", ReadScratchRunValue());
    }

    [Fact]
    public void SetAutostart_False_WithNoValuePresent_DoesNotThrowAndStaysDisabled()
    {
        // Reachable from the Settings toggle whenever the user turns autostart off on a fresh install:
        // DeleteValue(throwOnMissingValue: true) would surface as an unhandled exception there.
        RegistryHelper.SetAutostart(false);

        Assert.False(RegistryHelper.GetAutostart());
    }

    [Fact]
    public void GetAutostart_WithNoRunKeyAtAll_ReturnsFalse()
    {
        DeleteScratchRunKey();

        Assert.False(RegistryHelper.GetAutostart());
    }

    [Fact]
    public void SetAutostart_WithNoRunKeyAtAll_DoesNotThrowAndWritesNothing()
    {
        DeleteScratchRunKey();

        RegistryHelper.SetAutostart(true);

        // The key is not created on demand, so the write is silently skipped. Asserted rather than
        // assumed: switching to CreateSubKey would resurrect the scratch key here, and on the real
        // machine it would recreate a Run key that policy had deliberately removed.
        Assert.False(RegistryHelper.GetAutostart());
    }

    private static void DeleteScratchRunKey() =>
        Registry.CurrentUser.DeleteSubKeyTree(
            TestAutostartKeyRedirect.ScratchRunKeyPath,
            throwOnMissingSubKey: false);

    private static string? ReadScratchRunValue() =>
        ReadValue(TestAutostartKeyRedirect.ScratchRunKeyPath);

    private static string? ReadRealRunValue() => ReadValue(RealRunKeyPath);

    private static string? ReadValue(string keyPath)
    {
        using var key = Registry.CurrentUser.OpenSubKey(keyPath, writable: false);
        return key?.GetValue(AutostartValueName) as string;
    }
}
