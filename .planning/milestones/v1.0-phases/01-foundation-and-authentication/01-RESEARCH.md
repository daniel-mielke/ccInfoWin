# Phase 1: Foundation and Authentication - Research

**Researched:** 2026-03-09
**Domain:** WinUI 3 unpackaged app scaffold, WebView2 authentication, Windows Credential Manager, MVVM foundation
**Confidence:** HIGH

## Summary

Phase 1 establishes the entire project scaffold: solution structure, DI container, navigation shell, WebView2-based login, credential storage, and window management. This is a greenfield .NET 9 / WinUI 3 / Windows App SDK 1.8 project using the CommunityToolkit.Mvvm MVVM pattern with source generators.

The highest-risk integration point is WebView2 initialization and cookie extraction -- corrupted User Data Folders, threading constraints on cookie property access, and proper async initialization are all documented failure modes. The credential storage path uses AdysTech.CredentialManager (now v3.1.0, published Feb 2026) wrapping Win32 CredRead/CredWrite. Window sizing uses the AppWindow/OverlappedPresenter APIs including the `PreferredMinimumWidth`/`PreferredMinimumHeight` properties available in WinAppSDK 1.7+.

**Primary recommendation:** Build in strict order: project scaffold with .csproj -> DI container in App.xaml.cs -> NavigationService -> SettingsService (minimal, for window state) -> CredentialService -> MainWindow shell with Frame -> LoginView with WebView2 -> auth flow integration. Each layer is testable before the next is added.

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions
- Standard Windows title bar (no custom title bar)
- Normal minimize to taskbar (no System Tray)
- X button = app exits completely
- Always-on-top: No (default), option for Settings in future phase
- Initial window size: 360px wide x 900px tall
- Window is freely resizable and maximizable
- Minimum window size: 300x500px
- Window position AND size saved on close, restored on startup
- WebView2 fills the entire app window (full-window login view)
- 2FA/Captcha handled natively by WebView2 -- no special app-side handling needed
- Login success detected via Cookie-Check: look for `sessionKey` cookie after navigation
- Token expiry during use (HTTP 401): show InfoBar banner with "Session expired" + Re-Login button (not invasive, not auto-redirect)
- WebView2 User Data Folder explicitly set to `%LOCALAPPDATA%\CCInfoWindows\WebView2`
- Create `CLAUDE.md` in project root with stack, conventions, structure, build commands, security rules, spec file references, coding guideline references
- Specialized agents (`.claude/agents/`): fullstack-dev.md, code-review.md, git-agent.md

### Claude's Discretion
- DI container setup and service registration pattern
- Exact navigation service implementation (Frame-based)
- WebView2 initialization retry strategy (corrupted UDF handling)
- Cookie extraction timing and validation approach
- .gitignore exact entries beyond the baseline

### Deferred Ideas (OUT OF SCOPE)
- Always-on-top toggle in Settings -- Phase 6 (SETT category)
- System Tray minimize option in Settings -- potential v2 feature
</user_constraints>

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|-----------------|
| AUTH-01 | User can log in via embedded WebView2 showing claude.ai login page | WebView2 initialization pattern, CoreWebView2Environment.CreateAsync with explicit UDF, cookie extraction via CookieManager |
| AUTH-02 | Session tokens securely stored in Windows Credential Manager (DPAPI-encrypted) | AdysTech.CredentialManager 3.1.0 static API (SaveCredentials/GetCredentials/RemoveCredentials) |
| AUTH-03 | App validates stored tokens on startup and shows login if expired | CredentialService.GetCredentials on startup, HTTP validation call to claude.ai, NavigationService routing |
| AUTH-04 | User can log out, clearing all stored tokens | CredentialManager.RemoveCredentials + WebView2 cookie clearing + navigate to LoginView |
| SECU-01 | Zero hardcoded secrets in source code | .gitignore rules, code review agent, no string literals for tokens |
| SECU-02 | Tokens stored exclusively in Windows Credential Manager (DPAPI) | AdysTech.CredentialManager wraps Win32 CredWrite with DPAPI encryption |
| SECU-03 | No telemetry, no tracking, no data collection | No analytics packages, no outbound calls except claude.ai |
| SECU-04 | Network communication only to claude.ai and raw.githubusercontent.com (HTTPS) | HttpClient configured with base addresses, no other endpoints in Phase 1 |
| SECU-05 | WebView2 user data isolated in %LOCALAPPDATA% directory | CoreWebView2Environment.CreateAsync with explicit path |
| SECU-06 | Comprehensive .gitignore preventing accidental secret exposure | Visual Studio template + custom entries for credentials, WebView2 UDF, local settings |
| UIPF-01 | Persistent standalone window (not popup, not tray icon) | Standard WinUI 3 Window with OverlappedPresenter, standard title bar |
| UIPF-03 | Compact layout matching macOS MenuBar popup layout order | Frame-based navigation, initial empty MainView placeholder |
| UIPF-06 | Fixed window width (~360px), resizable, minimizable | AppWindow.Resize + OverlappedPresenter with PreferredMinimumWidth/Height |
| UIPF-08 | Runs on Windows 10 (19041+) and Windows 11 without admin rights | TargetFramework net9.0-windows10.0.19041.0, WindowsPackageType=None, no admin manifest |
</phase_requirements>

