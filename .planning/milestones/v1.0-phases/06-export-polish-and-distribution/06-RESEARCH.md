# Phase 6: Export, Polish, and Distribution — Research

**Researched:** 2026-03-16
**Domain:** Win2D offscreen rendering, WinUI 3 localization, Windows App SDK pickers, Inno Setup, GitHub Releases API
**Confidence:** HIGH

---

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions

**Chart Export — Button Placement**
- Export icon button positioned top-right of the 5-STUNDEN-FENSTER section header
- Button appears on hover/tap over the chart area — does not clutter the UI in normal use
- Single button opens a MenuFlyout with two options: "Speichern als PNG..." and "In Zwischenablage kopieren"

**Chart Export — Content and Layout**
- Export includes: percentage display, "5-STUNDEN-FENSTER" label, reset countdown, chart, and "CCINFO" watermark
- Export layout follows macOS reference (spec/v1.7.1/flächenfüllung-chart-macOS.png): percentage + countdown ABOVE chart, CCINFO branding bottom-right
- This differs from app layout (percentage below chart) — export renders its own composition via CanvasRenderTarget

**Chart Export — Rendering**
- Always dark background regardless of current app theme (matches EXPT-01 requirement "dark PNG")
- 2x Retina resolution (double pixel density, e.g., 656x480 for a ~328x240 export area)
- Thumbnail preview handled natively by Windows FileSavePicker (no custom preview dialog)
- Clipboard copy uses Win2D render → BitmapEncoder → DataPackage with SetBitmap

**Auto-Update — Banner**
- WinUI 3 InfoBar at top of MainView (same pattern as existing "Session Expired" InfoBar)
- Severity: Informational
- Message: "Update v{version} verfügbar"
- ActionButton: "Download" — opens GitHub Release page in default browser via Process.Start
- No in-app download or automatic installation

**Auto-Update — Version Check**
- Hourly check via GitHub Releases API: GET /repos/daniel-mielke/ccInfoWin/releases/latest
- SemVer tag comparison: parse tag_name (e.g., "v1.2.0") and compare against Assembly version
- Pre-releases ignored (only latest stable release)
- Network allowed to raw.githubusercontent.com already in SECU-04 — GitHub API (api.github.com) must be added to allowed hosts

**Auto-Update — Dismiss Behavior**
- User can close banner with X button
- Dismissed version stored in AppSettings (e.g., "dismissedUpdateVersion": "1.2.0")
- Banner suppressed for that version — reappears only when a newer version is detected
- On app restart: check runs, banner shown only if newer version than dismissed

**Distribution — Build**
- Self-contained publish: `dotnet publish -c Release -r win-x64 --self-contained -p:PublishTrimmed=true -p:TrimMode=partial`
- No .NET Runtime prerequisite for end users
- No code signing — unsigned installer (SmartScreen warning acceptable for open-source)

**Distribution — Installer**
- Inno Setup EXE installer, per-user installation (no admin required)
- Install to %LOCALAPPDATA%\Programs\CCInfoWindows (per-user default)
- Installer options checkboxes: Desktop shortcut (default: on), Autostart at login (default: on)
- Autostart via Registry Run key (HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Run)
- Autostart toggle also available in app Settings (reads/writes same Registry key)

**Distribution — GitHub**
- Repository: https://github.com/daniel-mielke/ccInfoWin
- Release includes: Installer EXE, README.md, LICENSE (MIT), screenshots
- GitHub Release tag format: v{major}.{minor}.{patch} (e.g., v1.0.0)

### Claude's Discretion
- Localization approach (x:Uid + .resw resource files vs code-behind string tables)
- System language detection vs manual-only language switch
- Accessibility label strategy (AutomationProperties.Name on all interactive elements)
- Window position save/restore implementation (already have WindowState in AppSettings + SaveWindowState/LoadWindowState — just need to wire up Window events)
- Inno Setup script structure and compression settings
- README content and screenshot selection
- Exact export image dimensions and padding
- MenuFlyout styling for export button
- GitHub Actions CI/CD for automated builds (if any)
- Trimming warnings resolution strategy

### Deferred Ideas (OUT OF SCOPE)
None — discussion stayed within phase scope
</user_constraints>

