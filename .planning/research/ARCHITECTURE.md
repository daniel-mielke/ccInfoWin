# Architecture Research

**Domain:** Windows desktop real-time monitoring app (WinUI 3 / MVVM)
**Researched:** 2026-03-09
**Confidence:** HIGH

## Standard Architecture

### System Overview

```
┌─────────────────────────────────────────────────────────────────┐
│                         PRESENTATION                            │
│  ┌───────────┐  ┌──────────────┐  ┌─────────────┐              │
│  │ MainView  │  │ SettingsView │  │  LoginView  │              │
│  │ (XAML)    │  │ (XAML)       │  │ (WebView2)  │              │
│  └─────┬─────┘  └──────┬───────┘  └──────┬──────┘              │
│        │               │                 │                      │
│  ┌─────┴─────┐  ┌──────┴───────┐  ┌──────┴──────┐              │
│  │ MainVM    │  │ SettingsVM   │  │  LoginVM    │              │
│  └─────┬─────┘  └──────┬───────┘  └──────┬──────┘              │
│        │               │                 │                      │
│  Custom Controls:                                               │
│  ┌────────────────┐ ┌───────────────┐ ┌──────────────────┐      │
│  │UsageChartCtrl  │ │ProgressBarCtrl│ │SessionPickerCtrl │      │
│  │(Win2D Canvas)  │ │(XAML)         │ │(XAML ComboBox)   │      │
│  └────────────────┘ └───────────────┘ └──────────────────┘      │
├─────────────────────────────────────────────────────────────────┤
│                      COMMUNICATION BUS                          │
│        CommunityToolkit.Mvvm Messenger (WeakReferenceMessenger) │
├─────────────────────────────────────────────────────────────────┤
│                          SERVICES                               │
│  ┌──────────────┐ ┌──────────────┐ ┌──────────────┐            │
│  │ClaudeApi     │ │Jsonl         │ │Pricing       │            │
│  │Service       │ │Service       │ │Service       │            │
│  │(HTTP polling)│ │(FileWatcher) │ │(HTTP + cache)│            │
│  └──────┬───────┘ └──────┬───────┘ └──────┬───────┘            │
│  ┌──────────────┐ ┌──────────────┐ ┌──────────────┐            │
│  │Credential    │ │Settings      │ │Update        │            │
│  │Service       │ │Service       │ │Service       │            │
│  │(Win32 DPAPI) │ │(JSON file)   │ │(GitHub API)  │            │
│  └──────────────┘ └──────────────┘ └──────────────┘            │
│  ┌──────────────┐ ┌──────────────┐                              │
│  │FileWatcher   │ │Navigation    │                              │
│  │Service       │ │Service       │                              │
│  │(FSWatcher)   │ │(Frame nav)   │                              │
│  └──────────────┘ └──────────────┘                              │
├─────────────────────────────────────────────────────────────────┤
│                           MODELS                                │
│  UsageData  WeeklyUsage  ContextWindow  SessionInfo             │
│  TokenStats  PricingData  AppSettings                           │
├─────────────────────────────────────────────────────────────────┤
│                       INFRASTRUCTURE                            │
│  ┌───────────┐ ┌──────────────┐ ┌──────────────┐               │
│  │ Win32 Cred│ │ JSON Files   │ │ JSONL Files  │               │
│  │ Manager   │ │ %LOCALAPPDATA│ │ %USERPROFILE%│               │
│  │ (DPAPI)   │ │ \CCInfoWin\  │ │ \.claude\    │               │
│  └───────────┘ └──────────────┘ └──────────────┘               │
│  ┌───────────┐ ┌──────────────┐                                 │
│  │claude.ai  │ │GitHub/LiteLLM│                                 │
│  │(HTTPS)    │ │(HTTPS)       │                                 │
│  └───────────┘ └──────────────┘                                 │
└─────────────────────────────────────────────────────────────────┘
```

### Component Responsibilities

