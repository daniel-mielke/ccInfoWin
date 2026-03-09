# Technology Stack

**Project:** ccInfoWin (Claude Code Usage Monitor for Windows)
**Researched:** 2026-03-09

## Recommended Stack

### Core Platform

| Technology | Version | Purpose | Why | Confidence |
|------------|---------|---------|-----|------------|
| C# | 13 (.NET 9) | Application language | Latest stable C# with partial properties support needed for CommunityToolkit.Mvvm 8.4 source generators. C# 13 ships with .NET 9 SDK. | HIGH |
| .NET | 9.0 | Runtime | Current LTS-adjacent stable release (Nov 2024). .NET 8 would also work but .NET 9 gives us C# 13 partial properties which CommunityToolkit.Mvvm 8.4 leverages for cleaner ViewModels. | HIGH |
| Target Framework | `net9.0-windows10.0.19041.0` | Build target | Targets Windows 10 2004+ minimum (Build 19041) as specified in requirements. Enables WinRT API access for Windows-specific features. | HIGH |

### UI Framework

| Technology | Version | Purpose | Why | Confidence |
|------------|---------|---------|-----|------------|
| Windows App SDK | 1.8.260209005 (1.8.5 stable) | WinUI 3 runtime + APIs | Latest stable release (Feb 2026). 1.8 is the current supported branch receiving frequent fixes. Do NOT use 2.0 (experimental only). | HIGH |
| WinUI 3 | (ships with WinAppSDK 1.8) | UI framework | Native Windows 11 controls, dark/light theme, InfoBar, NavigationView -- all needed by the spec. No alternative is viable for a native Windows desktop app in 2026. | HIGH |
| WebView2 | (ships with WinAppSDK 1.8) | Embedded Chromium for claude.ai login | Pre-installed on Windows 11, bundled via Windows App SDK. No separate NuGet needed -- accessed through `Microsoft.Web.WebView2.Core` namespace included in WinAppSDK. | HIGH |

### Graphics / Charts

| Technology | Version | Purpose | Why | Confidence |
|------------|---------|---------|-----|------------|
| Win2D (`Microsoft.Graphics.Win2D`) | 1.3.2 | Hardware-accelerated 2D chart rendering | The ONLY option for GPU-accelerated immediate-mode 2D drawing in WinUI 3. Used for the area chart with gradient fills, glow effects, and PNG export via `CanvasRenderTarget`. SkiaSharp is an alternative but adds complexity and doesn't integrate as cleanly with WinUI 3's rendering pipeline. | HIGH |

### MVVM / Architecture

| Technology | Version | Purpose | Why | Confidence |
|------------|---------|---------|-----|------------|
| CommunityToolkit.Mvvm | 8.4.0 | MVVM base classes + source generators | Microsoft-maintained, de facto standard for WinUI 3 MVVM. Source generators eliminate boilerplate (`[ObservableProperty]`, `[RelayCommand]`). v8.4 adds partial property support for C# 13. | HIGH |

### Credential Storage

| Technology | Version | Purpose | Why | Confidence |
|------------|---------|---------|-----|------------|
| AdysTech.CredentialManager | 2.6.0 | Windows Credential Manager wrapper | Clean C# wrapper around Win32 `CredRead`/`CredWrite`/`CredDelete`. Avoids manual P/Invoke boilerplate to `advapi32.dll`. `PasswordVault` (WinRT) has documented compatibility issues in WinUI 3 full-trust/unpackaged apps -- do NOT use it. | MEDIUM |

### Supporting Libraries (Built-in .NET)

| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| `System.Text.Json` | (ships with .NET 9) | JSON parsing for JSONL files, settings, API responses | All JSON/JSONL operations. Do NOT add Newtonsoft.Json -- System.Text.Json is faster and already included. |
| `System.Net.Http.HttpClient` | (ships with .NET 9) | HTTP client for Claude API + LiteLLM pricing | All HTTP communication. Use `IHttpClientFactory` pattern via a singleton for proper socket management. |
| `System.IO.FileSystemWatcher` | (ships with .NET 9) | Watch JSONL file changes | Real-time session monitoring. Needs debouncing (300ms) due to duplicate events. |
| `Microsoft.Windows.SDK.BuildTools` | latest | Windows SDK build integration | Always needed alongside Windows App SDK. Auto-included by project template. |