---

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|-----------------|
| EXPT-01 | 5-hour chart exportable as dark PNG via system save dialog | Win2D CanvasRenderTarget + Microsoft.Windows.Storage.Pickers.FileSavePicker (WindowId constructor) |
| EXPT-02 | Thumbnail preview shown during export | Native to FileSavePicker — no code required |
| EXPT-03 | Option to copy chart directly to clipboard | Win2D CanvasRenderTarget → InMemoryRandomAccessStream → BitmapEncoder → DataPackage.SetBitmap |
| SETT-02 | Autostart option to launch app at Windows login | HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Run via Microsoft.Win32.Registry API |
| SETT-04 | Language support for German and English | WinUI3Localizer v2.3.0 — runtime switch without restart, unpackaged app supported |
| SETT-07 | Settings displayed in-app (frame navigation, no separate window) | Already exists — SettingsView and NavigationService wired; extend with new settings rows |
| UPDT-01 | Hourly check for new version via GitHub Releases API | HttpClient GET api.github.com/repos/.../releases/latest + SemVer string comparison |
| UPDT-02 | In-app banner (InfoBar) when update available with download link | InfoBar.IsOpen binding — identical to existing Session Expired InfoBar pattern |
| UPDT-03 | No intrusive OS toast notifications — banner only | No Windows.UI.Notifications usage; InfoBar-only approach |
| UIPF-05 | Window position saved on close and restored on startup | Already implemented (SaveWindowState/LoadWindowState + AppWindow events) — just needs wiring in MainWindow.xaml.cs |
| UIPF-07 | All interactive elements screen-reader compatible | AutomationProperties.Name on every Button, ToggleSwitch, ComboBox; .resw entries for localized names |
| DIST-01 | Inno Setup EXE installer (per-user, no admin) | Inno Setup 6.3.3; PrivilegesRequired=lowest; DefaultDirName={localappdata}\Programs\CCInfoWindows |
| DIST-02 | GitHub public repository with README, LICENSE, screenshots | Standard GitHub release workflow; no tooling required |
| DIST-03 | Self-contained publish with runtime prerequisite check | `dotnet publish -c Release -r win-x64 --self-contained -p:PublishTrimmed=true -p:TrimMode=partial` |
</phase_requirements>

---

## Summary

Phase 6 covers six distinct technical areas: Win2D offscreen rendering for PNG export, WinUI 3 clipboard integration, the new Windows App SDK 1.8 file picker API, runtime localization without app restart, Registry-based autostart, and Inno Setup per-user installer creation.

Window position save/restore is already fully implemented in `MainWindow.xaml.cs` (lines 73–98) — `RestoreWindowState()` and `OnClosing` handler both exist. This requires no implementation work, only verification. The InfoBar update banner pattern is also proven via the existing "Session Expired" InfoBar. The two genuinely new technical areas are Win2D offscreen rendering and WinUI3Localizer.

The most critical discovery: for unpackaged WinUI 3 apps, `ResourceContext.SetGlobalQualifierValue` crashes on Windows 11 and does not update x:Uid-bound strings. The correct approach for runtime language switching without restart is **WinUI3Localizer v2.3.0** (NuGet), which stores .resw files next to the executable and applies language changes instantly to all bound XAML elements.

**Primary recommendation:** Use `Microsoft.Windows.Storage.Pickers.FileSavePicker` (Windows App SDK 1.8 namespace) with `WindowId` constructor for the file picker — no `InitializeWithWindow` hack needed. Use WinUI3Localizer for runtime language switching. Use Win2D `CanvasRenderTarget` at 192 DPI (2x) for the PNG export.

---

## Standard Stack

### Core
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| Microsoft.Graphics.Win2D | 1.3.2 (already in project) | Offscreen PNG rendering via CanvasRenderTarget | Same device used by existing CanvasControl chart |
| WinUI3Localizer | 2.3.0 | Runtime DE/EN language switching without restart | Only solution that works for unpackaged WinUI 3 at runtime |
| Microsoft.Win32.Registry | built-in (.NET) | Read/write HKCU autostart Run key | Standard .NET API, no extra NuGet needed |
| Microsoft.Windows.Storage.Pickers | Windows App SDK 1.8 (already in project) | FileSavePicker with WindowId | Cleaner WinAppSDK-native API, no HWND interop dance |

### Supporting
| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| Windows.ApplicationModel.DataTransfer.Clipboard | WinRT (built-in) | Clipboard.SetContent for PNG image copy | EXPT-03 clipboard path |
| Windows.Storage.Streams.InMemoryRandomAccessStream | WinRT (built-in) | PNG byte stream for clipboard DataPackage | Intermediate buffer between Win2D and clipboard |
| Windows.Graphics.Imaging.BitmapEncoder | WinRT (built-in) | PNG encoding from pixel data | Required for clipboard SetBitmap |
| System.Net.Http.HttpClient | .NET (singleton in DI) | GitHub Releases API version check | Already registered as singleton |