| Component | Responsibility | Communicates With |
|-----------|----------------|-------------------|
| **MainView + MainViewModel** | Primary dashboard: 5h chart, weekly usage, context window, session picker, token stats, cost display | ClaudeApiService, JsonlService, PricingService, NavigationService |
| **LoginView + LoginViewModel** | WebView2 login flow, cookie extraction from claude.ai | CredentialService, NavigationService |
| **SettingsView + SettingsViewModel** | App preferences (refresh interval, theme, language, autostart) | SettingsService, NavigationService |
| **UsageChartControl** | Win2D area chart with color-coded zones, glow indicator, PNG export | Receives data from MainViewModel via bindings |
| **ProgressBarControl** | Context window progress bars with model badges | Receives data from MainViewModel via bindings |
| **SessionPickerControl** | Multi-session dropdown with activity indicators | Receives session list from MainViewModel |
| **ClaudeApiService** | HTTP polling for 5h/weekly usage data from claude.ai | CredentialService (for auth tokens) |
| **JsonlService** | Parses JSONL log files for token/session/cost data | FileWatcherService (triggers re-parse) |
| **FileWatcherService** | FileSystemWatcher on `%USERPROFILE%\.claude\projects\`, debounced events | JsonlService (notifies of changes) |
| **PricingService** | Fetches and caches LiteLLM pricing, tiered pricing calculation | Local cache file, GitHub raw content |
| **CredentialService** | Win32 CredRead/CredWrite for session token storage | Windows Credential Manager via P/Invoke |
| **SettingsService** | Read/write settings.json, usage_history.json, caches | Local JSON files in %LOCALAPPDATA% |
| **UpdateService** | Periodic GitHub Releases API check, version comparison | GitHub API, MainViewModel (banner notification) |
| **NavigationService** | Frame-based page navigation within single window | All Views |

## Recommended Project Structure

```
CCInfoWindows/
├── CCInfoWindows.sln
├── CCInfoWindows/
│   ├── App.xaml / App.xaml.cs          # DI container setup, theme init
│   ├── MainWindow.xaml / .cs           # Single window with Frame
│   │
│   ├── Models/                         # Plain data objects (no logic)
│   │   ├── UsageData.cs
│   │   ├── WeeklyUsage.cs
│   │   ├── ContextWindow.cs
│   │   ├── SessionInfo.cs
│   │   ├── TokenStats.cs
│   │   ├── PricingData.cs
│   │   └── AppSettings.cs
│   │
│   ├── ViewModels/                     # Observable state + commands
│   │   ├── MainViewModel.cs
│   │   ├── SettingsViewModel.cs
│   │   ├── LoginViewModel.cs
│   │   └── SessionViewModel.cs
│   │
│   ├── Views/                          # XAML pages + custom controls
│   │   ├── MainView.xaml / .cs
│   │   ├── SettingsView.xaml / .cs
│   │   ├── LoginView.xaml / .cs
│   │   └── Controls/
│   │       ├── UsageChartControl.xaml / .cs
│   │       ├── ProgressBarControl.xaml / .cs
│   │       └── SessionPickerControl.xaml / .cs
│   │
│   ├── Services/                       # Business logic + I/O
│   │   ├── Interfaces/                 # Service interfaces for DI
│   │   │   ├── IClaudeApiService.cs
│   │   │   ├── IJsonlService.cs
│   │   │   ├── IPricingService.cs
│   │   │   ├── ICredentialService.cs
│   │   │   ├── ISettingsService.cs
│   │   │   ├── IUpdateService.cs
│   │   │   ├── IFileWatcherService.cs
│   │   │   └── INavigationService.cs
│   │   ├── ClaudeApiService.cs
│   │   ├── JsonlService.cs
│   │   ├── PricingService.cs
│   │   ├── CredentialService.cs
│   │   ├── SettingsService.cs
│   │   ├── UpdateService.cs
│   │   ├── FileWatcherService.cs
│   │   └── NavigationService.cs
│   │
│   ├── Helpers/                        # Pure utility functions
│   │   ├── ColorThresholds.cs
│   │   ├── TokenCalculator.cs
│   │   ├── JsonlParser.cs
│   │   └── ClipboardHelper.cs
│   │
│   ├── Converters/                     # XAML value converters
│   │   ├── BoolToVisibilityConverter.cs
│   │   └── PercentageToColorConverter.cs
│   │
│   ├── Messages/                       # Messenger message types
│   │   ├── AuthStateChangedMessage.cs
│   │   ├── UsageDataUpdatedMessage.cs
│   │   ├── ThemeChangedMessage.cs
│   │   └── SessionChangedMessage.cs
│   │
│   ├── Strings/                        # Localization resources
│   │   ├── de-DE/Resources.resw
│   │   └── en-US/Resources.resw
│   │
│   ├── Assets/                         # Static resources
│   │   ├── app-icon.ico
│   │   └── fallback_pricing.json
│   │
│   └── Native/                         # P/Invoke declarations
│       └── CredentialManagerInterop.cs
│
└── CCInfoWindows.Installer/
    └── setup.iss                       # Inno Setup script