### Optional WinUI Community Controls

| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| `CommunityToolkit.WinUI.Controls.SettingsControls` | 8.2.251219 | Settings page UI controls | Nice-to-have for the settings page. Provides `SettingsCard` and `SettingsExpander` that match Windows 11 Settings app style. Optional -- can be implemented manually. |
| `CommunityToolkit.WinUI.Animations` | 8.2.251219 | Smooth UI transitions | Only if chart/page transitions need polish. Not critical for MVP. |

### Build & Distribution

| Technology | Version | Purpose | Why | Confidence |
|------------|---------|---------|-----|------------|
| Visual Studio 2022 | 17.12+ | IDE | Required for .NET 9 + WinUI 3 workload. Earlier versions lack .NET 9 support. | HIGH |
| Inno Setup | 6.7.x | EXE installer | Proven, free, scriptable installer. Supports per-user install without admin rights. Simpler than MSIX for unpackaged distribution. Alternative NSIS works but Inno Setup has better documentation and community. | HIGH |
| Windows App SDK Runtime | 1.8.x | Runtime prerequisite | Unpackaged apps need the WinAppSDK runtime installed. Inno Setup script must check for / install the runtime redistributable as a prerequisite. | HIGH |

## Alternatives Considered

| Category | Recommended | Alternative | Why Not |
|----------|-------------|-------------|---------|
| UI Framework | WinUI 3 | WPF | WPF is legacy. No native Windows 11 styling, no built-in dark mode, no InfoBar, no WebView2 integration without extra work. WinUI 3 is Microsoft's current recommendation. |
| UI Framework | WinUI 3 | Avalonia UI | Cross-platform but loses native Windows 11 look/feel. WebView2 integration is more complex. Win2D not available. |
| UI Framework | WinUI 3 | Electron / Tauri | Massive overhead for a simple monitoring widget. 200+ MB vs <50 MB. Contradicts performance requirements. |
| Charts | Win2D | LiveCharts2 | LiveCharts2 works with WinUI 3 but doesn't support the specific gradient area chart with glow effects from the spec. Win2D gives full pixel-level control needed for the macOS-matching design. |
| Charts | Win2D | SkiaSharp | Works but adds a separate rendering engine alongside WinUI 3's own. Win2D is Microsoft's official 2D API for WinUI 3 and integrates natively. |
| Charts | Win2D | OxyPlot | No official WinUI 3 support. Dead end. |
| MVVM | CommunityToolkit.Mvvm | Prism | Prism is heavier (DI container, modules, regions). Overkill for a single-window app. CommunityToolkit.Mvvm is leaner and Microsoft-maintained. |
| MVVM | CommunityToolkit.Mvvm | ReactiveUI | Steeper learning curve, Rx complexity not justified for this app's simple data flows. |
| JSON | System.Text.Json | Newtonsoft.Json | Already built into .NET 9, faster, lower allocations. No reason to add an external dependency. |
| Credentials | AdysTech.CredentialManager | PasswordVault | Known bugs in WinUI 3 full-trust/unpackaged apps. Microsoft's own documentation acknowledges issues. |
| Credentials | AdysTech.CredentialManager | Raw P/Invoke | Works but AdysTech saves ~100 lines of marshaling boilerplate with zero overhead. |
| Installer | Inno Setup | MSIX | MSIX requires packaging, signing, and adds complexity for sideloading. Inno Setup produces a simple EXE that needs no Store, no certificates, no admin. |
| Installer | Inno Setup | WiX | WiX is powerful but far more complex for a simple per-user install scenario. |
| Runtime | .NET 9 | .NET 8 (LTS) | .NET 8 works but misses C# 13 partial properties that make CommunityToolkit.Mvvm 8.4 source generators significantly cleaner. .NET 9 is stable and well-supported by WinAppSDK 1.8. |

