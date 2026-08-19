using System.Collections.Immutable;
using System.Text.Json;
using CCInfoWindows.Helpers;
using CCInfoWindows.Models;
using CCInfoWindows.Services.Interfaces;

namespace CCInfoWindows.Services;

/// <summary>
/// Reads/writes settings.json in %LOCALAPPDATA%\CCInfoWindows\.
///
/// Trust boundary: the file is user-writable, so every value returned by <see cref="LoadSettings"/>
/// is validated against the allow-lists on <see cref="AppSettings"/> first. Validating here rather
/// than at each consumer means a hand-edited value cannot reach the localizer, the poll timer or
/// AppWindow.MoveAndResize at all. Missing or corrupt files degrade to defaults.
///
/// Durability: writes go to &lt;file&gt;.tmp and are committed with File.Move(overwrite: true) by
/// <see cref="AtomicJsonFile"/>. File.WriteAllText truncates before writing, so an
/// interruption would leave a half-written file that LoadSettings turns into defaults — which the
/// next save then cements, losing language, interval, window geometry and dismissed-update state.
/// </summary>
public class SettingsService : ISettingsService
{
    private const string FileName = "settings.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _directory;

    private string SettingsFilePath => Path.Combine(_directory, FileName);

    public SettingsService() : this(AppPaths.DataDirectory) { }

    public SettingsService(string directoryOverride)
    {
        _directory = directoryOverride;
    }

    public AppSettings LoadSettings() =>
        // Validate(null) is the defaults instance, so a missing, empty or unreadable file lands on the
        // same allow-listed result as a hand-edited one.
        Validate(AtomicJsonFile.Read<AppSettings>(
            SettingsFilePath,
            JsonOptions,
            $"{nameof(SettingsService)}.{nameof(LoadSettings)}",
            "settings unreadable, falling back to defaults"));

    public void SaveSettings(AppSettings settings) =>
        AtomicJsonFile.Write(
            SettingsFilePath, settings, JsonOptions,
            $"{nameof(SettingsService)}.{nameof(SaveSettings)}", "settings not persisted");

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

    /// <summary>
    /// Coerces every out-of-domain field to its default and reports the coerced field names in one
    /// log entry, so a permanently invalid file costs one line per load instead of one per field.
    /// </summary>
    internal static AppSettings Validate(AppSettings? persisted)
    {
        if (persisted is null) return new AppSettings();

        List<string> coerced = [];

        persisted.RefreshIntervalSeconds = KeepSupported(
            persisted.RefreshIntervalSeconds,
            AppSettings.SupportedRefreshIntervalSeconds,
            AppSettings.DefaultRefreshIntervalSeconds,
            nameof(AppSettings.RefreshIntervalSeconds),
            coerced);

        persisted.SessionActivityThresholdMinutes = KeepSupported(
            persisted.SessionActivityThresholdMinutes,
            AppSettings.SupportedSessionActivityThresholdMinutes,
            AppSettings.DefaultSessionActivityThresholdMinutes,
            nameof(AppSettings.SessionActivityThresholdMinutes),
            coerced);

        persisted.SessionVisibilityWindowDays = KeepSupported(
            persisted.SessionVisibilityWindowDays,
            AppSettings.SupportedSessionVisibilityWindowDays,
            AppSettings.DefaultSessionVisibilityWindowDays,
            nameof(AppSettings.SessionVisibilityWindowDays),
            coerced);

        persisted.ColorMode = KeepSupportedText(
            persisted.ColorMode,
            AppSettings.SupportedColorModes,
            AppSettings.DefaultColorMode,
            nameof(AppSettings.ColorMode),
            coerced);

        persisted.Language = ValidateLanguage(persisted.Language, coerced);
        persisted.WindowState = ValidateWindowState(persisted.WindowState, coerced);
        persisted.DismissedUpdateVersion = ValidateVersionTag(persisted.DismissedUpdateVersion, coerced);
        persisted.LastSelectedSessionId = ValidateSessionId(persisted.LastSelectedSessionId, coerced);

        if (coerced.Count > 0)
        {
            AppLog.Write($"{nameof(SettingsService)}.{nameof(Validate)}",
                $"settings.json holds unsupported values, reset to defaults: {string.Join(", ", coerced)}");
        }

        return persisted;
    }