```

### Structure Rationale

- **Services/Interfaces/**: Separate interfaces enable DI registration and testability. Every service gets an interface because this app has real I/O boundaries (filesystem, HTTP, Win32 API) that need mocking in tests.
- **Messages/**: Dedicated message types for the CommunityToolkit.Mvvm Messenger pattern. Keeps cross-component communication typed and discoverable rather than scattered across ViewModels.
- **Native/**: Isolates Win32 P/Invoke declarations (CredRead/CredWrite from advapi32.dll) from business logic. Clear boundary between managed and native code.
- **Helpers/**: Pure functions with no dependencies on services or ViewModels. TokenCalculator (tiered pricing math), ColorThresholds (usage zone colors), JsonlParser (line-by-line JSONL parsing).
- **Models/**: Strictly data-only. No INotifyPropertyChanged here -- that belongs in ViewModels. Models are DTOs for serialization and service-to-ViewModel data transfer.

## Architectural Patterns

### Pattern 1: DI-Based MVVM with CommunityToolkit.Mvvm

**What:** All services registered as singletons in a central DI container (Microsoft.Extensions.DependencyInjection). ViewModels receive services via constructor injection. Source generators (`[ObservableProperty]`, `[RelayCommand]`) eliminate boilerplate.

**When to use:** Always in this project. Every ViewModel and service.

**Trade-offs:** Slightly more setup in App.xaml.cs, but massive reduction in boilerplate and clean separation of concerns. The alternative (manual service locator or static singletons) is worse in every dimension.

**Example:**
```csharp
// App.xaml.cs -- DI container setup
public partial class App : Application
{
    public IServiceProvider Services { get; private set; }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        Services = ConfigureServices();
        m_window = new MainWindow();
        m_window.Activate();
    }

    private static IServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();

        // Services (singletons -- one instance for app lifetime)
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<ICredentialService, CredentialService>();
        services.AddSingleton<IClaudeApiService, ClaudeApiService>();
        services.AddSingleton<IJsonlService, JsonlService>();
        services.AddSingleton<IFileWatcherService, FileWatcherService>();
        services.AddSingleton<IPricingService, PricingService>();
        services.AddSingleton<IUpdateService, UpdateService>();
        services.AddSingleton<INavigationService, NavigationService>();

        // ViewModels (transient -- new instance per navigation)
        services.AddTransient<MainViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<LoginViewModel>();

        return services.BuildServiceProvider();
    }
}
```

### Pattern 2: WeakReferenceMessenger for Cross-Component Communication

**What:** CommunityToolkit.Mvvm's `WeakReferenceMessenger` decouples ViewModels and services. Instead of direct references, components publish/subscribe to typed messages. Weak references prevent memory leaks.

**When to use:** When a service or ViewModel needs to notify others without a direct dependency. Key scenarios:
- Auth state changes (LoginVM -> MainVM)
- Usage data updates (ClaudeApiService -> MainVM)
- Theme changes (SettingsVM -> UsageChartControl)
- Session selection changes (SessionPicker -> MainVM -> all data services)

**Trade-offs:** Indirection makes debugging harder (can't "Go to Definition" on a message send to find receivers). Mitigated by keeping message types in a dedicated folder and naming them clearly.

**Example:**
```csharp
// Message definition
public sealed class UsageDataUpdatedMessage : ValueChangedMessage<UsageData>
{
    public UsageDataUpdatedMessage(UsageData value) : base(value) { }
}

// Publisher (ClaudeApiService)
WeakReferenceMessenger.Default.Send(new UsageDataUpdatedMessage(newData));

