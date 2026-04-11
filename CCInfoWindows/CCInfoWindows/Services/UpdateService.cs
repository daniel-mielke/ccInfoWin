using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using CCInfoWindows.Models;
using CCInfoWindows.Services.Interfaces;

namespace CCInfoWindows.Services;

/// <summary>
/// Checks GitHub Releases API hourly for new versions and fires UpdateAvailable when a newer release is found.
/// Respects DismissedUpdateVersion from settings to avoid re-notifying for dismissed versions.
/// </summary>
public class UpdateService : IUpdateService
{
    private const string GitHubApiUrl = "https://api.github.com/repos/daniel-mielke/ccInfoWin/releases/latest";
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(1);

    private readonly HttpClient _httpClient;
    private readonly ISettingsService _settingsService;
    private CancellationTokenSource? _cancellationTokenSource;

    public event Action<string, string>? UpdateAvailable;

    public UpdateService(HttpClient httpClient, ISettingsService settingsService)
    {
        _httpClient = httpClient;
        _settingsService = settingsService;

        var localVersion = GetLocalVersion()?.ToString() ?? "0.0.0";
        _httpClient.DefaultRequestHeaders.UserAgent.TryParseAdd($"CCInfoWindows/{localVersion}");
    }

    public async Task CheckForUpdateAsync()
    {
        try
        {
            var release = await _httpClient.GetFromJsonAsync<GitHubRelease>(GitHubApiUrl);

            if (release == null || release.Prerelease) return;

            var remoteVersion = ParseVersion(release.TagName);
            var localVersion = GetLocalVersion() ?? new Version(0, 0, 0);

            if (!IsNewerVersion(release.TagName, localVersion)) return;

            var settings = _settingsService.LoadSettings();
            if (settings.DismissedUpdateVersion != null)
            {
                var dismissedVersion = ParseVersion(settings.DismissedUpdateVersion);
                if (remoteVersion <= dismissedVersion) return;
            }

            if (!release.HtmlUrl.StartsWith("https://github.com/", StringComparison.OrdinalIgnoreCase))
                return;

            UpdateAvailable?.Invoke(release.TagName, release.HtmlUrl);
        }
        catch
        {
            // Silent failure — network errors must not surface to UI
        }
    }

    public void StartPeriodicCheck()
    {
        _cancellationTokenSource?.Cancel();
        _cancellationTokenSource?.Dispose();
        _cancellationTokenSource = new CancellationTokenSource();
        var token = _cancellationTokenSource.Token;

        _ = RunPeriodicCheckLoopAsync(token);
    }

    public void StopPeriodicCheck()
    {
        _cancellationTokenSource?.Cancel();
        _cancellationTokenSource?.Dispose();
        _cancellationTokenSource = null;
    }

    /// <summary>
    /// Parses a SemVer version string, stripping any leading 'v' prefix.
    /// </summary>
    public static Version ParseVersion(string tagName)
    {
        return Version.Parse(tagName.TrimStart('v'));
    }

    /// <summary>
    /// Returns true if the remote version tag is strictly newer than the given local version.
    /// </summary>
    public static bool IsNewerVersion(string remoteTag, Version localVersion)
    {
        var remoteVersion = ParseVersion(remoteTag);
        return remoteVersion > localVersion;
    }

    private async Task RunPeriodicCheckLoopAsync(CancellationToken token)
    {
        using var timer = new PeriodicTimer(CheckInterval);
        while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
        {
            await CheckForUpdateAsync().ConfigureAwait(false);
        }
    }

    private static Version? GetLocalVersion()
    {
        return Assembly.GetExecutingAssembly().GetName().Version;
    }
}