## Standard Stack

### Core (Phase 1 subset)
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| .NET 9 / C# 13 | 9.0 | Runtime + language | C# 13 partial properties for CommunityToolkit.Mvvm 8.4 source generators |
| Windows App SDK | 1.8.260209005 | WinUI 3 runtime | Latest stable (Feb 2026). Includes WebView2, AppWindow APIs |
| CommunityToolkit.Mvvm | 8.4.0 | MVVM source generators | `[ObservableProperty]`, `[RelayCommand]`, WeakReferenceMessenger |
| AdysTech.CredentialManager | 3.1.0 | Windows Credential Manager wrapper | Static API wrapping CredRead/CredWrite/CredDelete. v3.1.0 is latest (Feb 2026), targets .NET 8+. No BinaryFormatter dependency. |
| Microsoft.Windows.SDK.BuildTools | 10.0.26100.1742 | Windows SDK build tools | Required alongside WinAppSDK |
| Microsoft.Extensions.DependencyInjection | 9.0.x | DI container | Standard .NET DI for service registration |

### Supporting (built-in .NET 9)
| Library | Purpose | When to Use |
|---------|---------|-------------|
| System.Text.Json | JSON serialization for settings.json | Settings persistence, token validation response parsing |
| System.Net.Http.HttpClient | HTTP client for token validation | Startup token validation against claude.ai |

### Version Update from Prior Research
| Item | STACK.md Version | Correct Version | Note |
|------|-----------------|-----------------|------|
| AdysTech.CredentialManager | 2.6.0 | **3.1.0** | v3.1.0 published Feb 27, 2026. Removed BinaryFormatter, added Roslyn analyzers. |

**Installation:**
```bash
dotnet new winui3 -n CCInfoWindows --framework net9.0
dotnet add package Microsoft.WindowsAppSDK --version 1.8.260209005
dotnet add package Microsoft.Windows.SDK.BuildTools --version 10.0.26100.1742
dotnet add package CommunityToolkit.Mvvm --version 8.4.0
dotnet add package AdysTech.CredentialManager --version 3.1.0
dotnet add package Microsoft.Extensions.DependencyInjection --version 9.0.0
```

## Architecture Patterns