### Alternatives Considered
| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| WinUI3Localizer | Native x:Uid + .resw only | Native approach does not support runtime switching in unpackaged apps; requires app restart |
| WinUI3Localizer | ResourceContext.QualifierValues override | Crashes on Windows 11; does not update x:Uid-bound elements |
| Microsoft.Windows.Storage.Pickers | Windows.Storage.Pickers + InitializeWithWindow | Old approach works but requires HWND boilerplate; new API is cleaner |

**Installation:**
```bash
dotnet add package WinUI3Localizer --version 2.3.0
```

---

## Architecture Patterns

### Recommended Project Structure (additions for Phase 6)
```
CCInfoWindows/CCInfoWindows/
  Services/
    UpdateService.cs          # IUpdateService — hourly GitHub Releases check
    Interfaces/
      IUpdateService.cs
  Helpers/
    ExportHelper.cs           # Win2D CanvasRenderTarget rendering, PNG save, clipboard copy
    RegistryHelper.cs         # Read/write HKCU autostart Run key
  Strings/
    en-US/
      Resources.resw          # English strings (default)
    de-DE/
      Resources.resw          # German strings
  installer/
    setup.iss                 # Inno Setup script
```

### Pattern 1: Win2D Offscreen PNG Export

**What:** Render chart to CanvasRenderTarget at 192 DPI (2x pixel density), then save via FileSavePicker.
**When to use:** EXPT-01 (save as PNG) and EXPT-03 (clipboard copy).

```csharp
// Source: https://microsoft.github.io/Win2D/WinUI3/html/T_Microsoft_Graphics_Canvas_CanvasRenderTarget.htm
// Note: CanvasDevice must come from the existing CanvasControl's Device property — reusing the
// same device avoids creating a second GPU context.
var exportWidth = 328f;
var exportHeight = 240f;
var dpi = 192f; // 2x retina

var device = CanvasDevice.GetSharedDevice();
using var renderTarget = new CanvasRenderTarget(device, exportWidth, exportHeight, dpi);
using (var ds = renderTarget.CreateDrawingSession())
{
    ds.Clear(ExportBackgroundColor); // always #1E1E1E regardless of app theme
    DrawExportComposition(ds, exportWidth, exportHeight, points, percentage, countdown);
}

// Save path
using var fileStream = await file.OpenAsync(FileAccessMode.ReadWrite);
await renderTarget.SaveAsync(fileStream, CanvasBitmapFileFormat.Png);
```

**Key constraint:** Close/dispose the drawing session BEFORE calling `SaveAsync` or using the render target as a source.

### Pattern 2: Win2D → Clipboard

**What:** Render to CanvasRenderTarget, encode PNG bytes into InMemoryRandomAccessStream, put in DataPackage.
**When to use:** EXPT-03.

```csharp
// Source: https://learn.microsoft.com/en-us/uwp/api/windows.applicationmodel.datatransfer.datapackage.setbitmap
var stream = new InMemoryRandomAccessStream();
await renderTarget.SaveAsync(stream, CanvasBitmapFileFormat.Png);
stream.Seek(0);

var dataPackage = new DataPackage();
dataPackage.SetBitmap(RandomAccessStreamReference.CreateFromStream(stream));
Clipboard.SetContent(dataPackage);
```

**Pitfall:** `Clipboard.SetContent` must be called on the UI thread. Use `DispatcherQueue.TryEnqueue` if the render was done on a background thread.

### Pattern 3: Windows App SDK 1.8 FileSavePicker

**What:** New `Microsoft.Windows.Storage.Pickers` namespace — takes `WindowId` in constructor, no `InitializeWithWindow` required.
**When to use:** EXPT-01 save dialog.

```csharp
// Source: https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.windows.storage.pickers.filesavepicker?view=windows-app-sdk-1.8
var windowId = App.MainWindow.AppWindow.Id;
var picker = new Microsoft.Windows.Storage.Pickers.FileSavePicker(windowId)
{
    SuggestedFileName = $"ccinfo-{DateTime.Now:yyyy-MM-dd-HHmm}",
    DefaultFileExtension = ".png"
};
picker.FileTypeChoices.Add("PNG Image", [".png"]);
var file = await picker.PickSaveFileAsync();
if (file != null)
{
    using var stream = await file.OpenAsync(FileAccessMode.ReadWrite);
    await renderTarget.SaveAsync(stream, CanvasBitmapFileFormat.Png);
}
```

