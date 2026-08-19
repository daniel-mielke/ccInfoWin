using System.Text.Json;
using CCInfoWindows.Models;
using CCInfoWindows.Services;
using CCInfoWindows.Tests.TestSupport;

namespace CCInfoWindows.Tests.Services;

/// <summary>
/// Covers the two contracts SettingsService gained in the wave-2 remediation:
///   - LoadSettings validates the untrusted %LOCALAPPDATA% file against the AppSettings allow-lists
///     (finding 19), so a hand-edited language can no longer abort app launch and a hand-edited
///     interval can no longer turn the poll loop into an unthrottled API hammer;
///   - SaveSettings writes atomically via tmp + File.Move (finding 35), so an interrupted write
///     cannot leave a half-written file that silently degrades to defaults.
/// Every test owns a private temp directory, so the suite never touches the developer's real
/// settings.json and the tests stay independent and repeatable.
/// </summary>
public class SettingsServiceTests : IDisposable
{
    private const string SettingsFileName = "settings.json";
    private const string TempFileName = SettingsFileName + ".tmp";

    private readonly TempDirectory _temp = new("ccinfo-settings-");

    public void Dispose() => _temp.Dispose();

    private string SettingsPath => Path.Combine(_temp.Path, SettingsFileName);

    private SettingsService CreateService() => new(_temp.Path);

    private void WriteRawSettings(string json) => File.WriteAllText(SettingsPath, json);

    /// <summary>
    /// Writes a settings.json holding exactly one property, the way a hand-edit or a partial sync
    /// would. Serializing a dictionary keeps the JSON valid for any value type without
    /// brace-escaping gymnastics in the test source.
    /// </summary>
    private void WriteSingleProperty(string jsonPropertyName, object? value) =>
        WriteRawSettings(JsonSerializer.Serialize(
            new Dictionary<string, object?> { [jsonPropertyName] = value }));

    // ─── Load / save basics ──────────────────────────────────────────────────────

    [Fact]
    public void LoadSettings_FileMissing_ReturnsDefaults()
    {
        var settings = CreateService().LoadSettings();

        Assert.Equal(AppSettings.DefaultRefreshIntervalSeconds, settings.RefreshIntervalSeconds);
        Assert.Equal(AppSettings.DefaultColorMode, settings.ColorMode);
        Assert.Null(settings.WindowState);
    }

    [Fact]
    public void LoadSettings_CorruptJson_ReturnsDefaultsWithoutThrowing()
    {
        WriteRawSettings("{ this is not json");

        var settings = CreateService().LoadSettings();

        Assert.Equal(AppSettings.DefaultRefreshIntervalSeconds, settings.RefreshIntervalSeconds);
    }

    [Fact]
    public void SaveSettings_ThenLoad_RoundtripsSupportedValues()
    {
        var service = CreateService();
        service.SaveSettings(new AppSettings
        {
            RefreshIntervalSeconds = 300,
            ColorMode = AppSettings.LightColorMode,
            Language = AppSettings.EnglishLanguage,
            SessionActivityThresholdMinutes = 120,
            SessionVisibilityWindowDays = AppSettings.UnlimitedSessionVisibilityWindowDays,
            WindowState = new WindowState(100, 200, 800, 600),
            DismissedUpdateVersion = "v1.6.0",
            LastSelectedSessionId = "-projects-alpha"
        });

        var loaded = service.LoadSettings();

        Assert.Equal(300, loaded.RefreshIntervalSeconds);
        Assert.Equal(AppSettings.LightColorMode, loaded.ColorMode);
        Assert.Equal(AppSettings.EnglishLanguage, loaded.Language);
        Assert.Equal(120, loaded.SessionActivityThresholdMinutes);
        Assert.Equal(AppSettings.UnlimitedSessionVisibilityWindowDays, loaded.SessionVisibilityWindowDays);
        Assert.Equal(new WindowState(100, 200, 800, 600), loaded.WindowState);
        Assert.Equal("v1.6.0", loaded.DismissedUpdateVersion);
        Assert.Equal("-projects-alpha", loaded.LastSelectedSessionId);
    }

    [Fact]
    public void SaveSettings_LeavesNoTempFileBehind()
    {
        CreateService().SaveSettings(new AppSettings());

        Assert.True(File.Exists(SettingsPath));
        Assert.False(File.Exists(Path.Combine(_temp.Path, TempFileName)));
    }

