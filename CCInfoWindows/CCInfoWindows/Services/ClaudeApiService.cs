using System.Text.Json;
using CCInfoWindows.Helpers;
using CCInfoWindows.Messages;
using CCInfoWindows.Models;
using CCInfoWindows.Services.Interfaces;
using CommunityToolkit.Mvvm.Messaging;

namespace CCInfoWindows.Services;

/// <summary>
/// Fetches Claude API usage data via WebView2 bridge with retry logic, disk caching, and auth error handling.
/// Routes requests through Chromium's fetch() to bypass Cloudflare bot protection.
/// </summary>
public class ClaudeApiService : IClaudeApiService
{
    private const string BaseUrl = ClaudeAiUrlPolicy.Origin;
    private const int MaxAttempts = 3;
    private const int RetryBaseDelayMs = 1_000;
    private const string CacheFileName = "usage_cache.json";

    private readonly IWebViewBridge _bridge;
    private readonly ICredentialService _credentialService;
    private readonly string _cacheFilePath;

    private UsageResponse? _cachedUsage;

    /// <param name="bridge">WebView2 bridge for Cloudflare-safe HTTP requests.</param>
    /// <param name="credentialService">Credential store for session token and org ID.</param>
    /// <param name="cacheDirectory">
    /// Override cache directory for testing. Defaults to %LOCALAPPDATA%\CCInfoWindows.
    /// </param>
    public ClaudeApiService(
        IWebViewBridge bridge,
        ICredentialService credentialService,
        string? cacheDirectory = null)
    {
        _bridge = bridge;
        _credentialService = credentialService;

        var dir = cacheDirectory ?? AppPaths.DataDirectory;
        _cacheFilePath = Path.Combine(dir, CacheFileName);
    }