### Recommended Project Structure (Phase 1 deliverable)
```
CCInfoWindows/
├── CCInfoWindows.sln
├── CLAUDE.md                          # Project instructions for AI agents
├── .gitignore                         # Comprehensive ignore rules
├── CCInfoWindows/
│   ├── CCInfoWindows.csproj           # Unpackaged WinUI 3 project
│   ├── app.manifest                   # No admin required manifest
│   ├── App.xaml / App.xaml.cs         # DI container, theme init, entry point
│   ├── MainWindow.xaml / .cs          # Single window with Frame navigation
│   │
│   ├── Models/                        # Plain data objects
│   │   └── AppSettings.cs             # Window position/size, future settings
│   │
│   ├── ViewModels/                    # Observable state + commands
│   │   ├── LoginViewModel.cs          # WebView2 login orchestration
│   │   └── MainViewModel.cs           # Placeholder for future phases
│   │
│   ├── Views/                         # XAML pages
│   │   ├── LoginView.xaml / .cs       # WebView2 full-window login
│   │   └── MainView.xaml / .cs        # Placeholder dashboard (empty shell)
│   │
│   ├── Services/                      # Business logic + I/O
│   │   ├── Interfaces/
│   │   │   ├── ICredentialService.cs
│   │   │   ├── INavigationService.cs
│   │   │   └── ISettingsService.cs
│   │   ├── CredentialService.cs       # AdysTech wrapper for Win Cred Manager
│   │   ├── NavigationService.cs       # Frame-based page navigation
│   │   └── SettingsService.cs         # JSON read/write for settings.json
│   │
│   ├── Messages/                      # Messenger message types
│   │   └── AuthStateChangedMessage.cs # Login/logout/expiry notifications
│   │
│   ├── Helpers/                       # Pure utility functions
│   │   └── WindowHelper.cs            # Window position validation
│   │
│   ├── Converters/                    # XAML value converters
│   │   └── BoolToVisibilityConverter.cs
│   │
│   └── Assets/                        # Static resources
│       └── app-icon.ico
│
└── .claude/
    └── agents/                        # Specialized agent definitions
        ├── fullstack-dev.md
        ├── code-review.md
        └── git-agent.md
```

### Pattern 1: DI Container in App.xaml.cs
**What:** Microsoft.Extensions.DependencyInjection ServiceCollection configured in App.OnLaunched. All services registered as singletons, ViewModels as transient.
**When to use:** Always. This is the foundation all subsequent phases build on.
**Example:**
```csharp
// Source: Microsoft Learn WinUI 3 MVVM + DI Tutorial
public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        Services = ConfigureServices();
        m_window = new MainWindow();
        m_window.Activate();
    }

    private static IServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<ICredentialService, CredentialService>();
        services.AddSingleton<INavigationService, NavigationService>();
        services.AddTransient<LoginViewModel>();
        services.AddTransient<MainViewModel>();
        return services.BuildServiceProvider();
    }
}
```

### Pattern 2: Frame-Based NavigationService
**What:** A service wrapping the WinUI 3 Frame control for page navigation. The Frame lives in MainWindow.xaml. NavigationService gets a reference to it after window creation.
**When to use:** All page transitions (Login -> Main, Main -> Settings, etc.)
**Example:**
```csharp
// Source: Microsoft TemplateStudio pattern
public interface INavigationService
{
    bool CanGoBack { get; }
    void Initialize(Frame frame);
    void NavigateTo<TPage>() where TPage : Page;
    void GoBack();
}

public class NavigationService : INavigationService
{
    private Frame? _frame;

    public bool CanGoBack => _frame?.CanGoBack == true;

    public void Initialize(Frame frame) => _frame = frame;

    public void NavigateTo<TPage>() where TPage : Page
    {
        _frame?.Navigate(typeof(TPage));
    }

    public void GoBack() => _frame?.GoBack();
}
```

### Pattern 3: WebView2 Cookie-Based Auth Flow
**What:** WebView2 loads claude.ai/login. After navigation completes, check for `sessionKey` cookie. If found, store in Credential Manager and navigate to MainView.
**When to use:** AUTH-01 login flow.
**Example:**
```csharp
// Source: WebView2 CookieManagement spec + ARCHITECTURE.md patterns
// CRITICAL: All cookie property access MUST be on UI thread
private async void WebView_NavigationCompleted(WebView2 sender,
    CoreWebView2NavigationCompletedEventArgs args)
{
    var cookies = await sender.CoreWebView2.CookieManager
        .GetCookiesAsync("https://claude.ai");

    // Access cookie properties on UI thread (threading constraint)
    var sessionCookie = cookies.FirstOrDefault(c => c.Name == "sessionKey");
    if (sessionCookie != null)
    {
        _credentialService.SaveSessionToken(sessionCookie.Value);
        WeakReferenceMessenger.Default.Send(new AuthStateChangedMessage(true));
        _navigationService.NavigateTo<MainView>();
    }
}
```

