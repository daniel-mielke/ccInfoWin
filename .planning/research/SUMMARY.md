# Project Research Summary

**Project:** ccInfoWin (Claude Code Usage Monitor for Windows)
**Domain:** Windows desktop real-time monitoring application
**Researched:** 2026-03-09
**Confidence:** HIGH

## Executive Summary

ccInfoWin is a native Windows desktop monitoring tool that tracks Claude Code usage metrics -- 5-hour rate limits, weekly quotas, token consumption, and costs. The reference implementation is ccInfo for macOS. The Windows landscape has exactly one competitor (claude-usage-widget, Electron-based, bare-bones). Building this as a WinUI 3 app with .NET 9, Win2D for custom charts, and CommunityToolkit.Mvvm for MVVM plumbing is the only viable path for a native Windows 11 experience. The stack is mature, well-documented, and every technology choice has HIGH confidence from official Microsoft sources.

The architecture follows standard DI-based MVVM with two independent data paths: (1) HTTP polling against the unofficial claude.ai API for rate limit data, and (2) FileSystemWatcher-driven JSONL parsing for local session/token/cost data. These two paths converge in the MainViewModel via the WeakReferenceMessenger pattern. The single-window design with Frame-based navigation keeps the app simple. Win2D provides the GPU-accelerated area chart that is the visual signature of ccInfo.

The critical risks are concentrated in three areas: WebView2 initialization reliability (corrupted User Data Folder, cookie threading constraints), Win2D memory leaks from reference cycles at the C#/C++ interop boundary, and FileSystemWatcher's notorious unreliability on Windows (duplicate events, buffer overflows, file locking conflicts). All three have well-documented prevention patterns. The fourth risk is deployment -- unpackaged WinUI 3 apps require the Windows App SDK Runtime, which is NOT pre-installed on any Windows version. The Inno Setup installer must handle this prerequisite or the app will silently fail to launch on end-user machines.

## Key Findings

### Recommended Stack

The stack is .NET 9 with C# 13, WinUI 3 via Windows App SDK 1.8.5, and Win2D 1.3.2 for chart rendering. CommunityToolkit.Mvvm 8.4 provides source-generated MVVM boilerplate elimination. Credentials go through AdysTech.CredentialManager (Win32 Credential Manager wrapper) because the WinRT PasswordVault has documented compatibility issues in unpackaged WinUI 3 apps. Distribution uses Inno Setup with framework-dependent publishing (~10-20 MB) plus runtime prerequisite checks.

**Core technologies:**
- **.NET 9 + C# 13:** Latest stable runtime, enables partial property source generators for cleaner ViewModels
- **Windows App SDK 1.8.5 (WinUI 3):** Native Windows 11 controls, dark/light theme, WebView2, InfoBar -- no viable alternative
- **Win2D 1.3.2:** Only option for GPU-accelerated 2D drawing in WinUI 3, needed for the gradient area chart with glow effects
- **CommunityToolkit.Mvvm 8.4:** Microsoft-maintained MVVM toolkit, de facto standard, source generators eliminate boilerplate
- **AdysTech.CredentialManager 2.6:** Clean wrapper around Win32 CredRead/CredWrite, avoids PasswordVault bugs
- **Inno Setup 6.7:** Per-user EXE installer without admin rights, chains runtime prerequisites

**Critical version constraints:** Visual Studio 17.12+ required. No AnyCPU platform support (WinUI 3 limitation). Target framework must be `net9.0-windows10.0.19041.0`.

### Expected Features

**Must have (table stakes):**
- 5-hour window percentage + reset countdown -- every competitor shows this
- Weekly usage limit display -- second most important metric
- Color-coded progress bars (green/yellow/orange/red) -- visual convention across all tools
- WebView2 authentication + secure token storage -- required for API access
- Auto-refresh on interval -- manual-only feels broken
- Dark mode -- developer tools default dark
- Session token re-validation on startup -- broken auth on launch means uninstall
- No telemetry / privacy-first -- hygiene factor for dev tools

