using System.Diagnostics.CodeAnalysis;

namespace CCInfoWindows.Helpers;

/// <summary>
/// The one implementation of the "absolute https URL on exactly this host" rule that every egress
/// allow-list in this app is built from, before a URL reaches a dangerous sink (WebView2 script
/// injection, Process.Start into the default browser).
///
/// Why it is shared: the rule used to be hand-written once per sink. A hardening prompted by one
/// sink — rejecting userinfo, rejecting a non-default port, normalising IDN before comparing — would
/// have landed on whichever copy the author was looking at, leaving the other sink accepting the form
/// just judged unsafe. It now has exactly one place to land.
///
/// The rule fails securely by construction: anything that does not parse as an absolute URI is
/// rejected, the https scheme is required explicitly rather than merely rejecting http, and the
/// authority is compared for ordinal-case-insensitive equality after parsing. A prefix or substring
/// test would also accept lookalike authorities such as "https://claude.ai.evil.example/".
/// Subdomains are rejected on purpose — every caller here talks to one apex host.
/// </summary>
internal static class UrlPolicy
{
    /// <summary>
    /// Returns true and the parsed URI when <paramref name="url"/> is an absolute https URL whose
    /// host equals <paramref name="host"/>. Callers that need the path use the parsed instance
    /// instead of re-parsing the string.
    /// </summary>
    internal static bool TryGetHttpsUriOn(
        string host, string? url, [NotNullWhen(true)] out Uri? uri)
    {
        uri = null;

        if (!Uri.TryCreate(url, UriKind.Absolute, out var parsed)) return false;
        if (parsed.Scheme != Uri.UriSchemeHttps) return false;
        if (!string.Equals(parsed.Host, host, StringComparison.OrdinalIgnoreCase)) return false;

        uri = parsed;
        return true;
    }
}