    [Fact]
    public void LoadSettings_IgnoresAStaleTempFile()
    {
        // The atomic-write contract: the target is replaced by a rename, never truncated in place, so
        // a .tmp left over from an interrupted write must never be mistaken for the settings.
        var service = CreateService();
        service.SaveSettings(new AppSettings { RefreshIntervalSeconds = 30 });
        File.WriteAllText(Path.Combine(_temp.Path, TempFileName), "{ half-written garbage");

        var loaded = service.LoadSettings();

        Assert.Equal(30, loaded.RefreshIntervalSeconds);
    }

    [Fact]
    public void SaveWindowState_KeepsUnrelatedSettings()
    {
        var service = CreateService();
        service.SaveSettings(new AppSettings { Language = AppSettings.EnglishLanguage });

        service.SaveWindowState(new WindowState(10, 20, 400, 500));

        var loaded = service.LoadSettings();
        Assert.Equal(AppSettings.EnglishLanguage, loaded.Language);
        Assert.Equal(new WindowState(10, 20, 400, 500), loaded.WindowState);
    }

    // ─── Finding 19: allow-list validation of the untrusted file ─────────────────

    [Fact]
    public void LoadSettings_UnsupportedLanguage_FallsBackToDefault()
    {
        // The detonation this prevents: App.InitializeLocalizerAsync calls SetLanguage without a
        // try/catch, WinUI3Localizer throws FailedToSetLanguageException, OnLaunched writes crash.log
        // and calls Exit() — on every launch, with no UI left to repair the value.
        WriteSingleProperty("language", "fr-FR");

        Assert.Equal(AppSettings.DefaultLanguage, CreateService().LoadSettings().Language);
    }

    [Fact]
    public void LoadSettings_LanguageInWrongCase_IsCanonicalized()
    {
        WriteSingleProperty("language", "EN-us");

        Assert.Equal(AppSettings.EnglishLanguage, CreateService().LoadSettings().Language);
    }

    [Fact]
    public void LoadSettings_EmptyLanguage_StaysEmpty()
    {
        // Empty means "no explicit preference", which App treats as "leave the localizer alone".
        WriteSingleProperty("language", string.Empty);

        Assert.Equal(string.Empty, CreateService().LoadSettings().Language);
    }

    [Theory]
    [InlineData(-30)]
    [InlineData(45)]
    [InlineData(int.MaxValue)]
    public void LoadSettings_UnsupportedRefreshInterval_FallsBackToDefault(int persisted)
    {
        WriteSingleProperty("refreshIntervalSeconds", persisted);

        Assert.Equal(
            AppSettings.DefaultRefreshIntervalSeconds,
            CreateService().LoadSettings().RefreshIntervalSeconds);
    }