// Subscriber (MainViewModel constructor)
WeakReferenceMessenger.Default.Register<UsageDataUpdatedMessage>(this, (r, m) =>
{
    ((MainViewModel)r).OnUsageDataUpdated(m.Value);
});
```

### Pattern 3: Timer-Driven Polling with Async/Await

**What:** Periodic data refresh using `PeriodicTimer` on a background thread, with UI dispatch via `DispatcherQueue.TryEnqueue()`. Separate from the event-driven FileWatcher path.

**When to use:** For Claude API polling (configurable 30s-10min interval) and update checks (hourly).

**Trade-offs:** Polling is simple and predictable but not real-time. For this app, the API data is inherently polled (no websocket endpoint exists), so polling is the correct pattern. The JSONL path uses FileSystemWatcher for near-real-time reactivity.

**Example:**
```csharp
// In ClaudeApiService
private readonly PeriodicTimer _timer;
private readonly DispatcherQueue _dispatcherQueue;

public async Task StartPollingAsync(CancellationToken ct)
{
    while (await _timer.WaitForNextTickAsync(ct))
    {
        var data = await FetchUsageDataAsync();
        _dispatcherQueue.TryEnqueue(() =>
        {
            WeakReferenceMessenger.Default.Send(new UsageDataUpdatedMessage(data));
        });
    }
}
```

### Pattern 4: Debounced FileSystemWatcher

**What:** FileSystemWatcher fires duplicate and rapid events. A debounce mechanism (300ms delay, reset on each event) coalesces rapid changes into a single parse operation.

**When to use:** Always for the JSONL file monitoring path. FileSystemWatcher is notoriously chatty on Windows.

**Trade-offs:** 300ms latency on file change detection. Acceptable for a monitoring dashboard where sub-second precision is irrelevant.

**Example:**
```csharp
// In FileWatcherService
private CancellationTokenSource _debounceCts;

private void OnFileChanged(object sender, FileSystemEventArgs e)
{
    _debounceCts?.Cancel();
    _debounceCts = new CancellationTokenSource();
    _ = DebounceAsync(e.FullPath, _debounceCts.Token);
}

private async Task DebounceAsync(string path, CancellationToken ct)
{
    try
    {
        await Task.Delay(300, ct);
        // Only fires if 300ms passed without another change
        var data = await _jsonlService.ParseFileAsync(path);
        _dispatcherQueue.TryEnqueue(() =>
        {
            WeakReferenceMessenger.Default.Send(new SessionDataUpdatedMessage(data));
        });
    }
    catch (TaskCanceledException) { /* debounced away */ }
}
```

### Pattern 5: DelegatingHandler for Cookie-Based Auth

**What:** A custom `HttpMessageHandler` that injects session cookies from CredentialService into every HTTP request to claude.ai. Keeps auth concerns out of ClaudeApiService business logic.

**When to use:** For all ClaudeApiService HTTP calls.

**Trade-offs:** Slightly more indirection than setting cookies directly, but cleanly separates auth transport from API logic. Also makes it trivial to handle 401/403 responses centrally (trigger re-auth flow).

**Example:**
```csharp
public class AuthenticatedHandler : DelegatingHandler
{
    private readonly ICredentialService _credentialService;

    public AuthenticatedHandler(ICredentialService credentialService)
        : base(new HttpClientHandler())
    {
        _credentialService = credentialService;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken ct)
    {
        var token = _credentialService.GetSessionToken();
        if (token != null)
            request.Headers.Add("Cookie", $"sessionKey={token}");

        var response = await base.SendAsync(request, ct);

        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            WeakReferenceMessenger.Default.Send(new AuthExpiredMessage());

        return response;
    }
}
```

## Data Flow

### Primary Data Flows

```
FLOW 1: Authentication
========================
LoginView (WebView2)
    │ User completes login on claude.ai
    ↓
LoginViewModel
    │ Extracts cookies via CoreWebView2.CookieManager.GetCookiesAsync()
    ↓
CredentialService
    │ Stores sessionKey via Win32 CredWrite (DPAPI encrypted)
    ↓
Messenger: AuthStateChangedMessage
    │
    ↓
NavigationService → navigates to MainView
ClaudeApiService → begins polling with stored token


FLOW 2: API Usage Data (Polling)
==================================
PeriodicTimer tick (configurable interval)
    ↓
ClaudeApiService
    │ GET https://claude.ai/api/organizations/{orgId}/usage
    │ GET https://claude.ai/api/organizations/{orgId}/usage?scope=weekly
    │ Cookies injected by AuthenticatedHandler
    ↓
UsageData / WeeklyUsage models
    ↓
Messenger: UsageDataUpdatedMessage
    ↓
