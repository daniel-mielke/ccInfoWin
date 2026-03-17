namespace CCInfoWindows.Services.Interfaces;

/// <summary>
/// Abstraction for executing HTTP requests through WebView2's Chromium engine.
/// Bypasses Cloudflare bot protection by using browser-native TLS fingerprint and cookies.
/// </summary>
public interface IWebViewBridge
{
    /// <summary>
    /// Whether the bridge has been initialized with a WebView2 instance.
    /// </summary>
    bool IsInitialized { get; }

    /// <summary>
    /// Executes a GET request via WebView2's fetch() and returns the response body as string.
    /// Returns null on network error or non-OK status. Throws on 401 (auth expired).
    /// </summary>
    Task<string?> FetchJsonAsync(string url);

    /// <summary>
    /// Releases the WebView2 reference and drains all pending requests on logout.
    /// Prevents 30-second ghost hangs from in-flight fetch requests after navigation away.
    /// </summary>
    void Reset();
}
