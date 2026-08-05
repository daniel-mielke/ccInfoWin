# Phase 6: Export, Polish, and Distribution - Context

**Gathered:** 2026-03-16
**Status:** Ready for planning

<domain>
## Phase Boundary

App is feature-complete, localized, accessible, and distributed as a standalone installer on GitHub. Includes: chart export as PNG (save + clipboard), DE/EN localization, autostart at Windows login, auto-update via GitHub Releases, window position persistence, accessibility labels, and Inno Setup per-user installer published to GitHub. No new monitoring features, no new data sources, no chart redesign.

</domain>

<decisions>
## Implementation Decisions

### Chart Export — Button Placement
- Export icon button positioned top-right of the 5-STUNDEN-FENSTER section header
- Button appears on hover/tap over the chart area — does not clutter the UI in normal use
- Single button opens a MenuFlyout with two options: "Speichern als PNG..." and "In Zwischenablage kopieren"

### Chart Export — Content and Layout
- Export includes: percentage display, "5-STUNDEN-FENSTER" label, reset countdown, chart, and "CCINFO" watermark
- Export layout follows macOS reference (spec/v1.7.1/flächenfüllung-chart-macOS.png): percentage + countdown ABOVE chart, CCINFO branding bottom-right
- This differs from app layout (percentage below chart) — export renders its own composition via CanvasRenderTarget

### Chart Export — Rendering
- Always dark background regardless of current app theme (matches EXPT-01 requirement "dark PNG")
- 2x Retina resolution (double pixel density, e.g., 656x480 for a ~328x240 export area)
- Thumbnail preview handled natively by Windows FileSavePicker (no custom preview dialog)
- Clipboard copy uses Win2D render → BitmapEncoder → DataPackage with SetBitmap

### Auto-Update — Banner
- WinUI 3 InfoBar at top of MainView (same pattern as existing "Session Expired" InfoBar)
- Severity: Informational
- Message: "Update v{version} verfügbar"
- ActionButton: "Download" — opens GitHub Release page in default browser via Process.Start
- No in-app download or automatic installation

### Auto-Update — Version Check
- Hourly check via GitHub Releases API: GET /repos/daniel-mielke/ccInfoWin/releases/latest
- SemVer tag comparison: parse tag_name (e.g., "v1.2.0") and compare against Assembly version
- Pre-releases ignored (only latest stable release)
- Network allowed to raw.githubusercontent.com already in SECU-04 — GitHub API (api.github.com) must be added to allowed hosts

### Auto-Update — Dismiss Behavior
- User can close banner with X button
- Dismissed version stored in AppSettings (e.g., "dismissedUpdateVersion": "1.2.0")
- Banner suppressed for that version — reappears only when a newer version is detected
- On app restart: check runs, banner shown only if newer version than dismissed

### Distribution — Build
- Self-contained publish: `dotnet publish -c Release -r win-x64 --self-contained -p:PublishTrimmed=true -p:TrimMode=partial`
- No .NET Runtime prerequisite for end users
- No code signing — unsigned installer (SmartScreen warning acceptable for open-source)

### Distribution — Installer
- Inno Setup EXE installer, per-user installation (no admin required)
- Install to %LOCALAPPDATA%\Programs\CCInfoWindows (per-user default)
- Installer options checkboxes: Desktop shortcut (default: on), Autostart at login (default: on)
- Autostart via Registry Run key (HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Run)
- Autostart toggle also available in app Settings (reads/writes same Registry key)

### Distribution — GitHub
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

</decisions>

<canonical_refs>
## Canonical References

**Downstream agents MUST read these before planning or implementing.**

### Chart Export
- `spec/v1.7.1/flächenfüllung-chart-macOS.png` — macOS export reference image showing exact layout: percentage + countdown above chart, CCINFO branding bottom-right, dark background
- `spec/v1.7.1/ccinfo-spec.md` §2.8 — Chart export requirements (FA-080, EXPT-01, EXPT-02, EXPT-03)

### UI Design
- `spec/v1.7.1/ccinfo-styleguide.md` — Overall design language, colors, typography
- `spec/v1.7.1/ccinfo-spec.md` §2.10 — Settings requirements (SETT-02, SETT-04, SETT-07)

### Auto-Update
- `spec/v1.7.1/ccinfo-spec.md` §2.9 — Auto-update requirements (UPDT-01, UPDT-02, UPDT-03)

### Prior Phase Context
- `.planning/phases/03-area-chart/03-CONTEXT.md` — Win2D chart rendering decisions, CanvasControl setup, color zone logic
- `.planning/phases/05-cost-analytics/05-CONTEXT.md` — Tab bar and statistics UI decisions

### Project Requirements
- `.planning/REQUIREMENTS.md` — EXPT-01, EXPT-02, EXPT-03, SETT-02, SETT-04, SETT-07, UPDT-01, UPDT-02, UPDT-03, UIPF-05, UIPF-07, DIST-01, DIST-02, DIST-03

</canonical_refs>

<code_context>
## Existing Code Insights

### Reusable Assets
- `ChartRenderer` (MainView.xaml.cs Draw handler): Win2D chart rendering logic — export reuses same drawing code with CanvasRenderTarget instead of CanvasControl
- `ColorThresholds.cs` + `PercentageToColorConverter.cs`: Zone-based coloring — reuse for export chart colors
- `UsageHistoryService.cs`: History data access — export reads from same data source
- `SettingsService.cs` + `AppSettings.cs`: JSON persistence with WindowState already implemented — extend for dismissedUpdateVersion, language preference, autostart setting
- `SaveWindowState()` / `LoadWindowState()`: Already exist in SettingsService — just need to wire up Window.Closed and activation events

### Established Patterns
- InfoBar pattern for non-intrusive notifications (Session Expired banner exists)
- DispatcherQueue.TryEnqueue() for background-to-UI updates (used in polling)
- WeakReferenceMessenger for cross-ViewModel events (ThemeChangedMessage)
- Singleton DI for stateful services
- MenuFlyout already used in WinUI 3 ecosystem — standard XAML pattern

### Integration Points
- `MainView.xaml`: Add export button overlay on chart section, add update InfoBar in Row 0
- `MainViewModel.cs`: Add export commands, update check logic, window state save on close
- `SettingsView.xaml`: Add language selector, autostart toggle
- `SettingsViewModel.cs`: Add language/autostart bindings
- `App.xaml.cs`: Register update check service, wire window close event for position save
- `AppSettings.cs`: Add dismissedUpdateVersion, language, autostart properties
- `MainWindow.xaml.cs`: Wire Window.Closed for position save, Window.Activated for position restore

</code_context>

<specifics>
## Specific Ideas

- Export-Layout folgt dem macOS-Original (spec/v1.7.1/flächenfüllung-chart-macOS.png): Prozentzahl + Countdown OBEN, Chart darunter, CCINFO-Branding rechts unten
- Export immer auf dunklem Hintergrund — unabhängig vom aktuellen App-Theme
- 2x Retina-Auflösung für scharfe Bilder auf HiDPI-Displays und beim Teilen
- Auto-Update Banner identisch zum bestehenden "Session Expired" InfoBar-Pattern
- Download-Button öffnet nur den Browser — kein In-App-Download oder automatische Installation
- Installer per-user (kein Admin) mit Autostart-Checkbox (default: an)
- Self-contained Build ohne Runtime-Abhängigkeit für den Endnutzer

</specifics>

<deferred>
## Deferred Ideas

None — discussion stayed within phase scope

</deferred>

---

*Phase: 06-export-polish-and-distribution*
*Context gathered: 2026-03-16*