MainViewModel
    │ Updates [ObservableProperty] fields
    ↓
MainView bindings → UsageChartControl.Invalidate()
                  → Weekly usage text/bars
                  → Context window progress bars


FLOW 3: Local Session Data (Event-Driven)
===========================================
Claude Code writes to JSONL file
    ↓
FileSystemWatcher (FileWatcherService)
    │ Changed/Created event (background thread)
    ↓
Debounce (300ms)
    ↓
JsonlService.ParseFileAsync()
    │ Reads last ~1MB, parses line-by-line
    │ Deduplicates by messageId/requestId
    ↓
TokenStats / SessionInfo models
    ↓
DispatcherQueue.TryEnqueue()
    ↓
Messenger: SessionDataUpdatedMessage
    ↓
MainViewModel
    │ Aggregates by session/today/week/month
    │ Calculates costs via PricingService
    ↓
MainView bindings → Token stats tabs
                  → Cost display with burn rate
                  → Session picker list


FLOW 4: Pricing Data (Cached HTTP)
=====================================
App start OR 12-hour cache expiry
    ↓
PricingService
    │ GET https://raw.githubusercontent.com/.../model_prices_and_context_window.json
    │ Falls back to Assets/fallback_pricing.json on failure
    ↓
pricing_cache.json (%LOCALAPPDATA%)
    ↓
TokenCalculator
    │ Applies tiered pricing (>200K tokens = higher rate)
    ↓
Cost calculation in MainViewModel


FLOW 5: Settings Changes
===========================
SettingsView user interaction
    ↓
SettingsViewModel [RelayCommand]
    │ Updates AppSettings model
    ↓
SettingsService
    │ Persists to settings.json
    ↓
Messenger: ThemeChangedMessage / RefreshIntervalChangedMessage
    ↓