    private static int KeepSupported(
        int value, ImmutableArray<int> supported, int fallback, string fieldName, List<string> coerced)
    {
        if (supported.Contains(value)) return value;

        coerced.Add(fieldName);
        return fallback;
    }

    private static string KeepSupportedText(
        string? value, ImmutableArray<string> supported, string fallback, string fieldName, List<string> coerced)
    {
        // Case-insensitive match returning the canonical spelling: consumers compare ordinally, so
        // "Light" must resolve to "light" rather than falling through to the dark-mode default.
        foreach (var option in supported)
        {
            if (string.Equals(option, value, StringComparison.OrdinalIgnoreCase)) return option;
        }

        coerced.Add(fieldName);
        return fallback;
    }

    /// <summary>
    /// An unsupported language tag would make App.InitializeLocalizerAsync throw
    /// FailedToSetLanguageException on every launch, and the user cannot reach the language dropdown
    /// to repair it. Empty stays empty — that means "no explicit preference", not "invalid".
    /// </summary>
    private static string ValidateLanguage(string? language, List<string> coerced)
    {
        if (string.IsNullOrWhiteSpace(language)) return string.Empty;

        return KeepSupportedText(
            language,
            AppSettings.SupportedLanguages,
            AppSettings.DefaultLanguage,
            nameof(AppSettings.Language),
            coerced);
    }

    private static WindowState? ValidateWindowState(WindowState? state, List<string> coerced)
    {
        if (state is null) return null;

        if (IsInRange(state.Width, AppSettings.MinWindowDimensionPixels, AppSettings.MaxWindowDimensionPixels)
            && IsInRange(state.Height, AppSettings.MinWindowDimensionPixels, AppSettings.MaxWindowDimensionPixels)
            && IsInRange(state.X, -AppSettings.MaxWindowCoordinatePixels, AppSettings.MaxWindowCoordinatePixels)
            && IsInRange(state.Y, -AppSettings.MaxWindowCoordinatePixels, AppSettings.MaxWindowCoordinatePixels))
        {
            return state;
        }

        // Dropped rather than clamped: a geometry this far out is not a user preference worth
        // approximating, and ConfigureWindow's default size is a known-good fallback.
        coerced.Add(nameof(AppSettings.WindowState));
        return null;
    }

    /// <summary>
    /// UpdateService.ParseVersion is Version.Parse over a "v"-stripped tag and throws on anything
    /// else, which would disable update checks for good. Accepts the same shapes it does.
    /// </summary>
    private static string? ValidateVersionTag(string? tag, List<string> coerced)
    {
        if (tag is null) return null;

        if (tag.Length <= AppSettings.MaxDismissedUpdateVersionLength
            && Version.TryParse(tag.TrimStart('v'), out _))
        {
            return tag;
        }

        coerced.Add(nameof(AppSettings.DismissedUpdateVersion));
        return null;
    }

    /// <summary>
    /// The id is only ever compared against live session ids, so an unknown value is harmless — but
    /// an over-long or control-character-bearing one would still reach tooltips and the log.
    /// </summary>
    private static string? ValidateSessionId(string? sessionId, List<string> coerced)
    {
        if (sessionId is null) return null;

        if (sessionId.Length <= AppSettings.MaxLastSelectedSessionIdLength
            && !sessionId.Any(char.IsControl))
        {
            return sessionId;
        }

        coerced.Add(nameof(AppSettings.LastSelectedSessionId));
        return null;
    }

    private static bool IsInRange(int value, int minimum, int maximum) =>
        value >= minimum && value <= maximum;
}
