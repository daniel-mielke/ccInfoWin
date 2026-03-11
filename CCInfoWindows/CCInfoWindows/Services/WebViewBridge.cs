using System.Collections.Concurrent;
using System.Text.Json;
using CCInfoWindows.Services.Interfaces;
using Microsoft.UI.Dispatching;
using Microsoft.Web.WebView2.Core;

namespace CCInfoWindows.Services;

/// <summary>
/// Routes HTTP requests through WebView2's Chromium engine to bypass Cloudflare bot protection.
/// Uses WebMessageReceived callback pattern because ExecuteScriptAsync cannot await JS Promises.
/// </summary>
public class WebViewBridge : IWebViewBridge
{
    private CoreWebView2? _coreWebView;
    private DispatcherQueue? _dispatcherQueue;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<string?>> _pending = new();

    public bool IsInitialized => _coreWebView is not null;

    /// <summary>
    /// Binds the bridge to a WebView2 instance that has already navigated to claude.ai.
    /// Must be called from UI thread after WebView2 initialization.
    /// </summary>
    public void Initialize(CoreWebView2 coreWebView, DispatcherQueue dispatcherQueue)
    {
        _coreWebView = coreWebView;
        _dispatcherQueue = dispatcherQueue;
        _coreWebView.WebMessageReceived += OnWebMessageReceived;
    }

    /// <summary>
    /// Clears the WebView2 reference (e.g., on logout).
    /// </summary>
    public void Reset()
    {
        if (_coreWebView is not null)
        {
            _coreWebView.WebMessageReceived -= OnWebMessageReceived;
        }
        _coreWebView = null;
        _dispatcherQueue = null;
    }

    public async Task<string?> FetchJsonAsync(string url)
    {
        if (_coreWebView is null || _dispatcherQueue is null)
        {
            return null;
        }

        var requestId = Guid.NewGuid().ToString("N");
        var tcs = new TaskCompletionSource<string?>();
        _pending[requestId] = tcs;

        // Inject JS that does fetch() and posts result back via window.chrome.webview.postMessage.
        // Cannot use ExecuteScriptAsync with async/await JS — it returns the Promise object, not the resolved value.
        var script = $$"""
            (function() {
                fetch('{{url}}', { credentials: 'include' })
                    .then(function(r) {
                        return r.text().then(function(body) {
                            window.chrome.webview.postMessage(JSON.stringify({
                                id: '{{requestId}}',
                                status: r.status,
                                body: body
                            }));
                        });
                    })
                    .catch(function(e) {
                        window.chrome.webview.postMessage(JSON.stringify({
                            id: '{{requestId}}',
                            status: 0,
                            body: e.message
                        }));
                    });
            })();
            """;

        var enqueued = _dispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                _coreWebView?.ExecuteScriptAsync(script);
            }
            catch (Exception)
            {
                if (_pending.TryRemove(requestId, out var removed))
                {
                    removed.TrySetResult(null);
                }
            }
        });

        if (!enqueued)
        {
            _pending.TryRemove(requestId, out _);
            return null;
        }

        // Timeout after 30 seconds
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        cts.Token.Register(() =>
        {
            if (_pending.TryRemove(requestId, out var removed))
            {
                removed.TrySetResult(null);
            }
        });

        return await tcs.Task;
    }

    private void OnWebMessageReceived(CoreWebView2 sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        try
        {
            var json = args.TryGetWebMessageAsString();
            if (json is null) return;

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("id", out var idProp)) return;
            var id = idProp.GetString();
            if (id is null || !_pending.TryRemove(id, out var tcs)) return;

            var status = root.GetProperty("status").GetInt32();
            var body = root.GetProperty("body").GetString();

            if (status == 401)
            {
                tcs.TrySetException(new UnauthorizedAccessException("Session expired (401)"));
                return;
            }

            tcs.TrySetResult(status is >= 200 and < 300 ? body : null);
        }
        catch (Exception)
        {
            // Malformed message from page — ignore
        }
    }
}