MainViewModel → reconfigures polling timer
UsageChartControl → recalculates color palette, invalidates
App root element → RequestedTheme = Dark/Light
```

### State Management

There is no central state store. State lives in ViewModels as `[ObservableProperty]` fields. Cross-ViewModel communication happens exclusively through the Messenger. This is the standard WinUI 3 MVVM approach -- simpler than Redux-style patterns and appropriate for a single-window dashboard app with modest state complexity.

**State ownership:**
| State | Owner | Persistence |
|-------|-------|-------------|
| 5h usage data | MainViewModel | In-memory + usage_history.json |
| Weekly usage data | MainViewModel | In-memory only |
| Context window | MainViewModel | In-memory only |
| Token stats | MainViewModel | token_stats_cache.json |
| Session list | MainViewModel | Derived from JSONL scan |
| Pricing data | PricingService | pricing_cache.json |
| App settings | SettingsService | settings.json |
| Auth token | CredentialService | Windows Credential Manager |
| Update availability | MainViewModel | In-memory only |

## Build Order (Dependency Chain)

The architecture has clear dependency layers that dictate build order:

### Phase 1: Foundation (no feature dependencies)
1. **Project scaffold** -- Solution, .csproj, NuGet packages, folder structure
2. **Models** -- Pure data classes, no dependencies
3. **DI container** -- App.xaml.cs with ServiceCollection
4. **NavigationService** -- Frame navigation between pages
5. **SettingsService** -- JSON read/write for settings.json
6. **MainWindow + empty MainView/SettingsView** -- Navigation shell

### Phase 2: Authentication (blocks all API features)
1. **CredentialService** -- Win32 P/Invoke for CredRead/CredWrite
2. **LoginView + LoginViewModel** -- WebView2 login + cookie extraction
3. **AuthenticatedHandler** -- DelegatingHandler for HttpClient
4. **Auth flow integration** -- Login -> credential store -> navigate to main

### Phase 3: API Data (depends on Phase 2)
1. **ClaudeApiService** -- HTTP polling for usage endpoints
2. **MainViewModel** -- Observable properties for usage data
3. **MainView** -- Basic text display of 5h + weekly usage
4. **Timer-driven polling** -- PeriodicTimer + DispatcherQueue dispatch

### Phase 4: Charts (depends on Phase 3 data)
1. **UsageChartControl** -- Win2D CanvasControl + area chart rendering
2. **ColorThresholds** -- Zone color calculation (green/yellow/orange/red)
3. **Chart export** -- CanvasRenderTarget to PNG file/clipboard

### Phase 5: Local Data (independent of Phase 3, can parallel)
1. **FileWatcherService** -- FileSystemWatcher + debounce
2. **JsonlParser** -- Line-by-line JSONL parsing
3. **JsonlService** -- Aggregation + deduplication
4. **TokenCalculator** -- Cost calculation with tiered pricing
5. **PricingService** -- LiteLLM fetch + cache + fallback
6. **Session management UI** -- SessionPickerControl + token stats tabs

### Phase 6: Polish (depends on Phases 3-5)
1. **ProgressBarControl** -- Context window bars with model badges
2. **Dark/light mode toggle** -- RequestedTheme + chart palette recalc
3. **Localization** -- .resw files for DE/EN
4. **UpdateService** -- GitHub Releases check + InfoBar banner
5. **Window position persistence** -- Save/restore on close/open
6. **Inno Setup installer**

**Critical path:** Phase 1 -> Phase 2 -> Phase 3 -> Phase 4. Phases 5 and 6 can partially overlap with Phase 3/4.

## Anti-Patterns

### Anti-Pattern 1: Calling Services Directly from Code-Behind

**What people do:** Inject or create service instances in XAML code-behind (.xaml.cs) files and call them directly, bypassing the ViewModel.
**Why it's wrong:** Breaks MVVM separation. Code-behind becomes untestable and tightly coupled. State scattered across code-behind and ViewModel.
**Do this instead:** Code-behind should only do things that require a direct reference to XAML elements (e.g., WebView2 initialization, Win2D draw session setup). All logic and state goes through the ViewModel. Use `x:Bind` to connect View to ViewModel.

### Anti-Pattern 2: Static Singletons Instead of DI

**What people do:** Create `public static ClaudeApiService Instance { get; }` singletons instead of registering services in DI.
**Why it's wrong:** Untestable (can't mock), hidden dependencies, initialization order bugs, thread-safety landmines.
**Do this instead:** Register everything in `ServiceCollection`. Access via constructor injection in ViewModels and services.

### Anti-Pattern 3: Raising PropertyChanged from Background Threads

**What people do:** Update `[ObservableProperty]` fields directly from async service callbacks running on thread pool threads.
**Why it's wrong:** WinUI 3 throws `COMException` (RPC_E_WRONG_THREAD) when UI bindings try to process property changes from non-UI threads. Unlike WPF, WinUI 3 does not auto-marshal.
**Do this instead:** Always dispatch to UI thread via `DispatcherQueue.TryEnqueue()` before updating any observable property. Alternatively, use Messenger to send messages that ViewModels handle on the UI thread.

### Anti-Pattern 4: Trusting FileSystemWatcher Events Directly

**What people do:** Parse JSONL files on every FileSystemWatcher event without debouncing.
**Why it's wrong:** Windows FSW fires duplicate events, partial-write events, and rapid bursts. Parsing on each event causes excessive I/O, partial reads, and wasted CPU.
**Do this instead:** Debounce (300ms), verify file is not locked before reading, read only the tail of the file (seek to last ~1MB).

### Anti-Pattern 5: Hardcoding Win2D Draw Calls in ViewModel

**What people do:** Put chart drawing logic in the ViewModel to keep the View "dumb."
**Why it's wrong:** Win2D drawing sessions (`CanvasDrawingSession`) are GPU-bound UI resources that must run on the UI thread. They belong in the control. The ViewModel should expose data; the control decides how to render it.
**Do this instead:** ViewModel exposes `IReadOnlyList<UsageDataPoint>` as an observable property. The UsageChartControl subscribes to changes and calls `Invalidate()` to trigger a redraw in its `Draw` event handler.

## Integration Points

### External Services

| Service | Integration Pattern | Notes |
|---------|---------------------|-------|
| claude.ai API | HttpClient + DelegatingHandler (cookie auth) | Polling-based. Must handle 401/403 -> re-auth. orgId must be percent-encoded. |
| LiteLLM pricing (GitHub raw) | HttpClient GET + JSON cache | 12h cache. Fallback to bundled JSON. No auth needed. |
| GitHub Releases API | HttpClient GET (unauthenticated) | Rate limit: 60 req/hour for unauthenticated. Hourly check is fine. |
| Claude Code JSONL files | FileSystemWatcher + direct file I/O | Read-only. Files may be locked by Claude Code process -- handle IOException with retry. |
| Windows Credential Manager | Win32 P/Invoke (advapi32.dll) | CredRead/CredWrite/CredDelete. DPAPI-encrypted. Or use AdysTech.CredentialManager NuGet wrapper. |

### Internal Boundaries

| Boundary | Communication | Notes |
|----------|---------------|-------|
| View <-> ViewModel | x:Bind (compiled bindings) | One-way for display, two-way for settings inputs. Prefer `x:Bind` over `{Binding}` for compile-time safety and performance. |
| ViewModel <-> ViewModel | WeakReferenceMessenger | Typed messages. No direct ViewModel references. |
| ViewModel <-> Service | Constructor injection (DI) | ViewModels call service methods. Services notify via Messenger or return Task results. |
| Service <-> Service | Constructor injection (DI) | E.g., ClaudeApiService depends on ICredentialService. Keep dependency graph acyclic. |
| Background thread <-> UI thread | DispatcherQueue.TryEnqueue() | All UI property updates must go through this. Non-negotiable in WinUI 3. |
| Win2D Control <-> ViewModel | Data binding + Invalidate() | ViewModel pushes data, control pulls during Draw event. |

## Performance Boundaries

This is a single-user desktop app, not a server. "Scaling" means handling increasing data volumes gracefully.

| Concern | With 10 sessions | With 100 sessions | With 500+ sessions |
|---------|-------------------|--------------------|--------------------|
| JSONL parsing | <100ms, parse all files | 1-2s, need selective parsing | Cache parsed results, only re-parse changed files |
| Memory (JSONL data) | <5 MB | ~20 MB | Need rolling window, drop old data from memory |
| FileSystemWatcher | Works fine | Works fine (recursive) | May need to watch only active session directories |
| API polling | Trivial (2 requests/interval) | Same (API data is per-org, not per-session) | Same |
| Win2D chart render | <5ms | Same (chart shows aggregate, not per-session) | Same |

### First bottleneck: JSONL parsing at startup
Large JSONL files (>50MB) will cause noticeable startup delay. Mitigation: Parse only tail of files (last ~1MB), cache aggregated stats in token_stats_cache.json, load cache first and backfill lazily.

### Second bottleneck: FileSystemWatcher reliability
FSW can miss events under heavy I/O load. Mitigation: Periodic full rescan (every 5 minutes) as a safety net alongside event-driven updates.

## Sources

- [CommunityToolkit.Mvvm Introduction](https://learn.microsoft.com/en-us/dotnet/communitytoolkit/mvvm/) -- HIGH confidence
- [WinUI 3 MVVM + DI Tutorial](https://learn.microsoft.com/en-us/windows/apps/tutorials/winui-mvvm-toolkit/dependency-injection) -- HIGH confidence
- [Win2D for WinUI 3](https://microsoft.github.io/Win2D/WinUI3/html/Introduction.htm) -- HIGH confidence
- [Microsoft.Graphics.Win2D NuGet (v1.3.2)](https://www.nuget.org/packages/Microsoft.Graphics.Win2D) -- HIGH confidence
- [CanvasControl Class Reference](https://microsoft.github.io/Win2D/WinUI3/html/T_Microsoft_Graphics_Canvas_UI_Xaml_CanvasControl.htm) -- HIGH confidence
- [WebView2 Cookie Management Spec](https://github.com/MicrosoftEdge/WebView2Feedback/blob/main/specs/CookieManagement.md) -- HIGH confidence
- [WebView2 Cookie Auth Pattern (Anthony Simmon)](https://anthonysimmon.com/authenticating-http-requests-cookies-webview2-wpf/) -- MEDIUM confidence (WPF example, pattern applies to WinUI 3)
- [DispatcherQueue in WinUI 3](https://learn.microsoft.com/en-us/windows/apps/develop/dispatcherqueue) -- HIGH confidence
- [DI with WinUI 3 (Albert Akhmetov)](https://albertakhmetov.com/posts/2024/using-.net-build-in-dependency-injection-with-winui-apps/) -- MEDIUM confidence
- [Win2D Performance Discussion](https://github.com/microsoft/Win2D/issues/828) -- MEDIUM confidence

---
*Architecture research for: WinUI 3 MVVM real-time monitoring desktop app*
*Researched: 2026-03-09*
