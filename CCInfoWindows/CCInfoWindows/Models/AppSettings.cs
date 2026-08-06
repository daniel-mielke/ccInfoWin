using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace CCInfoWindows.Models;

/// <summary>
/// Persisted window state (position and size).
/// </summary>
public record WindowState(int X, int Y, int Width, int Height);

/// <summary>
/// Application settings persisted to settings.json.
///
/// settings.json lives in user-writable %LOCALAPPDATA% and is therefore untrusted input. The
/// Supported* allow-lists below define the only values a consumer may ever observe;
/// SettingsService.LoadSettings coerces anything else to the matching Default* before returning,
/// so no consumer has to re-validate. Each Supported* list is index-aligned with the corresponding
/// SettingsView ComboBox items — reordering one requires reordering the other.
/// </summary>
public class AppSettings
{
    /// <summary>Refresh interval meaning "no automatic polling" (the "Manual" dropdown option).</summary>
    public const int ManualRefreshSeconds = 0;

    public const int DefaultRefreshIntervalSeconds = 60;

    public const string DarkColorMode = "dark";
    public const string LightColorMode = "light";
    public const string DefaultColorMode = DarkColorMode;

    public const string GermanLanguage = "de-DE";
    public const string EnglishLanguage = "en-US";
    public const string DefaultLanguage = GermanLanguage;

    public const int DefaultSessionActivityThresholdMinutes = 30;

    /// <summary>Visibility window meaning "show sessions of any age".</summary>
    public const int UnlimitedSessionVisibilityWindowDays = 0;

    public const int DefaultSessionVisibilityWindowDays = 30;

    /// <summary>
    /// Bounds for a restored window. The lower bound keeps a degenerate 0-pixel window off the
    /// screen; the upper bound keeps <c>X + Width / 2</c> in WindowHelper.IsPositionOnScreen from
    /// overflowing, which would otherwise turn an absurd persisted size into a plausible-looking
    /// display point.
    /// </summary>
    public const int MinWindowDimensionPixels = 200;

    public const int MaxWindowDimensionPixels = 32_000;

    public const int MaxWindowCoordinatePixels = 32_000;

    /// <summary>Long enough for "v10.20.30.40", short enough that a padded value is rejected.</summary>
    public const int MaxDismissedUpdateVersionLength = 32;

    /// <summary>A session id is an encoded project directory name, so the Win32 path limit bounds it.</summary>
    public const int MaxLastSelectedSessionIdLength = 260;

    public static readonly ImmutableArray<int> SupportedRefreshIntervalSeconds =
        [30, DefaultRefreshIntervalSeconds, 120, 300, 600, ManualRefreshSeconds];

    public static readonly ImmutableArray<string> SupportedColorModes =
        [DarkColorMode, LightColorMode];

    public static readonly ImmutableArray<string> SupportedLanguages =
        [GermanLanguage, EnglishLanguage];

    public static readonly ImmutableArray<int> SupportedSessionActivityThresholdMinutes =
        [15, DefaultSessionActivityThresholdMinutes, 60, 120];

    public static readonly ImmutableArray<int> SupportedSessionVisibilityWindowDays =
        [7, DefaultSessionVisibilityWindowDays, 90, UnlimitedSessionVisibilityWindowDays];

    [JsonPropertyName("windowState")]
    public WindowState? WindowState { get; set; }

    [JsonPropertyName("refreshIntervalSeconds")]
    public int RefreshIntervalSeconds { get; set; } = DefaultRefreshIntervalSeconds;

    [JsonPropertyName("colorMode")]
    public string ColorMode { get; set; } = DefaultColorMode;

    [JsonPropertyName("lastSelectedSessionId")]
    public string? LastSelectedSessionId { get; set; }

    [JsonPropertyName("sessionActivityThresholdMinutes")]
    public int SessionActivityThresholdMinutes { get; set; } = DefaultSessionActivityThresholdMinutes;

    [JsonPropertyName("pricingSource")]
    public string PricingSource { get; set; } = "Unknown";

    [JsonPropertyName("lastPricingFetch")]
    public DateTimeOffset? LastPricingFetch { get; set; }

    [JsonPropertyName("dismissedUpdateVersion")]
    public string? DismissedUpdateVersion { get; set; }

    /// <summary>
    /// Empty or null means "no explicit preference" — App.InitializeLocalizerAsync then leaves the
    /// localizer on its own default language instead of calling SetLanguage.
    /// </summary>
    [JsonPropertyName("language")]
    public string Language { get; set; } = DefaultLanguage;

    [JsonPropertyName("sessionVisibilityWindowDays")]
    public int SessionVisibilityWindowDays { get; set; } = DefaultSessionVisibilityWindowDays;

    [JsonPropertyName("sessionVisibilityMigrationShown")]
    public bool SessionVisibilityMigrationShown { get; set; }
}