### Pattern 4: Window Size and Position Management via AppWindow
**What:** Use AppWindow.Resize for initial size, OverlappedPresenter for minimum size constraints, AppWindow.Changed event to track position/size, SettingsService to persist.
**When to use:** MainWindow initialization and close handling.
**Example:**
```csharp
// Source: Microsoft Learn - Manage app windows (updated 2026-03-06)
public MainWindow()
{
    InitializeComponent();

    // Set initial size (360x900)
    AppWindow.Resize(new Windows.Graphics.SizeInt32(360, 900));

    // Set minimum size via OverlappedPresenter
    var presenter = OverlappedPresenter.Create();
    presenter.PreferredMinimumWidth = 300;
    presenter.PreferredMinimumHeight = 500;
    AppWindow.SetPresenter(presenter);

    // Restore saved position/size
    var settings = App.Services.GetRequiredService<ISettingsService>();
    var saved = settings.LoadWindowState();
    if (saved != null && IsPositionOnScreen(saved))
    {
        AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(
            saved.X, saved.Y, saved.Width, saved.Height));
    }

    // Save on close
    AppWindow.Closing += (s, e) =>
    {
        settings.SaveWindowState(new WindowState(
            AppWindow.Position.X, AppWindow.Position.Y,
            AppWindow.Size.Width, AppWindow.Size.Height));
    };
}
```

### Anti-Patterns to Avoid
- **Accessing WebView2 cookie properties off UI thread:** CookieManager.GetCookiesAsync returns cookies from any thread, but accessing .Name/.Value properties off UI thread throws. Always extract cookie data on UI thread.
- **Fire-and-forget EnsureCoreWebView2Async:** Must be properly awaited. Failing to await causes silent init failures.
- **Using PasswordVault instead of Win32 Credential Manager:** Known compatibility issues with WinUI 3 full-trust/unpackaged apps.
- **Hardcoding window position without validation:** Saved positions may be off-screen after monitor changes.

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Credential storage | Custom P/Invoke for advapi32.dll CredRead/CredWrite | AdysTech.CredentialManager 3.1.0 | Saves ~100 lines of marshaling boilerplate, handles edge cases, Roslyn analyzer integration |
| MVVM boilerplate | Manual INotifyPropertyChanged, manual ICommand | CommunityToolkit.Mvvm source generators | `[ObservableProperty]` and `[RelayCommand]` eliminate hundreds of lines |
| DI container | Static singleton services or service locator | Microsoft.Extensions.DependencyInjection | Standard, testable, well-understood pattern |
| Window minimum size | Win32 WM_GETMINMAXINFO message handling via subclassing | OverlappedPresenter.PreferredMinimumWidth/Height | Native API available in WinAppSDK 1.7+, no interop needed |

## Common Pitfalls

### Pitfall 1: WebView2 Initialization Failure
**What goes wrong:** `EnsureCoreWebView2Async()` hangs or throws `E_UNEXPECTED` due to corrupted User Data Folder.
**Why it happens:** Previous GPU crash, forced termination, or disk full corrupted the WebView2 cache directory.
**How to avoid:** Explicitly set UDF to `%LOCALAPPDATA%\CCInfoWindows\WebView2`. Wrap init in try/catch. On failure, delete UDF directory and retry once. Set a 30-second timeout.
**Warning signs:** Login page never appears. App seems frozen on startup.

### Pitfall 2: Cookie Threading Constraint
**What goes wrong:** Accessing cookie.Name or cookie.Value from a background thread throws intermittently.
**Why it happens:** WebView2 cookie objects have thread affinity. The async GetCookiesAsync call works from any thread, but property access requires UI thread.
**How to avoid:** Ensure NavigationCompleted handler runs on UI thread (it does by default). Do NOT offload cookie extraction to Task.Run.
**Warning signs:** Intermittent crashes during login that work "most of the time."

### Pitfall 3: OverlappedPresenter Replaces Default
**What goes wrong:** Creating and applying a new OverlappedPresenter via `SetPresenter()` replaces the default presenter. If you later want to modify it again, you need your own reference.
**Why it happens:** Each AppWindow has one presenter. Setting a new one discards the old.
**How to avoid:** Keep a reference to your custom OverlappedPresenter. Or modify the default presenter directly via `(OverlappedPresenter)AppWindow.Presenter` after ensuring it's the default.
**Warning signs:** Window behavior changes unexpectedly after presenter operations.

### Pitfall 4: Window Position Off-Screen After Monitor Change
**What goes wrong:** Saved window position (X=3840, Y=200) refers to a disconnected monitor. Window opens invisibly.
**Why it happens:** Absolute pixel positions saved without monitor context.
**How to avoid:** On startup, validate saved position against `DisplayArea.GetFromPoint` or enumerate display areas. If off-screen, center on primary display.
**Warning signs:** "App won't open" reports from users who changed monitor setups.

