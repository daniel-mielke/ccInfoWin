using System.Diagnostics.CodeAnalysis;
using CCInfoWindows.Helpers;

namespace CCInfoWindows.Services;

/// <summary>
/// Single definition of the claude.ai egress allow-list, shared by the WebView2 bridge and the
/// login flow. The authority must be compared after parsing: a prefix test against
/// "https://claude.ai" also accepts lookalike hosts such as "https://claude.ai.evil.example/",
/// which would let an untrusted origin host the bridge script.
/// Subdomains are rejected on purpose — claude.ai serves the login flow and the API from the
/// apex host, and the prefix test it replaces never matched a subdomain either.
/// </summary>
internal static class ClaudeAiUrlPolicy
{
    internal const string Origin = "https://claude.ai";
    internal const string AllowedHost = "claude.ai";

    /// <summary>
    /// Returns true and the parsed URI when <paramref name="url"/> is an absolute https URL on
    /// the allowed host. Callers that need the path use the parsed instance instead of
    /// re-parsing the string.
    /// </summary>
    internal static bool TryGetAllowedUri(string? url, [NotNullWhen(true)] out Uri? allowed) =>
        UrlPolicy.TryGetHttpsUriOn(AllowedHost, url, out allowed);

    internal static bool IsAllowed(string? url) => TryGetAllowedUri(url, out _);
}
