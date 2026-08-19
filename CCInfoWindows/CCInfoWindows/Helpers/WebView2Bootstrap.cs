using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;

namespace CCInfoWindows.Helpers;

/// <summary>
/// The one place a WebView2 host is brought up over the app's isolated user data folder.
///
/// Both hosts share it — the visible login browser and MainView's hidden API-bridge browser — and
/// WebView2 refuses a second environment over an occupied folder whose options do not match the
/// first. Two independent copies of the creation call therefore break each other the moment one
/// gains an option the other lacks (AdditionalBrowserArguments, Language, a custom executable
/// folder): login never renders, or the bridge stays unbound and the dashboard shows cached data
/// only.
///
/// The delete-and-retry recovery below used to exist on the login path alone, so a corrupted profile
/// healed itself when the user logged in and failed forever on a cold start with a saved token.
/// </summary>
internal static class WebView2Bootstrap
{
    /// <summary>
    /// Binds <paramref name="webView"/> to a CoreWebView2 over the shared user data folder, recreating
    /// that folder once if the first attempt fails. Throws when even a fresh folder does not work —
    /// what an unusable browser means for the page is the caller's decision, not this helper's.
    /// </summary>
    internal static async Task EnsureAsync(WebView2 webView, string logSource)
    {
        var udfPath = AppPaths.WebView2UserDataFolder;

        try
        {
            await CreateAndBindAsync(webView, udfPath);
        }
        catch (Exception firstAttemptEx)
        {
            AppLog.Write(logSource, firstAttemptEx, "retrying with a fresh UDF");

            // Pitfall 1 from the WebView2 research: a half-written profile survives a crash, and every
            // later environment creation over it fails the same way until the folder is gone.
            if (Directory.Exists(udfPath))
            {
                Directory.Delete(udfPath, recursive: true);
            }

            await CreateAndBindAsync(webView, udfPath);
        }
    }

    private static async Task CreateAndBindAsync(WebView2 webView, string udfPath)
    {
        Directory.CreateDirectory(udfPath);

        var environment = await CoreWebView2Environment.CreateWithOptionsAsync(
            browserExecutableFolder: null,
            userDataFolder: udfPath,
            options: null);
        await webView.EnsureCoreWebView2Async(environment);
    }
}