## Project File (.csproj) Template

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
    <PackageReference Include="Microsoft.Graphics.Win2D" Version="1.3.2" />
    <PackageReference Include="CommunityToolkit.Mvvm" Version="8.4.0" />
    <PackageReference Include="AdysTech.CredentialManager" Version="2.6.0" />
  </ItemGroup>

</Project>
```

**Critical notes on the .csproj:**
- `WindowsPackageType=None` is required for unpackaged deployment (no MSIX).
- `Platforms` must NOT include `AnyCPU` -- WinUI 3 does not support it. Use `x64` and optionally `ARM64`.
- `UseWinUI=true` enables WinUI 3 XAML compilation.
- `TargetPlatformMinVersion` ensures compatibility with Windows 10 Build 19041+.

## Installation Commands

```bash
# Create project from template (Visual Studio 2022 recommended)
# Use "Blank App, Packaged (WinUI 3 in Desktop)" template, then switch to unpackaged

# Or via dotnet CLI:
dotnet new winui3 -n CCInfoWindows --framework net9.0

# NuGet packages (if adding manually)
dotnet add package Microsoft.WindowsAppSDK --version 1.8.260209005
dotnet add package Microsoft.Graphics.Win2D --version 1.3.2
dotnet add package CommunityToolkit.Mvvm --version 8.4.0
dotnet add package AdysTech.CredentialManager --version 2.6.0

# Optional (settings page controls)
dotnet add package CommunityToolkit.WinUI.Controls.SettingsControls --version 8.2.251219
```

## Runtime Prerequisites for End Users

| Prerequisite | How to Handle |
|-------------|---------------|
| .NET 9 Desktop Runtime | Bundle via self-contained publish OR check/install via Inno Setup prerequisite |
| Windows App SDK 1.8 Runtime | Must be installed. Inno Setup script should include the WinAppSDK runtime installer as a prerequisite check. |
| WebView2 Runtime | Pre-installed on Windows 11. For Windows 10: Inno Setup should check and offer Evergreen Bootstrapper download. |

**Recommended publish strategy:** Framework-dependent (smaller download, ~10-20 MB) with Inno Setup checking for and installing .NET 9 Desktop Runtime + WinAppSDK Runtime as prerequisites. Self-contained publish (~80-150 MB) is the fallback if prerequisite management proves too complex.

## Key Version Constraints

| Constraint | Details |
|-----------|---------|
| Visual Studio | 17.12+ required for .NET 9 targeting |
| Windows SDK | 10.0.19041.0 minimum for WinUI 3 compatibility |
| Win2D 1.3.2 | Requires WinAppSDK 1.5+, confirmed compatible with 1.8 |
| CommunityToolkit.Mvvm 8.4 | Requires .NET 8+ and C# 13 for partial property generators |

## Sources

- [Windows App SDK Downloads - Microsoft Learn](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/downloads)
- [Windows App SDK 1.8 Release Notes](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/release-notes/windows-app-sdk-1-8)
- [WinUI 3 Overview - Microsoft Learn](https://learn.microsoft.com/en-us/windows/apps/winui/winui3/)
- [NuGet: Microsoft.WindowsAppSDK 1.8.260209005](https://www.nuget.org/packages/Microsoft.WindowsAppSdk/)
- [NuGet: Microsoft.Graphics.Win2D 1.3.2](https://www.nuget.org/packages/Microsoft.Graphics.Win2D)
- [NuGet: CommunityToolkit.Mvvm 8.4.0](https://www.nuget.org/packages/CommunityToolkit.Mvvm)
- [.NET Community Toolkit 8.4 Announcement](https://devblogs.microsoft.com/dotnet/announcing-the-dotnet-community-toolkit-840/)
- [NuGet: AdysTech.CredentialManager 2.6.0](https://www.nuget.org/packages/AdysTech.CredentialManager)
- [WinUI 3 Unpackaged Deployment Guide](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/deploy-unpackaged-apps)
- [Inno Setup Official Site](https://jrsoftware.org/isinfo.php)
- [CommunityToolkit.WinUI Controls](https://github.com/CommunityToolkit/Windows)
- [Win2D Overview - Microsoft Learn](https://learn.microsoft.com/en-us/windows/apps/develop/win2d/)