**Should have (differentiators vs. Windows competition):**
- Interactive area chart with color zones -- THE signature feature, no Windows tool has this
- Context window status with subagent tracking -- unique to ccInfo, prevents autocompact surprise
- Multi-session management -- essential for devs with multiple projects
- Token statistics by time period -- only CLI tools offer this, no Windows GUI
- Cost calculation with live LiteLLM pricing -- real-time cost awareness
- Model-specific breakdown (Sonnet/Opus) -- which model eats your quota

**Defer (v2+):**
- ML-based usage predictions, multi-account support, mobile companion, GitHub activity heatmaps, JSON/CSV export, notification toasts, configurable color thresholds, system tray integration, transparent/blur background

### Architecture Approach

Standard DI-based MVVM with a single window, Frame navigation between three views (Main, Login, Settings), and a flat service layer. Cross-component communication uses WeakReferenceMessenger. Two independent data pipelines (API polling + JSONL file watching) feed into the MainViewModel. Win2D chart rendering is isolated in a custom control that receives data via binding and handles all GPU drawing internally.

**Major components:**
1. **MainView + MainViewModel** -- Primary dashboard: chart, usage bars, context window, session picker, token stats, costs
2. **LoginView + LoginViewModel** -- WebView2 login flow, cookie extraction, credential storage
3. **SettingsView + SettingsViewModel** -- App preferences (refresh interval, theme, language, autostart)
4. **ClaudeApiService** -- HTTP polling for 5h/weekly usage data with cookie-based auth via DelegatingHandler
5. **JsonlService + FileWatcherService** -- JSONL parsing with debounced FileSystemWatcher for local session data
6. **PricingService** -- LiteLLM pricing fetch, 12h cache, fallback to bundled JSON
7. **UsageChartControl** -- Win2D CanvasControl for the area chart with gradient fills and glow effects

### Critical Pitfalls

1. **Win2D memory leak from reference cycles** -- MUST call `RemoveFromVisualTree()` and null the control reference in the page Unloaded handler. Without this, GPU memory grows continuously and the app crashes after hours of use.
2. **WebView2 initialization failure** -- Explicitly set User Data Folder path, await `EnsureCoreWebView2Async()`, catch failures and retry after deleting corrupted UDF. Without this, login screen never appears.
3. **WebView2 cookie threading** -- Access cookie properties (Name, Value) ONLY on the UI thread. The async method looks thread-safe but cookie objects have UI thread affinity.
4. **FileSystemWatcher unreliability** -- Debounce 300ms, increase buffer to 64KB, handle Error event for buffer overflow recovery, open files with `FileShare.ReadWrite | FileShare.Delete`.
5. **Missing Windows App Runtime on end-user machines** -- Bundle the runtime installer as Inno Setup prerequisite. Without this, the EXE silently fails to start on non-developer machines.

## Implications for Roadmap

Based on research, suggested phase structure:

### Phase 1: Foundation and Authentication
**Rationale:** Authentication is the hard gate -- nothing works without API access. WebView2 initialization is the highest-risk integration point (Pitfalls 2, 3). Get this working and proven first.
**Delivers:** Project scaffold, DI container, navigation shell, WebView2 login, credential storage, session validation, logout. A launchable app that can authenticate.
**Addresses:** FA-010 through FA-013 (auth features), project structure, NuGet setup
**Avoids:** Pitfall 2 (WebView2 init), Pitfall 3 (cookie threading), Pitfall 4 (unpackaged deployment basics)

### Phase 2: Core Monitoring Dashboard
**Rationale:** With auth working, the API polling path delivers the core value proposition. This is where ccInfoWin becomes usable and matches the basic Windows competitor.
**Delivers:** 5-hour usage percentage, reset countdown, weekly limit display, color-coded progress bars, auto-refresh polling, dark/light mode.
**Addresses:** FA-020, FA-021, FA-030, FA-090, FA-094, FA-095, NF-013
**Avoids:** Pitfall 6 (DispatcherQueue threading), Pitfall 9 (auth expiry handling)