### Pitfall 5: Unpackaged App Missing Bootstrapper
**What goes wrong:** App crashes immediately on machines without WinAppSDK runtime.
**Why it happens:** Unpackaged WinUI 3 apps need the Bootstrapper API initialized before WinUI features work. The `WindowsPackageType=None` setting in .csproj handles this for .NET apps via auto-generated code, but only if configured correctly.
**How to avoid:** Ensure `WindowsPackageType=None` is set. Test on a clean VM without Visual Studio installed.
**Warning signs:** App exits silently or shows cryptic COM errors on non-dev machines.

## Code Examples

### CredentialService Implementation
```csharp
// Source: AdysTech.CredentialManager 3.1.0 API
using AdysTech.CredentialManager;
using System.Net;

public class CredentialService : ICredentialService
{
    private const string CredentialTarget = "CCInfoWindows/claude-session";

    public void SaveSessionToken(string token)
    {
        var cred = new NetworkCredential("sessionKey", token);
        CredentialManager.SaveCredentials(CredentialTarget, cred);
    }

    public string? GetSessionToken()
    {
        var cred = CredentialManager.GetCredentials(CredentialTarget);
        return cred?.Password;
    }

    public void ClearCredentials()
    {
        CredentialManager.RemoveCredentials(CredentialTarget);
    }
}
```

### WebView2 Initialization with Retry
```csharp
// Source: PITFALLS.md Pitfall 2 + WebView2Feedback issues
private async Task InitializeWebViewAsync(WebView2 webView)
{
    var udfPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CCInfoWindows", "WebView2");

    try
    {
        var env = await CoreWebView2Environment.CreateAsync(null, udfPath);
        await webView.EnsureCoreWebView2Async(env);
    }
    catch (Exception)
    {
        // Retry once after deleting corrupted UDF
        if (Directory.Exists(udfPath))
        {
            Directory.Delete(udfPath, recursive: true);
        }
        var env = await CoreWebView2Environment.CreateAsync(null, udfPath);
        await webView.EnsureCoreWebView2Async(env);
    }
}
```

### App Manifest (No Admin Required)
```xml
<!-- app.manifest -->
<?xml version="1.0" encoding="utf-8"?>
<assembly manifestVersion="1.0" xmlns="urn:schemas-microsoft-com:asm.v1">
  <trustInfo xmlns="urn:schemas-microsoft-com:asm.v3">
    <security>
      <requestedPrivileges xmlns="urn:schemas-microsoft-com:asm.v2">
        <requestedExecutionLevel level="asInvoker" uiAccess="false" />
      </requestedPrivileges>
    </security>
  </trustInfo>
  <compatibility xmlns="urn:schemas-microsoft-com:compatibility.v1">
    <application>
      <!-- Windows 10 version 2004 -->
      <supportedOS Id="{8e0f7a12-bfb3-4fe8-b9a5-48fd50a15a9a}" />
    </application>
  </compatibility>
</assembly>
```

### .csproj Configuration
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net9.0-windows10.0.19041.0</TargetFramework>
    <TargetPlatformMinVersion>10.0.19041.0</TargetPlatformMinVersion>
    <RootNamespace>CCInfoWindows</RootNamespace>
    <ApplicationManifest>app.manifest</ApplicationManifest>
    <Platforms>x64;ARM64</Platforms>
    <RuntimeIdentifiers>win-x64;win-arm64</RuntimeIdentifiers>
    <UseWinUI>true</UseWinUI>
    <WindowsPackageType>None</WindowsPackageType>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <LangVersion>13.0</LangVersion>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.WindowsAppSDK" Version="1.8.260209005" />
    <PackageReference Include="Microsoft.Windows.SDK.BuildTools" Version="10.0.26100.1742" />
    <PackageReference Include="CommunityToolkit.Mvvm" Version="8.4.0" />
    <PackageReference Include="AdysTech.CredentialManager" Version="3.1.0" />
    <PackageReference Include="Microsoft.Extensions.DependencyInjection" Version="9.0.0" />
  </ItemGroup>
