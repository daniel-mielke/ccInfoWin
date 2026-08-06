using System.Text.Json;
using CCInfoWindows.Models;

namespace CCInfoWindows.Tests.Models;

public class AppSettingsTests
{
    [Fact]
    public void DefaultValues_AreCorrect()
    {
        var settings = new AppSettings();

        Assert.Equal(60, settings.RefreshIntervalSeconds);
        Assert.Equal("dark", settings.ColorMode);
        Assert.Null(settings.WindowState);
    }

    [Fact]
    public void Roundtrip_SerializeDeserialize_PreservesAllFields()
    {
        var original = new AppSettings
        {
            WindowState = new WindowState(100, 200, 800, 600),
            RefreshIntervalSeconds = 30,
            ColorMode = "light"
        };

        var json = JsonSerializer.Serialize(original);
        var deserialized = JsonSerializer.Deserialize<AppSettings>(json);

        Assert.NotNull(deserialized);
        Assert.Equal(30, deserialized.RefreshIntervalSeconds);
        Assert.Equal("light", deserialized.ColorMode);
        Assert.NotNull(deserialized.WindowState);
        Assert.Equal(100, deserialized.WindowState.X);
        Assert.Equal(200, deserialized.WindowState.Y);
        Assert.Equal(800, deserialized.WindowState.Width);
        Assert.Equal(600, deserialized.WindowState.Height);
    }

    /// <summary>
    /// Finding 19: SettingsService validates the untrusted file against these allow-lists and falls
    /// back to the Default* constants, so a default outside its own allow-list would make validation
    /// produce a value the UI cannot represent.
    /// </summary>
    [Fact]
    public void EveryDefault_IsAMemberOfItsAllowList()
    {
        Assert.Contains(AppSettings.DefaultRefreshIntervalSeconds, AppSettings.SupportedRefreshIntervalSeconds);
        Assert.Contains(AppSettings.ManualRefreshSeconds, AppSettings.SupportedRefreshIntervalSeconds);
        Assert.Contains(AppSettings.DefaultColorMode, AppSettings.SupportedColorModes);
        Assert.Contains(AppSettings.DefaultLanguage, AppSettings.SupportedLanguages);
        Assert.Contains(
            AppSettings.DefaultSessionActivityThresholdMinutes,
            AppSettings.SupportedSessionActivityThresholdMinutes);
        Assert.Contains(
            AppSettings.DefaultSessionVisibilityWindowDays,
            AppSettings.SupportedSessionVisibilityWindowDays);
        Assert.Contains(
            AppSettings.UnlimitedSessionVisibilityWindowDays,
            AppSettings.SupportedSessionVisibilityWindowDays);
    }

    /// <summary>
    /// The property initializers and the allow-lists are two statements of the same default; a fresh
    /// AppSettings must therefore already be valid input for every consumer.
    /// </summary>
    [Fact]
    public void FreshInstance_HoldsOnlySupportedValues()
    {
        var settings = new AppSettings();

        Assert.Contains(settings.RefreshIntervalSeconds, AppSettings.SupportedRefreshIntervalSeconds);
        Assert.Contains(settings.ColorMode, AppSettings.SupportedColorModes);
        Assert.Contains(settings.Language, AppSettings.SupportedLanguages);
        Assert.Contains(settings.SessionActivityThresholdMinutes, AppSettings.SupportedSessionActivityThresholdMinutes);
        Assert.Contains(settings.SessionVisibilityWindowDays, AppSettings.SupportedSessionVisibilityWindowDays);
    }

    /// <summary>Duplicates would make dropdown index mapping ambiguous in both directions.</summary>
    [Fact]
    public void AllowLists_ContainNoDuplicates()
    {
        Assert.Equal(
            AppSettings.SupportedRefreshIntervalSeconds.Length,
            AppSettings.SupportedRefreshIntervalSeconds.Distinct().Count());
        Assert.Equal(
            AppSettings.SupportedSessionActivityThresholdMinutes.Length,
            AppSettings.SupportedSessionActivityThresholdMinutes.Distinct().Count());
        Assert.Equal(
            AppSettings.SupportedSessionVisibilityWindowDays.Length,
            AppSettings.SupportedSessionVisibilityWindowDays.Distinct().Count());
        Assert.Equal(
            AppSettings.SupportedLanguages.Length,
            AppSettings.SupportedLanguages.Distinct().Count());
    }

    /// <summary>
    /// The threshold and visibility lists are index-aligned with the SettingsView ComboBox items
    /// (15/30/60/120 minutes and 7/30/90/unlimited days); a silent reorder would remap the dropdown.
    /// </summary>
    [Fact]
    public void AllowLists_KeepTheirDocumentedComboBoxOrder()
    {
        // ToArray on both sides: ImmutableArray<T>.Equals compares the underlying array by
        // reference, so comparing the struct directly would never match a freshly built expectation.
        Assert.Equal(
            new[] { 15, 30, 60, 120 },
            AppSettings.SupportedSessionActivityThresholdMinutes.ToArray());
        Assert.Equal(
            new[] { 7, 30, 90, 0 },
            AppSettings.SupportedSessionVisibilityWindowDays.ToArray());
        Assert.Equal(
            new[] { "de-DE", "en-US" },
            AppSettings.SupportedLanguages.ToArray());
        Assert.Equal(
            new[] { 30, 60, 120, 300, 600, 0 },
            AppSettings.SupportedRefreshIntervalSeconds.ToArray());
    }

    [Fact]
    public void Deserialize_LegacyJson_WithoutNewFields_AppliesDefaults()
    {
        // Simulates settings.json from Phase 1 (only WindowState)
        var json = """{"windowState":{"X":50,"Y":50,"Width":400,"Height":300}}""";

        var settings = JsonSerializer.Deserialize<AppSettings>(json);

        Assert.NotNull(settings);
        Assert.Equal(60, settings.RefreshIntervalSeconds);
        Assert.Equal("dark", settings.ColorMode);
        Assert.NotNull(settings.WindowState);
        Assert.Equal(50, settings.WindowState.X);
    }
}