### Phase 3: Area Chart (Signature Feature)
**Rationale:** The interactive area chart is THE differentiator. It depends on Phase 2 data being available. Win2D integration carries the memory leak risk (Pitfall 1) so it needs focused attention.
**Delivers:** Win2D area chart with color zones, glow indicator, time axis, interactive hover. The feature that makes screenshots compelling.
**Addresses:** FA-022 through FA-028
**Avoids:** Pitfall 1 (Win2D memory leak), Pitfall 8 (DPI/theme changes)

### Phase 4: Local Session Data Pipeline
**Rationale:** Independent of the API path. FileSystemWatcher + JSONL parsing enables context window, multi-session, and token stats. This is the second data pipeline and can be built in parallel with chart polish.
**Delivers:** JSONL file watching, session discovery, context window status with subagents, multi-session picker, token statistics by time period.
**Addresses:** FA-040 through FA-044, FA-050 through FA-054, FA-060 through FA-063
**Avoids:** Pitfall 5 (FileSystemWatcher), Pitfall 7 (file locking)

### Phase 5: Cost Analytics
**Rationale:** Cost calculation depends on token data (Phase 4) and LiteLLM pricing integration. Complex tiered pricing logic needs the data pipeline to be stable first.
**Delivers:** Cost per session/day/week/month, burn rate, model-specific breakdown, LiteLLM pricing with cache and fallback.
**Addresses:** FA-070 through FA-075, FA-031
**Avoids:** Pitfall 13 (LiteLLM schema changes)

### Phase 6: Export, Polish, and Distribution
**Rationale:** Polish and distribution features come last. Chart export requires the chart to be complete. Installer requires the app to be feature-complete. Localization is low-effort but should not block earlier phases.
**Delivers:** Chart PNG export + clipboard, localization (DE/EN), autostart, auto-update check, window position persistence, accessibility, Inno Setup installer with runtime prerequisites.
**Addresses:** FA-080 through FA-082, FA-091, FA-093, FA-100 through FA-102, NF-010, NF-040
**Avoids:** Pitfall 4 (missing runtime), Pitfall 10 (off-screen window), Pitfall 12 (localization resources), Pitfall 14 (per-user install)

### Phase Ordering Rationale

- **Auth first:** Every API feature is blocked by authentication. WebView2 is the highest-risk integration. Proving it works removes the biggest uncertainty.
- **API dashboard before JSONL pipeline:** The API path delivers visible value faster (usage percentages, countdown). JSONL parsing is more complex and delivers secondary features.
- **Chart as its own phase:** Win2D is a completely different rendering paradigm from XAML. It deserves focused attention to get the memory management right from the start.
- **JSONL before costs:** Token data is a prerequisite for cost calculation. The file watching infrastructure must be stable before building analytics on top of it.
- **Distribution last:** Inno Setup + runtime bundling is mechanical work that should happen when the app is feature-complete. Testing on clean VMs is the final validation.

### Research Flags

Phases likely needing deeper research during planning:
- **Phase 1 (Auth):** WebView2 cookie extraction patterns for the specific claude.ai login flow need validation. The unofficial API endpoints may have changed since the macOS reference was built.
- **Phase 3 (Chart):** Win2D area chart with gradient fills and glow effects is custom rendering. No off-the-shelf examples match the ccInfo design exactly. Needs prototype validation.
- **Phase 5 (Costs):** LiteLLM pricing JSON schema and tiered pricing calculation need validation against current API responses. The pricing model names must match Claude's current model identifiers.

Phases with standard patterns (skip research-phase):
- **Phase 2 (Dashboard):** Standard WinUI 3 MVVM with HttpClient polling. Well-documented, established patterns.
- **Phase 4 (JSONL):** FileSystemWatcher + JSON parsing is well-documented .NET territory. Pitfalls are known and prevention patterns are clear.
- **Phase 6 (Polish):** Inno Setup, localization, autostart are all commodity patterns with extensive documentation.