</Project>
```

### .gitignore (Phase 1 Baseline)
```gitignore
# Visual Studio
.vs/
bin/
obj/
*.user
*.suo
*.userprefs

# Build outputs
publish/
*.exe
*.dll
*.pdb

# NuGet
packages/

# Local app data (never commit)
**/WebView2/
settings.json
*.cache.json

# Secrets and credentials
*.pfx
*.snk
.env
appsettings.*.json

# OS files
Thumbs.db
desktop.ini
.DS_Store
```

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| PasswordVault (WinRT) | AdysTech.CredentialManager (Win32 CredRead/CredWrite) | Known issue since WinUI 3 GA | PasswordVault has documented compatibility issues in unpackaged full-trust apps |
| Win32 WM_GETMINMAXINFO subclassing for min size | OverlappedPresenter.PreferredMinimumWidth/Height | WinAppSDK 1.7 (2025) | Native API, no interop needed |
| Manual HWND lookup for AppWindow | Window.AppWindow property | WinAppSDK 1.3 | Direct access without Win32Interop |
| AdysTech.CredentialManager 2.6.0 | AdysTech.CredentialManager 3.1.0 | Feb 2026 | Removed BinaryFormatter, added Roslyn analyzers, targets .NET 8+ |

## Open Questions

1. **Cookie name for claude.ai session**
   - What we know: CONTEXT.md specifies `sessionKey` cookie. The macOS original uses this.
   - What's unclear: Whether Anthropic has changed or will change the cookie name.
   - Recommendation: Check for `sessionKey` first. Log all cookie names during development for debugging. Design extraction to be configurable.

2. **Token validation endpoint**
   - What we know: AUTH-03 requires validating stored tokens on startup. The organizations endpoint (`/api/organizations`) is a lightweight way to test auth.
   - What's unclear: Exact response when token is expired (401 vs 403 vs redirect).
   - Recommendation: Try GET `https://claude.ai/api/organizations`. If 2xx, token is valid. If 401/403, show login. If network error, show cached state with warning.

3. **WebView2 cookie persistence vs manual storage**
   - What we know: WebView2 has its own cookie storage in the UDF. We also store sessionKey in Credential Manager.
   - What's unclear: Whether WebView2's own cookies survive app restart (they should, since UDF is persistent).
   - Recommendation: Store in Credential Manager as the authoritative source. On startup, if Credential Manager has a valid token, skip WebView2 entirely. Only show WebView2 for fresh login.

## Validation Architecture

### Test Framework
| Property | Value |
|----------|-------|
| Framework | MSTest + Microsoft.UI.Xaml.Testing (or manual verification for UI) |
| Config file | None -- Wave 0 |
| Quick run command | `dotnet test --filter "Category=Unit"` |
| Full suite command | `dotnet test` |

### Phase Requirements -> Test Map
| Req ID | Behavior | Test Type | Automated Command | File Exists? |
|--------|----------|-----------|-------------------|-------------|
| AUTH-01 | WebView2 shows claude.ai login | manual-only | N/A (requires browser interaction) | N/A |
| AUTH-02 | Token stored in Credential Manager | unit | `dotnet test --filter "CredentialService"` | Wave 0 |
| AUTH-03 | Stored token validated on startup | unit | `dotnet test --filter "TokenValidation"` | Wave 0 |
| AUTH-04 | Logout clears all tokens | unit | `dotnet test --filter "Logout"` | Wave 0 |
| SECU-01 | Zero hardcoded secrets | smoke | `dotnet build` + grep scan for patterns | Wave 0 |
| SECU-02 | Tokens in Credential Manager only | unit | `dotnet test --filter "CredentialService"` | Wave 0 |
| SECU-03 | No telemetry | manual-only | Wireshark/Fiddler network inspection | N/A |
| SECU-04 | Network only to claude.ai / github | manual-only | Wireshark/Fiddler network inspection | N/A |
| SECU-05 | WebView2 UDF in %LOCALAPPDATA% | unit | `dotnet test --filter "WebViewUdf"` | Wave 0 |
| SECU-06 | .gitignore comprehensive | smoke | Verify file exists and contains key entries | Wave 0 |
| UIPF-01 | Persistent standalone window | manual-only | Launch app, verify window behavior | N/A |
| UIPF-03 | Compact layout | manual-only | Visual inspection | N/A |
| UIPF-06 | Window sizing (360px, min 300x500) | unit | `dotnet test --filter "WindowSize"` | Wave 0 |
| UIPF-08 | Runs on Win10 19041+ without admin | smoke | Build target verification | Wave 0 |

