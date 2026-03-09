# Domain Pitfalls

**Domain:** Windows desktop monitoring app (WinUI 3 / WebView2 / Win2D / FileSystemWatcher)
**Project:** ccInfo Windows
**Researched:** 2026-03-09

---

## Critical Pitfalls

Mistakes that cause rewrites, crashes in production, or fundamental architectural problems.

### Pitfall 1: Win2D CanvasControl Memory Leak via Reference Cycles

**What goes wrong:** Win2D controls (CanvasControl, CanvasAnimatedControl) use C++ reference counting while XAML pages use .NET garbage collection. When you subscribe to Win2D events (e.g., `Draw`, `CreateResources`) from a XAML page, a reference cycle forms: Page -> Control -> EventDelegate -> Page. The GC cannot detect this cycle because one side is reference-counted C++. The control and its GPU resources are never freed.

**Why it happens:** This is inherent to the C#/C++ interop boundary. Every Win2D event subscription creates a strong reference chain that crosses the GC/refcount boundary.

**Consequences:** GPU memory grows continuously. In a monitoring app that runs for hours/days, this causes visible memory creep and eventually GPU resource exhaustion. The app will slow down, the chart will stop rendering, or Windows will kill the process.

**Prevention:**
```csharp
// In your page's Unloaded handler:
void Page_Unloaded(object sender, RoutedEventArgs e)
{
    this.usageChart.RemoveFromVisualTree();
    this.usageChart = null;
}
```
Every page containing a Win2D control MUST call `RemoveFromVisualTree()` and null the reference in the `Unloaded` event. This applies to UsageChartControl and any custom control wrapping CanvasControl.

**Detection:** Monitor GPU memory in Task Manager during extended use. If it grows steadily without stabilizing, you have this leak.

**Phase:** Must be implemented from the very first Win2D integration (chart rendering phase). Not something to "fix later."

**Confidence:** HIGH (official Win2D documentation at microsoft.github.io/Win2D)

---

### Pitfall 2: WebView2 Initialization Failures and User Data Folder Corruption

**What goes wrong:** `EnsureCoreWebView2Async()` can fail silently, hang indefinitely, or throw `E_UNEXPECTED (0x8000FFFF)` on launch. Three root causes: (a) fire-and-forget async call without proper await, (b) corrupted User Data Folder from a previous GPU/renderer crash, (c) permission issues on the UDF path.

**Why it happens:** WebView2 spawns a separate Chromium process. If the previous session's cache/profile directory was damaged (GPU crash, forced termination, disk full), WebView2 cannot reuse it and throws cryptically. Unpackaged apps sometimes default to a UDF path that is not writable.

**Consequences:** Login screen never appears. Users cannot authenticate. The app is completely non-functional with no useful error message.

**Prevention:**
1. Always `await` the `EnsureCoreWebView2Async()` call -- never fire-and-forget.
2. Explicitly set the User Data Folder to `%LOCALAPPDATA%\CCInfoWindows\WebView2` via `CoreWebView2Environment.CreateAsync()`.
3. Wrap initialization in try/catch. On failure, delete the UDF and retry once before showing an error.
4. Set a timeout (e.g., 30 seconds) on initialization and show a meaningful error if it expires.

```csharp
var env = await CoreWebView2Environment.CreateAsync(
    null,
    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CCInfoWindows", "WebView2"));
await webView.EnsureCoreWebView2Async(env);
```

**Detection:** Test on a clean machine with no Edge profile. Test after force-killing the app during login. Test with a read-only UDF path.

**Phase:** Authentication phase (Phase 1). This is the first thing users interact with.

**Confidence:** HIGH (multiple documented issues on MicrosoftEdge/WebView2Feedback)

---

### Pitfall 3: WebView2 CookieManager Threading Constraint

**What goes wrong:** `CookieManager.GetCookiesAsync()` returns cookies successfully from any thread, but accessing individual cookie properties (Name, Value, Domain) from a non-UI thread throws an exception. The API looks thread-safe but is not.