### Pattern 4: WinUI3Localizer Initialization (unpackaged)

**What:** Initialize in App.xaml.cs before first Window creation. Strings folder sits alongside the .exe.
**When to use:** SETT-04 — DE/EN localization.

```csharp
// Source: https://github.com/AndrewKeepCoding/WinUI3Localizer
// In App.xaml.cs OnLaunched, before m_window = new MainWindow():
var stringsFolderPath = Path.Combine(AppContext.BaseDirectory, "Strings");
ILocalizer localizer = await new LocalizerBuilder()
    .AddStringResourcesFolderForLanguageDictionaries(stringsFolderPath)
    .SetOptions(options =>
    {
        options.DefaultLanguage = "en-US";
    })
    .Build();

// Runtime language switch (no restart):
await Localizer.Get().SetLanguage("de-DE");
```

**Project file addition required:**
```xml
<ItemGroup>
  <Content Include="Strings\**\*.resw">
    <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
  </Content>
</ItemGroup>
```

### Pattern 5: Autostart Registry Key

**What:** Read/write `HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Run` via `Microsoft.Win32.Registry`.
**When to use:** SETT-02 — autostart toggle in Settings.

```csharp
// Source: .NET Microsoft.Win32.Registry documentation (built-in)
private const string RunKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
private const string AppName = "CCInfoWindows";

public bool GetAutostart()
{
    using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
    return key?.GetValue(AppName) != null;
}

public void SetAutostart(bool enable)
{
    using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
    if (enable)
        key?.SetValue(AppName, Environment.ProcessPath ?? string.Empty);
    else
        key?.DeleteValue(AppName, throwOnMissingValue: false);
}
```

### Pattern 6: GitHub Releases Version Check

**What:** GET api.github.com/repos/daniel-mielke/ccInfoWin/releases/latest, parse `tag_name`, compare SemVer against assembly version.
**When to use:** UPDT-01 hourly check.

```csharp
// SemVer parse: tag_name format is "v1.2.3"
var response = await _httpClient.GetFromJsonAsync<GitHubRelease>(
    "https://api.github.com/repos/daniel-mielke/ccInfoWin/releases/latest");

var remoteVersion = Version.Parse(response.TagName.TrimStart('v'));
var localVersion = Assembly.GetExecutingAssembly().GetName().Version!;
if (remoteVersion > localVersion)
{
    // show InfoBar banner
}
```

**Required:** `_httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("CCInfoWindows", localVersion.ToString()))` — GitHub API rejects requests without a User-Agent header.

### Pattern 7: InfoBar Update Banner

**What:** Second InfoBar in MainView Row 0 (stacked below existing Session Expired InfoBar).
**When to use:** UPDT-02.

```xaml
<InfoBar
    Grid.Row="0"
    Title="Update verfügbar"
    Message="{x:Bind ViewModel.UpdateMessage, Mode=OneWay}"
    Severity="Informational"
    IsOpen="{x:Bind ViewModel.IsUpdateAvailable, Mode=OneWay}"
    IsClosable="True"
    Closing="OnUpdateInfoBarClosing">
    <InfoBar.ActionButton>
        <Button Content="Download" Command="{x:Bind ViewModel.OpenUpdateDownloadCommand}" />
    </InfoBar.ActionButton>
</InfoBar>
```

The `Closing` event handler captures the dismissed version and saves it to AppSettings.

### Anti-Patterns to Avoid

- **Calling `CanvasRenderTarget.SaveAsync` with open drawing session:** Drawing session must be disposed before SaveAsync. Use a `using` block for the session.
- **Using `Windows.Storage.Pickers.FileSavePicker` directly:** The WinRT version requires `InitializeWithWindow.Initialize(picker, hwnd)`. Use `Microsoft.Windows.Storage.Pickers.FileSavePicker` (Windows App SDK 1.8) with `WindowId` constructor instead.
- **Using `ResourceContext.SetGlobalQualifierValue` for runtime language switch:** Crashes on Windows 11 in unpackaged apps. Does not update x:Uid-bound XAML elements even when it doesn't crash.
- **Creating a new `CanvasDevice` for the render target:** Use `CanvasDevice.GetSharedDevice()` — creating a second device wastes GPU resources and is unnecessary since Win2D 1.x.
- **Calling `Clipboard.SetContent` off the UI thread:** Throws `CO_E_NOTINITIALIZED`. Always dispatch to the UI thread via `DispatcherQueue.TryEnqueue`.
- **GitHub API without User-Agent header:** Returns 403. Must set User-Agent in HttpClient headers.