## Confidence Assessment

| Area | Confidence | Notes |
|------|------------|-------|
| Stack | HIGH | All technologies are Microsoft-maintained with official documentation. Version compatibility verified across NuGet packages. |
| Features | HIGH | Competitive landscape thoroughly analyzed with 7+ tools compared. Feature dependencies mapped. Anti-features explicitly identified. |
| Architecture | HIGH | Standard WinUI 3 MVVM patterns from official Microsoft tutorials. Data flow patterns verified against Win2D and WebView2 documentation. |
| Pitfalls | HIGH | 14 pitfalls identified, 10 with HIGH-confidence sources (official docs, confirmed GitHub issues). Prevention patterns include code examples. |

**Overall confidence:** HIGH

### Gaps to Address

- **Claude.ai API stability:** This is an unofficial API. Endpoint URLs, cookie names, and response formats could change without notice. Monitor the macOS ccInfo repository for API change tracking. Build the ClaudeApiService as a replaceable component.
- **Win2D area chart design:** No existing Win2D example matches the exact ccInfo chart design (gradient fills, color zones, glow indicator, interactive hover). Will need prototype iteration during Phase 3.
- **LiteLLM pricing model names:** The mapping between LiteLLM's model identifiers and Claude's actual model names needs runtime validation. Ship a fallback pricing file to handle mismatches.
- **.NET 9 + WinAppSDK 1.8 long-term support:** .NET 9 is not LTS (ends May 2026). Plan migration to .NET 10 (LTS, ships Nov 2025) when WinAppSDK confirms compatibility. This is not urgent but should be tracked.

## Sources

### Primary (HIGH confidence)
- [Windows App SDK 1.8 Release Notes](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/release-notes/windows-app-sdk-1-8) -- stack compatibility
- [WinUI 3 Overview](https://learn.microsoft.com/en-us/windows/apps/winui/winui3/) -- UI framework
- [Win2D for WinUI 3](https://microsoft.github.io/Win2D/WinUI3/html/Introduction.htm) -- chart rendering
- [Win2D: Avoiding Memory Leaks](https://microsoft.github.io/Win2D/WinUI3/html/RefCycles.htm) -- critical pitfall
- [CommunityToolkit.Mvvm](https://learn.microsoft.com/en-us/dotnet/communitytoolkit/mvvm/) -- MVVM framework
- [Windows App SDK Unpackaged Deployment](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/deploy-unpackaged-apps) -- deployment
- [FileSystemWatcher Duplicate Events](https://learn.microsoft.com/en-us/archive/blogs/ahamza/filesystemwatcher-generates-duplicate-events-how-to-workaround) -- file watching pitfall
- [WebView2 CookieManager Threading (#1283)](https://github.com/MicrosoftEdge/WebView2Feedback/issues/1283) -- auth pitfall

### Secondary (MEDIUM confidence)
- [ccInfo macOS reference app](https://github.com/stefanlange/ccInfo) -- feature reference
- [claude-usage-widget](https://github.com/SlavomirDurej/claude-usage-widget) -- Windows competitor analysis
- [WebView2 Cookie Auth Pattern (Anthony Simmon)](https://anthonysimmon.com/authenticating-http-requests-cookies-webview2-wpf/) -- auth implementation
- [AdysTech.CredentialManager NuGet](https://www.nuget.org/packages/AdysTech.CredentialManager) -- credential storage
- [Inno Setup Documentation](https://jrsoftware.org/isinfo.php) -- installer

### Tertiary (LOW confidence)
- Claude.ai API endpoints -- unofficial, no documentation, reverse-engineered from macOS reference. Stability unknown.
- LiteLLM pricing JSON schema -- external dependency, schema may change without notice.

---
*Research completed: 2026-03-09*
*Ready for roadmap: yes*
