using System.Net;
using System.Text.Json;
using CCInfoWindows.Messages;
using CCInfoWindows.Models;
using CCInfoWindows.Services.Interfaces;
using CommunityToolkit.Mvvm.Messaging;

namespace CCInfoWindows.Services;

/// <summary>
/// HTTP client for Claude API usage data with retry logic, disk caching, and auth error handling.
/// Constructs requests to https://claude.ai/api/organizations/{orgId}/usage.
/// </summary>
public class ClaudeApiService : IClaudeApiService
{
    private const string BaseUrl = "https://claude.ai";
    private const int MaxAttempts = 3;

    private readonly HttpClient _httpClient;
    private readonly ICredentialService _credentialService;
    private readonly string _cacheFilePath;

    private UsageResponse? _cachedUsage;

    /// <param name="httpClient">Singleton HttpClient from DI.</param>
    /// <param name="credentialService">Credential store for session token and org ID.</param>
    /// <param name="cacheDirectory">
    /// Override cache directory for testing. Defaults to %LOCALAPPDATA%\CCInfoWindows.
    /// </param>
    public ClaudeApiService(
        HttpClient httpClient,
        ICredentialService credentialService,
        string? cacheDirectory = null)
    {
        _httpClient = httpClient;
        _credentialService = credentialService;

        var dir = cacheDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CCInfoWindows");
        _cacheFilePath = Path.Combine(dir, "usage_cache.json");
    }

    public async Task<UsageResponse?> FetchUsageAsync(CancellationToken ct = default)
    {
        var sessionKey = _credentialService.GetSessionToken();
        if (sessionKey is null)
        {
            return null;
        }

        var orgId = _credentialService.GetOrganizationId();
        if (orgId is null)
        {
            orgId = await TryMigrateOrgIdAsync(sessionKey, ct);
            if (orgId is null)
            {
                return null;
            }
        }

        var encodedOrgId = Uri.EscapeDataString(orgId);
        var url = $"{BaseUrl}/api/organizations/{encodedOrgId}/usage";

        for (int attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.TryAddWithoutValidation("Cookie", $"sessionKey={sessionKey}");
                request.Headers.TryAddWithoutValidation("anthropic-client-platform", "web_claude_ai");

                var response = await _httpClient.SendAsync(request, ct);

                if (response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    WeakReferenceMessenger.Default.Send(new AuthStateChangedMessage(false));
                    return null;
                }

                if (response.StatusCode == HttpStatusCode.TooManyRequests ||
                    (int)response.StatusCode >= 500)
                {
                    if (attempt < MaxAttempts)
                    {
                        await Task.Delay(attempt * 1000, ct);
                        continue;
                    }
                    return null;
                }

                response.EnsureSuccessStatusCode();

                var stream = await response.Content.ReadAsStreamAsync(ct);
                var usage = await JsonSerializer.DeserializeAsync<UsageResponse>(stream, cancellationToken: ct);

                if (usage is not null)
                {
                    _cachedUsage = usage;
                    await SaveCacheAsync(usage);
                }

                return usage;
            }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested)
            {
                // Timeout -- retry
                if (attempt < MaxAttempts)
                {
                    await Task.Delay(attempt * 1000, ct);
                    continue;
                }
                return null;
            }
            catch (HttpRequestException)
            {
                // Network error -- retry
                if (attempt < MaxAttempts)
                {
                    await Task.Delay(attempt * 1000, ct);
                    continue;
                }
                return null;
            }
        }

        return null;
    }

    public UsageResponse? GetCachedUsage() => _cachedUsage;

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
        catch (Exception)
        {
            // Corrupt cache file -- safe to ignore
            return null;
        }
    }

    /// <summary>
    /// Fetches org ID from /api/organizations when lastActiveOrg cookie was not captured.
    /// Extracts first organization's uuid and persists it for future use.
    /// </summary>
    private async Task<string?> TryMigrateOrgIdAsync(string sessionKey, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/api/organizations");
            request.Headers.TryAddWithoutValidation("Cookie", $"sessionKey={sessionKey}");
            request.Headers.TryAddWithoutValidation("anthropic-client-platform", "web_claude_ai");

            var response = await _httpClient.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();

            var stream = await response.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

            var orgs = doc.RootElement;
            if (orgs.GetArrayLength() > 0)
            {
                var uuid = orgs[0].GetProperty("uuid").GetString();
                if (!string.IsNullOrEmpty(uuid))
                {
                    _credentialService.SaveOrganizationId(uuid);
                    return uuid;
                }
            }
        }
        catch (Exception)
        {
            // Migration failed -- user needs to re-login
        }

        return null;
    }
}