---

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Runtime language switch in unpackaged WinUI 3 | Custom string lookup dictionary + page reload | WinUI3Localizer 2.3.0 | Native MRT Core approach crashes on Win11; WinUI3Localizer handles x:Uid rebinding correctly |
| PNG encoding for clipboard | Manual Direct2D P/Invoke | `BitmapEncoder.PngEncoderId` + `InMemoryRandomAccessStream` | Edge cases with premultiplied alpha, stride alignment |
| SemVer comparison | String splitting by "." | `System.Version.Parse()` | Handles edge cases, already in .NET BCL |
| File picker HWND wiring | Win32 `GetForegroundWindow()` | `Microsoft.Windows.Storage.Pickers.FileSavePicker(WindowId)` | Windows App SDK 1.8 provides clean API |

**Key insight:** The runtime localization problem in unpackaged WinUI 3 is a well-known platform gap. The MRT Core APIs were designed for packaged apps and behave differently (sometimes incorrectly) in unpackaged scenarios. WinUI3Localizer exists precisely to paper over this gap.

---

## Common Pitfalls

### Pitfall 1: Drawing Session Not Closed Before SaveAsync
**What goes wrong:** `SaveAsync` returns a corrupted/partial PNG or throws an access violation.
**Why it happens:** Direct2D doesn't flush the command buffer until the drawing session is disposed.
**How to avoid:** Always wrap `CreateDrawingSession()` in a `using` block that ends before `SaveAsync` is called.
**Warning signs:** PNG file has correct dimensions but is completely black or partially rendered.

### Pitfall 2: WinUI3Localizer Strings Folder Not Copied to Output
**What goes wrong:** App starts, WinUI3Localizer initialization throws `DirectoryNotFoundException` or silently falls back to hardcoded strings.
**Why it happens:** .resw files default to `Build Action: Resource` which embeds them in the assembly rather than copying to output.
**How to avoid:** Add the `<Content Include="Strings\**\*.resw">` ItemGroup with `CopyToOutputDirectory: PreserveNewest` to the .csproj.
**Warning signs:** Localization works in debug but not after publish/install.

### Pitfall 3: GitHub API 403 on Version Check
**What goes wrong:** `HttpClient.GetFromJsonAsync` throws `HttpRequestException` with status 403.
**Why it happens:** GitHub API requires a `User-Agent` header; .NET `HttpClient` does not set one by default.
**How to avoid:** Set `_httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("CCInfoWindows", version))` during DI registration.
**Warning signs:** Works in browser but fails from app.

### Pitfall 4: Inno Setup `{app}` Path Contains Spaces in Autostart Value
**What goes wrong:** Windows fails to launch the app at startup — the Run key value is treated as two separate tokens.
**Why it happens:** `{app}\CCInfoWindows.exe` expands to a path with spaces (e.g., `C:\Users\Dan\AppData\Local\Programs\CCInfoWindows\CCInfoWindows.exe`) and no quotes.
**How to avoid:** Wrap the value in quotes in the Inno Setup Registry section: `ValueData: """{app}\CCInfoWindows.exe"""`.
**Warning signs:** Autostart entry visible in Task Manager Startup tab but app never actually starts at login.

### Pitfall 5: Trimming Breaks Win2D or WinRT Interop
**What goes wrong:** Self-contained publish succeeds but runtime throws `MissingMethodException` or `TypeLoadException` for Win2D or WinRT types.
**Why it happens:** `TrimMode=link` (full trimming) removes reflection-accessed WinRT marshaling code.
**How to avoid:** Use `TrimMode=partial` (already in locked decision) which trims SDK assemblies but preserves app code and WinRT interop glue. Accept the larger output size.
**Warning signs:** Works in `dotnet run` but crashes immediately after publish.

### Pitfall 6: Update Check Using Packaged Release (Pre-Release)
**What goes wrong:** App shows update banner for a pre-release tag not intended for end users.
**Why it happens:** GitHub Releases API `/releases/latest` returns only the latest stable release, but if all releases are pre-release the endpoint returns 404.
**How to avoid:** Handle 404 from `/releases/latest` gracefully (no update available). Only create stable releases for distribution.
**Warning signs:** Version check silently stops working.