    [Fact]
    public void LoadSettings_ManualRefreshInterval_IsAccepted()
    {
        // 0 is only invalid as a *timer interval*; as a persisted setting it is the "Manual" option,
        // so validation must not silently re-enable polling for a user who turned it off.
        var service = CreateService();
        service.SaveSettings(new AppSettings { RefreshIntervalSeconds = AppSettings.ManualRefreshSeconds });

        Assert.Equal(AppSettings.ManualRefreshSeconds, service.LoadSettings().RefreshIntervalSeconds);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    [InlineData(int.MinValue)]
    public void LoadSettings_UnsupportedActivityThreshold_FallsBackToDefault(int persisted)
    {
        WriteSingleProperty("sessionActivityThresholdMinutes", persisted);

        Assert.Equal(
            AppSettings.DefaultSessionActivityThresholdMinutes,
            CreateService().LoadSettings().SessionActivityThresholdMinutes);
    }

    [Fact]
    public void LoadSettings_UnsupportedVisibilityWindow_FallsBackToDefault()
    {
        WriteSingleProperty("sessionVisibilityWindowDays", 365);

        Assert.Equal(
            AppSettings.DefaultSessionVisibilityWindowDays,
            CreateService().LoadSettings().SessionVisibilityWindowDays);
    }

    [Fact]
    public void LoadSettings_UnlimitedVisibilityWindow_IsAccepted()
    {
        WriteSingleProperty("sessionVisibilityWindowDays", AppSettings.UnlimitedSessionVisibilityWindowDays);

        Assert.Equal(
            AppSettings.UnlimitedSessionVisibilityWindowDays,
            CreateService().LoadSettings().SessionVisibilityWindowDays);
    }

    [Fact]
    public void LoadSettings_UnsupportedColorMode_FallsBackToDark()
    {
        WriteSingleProperty("colorMode", "neon");

        Assert.Equal(AppSettings.DefaultColorMode, CreateService().LoadSettings().ColorMode);
    }

    [Fact]
    public void LoadSettings_ColorModeInWrongCase_IsCanonicalized()
    {
        // Consumers compare ordinally (ColorMode != "light"), so "Light" must not read as dark.
        WriteSingleProperty("colorMode", "Light");

        Assert.Equal(AppSettings.LightColorMode, CreateService().LoadSettings().ColorMode);
    }

    [Theory]
    [InlineData(0, 0, 0, 0)]                          // degenerate size
    [InlineData(0, 0, 800, 10)]                       // one dimension below the floor
    [InlineData(0, 0, int.MaxValue, 600)]             // overflows X + Width / 2 in IsPositionOnScreen
    [InlineData(int.MinValue, 0, 800, 600)]           // absurd coordinate
    public void LoadSettings_ImplausibleWindowState_IsDropped(int x, int y, int width, int height)
    {
        WriteRawSettings(JsonSerializer.Serialize(new AppSettings
        {
            WindowState = new WindowState(x, y, width, height)
        }));

        Assert.Null(CreateService().LoadSettings().WindowState);
    }

    [Fact]
    public void LoadSettings_PlausibleWindowState_IsKept()
    {
        var state = new WindowState(-1200, 40, 360, 980);   // negative X is a legitimate second monitor
        WriteRawSettings(JsonSerializer.Serialize(new AppSettings { WindowState = state }));

        Assert.Equal(state, CreateService().LoadSettings().WindowState);
    }

    [Theory]
    [InlineData("not-a-version")]
    [InlineData("1.2.3.4.5")]
    [InlineData("v")]
    public void LoadSettings_UnparseableDismissedUpdateVersion_IsDropped(string persisted)
    {
        // UpdateService.ParseVersion is Version.Parse and throws, and CheckForUpdateAsync's catch-all
        // swallows it — the whole update check would stay dead until settings.json was hand-repaired.
        WriteSingleProperty("dismissedUpdateVersion", persisted);

        Assert.Null(CreateService().LoadSettings().DismissedUpdateVersion);
    }

    [Fact]
    public void LoadSettings_OverlongDismissedUpdateVersion_IsDropped()
    {
        var padded = new string('1', AppSettings.MaxDismissedUpdateVersionLength) + ".0";
        WriteSingleProperty("dismissedUpdateVersion", padded);

        Assert.Null(CreateService().LoadSettings().DismissedUpdateVersion);
    }

    [Theory]
    [InlineData("1.6.0")]
    [InlineData("v1.6.0")]
    public void LoadSettings_VersionTagsUpdateServiceAccepts_AreKept(string persisted)
    {
        WriteSingleProperty("dismissedUpdateVersion", persisted);

        Assert.Equal(persisted, CreateService().LoadSettings().DismissedUpdateVersion);
    }

    [Fact]
    public void LoadSettings_SessionIdWithControlCharacters_IsDropped()
    {
        // JsonSerializer escapes the BEL to \u0007, so the deserialized id really carries a
        // control character instead of the file being rejected as malformed JSON.
        WriteSingleProperty("lastSelectedSessionId", "-projects-\u0007alpha");

        Assert.Null(CreateService().LoadSettings().LastSelectedSessionId);
    }

    [Fact]
    public void LoadSettings_OverlongSessionId_IsDropped()
    {
        var tooLong = new string('a', AppSettings.MaxLastSelectedSessionIdLength + 1);
        WriteSingleProperty("lastSelectedSessionId", tooLong);

        Assert.Null(CreateService().LoadSettings().LastSelectedSessionId);
    }

    [Fact]
    public void LoadSettings_JsonNullDocument_ReturnsDefaults()
    {
        WriteRawSettings("null");

        Assert.Equal(
            AppSettings.DefaultRefreshIntervalSeconds,
            CreateService().LoadSettings().RefreshIntervalSeconds);
    }

    [Fact]
    public void LoadSettings_OneInvalidField_DoesNotDiscardTheValidOnes()
    {
        WriteRawSettings("""{"language":"fr-FR","refreshIntervalSeconds":300,"colorMode":"light"}""");

        var loaded = CreateService().LoadSettings();

        Assert.Equal(AppSettings.DefaultLanguage, loaded.Language);
        Assert.Equal(300, loaded.RefreshIntervalSeconds);
        Assert.Equal(AppSettings.LightColorMode, loaded.ColorMode);
    }
}