### Sampling Rate
- **Per task commit:** `dotnet build` (compile check)
- **Per wave merge:** `dotnet test` (full suite when tests exist)
- **Phase gate:** Manual launch + login flow verification + `dotnet test`

### Wave 0 Gaps
- [ ] `CCInfoWindows.Tests/` project -- test project not yet created
- [ ] `CCInfoWindows.Tests/Services/CredentialServiceTests.cs` -- covers AUTH-02, AUTH-04, SECU-02
- [ ] `CCInfoWindows.Tests/Services/SettingsServiceTests.cs` -- covers window state persistence
- [ ] `CCInfoWindows.Tests/Helpers/WindowHelperTests.cs` -- covers UIPF-06 position validation
- [ ] Framework install: `dotnet add CCInfoWindows.Tests package MSTest.TestFramework`

Note: WinUI 3 UI testing is notoriously difficult for unit tests. AUTH-01, UIPF-01, UIPF-03, SECU-03, SECU-04 are manual verification only. Service-layer logic (credential storage, settings, window helpers) is fully testable.

## Sources

### Primary (HIGH confidence)
- [Microsoft Learn: Manage app windows](https://learn.microsoft.com/en-us/windows/apps/develop/ui/manage-app-windows) -- AppWindow, OverlappedPresenter, PreferredMinimumWidth/Height, Resize, Move, window position
- [Microsoft Learn: WinUI 3 MVVM + DI](https://learn.microsoft.com/en-us/windows/apps/tutorials/winui-mvvm-toolkit/dependency-injection) -- DI container pattern
- [WebView2 CookieManagement spec](https://github.com/MicrosoftEdge/WebView2Feedback/blob/main/specs/CookieManagement.md) -- GetCookiesAsync, cookie extraction
- [NuGet: AdysTech.CredentialManager 3.1.0](https://www.nuget.org/packages/AdysTech.CredentialManager) -- Latest version, API surface, .NET 8+ target
- [GitHub: AdysTech/CredentialManager](https://github.com/AdysTech/CredentialManager) -- SaveCredentials/GetCredentials/RemoveCredentials static API
- [Win2D: Avoiding Memory Leaks](https://microsoft.github.io/Win2D/WinUI3/html/RefCycles.htm) -- RemoveFromVisualTree pattern (future phases)

### Secondary (MEDIUM confidence)
- [Anthony Simmon: Authenticating HTTP requests with cookies from WebView2](https://anthonysimmon.com/authenticating-http-requests-cookies-webview2-wpf/) -- DelegatingHandler pattern (WPF example, pattern applies)
- [Nick's .NET Travels: WinAppSDK Windowing](https://nicksnettravels.builttoroam.com/winappsdk-windowing/) -- AppWindow usage patterns
- [xakpc: WinUI 3 Set Minimum Window Size](https://xakpc.info/winui-3-how-to-set-minimum-window-size-desktop/) -- Pre-1.7 subclassing approach (now superseded by PreferredMinimumWidth)
- [Microsoft TemplateStudio: Navigation](https://github.com/microsoft/TemplateStudio/blob/main/docs/WinUI/navigation.md) -- NavigationService pattern

### Tertiary (LOW confidence)
- Cookie name `sessionKey` for claude.ai -- based on user decision and macOS original, not verifiable against Anthropic docs
- Token validation via `/api/organizations` endpoint -- unofficial API, may change

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH -- all versions verified via NuGet and official docs
- Architecture: HIGH -- follows Microsoft-recommended WinUI 3 MVVM + DI patterns
- Window management: HIGH -- OverlappedPresenter APIs verified in official docs (updated 2026-03-06)
- WebView2 auth flow: HIGH -- CookieManager API well-documented, threading pitfall well-known
- Credential storage: HIGH -- AdysTech 3.1.0 verified on NuGet, static API confirmed
- Pitfalls: HIGH -- all sourced from official docs or verified GitHub issues
- Token validation: LOW -- unofficial API, behavior may vary

**Research date:** 2026-03-09
**Valid until:** 2026-04-09 (stable stack, 30-day validity)