---

## Code Examples

### Complete Export Flow (Save to File)
```csharp
// Source: Win2D WinUI3 docs + Windows App SDK 1.8 picker API
public async Task ExportChartAsPngAsync(
    IReadOnlyList<UsageHistoryPoint> points,
    double utilization,
    string percentageText,
    string countdownText)
{
    const float ExportWidth = 328f;
    const float ExportHeight = 240f;
    const float ExportDpi = 192f; // 2x retina
    var backgroundColor = Color.FromArgb(255, 30, 30, 30); // #1E1E1E always dark

    var windowId = App.MainWindow.AppWindow.Id;
    var picker = new Microsoft.Windows.Storage.Pickers.FileSavePicker(windowId)
    {
        SuggestedFileName = $"ccinfo-{DateTimeOffset.Now:yyyy-MM-dd-HHmm}",
        DefaultFileExtension = ".png"
    };
    picker.FileTypeChoices.Add("PNG Image", [".png"]);

    var file = await picker.PickSaveFileAsync();
    if (file == null) return;

    var device = CanvasDevice.GetSharedDevice();
    using var renderTarget = new CanvasRenderTarget(device, ExportWidth, ExportHeight, ExportDpi);
    using (var ds = renderTarget.CreateDrawingSession())
    {
        ds.Clear(backgroundColor);
        DrawExportLayout(ds, ExportWidth, ExportHeight, points, percentageText, countdownText);
    }

    using var stream = await file.OpenAsync(FileAccessMode.ReadWrite);
    await renderTarget.SaveAsync(stream, CanvasBitmapFileFormat.Png);
}
```

### Complete Export Flow (Clipboard)
```csharp
// Source: Win2D docs + Windows.ApplicationModel.DataTransfer
public async Task CopyChartToClipboardAsync(
    IReadOnlyList<UsageHistoryPoint> points,
    string percentageText,
    string countdownText)
{
    const float ExportWidth = 328f;
    const float ExportHeight = 240f;
    const float ExportDpi = 192f;
    var backgroundColor = Color.FromArgb(255, 30, 30, 30);

    var device = CanvasDevice.GetSharedDevice();
    using var renderTarget = new CanvasRenderTarget(device, ExportWidth, ExportHeight, ExportDpi);
    using (var ds = renderTarget.CreateDrawingSession())
    {
        ds.Clear(backgroundColor);
        DrawExportLayout(ds, ExportWidth, ExportHeight, points, percentageText, countdownText);
    }

    var memStream = new InMemoryRandomAccessStream();
    await renderTarget.SaveAsync(memStream, CanvasBitmapFileFormat.Png);
    memStream.Seek(0);

    var dataPackage = new DataPackage();
    dataPackage.SetBitmap(RandomAccessStreamReference.CreateFromStream(memStream));

    // Must be on UI thread
    _dispatcherQueue.TryEnqueue(() => Clipboard.SetContent(dataPackage));
}
```

### WinUI3Localizer .resw Key Naming for AutomationProperties
```xml
<!-- Strings/en-US/Resources.resw -->
<!-- For AutomationProperties.Name on a Button with x:Uid="ExportButton": -->
<!-- Name column: ExportButton.[using:Microsoft.UI.Xaml.Automation]AutomationProperties.Name -->
<!-- Value column: Export chart -->

<!-- For regular TextBlock Text with x:Uid="SectionHeader5h": -->
<!-- Name column: SectionHeader5h.Text -->
<!-- Value column: 5-HOUR WINDOW -->
```

### Inno Setup Script (Key Sections)
```pascal
; Source: https://jrsoftware.org/ishelp/topic_registrysection.htm
[Setup]
AppName=CCInfoWindows
AppVersion=1.0.0
PrivilegesRequired=lowest
DefaultDirName={localappdata}\Programs\CCInfoWindows
DefaultGroupName=CCInfoWindows
OutputBaseFilename=CCInfoWindows-1.0.0-Setup
Compression=lzma2/ultra
SolidCompression=yes
WizardStyle=modern

[Tasks]
Name: "desktopicon"; Description: "Create desktop shortcut"; GroupDescription: "Additional icons"; Flags: checked
Name: "autostart"; Description: "Start at Windows login"; GroupDescription: "Options"; Flags: checked

[Files]
Source: "publish\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs

[Icons]
Name: "{autorunprograms}\CCInfoWindows"; Filename: "{app}\CCInfoWindows.exe"
Name: "{userdesktop}\CCInfoWindows"; Filename: "{app}\CCInfoWindows.exe"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "SOFTWARE\Microsoft\Windows\CurrentVersion\Run"; \
  ValueType: string; ValueName: "CCInfoWindows"; \
  ValueData: """{app}\CCInfoWindows.exe"""; \
  Flags: uninsdeletevalue; Tasks: autostart
```