    public async Task<UsageResponse?> FetchUsageAsync(CancellationToken ct = default)
    {
        if (!_bridge.IsInitialized)
        {
            throw new InvalidOperationException("WebView2 bridge is not initialized. Restart the app or re-login.");
        }

        var orgId = _credentialService.GetOrganizationId();
        if (orgId is null)
        {
            // Migration saves the UUID but does NOT retry the current API call — caller waits for next poll cycle
            orgId = await TryMigrateOrgIdAsync(ct);
            if (orgId is null)
            {
                throw new InvalidOperationException("Organization ID could not be retrieved. Try logging out and back in.");
            }
        }

        var url = BuildUsageUrl(orgId);

        Exception? lastException = null;
        var orgIdReresolved = false;

        for (int attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var responseBody = await _bridge.FetchJsonAsync(url);
                if (responseBody is null) return null;

                var usage = JsonSerializer.Deserialize<UsageResponse>(responseBody);

                if (usage is not null)
                {
                    _cachedUsage = usage;
                    await TrySaveCacheAsync(usage);
                }

                return usage;
            }
            catch (SessionExpiredException ex)
            {
                AppLog.Write("ClaudeApiService.FetchUsage", ex, "session expired — requesting re-login");
                WeakReferenceMessenger.Default.Send(new AuthStateChangedMessage(false));
                return null;
            }
            catch (HttpFetchException ex) when (ex.StatusCode is 403 or 404 && !orgIdReresolved)
            {
                // A 403/404 on this endpoint almost always means the cached org id no longer
                // belongs to the current session (e.g. re-login into a different account).
                // Re-resolve once against /api/organizations and retry with the fresh id
                // instead of failing every poll until the user logs out manually.
                orgIdReresolved = true;
                _credentialService.ClearOrganizationId();

                var freshOrgId = await TryMigrateOrgIdAsync(ct);
                if (freshOrgId is null || freshOrgId == orgId) throw;

                AppLog.Write("ClaudeApiService.FetchUsage", $"org id re-resolved after HTTP {ex.StatusCode}");
                orgId = freshOrgId;
                url = BuildUsageUrl(orgId);
                attempt--;  // this attempt probed a stale id — don't spend it
            }
            catch (HttpFetchException ex) when (ex.StatusCode is >= 400 and < 500)
            {
                // Client errors (4xx) are not transient — no retry
                throw;
            }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested)
            {
                lastException = new TimeoutException($"Request timed out on attempt {attempt}/{MaxAttempts}");
                if (attempt < MaxAttempts)
                {
                    await Task.Delay(attempt * RetryBaseDelayMs, ct);
                    continue;
                }
            }
            catch (Exception ex) when (attempt < MaxAttempts)
            {
                lastException = ex;
                await Task.Delay(attempt * RetryBaseDelayMs, ct);
            }
            catch (Exception ex)
            {
                lastException = ex;
            }
        }

        throw lastException ?? new InvalidOperationException("API request failed after all retry attempts.");
    }

    public UsageResponse? GetCachedUsage() => _cachedUsage;

    /// <summary>
    /// ORGID-01 (D-OG-01): public org-list endpoint. Extracted from the private TryMigrateOrgIdAsync —
    /// the same /api/organizations endpoint. Returns parsed entries; never throws to the caller
    /// (returns empty list on any failure).
    /// </summary>
    public async Task<IReadOnlyList<OrganizationInfo>> ListAvailableOrganizationsAsync(CancellationToken ct = default)
    {
        try
        {
            ct.ThrowIfCancellationRequested();

            if (!_bridge.IsInitialized)
            {
                AppLog.Write("ClaudeApiService.ListAvailableOrganizations", "bridge not initialized");
                return Array.Empty<OrganizationInfo>();
            }

            var responseBody = await _bridge.FetchJsonAsync($"{BaseUrl}/api/organizations");
            if (responseBody is null)
            {
                AppLog.Write("ClaudeApiService.ListAvailableOrganizations", "null response body");
                return Array.Empty<OrganizationInfo>();
            }

            using var doc = JsonDocument.Parse(responseBody);
            var root = doc.RootElement;

            if (root.ValueKind != JsonValueKind.Array)
            {
                AppLog.Write(
                    "ClaudeApiService.ListAvailableOrganizations",
                    $"expected a JSON array, got {root.ValueKind}");
                return Array.Empty<OrganizationInfo>();
            }

            var list = new List<OrganizationInfo>(root.GetArrayLength());
            foreach (var element in root.EnumerateArray())
            {
                if (!element.TryGetProperty("uuid", out var uuidProp)) continue;
                var uuid = uuidProp.GetString();
                if (string.IsNullOrEmpty(uuid)) continue;

                // Name fallback chain: name → uuid (defensive — API typically returns name)
                var name = element.TryGetProperty("name", out var nameProp)
                    ? nameProp.GetString() ?? uuid
                    : uuid;

                list.Add(new OrganizationInfo(uuid, name));
            }

            return list;
        }
        catch (SessionExpiredException ex)
        {
            AppLog.Write("ClaudeApiService.ListAvailableOrganizations", ex, "session expired — requesting re-login");
            WeakReferenceMessenger.Default.Send(new AuthStateChangedMessage(false));
            return Array.Empty<OrganizationInfo>();
        }
        catch (Exception ex)
        {
            // Defensive — caller renders the empty-state in the dialog
            AppLog.Write("ClaudeApiService.ListAvailableOrganizations", ex);
            return Array.Empty<OrganizationInfo>();
        }
    }

    public async Task SaveCacheAsync(UsageResponse data)
    {
        var dir = Path.GetDirectoryName(_cacheFilePath)!;
        Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(data, new JsonSerializerOptions
        {
            WriteIndented = false
        });
        await File.WriteAllTextAsync(_cacheFilePath, json);
    }

    public async Task<UsageResponse?> LoadCacheAsync()
    {
        try
        {
            if (!File.Exists(_cacheFilePath))
            {
                return null;
            }

            var json = await File.ReadAllTextAsync(_cacheFilePath);
            var usage = JsonSerializer.Deserialize<UsageResponse>(json);
            _cachedUsage = usage;
            return usage;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // Corrupt or inaccessible cache file — safe to ignore. UnauthorizedAccessException
            // belongs here too: an ACL'd cache file must degrade to "no cache", not escape into
            // the caller, which is where it used to be mistaken for a session expiry.
            AppLog.Write("ClaudeApiService.LoadCache", ex, "cached usage could not be read");
            return null;
        }
    }

    /// <summary>
    /// Drops the cached usage snapshot from memory and from disk so a following login cannot
    /// render the previous account's figures. Best-effort on the file: the in-memory copy is
    /// always cleared, even when the delete fails.
    /// </summary>
    public void ClearCache()
    {
        _cachedUsage = null;

        try
        {
            if (File.Exists(_cacheFilePath))
            {
                File.Delete(_cacheFilePath);
            }
        }
        catch (Exception ex)
        {
            AppLog.Write("ClaudeApiService.ClearCache", ex, "cached usage file could not be deleted");
        }
    }

    private static string BuildUsageUrl(string orgId) =>
        $"{BaseUrl}/api/organizations/{Uri.EscapeDataString(orgId)}/usage";

    /// <summary>
    /// The disk cache is best-effort. Its failures must never reach the retry loop or the
    /// session-expiry handler: the filesystem raises <see cref="UnauthorizedAccessException"/>
    /// for ACL and read-only failures, which used to be misread as HTTP 401 and forced a logout.
    /// </summary>
    private async Task TrySaveCacheAsync(UsageResponse usage)
    {
        try
        {
            await SaveCacheAsync(usage);
        }
        catch (Exception ex)
        {
            AppLog.Write("ClaudeApiService.SaveCache", ex, "usage cache could not be written");
        }
    }

    /// <summary>
    /// Fetches org ID from /api/organizations when lastActiveOrg cookie was not captured.
    /// Delegates to ListAvailableOrganizationsAsync (DRY) — preserves first-org auto-pick behavior.
    /// </summary>
    private async Task<string?> TryMigrateOrgIdAsync(CancellationToken ct)
    {
        var orgs = await ListAvailableOrganizationsAsync(ct);
        if (orgs.Count == 0)
        {
            return null;
        }

        // Preserve original first-org auto-pick behavior (cookie-fallback case)
        var first = orgs[0];
        _credentialService.SaveOrganizationId(first.Uuid);
        return first.Uuid;
    }
}