**Why it happens:** WebView2 cookie objects have thread affinity to the UI thread. The async method itself does not enforce this, creating a trap where the call succeeds but property access fails.

**Consequences:** Intermittent crashes during cookie extraction after login. Hard to reproduce because timing-dependent.

**Prevention:** Always call `GetCookiesAsync()` AND access cookie properties on the UI thread. Do not offload cookie extraction to a background task.

```csharp
// CORRECT: Everything on UI thread
var cookies = await webView.CoreWebView2.CookieManager.GetCookiesAsync("https://claude.ai");
foreach (var cookie in cookies)
{
    // Access properties here, on the UI thread
    var name = cookie.Name;
    var value = cookie.Value;
}
```

**Detection:** Run cookie extraction repeatedly under load. If it works 90% of the time but occasionally crashes, this is the cause.

**Phase:** Authentication phase.

**Confidence:** HIGH (documented in WebView2Feedback issue #1283)

---

### Pitfall 4: Unpackaged App Deployment - Missing Windows App Runtime

**What goes wrong:** The published EXE runs perfectly on the dev machine but fails to start on end-user machines with a cryptic error or no error at all. The app requires the Windows App SDK Runtime, which is NOT bundled with Windows (not even Windows 11).

**Why it happens:** Unpackaged WinUI 3 apps have a hard dependency on the Windows App Runtime (WindowsAppRuntime). Unlike .NET Runtime, this is a separate install that most users do not have. The Bootstrapper API must be called before any WinUI 3 features work.

**Consequences:** Users download the app, double-click the EXE, and nothing happens. They assume it is broken and leave. This is the #1 deployment killer for WinUI 3 apps.

**Prevention:** Two options:
1. **Recommended:** Bundle the WindowsAppRuntime installer as a prerequisite in the Inno Setup script. Chain it silently during installation.
2. **Alternative:** Use `WindowsAppSDKSelfContained=true` in the .csproj, which embeds the runtime (~200 MB size increase).

```iss
; Inno Setup prerequisite example
[Run]
Filename: "{tmp}\WindowsAppRuntimeInstall.exe"; Parameters: "--quiet"; \
    StatusMsg: "Installing Windows App Runtime..."; Flags: waituntilterminated
```

**Detection:** Test installation on a clean Windows VM with no dev tools installed.

**Phase:** Build & Deployment phase. Must be solved before any public release.

**Confidence:** HIGH (Microsoft official deployment docs, multiple GitHub issues)

---

### Pitfall 5: FileSystemWatcher Duplicate and Missed Events

**What goes wrong:** FileSystemWatcher fires multiple `Changed` events for a single file write (JSONL append). It can also silently stop raising events if the internal buffer overflows, and it may miss events entirely on network/cloud-synced folders.

**Why it happens:** File writes are not atomic OS operations. A single JSONL append triggers: open file, write data, flush buffer, close file -- each step can fire a `Changed` event. The default internal buffer is 8 KB; in a directory with many JSONL files being written simultaneously (multiple Claude Code sessions), events can overflow the buffer.

**Consequences:** Without debouncing: the app re-parses JSONL files 2-5x per write, causing CPU spikes and UI flickering. With buffer overflow: the app silently stops updating until restart.

**Prevention:**
1. **Debounce all events** with a 300-500ms timer per file path. Reset the timer on each duplicate event.
2. **Increase InternalBufferSize** to 64 KB (default 8 KB is too small for recursive watching).
3. **Handle the `Error` event** to detect buffer overflows and recover (re-scan the directory).
4. **Use a HashSet of recently-processed files** with timestamps to deduplicate.

```csharp
watcher.InternalBufferSize = 65536; // 64 KB
watcher.Error += (s, e) => {
    // Buffer overflow -- trigger full rescan
    ScheduleFullRescan();
};
```

**Detection:** Open 5+ Claude Code sessions simultaneously and monitor CPU usage. If it spikes on every keystroke, debouncing is insufficient.

**Phase:** JSONL parsing phase. Must be implemented correctly from the start, not retrofitted.

**Confidence:** HIGH (well-documented .NET issue, Microsoft's own blog post acknowledges it)

---

## Moderate Pitfalls

### Pitfall 6: DispatcherQueue Cross-Thread UI Updates

**What goes wrong:** FileSystemWatcher events, HttpClient callbacks, and timer callbacks all fire on thread-pool threads. Updating any UI-bound property or ObservableCollection from these threads causes a `COMException: The application called an interface that was marshalled for a different thread`.

**Why it happens:** WinUI 3 enforces strict thread affinity for all UI elements. Unlike WPF's `Dispatcher.Invoke`, WinUI 3 uses `DispatcherQueue.TryEnqueue()` which has a different API surface and failure modes.

**Prevention:**
1. Capture `DispatcherQueue` reference during ViewModel construction (on the UI thread).
2. Use it for ALL property change notifications from background work.
3. Consider using `CommunityToolkit.Mvvm`'s `ObservableObject` which does NOT auto-marshal -- you must dispatch yourself.

```csharp
// In ViewModel constructor (runs on UI thread):
private readonly DispatcherQueue _dispatcher = DispatcherQueue.GetForCurrentThread();

// In background callback:
_dispatcher.TryEnqueue(() => {
    UsagePercentage = newValue; // Safe UI update
});
```

**Detection:** Run the app and trigger data updates while scrolling or interacting with the UI. Crashes will be intermittent and timing-dependent.

**Phase:** Affects every phase that introduces background work (API polling, JSONL watching, pricing updates).

**Confidence:** HIGH (Microsoft documentation, WinUI 3 specs)

---

### Pitfall 7: JSONL File Locking Conflicts with Claude Code

**What goes wrong:** The app opens a JSONL file for reading at the exact moment Claude Code is writing to it. Without proper file sharing flags, one process blocks the other, causing either a read failure in ccInfoWin or (worse) a write failure in Claude Code that disrupts the user's coding session.

**Why it happens:** Default `FileStream` opens with `FileShare.None`. Claude Code holds the file open with write access. Two processes accessing the same file requires explicit sharing.

**Consequences:** If ccInfoWin blocks Claude Code's writes, the user's actual work is disrupted. This is unacceptable for a monitoring tool -- it must be invisible to the monitored process.

**Prevention:** Always open JSONL files with maximum sharing permissions and read-only access:

```csharp
using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
using var reader = new StreamReader(stream);
```

**Detection:** Run Claude Code actively while ccInfoWin is reading. If Claude Code reports write errors or hangs, file locking is wrong.

**Phase:** JSONL parsing phase.

**Confidence:** HIGH (standard .NET file I/O knowledge, confirmed by project requirements)

---

### Pitfall 8: Win2D Chart Rendering on Theme/DPI Changes

**What goes wrong:** When the user switches between dark and light mode, or moves the window to a monitor with different DPI, the Win2D CanvasControl does not automatically re-render. Cached colors, font sizes, and layout calculations become stale.

**Why it happens:** Win2D draws pixels directly -- it has no automatic layout system like XAML controls. DPI changes require recalculating all coordinates. Theme changes require rebuilding the color palette. Neither happens automatically.

**Prevention:**
1. Listen for `ActualThemeChanged` on the root element and call `CanvasControl.Invalidate()`.
2. Handle `CanvasControl.DpiChanged` event and recalculate all layout constants.
3. Never cache absolute pixel coordinates -- always compute from control size * DPI scale.

**Detection:** Change Windows display scaling to 150%, then 200%, while the app is running. Switch dark/light mode. If the chart looks wrong, clipped, or blurry, DPI handling is broken.

**Phase:** Chart rendering phase.

**Confidence:** MEDIUM (derived from Win2D documentation and general DPI handling patterns)

---

### Pitfall 9: Claude.ai API Authentication Fragility

**What goes wrong:** The session cookie extracted from WebView2 expires, gets invalidated server-side, or the API endpoint changes. The app continues polling with an invalid token, receives 401/403 errors, but does not gracefully degrade or re-authenticate.

**Why it happens:** This is an unofficial API. Anthropic can change endpoints, cookie formats, or authentication mechanisms at any time without notice. The macOS original (ccInfo) has the same fragility.

**Consequences:** The app shows stale data or errors without explanation. Users lose trust in the monitoring tool.

**Prevention:**
1. Detect 401/403 responses immediately and trigger re-authentication flow.
2. Show a clear "Session expired -- please log in again" UI state (not a crash).
3. Cache the last known good data and display it with a "stale data" indicator.
4. Design the ClaudeApiService as a replaceable component so endpoint changes can be updated quickly.
5. Monitor the macOS ccInfo repo for API changes -- they will hit the same issues.

**Detection:** Manually invalidate the stored session token and observe app behavior. It should gracefully redirect to login.

**Phase:** API integration phase. The error handling must be designed in from the start, not bolted on.

**Confidence:** MEDIUM (based on unofficial API patterns, not verifiable against Anthropic documentation)

---

### Pitfall 10: Window Position Restoration on Multi-Monitor Changes

**What goes wrong:** The app saves window position (X=3840, Y=200) for a multi-monitor setup. User disconnects the external monitor. On next launch, the window opens off-screen and is invisible.

**Why it happens:** Saved coordinates are absolute pixel positions that may refer to monitors that no longer exist.

**Prevention:** On startup, validate saved coordinates against current screen bounds (`DisplayArea.GetFromPoint` or `MonitorFromPoint` Win32 API). If the saved position is off-screen, reset to center of primary monitor.

**Detection:** Save position on a dual-monitor setup, disconnect external monitor, relaunch.

**Phase:** Settings/window management phase.

**Confidence:** HIGH (common Windows desktop app issue, well-known pattern)

---

## Minor Pitfalls

### Pitfall 11: WinUI 3 XAML Hot Reload Unreliability

**What goes wrong:** XAML Hot Reload stops working mid-session, does not apply changes to custom controls, or crashes the app. Developers waste time restarting the app instead of iterating quickly on UI.

**Prevention:** Do not rely on Hot Reload for Win2D controls or complex custom controls. Accept manual restart cycles for those. Use Hot Reload only for simple XAML layout changes.

**Phase:** All UI development phases. Development velocity concern, not a runtime issue.

**Confidence:** HIGH (widely reported across WinUI 3 developer community)

---

### Pitfall 12: Localization Resource Loading in Unpackaged Apps

**What goes wrong:** `ResourceLoader` fails to find `.resw` files in unpackaged apps because the resource indexing (PRI) behaves differently than in packaged apps.

**Prevention:** Verify localization works in both Debug and Release configurations. Test with `dotnet publish` output, not just F5 debug. Ensure `MakePri.exe` is part of the build pipeline and PRI files are included in the publish output.

**Detection:** Build a Release publish, run from the output folder (not Visual Studio). If all strings show as key names instead of localized values, PRI generation is broken.

**Phase:** Localization phase.

**Confidence:** MEDIUM (reported in WinUI 3 discussions, not consistently reproduced)

---

### Pitfall 13: LiteLLM Pricing JSON Schema Changes

**What goes wrong:** The LiteLLM pricing JSON structure changes (new fields, renamed models, restructured nesting). The app crashes or shows $0.00 for all costs.

**Prevention:** Use defensive JSON parsing with fallback values. Never assume a field exists. Ship a `fallback_pricing.json` as specified in the tech spec. Log parsing errors but do not crash. Validate the downloaded JSON schema before replacing the cache.

**Detection:** Corrupt the cached `pricing_cache.json` and verify the app falls back gracefully.

**Phase:** Cost calculation phase.

**Confidence:** MEDIUM (external API dependency, schema stability unknown)

---

### Pitfall 14: Inno Setup Per-User Install Without Admin Rights

**What goes wrong:** The Inno Setup script defaults to `{autopf}` (Program Files), which requires admin rights. The PROJECT.md explicitly requires no admin elevation.

**Prevention:** Use `{localappdata}\CCInfoWindows` as the default install directory. Set `PrivilegesRequired=lowest` in the Inno Setup script. Do NOT use `{autopf}` or `{commonpf}`.

```iss
[Setup]
PrivilegesRequired=lowest
DefaultDirName={localappdata}\CCInfoWindows
```

**Detection:** Run the installer on a standard (non-admin) Windows account.

**Phase:** Deployment phase.

**Confidence:** HIGH (Inno Setup documentation, project requirement)

---

## Phase-Specific Warnings

| Phase Topic | Likely Pitfall | Mitigation | Severity |
|-------------|---------------|------------|----------|
| Authentication (WebView2 login) | WebView2 init failure, cookie threading | Explicit UDF path, await init, UI-thread cookie access | Critical |
| JSONL Parsing (FileSystemWatcher) | Duplicate events, file locking, buffer overflow | Debounce 300ms, FileShare.ReadWrite, 64KB buffer, Error handler | Critical |
| Chart Rendering (Win2D) | Memory leak, DPI/theme invalidation | RemoveFromVisualTree pattern, Invalidate on theme change | Critical |
| API Polling (ClaudeApiService) | Session expiry, silent failures | 401/403 detection, graceful re-auth, stale data indicator | Moderate |
| Threading (all background work) | COMException on UI update | Capture DispatcherQueue early, dispatch all property changes | Moderate |
| Deployment (Inno Setup) | Missing runtime, admin requirement | Bundle WindowsAppRuntime, PrivilegesRequired=lowest | Critical |
| Settings (window position) | Off-screen window | Validate against current display bounds | Low |
| Localization (.resw) | Missing resources in publish | Verify PRI generation in Release build | Low |
| Cost Calculation (LiteLLM) | Schema change | Defensive parsing, fallback JSON | Low |

---

## Sources

### Official Documentation
- [Win2D: Avoiding Memory Leaks](https://microsoft.github.io/Win2D/WinUI3/html/RefCycles.htm) (HIGH confidence)
- [Windows App SDK Unpackaged Deployment Guide](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/deploy-unpackaged-apps) (HIGH confidence)
- [CoreWebView2CookieManager API](https://learn.microsoft.com/en-us/dotnet/api/microsoft.web.webview2.core.corewebview2cookiemanager.getcookiesasync) (HIGH confidence)
- [FileSystemWatcher Duplicate Events](https://learn.microsoft.com/en-us/archive/blogs/ahamza/filesystemwatcher-generates-duplicate-events-how-to-workaround) (HIGH confidence)

### GitHub Issues
- [Win2D CanvasControl Memory Leak on Resize (#821)](https://github.com/microsoft/Win2D/issues/821) (HIGH confidence)
- [WebView2 EnsureCoreWebView2Async in Unpackaged Apps (#7201)](https://github.com/microsoft/microsoft-ui-xaml/issues/7201) (HIGH confidence)
- [WebView2 CookieManager Threading (#1283)](https://github.com/MicrosoftEdge/WebView2Feedback/issues/1283) (HIGH confidence)
- [WinUI 3 COMException Marshalled Thread (#8410)](https://github.com/microsoft/microsoft-ui-xaml/discussions/8410) (HIGH confidence)
- [WinUI 3 Unpackaged App Distribution (#6620)](https://github.com/microsoft/microsoft-ui-xaml/issues/6620) (HIGH confidence)
- [WebView2 GetCookiesAsync in WinUI (#1655)](https://github.com/MicrosoftEdge/WebView2Feedback/issues/1655) (MEDIUM -- fixed but verify with current SDK version)

### Community Sources
- [FileSystemWatcher is a Bit Broken](https://failingfast.io/a-robust-solution-for-filesystemwatcher-firing-events-multiple-times/) (MEDIUM confidence)
- [Nick's .NET Travels: Packaged vs Unpackaged WinUI 3](https://nicksnettravels.builttoroam.com/packaged-unpackaged-self-contained/) (MEDIUM confidence)
- [Authenticating HTTP Requests with Cookies from WebView2](https://anthonysimmon.com/authenticating-http-requests-cookies-webview2-wpf/) (MEDIUM confidence)