---

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| `Windows.Storage.Pickers.FileSavePicker` + `InitializeWithWindow.Initialize` | `Microsoft.Windows.Storage.Pickers.FileSavePicker(WindowId)` | Windows App SDK 1.8 (2025) | Cleaner API, no HWND interop boilerplate |
| `ResourceContext.SetGlobalQualifierValue` for runtime language | WinUI3Localizer NuGet package | 2022–2023 (platform gap recognized) | Unpackaged apps need library; native approach is broken/unreliable |
| Creating new `CanvasDevice` per render | `CanvasDevice.GetSharedDevice()` | Win2D design intent since 1.x | Shared device avoids duplicate GPU context |

**Deprecated/outdated:**
- `ResourceContext.SetGlobalQualifierValue`: Crashes on Windows 11 in unpackaged apps. Do not use.
- `Windows.Storage.Pickers` (WinRT namespace) with `InitializeWithWindow`: Still works but is the legacy approach when on Windows App SDK 1.8+.

---

## Already Implemented (No Phase 6 Work Needed)

These items are complete based on reading the existing code:

| Item | Evidence | Phase 6 Task |
|------|----------|--------------|
| UIPF-05: Window position save on close | `MainWindow.xaml.cs` lines 89–98 `OnClosing` + `RestoreWindowState()` | Verification only |
| UIPF-05: Window position restore on startup | `MainWindow.xaml.cs` `RestoreWindowState()` called in constructor | Verification only |
| SETT-07: Settings in-app frame navigation | `SettingsView.xaml` exists, `NavigationService.Initialize(RootFrame)` wired | Extend with new settings rows |

---

## Open Questions

1. **Export composition exact pixel dimensions**
   - What we know: Context says ~328×240 for the export area at 1x; CanvasControl in XAML is Height=120 with padding
   - What's unclear: Final pixel dimensions to match macOS reference image aspect ratio
   - Recommendation: Measure `spec/v1.7.1/flächenfüllung-chart-macOS.png` dimensions during plan; use those ratios

2. **WinUI3Localizer with existing hardcoded German strings in XAML**
   - What we know: All existing XAML uses hardcoded German strings (no x:Uid, no Resources.resw)
   - What's unclear: Whether to localize all existing strings or only new Phase 6 strings
   - Recommendation: Localize all visible strings as part of this phase — leaving partial localization creates a confusing EN/DE mix

3. **GitHub Actions CI/CD**
   - What we know: Context marks this as Claude's Discretion
   - What's unclear: Whether to include a basic `.github/workflows/release.yml` that builds and attaches installer to GitHub Release
   - Recommendation: Include a minimal workflow that triggers on tag push `v*.*.*`, runs `dotnet publish`, and runs Inno Setup; this enables repeatable releases without local build environment

---

## Validation Architecture

### Test Framework
| Property | Value |
|----------|-------|
| Framework | xUnit 2.9.3 |
| Config file | CCInfoWindows.Tests/CCInfoWindows.Tests.csproj |
| Quick run command | `dotnet test CCInfoWindows.Tests/ -c Release -r win-x64 --no-build` |
| Full suite command | `dotnet test CCInfoWindows.Tests/ -c Release -r win-x64` |

### Phase Requirements → Test Map

| Req ID | Behavior | Test Type | Automated Command | File Exists? |
|--------|----------|-----------|-------------------|-------------|
| EXPT-01/03 | Export renders non-black PNG at correct dimensions | unit | `dotnet test ... --filter ExportHelper` | ❌ Wave 0 |
| UPDT-01 | UpdateService parses SemVer tag and compares correctly | unit | `dotnet test ... --filter UpdateServiceTests` | ❌ Wave 0 |
| UPDT-01 | UpdateService ignores pre-releases | unit | `dotnet test ... --filter UpdateServiceTests` | ❌ Wave 0 |
| SETT-02 | RegistryHelper reads/writes HKCU Run key correctly | unit | `dotnet test ... --filter RegistryHelperTests` | ❌ Wave 0 |
| UIPF-05 | SaveWindowState / LoadWindowState round-trips correctly | unit (already exists via SettingsService) | `dotnet test ... --filter SettingsService` | manual check |
| SETT-04 | Localization language switch changes observable string | manual-only | n/a — requires WinUI runtime | manual |
| UIPF-07 | Accessibility labels present on buttons | manual-only | n/a — requires Narrator/NVDA | manual |
| DIST-01 | Installer builds without error | manual-only | Inno Setup Compiler run | manual |

