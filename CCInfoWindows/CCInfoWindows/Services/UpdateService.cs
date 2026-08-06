using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using CCInfoWindows.Helpers;
using CCInfoWindows.Models;
using CCInfoWindows.Services.Interfaces;

namespace CCInfoWindows.Services;

/// <summary>
/// Checks GitHub Releases API hourly for new versions and fires UpdateAvailable when a newer release is found.
/// Respects DismissedUpdateVersion from settings to avoid re-notifying for dismissed versions.
/// Egress hosts: api.github.com (this check) and github.com (the release page opened on click).
/// </summary>
public class UpdateService : IUpdateService
{
    private const string GitHubApiUrl = "https://api.github.com/repos/daniel-mielke/ccInfoWin/releases/latest";
    private const string ReleasePageHost = "github.com";
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
            // ConfigureAwait(false): this is service code, and the only consumer of
            // UpdateAvailable marshals to the dispatcher itself (MainViewModel.OnUpdateAvailable).
            var release = await _httpClient
                .GetFromJsonAsync<GitHubRelease>(GitHubApiUrl)
                .ConfigureAwait(false);

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

            if (!IsReleasePageUrl(release.HtmlUrl)) return;

            UpdateAvailable?.Invoke(release.TagName, release.HtmlUrl);
        }
        catch (Exception ex)
        {
            // Network, JSON and version-parse failures must not surface to the UI, but they must
            // leave a trace: a swallowed failure here means silently broken update checks forever.
            AppLog.Write("UpdateService.CheckForUpdate", ex, "update check failed");
        }
    }

    /// <summary>
    /// Allow-list for the URL handed to the browser. The host is compared after parsing rather
    /// than by prefix, so a lookalike authority cannot be launched from a spoofed API response.
    /// </summary>
    public static bool IsReleasePageUrl(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
        uri.Scheme == Uri.UriSchemeHttps &&
        string.Equals(uri.Host, ReleasePageHost, StringComparison.OrdinalIgnoreCase);

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
        try
        {
            using var timer = new PeriodicTimer(CheckInterval);
            while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
            {
                await CheckForUpdateAsync().ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // StopPeriodicCheck cancelled the token. The loop is fire-and-forget, so letting the
            // cancellation escape would leave an unobserved faulted task behind on every logout.
        }
    }

    private static Version? GetLocalVersion()
    {
        return Assembly.GetExecutingAssembly().GetName().Version;
    }
}