**Note:** Win2D `CanvasRenderTarget` requires a GPU device. Unit tests for `ExportHelper` must use `CanvasDevice.GetSharedDevice()` — this works in a test process on machines with a GPU but cannot run in headless CI without a GPU. Mark export tests as `[Trait("Category", "RequiresGPU")]` and skip in CI.

### Sampling Rate
- **Per task commit:** `dotnet test CCInfoWindows.Tests/ -c Release -r win-x64 --filter "Category!=RequiresGPU"`
- **Per wave merge:** `dotnet test CCInfoWindows.Tests/ -c Release -r win-x64`
- **Phase gate:** Full suite green before `/gsd:verify-work`

### Wave 0 Gaps
- [ ] `CCInfoWindows.Tests/Services/UpdateServiceTests.cs` — covers UPDT-01 SemVer parsing
- [ ] `CCInfoWindows.Tests/Helpers/ExportHelperTests.cs` — covers EXPT-01/03 (GPU-dependent, marked)
- [ ] `CCInfoWindows.Tests/Helpers/RegistryHelperTests.cs` — covers SETT-02 autostart read/write

---

## Sources

### Primary (HIGH confidence)
- Win2D WinUI3 API docs (https://microsoft.github.io/Win2D/WinUI3/html/T_Microsoft_Graphics_Canvas_CanvasRenderTarget.htm) — CanvasRenderTarget constructor, CreateDrawingSession, SaveAsync
- Windows App SDK 1.8 FileSavePicker API (https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.windows.storage.pickers.filesavepicker?view=windows-app-sdk-1.8) — WindowId constructor confirmed
- Microsoft localize-strings doc (https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/mrtcore/localize-strings) — .resw folder structure, unpackaged app limitations
- DataPackage.SetBitmap API (https://learn.microsoft.com/en-us/uwp/api/windows.applicationmodel.datatransfer.datapackage.setbitmap) — clipboard image pattern
- Inno Setup Registry section (https://jrsoftware.org/ishelp/topic_registrysection.htm) — HKCU Run key autostart pattern

### Secondary (MEDIUM confidence)
- WinUI3Localizer GitHub README (https://github.com/AndrewKeepCoding/WinUI3Localizer) — v2.3.0 latest, unpackaged initialization, runtime switching confirmed no-restart
- WinUI 3 localize-winui3-app doc (https://learn.microsoft.com/en-us/windows/apps/winui/winui3/localize-winui3-app) — confirms unpackaged apps don't need appxmanifest changes
- AutomationProperties API (https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.automation.automationproperties?view=windows-app-sdk-1.8) — AutomationProperties.Name pattern for .resw attached properties

### Tertiary (LOW confidence — flag for validation)
- Win2D clipboard DataPackage approach (inferred from UWP samples and multiple blog sources — no single authoritative Win2D WinUI3 clipboard example found)
- Inno Setup 6.3.3 current version claim (from Neowin listing, not confirmed from jrsoftware.org directly)

---

## Metadata

**Confidence breakdown:**
- Chart export (Win2D): HIGH — official Win2D WinUI3 docs confirm API shapes and CanvasBitmap.SaveAsync
- FileSavePicker (Windows App SDK 1.8): HIGH — official API reference confirms WindowId constructor
- Localization (WinUI3Localizer): HIGH — official docs + GitHub README confirm unpackaged support and no-restart switching
- Autostart (Registry): HIGH — standard .NET API, well-documented
- Inno Setup structure: MEDIUM — documented pattern, but exact Inno Setup 6.3.3 release date not verified from primary source
- Clipboard SetBitmap: MEDIUM — API is documented but no official Win2D WinUI3 clipboard example exists; pattern inferred from UWP samples

**Research date:** 2026-03-16
**Valid until:** 2026-06-16 (stable APIs, 90-day estimate)
