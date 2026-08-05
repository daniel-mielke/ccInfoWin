# Phase 2: Core Monitoring Dashboard - Context

**Gathered:** 2026-03-10
**Status:** Ready for planning

<domain>
## Phase Boundary

User can see their 5-hour usage percentage and reset countdown, weekly quota (Opus + Sonnet) with progress bars, and reset dates — all auto-refreshing. User can configure refresh interval and toggle dark/light mode in a Settings page. Dashboard follows styleguide layout (pixel-precise colors, typography, spacing from ccinfo-styleguide.md). No area chart (Phase 3), no sessions/context window (Phase 4), no token stats (Phase 5).

</domain>

<decisions>
## Implementation Decisions

### API-Polling & Data Flow
- Silent retry with backoff (2-3 attempts) on transient errors (429, 5xx, network failure), then show subtle warning badge next to refresh timer — no popup, no InfoBar for transient errors
- Last valid data remains visible during errors
- On 401 (token expired): immediately stop polling, show existing InfoBar "Session expired" banner with Re-Login button (from Phase 1), no retry
- API call strategy (single vs. multiple endpoints): Claude's discretion — researcher investigates available endpoints and chooses optimal approach
- Organization IDs must be percent-encoded in API URLs (DATA-02)
- Local cache: persist last API response to disk so app shows last-known values immediately on startup with "Updating..." indicator, then refresh from API

### Refresh Behavior
- Default mode: Auto-refresh every 60 seconds on first start
- Configurable intervals: 30s, 1min, 2min, 5min, 10min + Manual (6 options total)
- Interval selection persisted in settings.json, restored on startup
- During refresh: "Aktualisieren" icon in footer spins (rotating animation) — subtle, non-blocking
- Manual refresh via footer "Aktualisieren" button always available regardless of auto-refresh setting

### Countdown Timers
- Reset countdowns tick locally every minute (not every second — unnecessary precision)
- API poll corrects/resyncs the countdown value
- Format: "2h 14min" (German locale by default, English locale in Phase 6)

### Dark/Light Mode
- Toggle lives exclusively in the Settings page (not in footer or main dashboard)
- Default: always Dark on first start (SETT-06), regardless of Windows system theme
- User selection persisted in settings.json, restored on startup
- Theme switch: immediate via WinUI 3 `RequestedTheme` — no animation, no restart
- All styleguide colors defined as central XAML ThemeResources in a dedicated ResourceDictionary (e.g., AppTheme.xaml) — Dark and Light variants per ThemeDictionary

### Dashboard Layout
- Show only implemented sections — no placeholders for future features
- Phase 2 sections (top to bottom):
  1. **5-STUNDEN-FENSTER**: Section header, progress bar, large percentage (28px bold) + countdown with clock icon — NO chart container (Phase 3 adds it later)
  2. **WOCHENLIMIT**: Section header, progress bar, percentage + countdown + reset date
  3. **SONNET WOCHENLIMIT**: Same layout as Wochenlimit
  4. **Footer**: Three icon-only buttons side by side (horizontal), no labels — tooltip on hover shows label
     - Aktualisieren (ArrowSync icon, \uE895)
     - Einstellungen (Settings icon, \uE713)
     - Beenden (PowerButton icon, \uE7E8)
- Sections separated by 1px divider lines per styleguide
- Section headers: UPPERCASE, 11px, Semibold, muted gray

### Footer (Deviation from Styleguide)
- **Styleguide shows**: vertical list with Icon + Text per row
- **User decision**: horizontal row of icon-only buttons with tooltip on hover
- This is a deliberate deviation for compactness

### Logout Placement
- Remove current Logout button from MainView dashboard
- Move Logout to Settings page as a dedicated section at the bottom
- Footer stays clean with only 3 icons (Aktualisieren, Einstellungen, Beenden)

### Settings Page
- Accessible via Einstellungen footer icon
- Frame navigation (slide from right, per styleguide animation)
- Back navigation to dashboard
- Contents in Phase 2:
  - Refresh interval selector (ComboBox with 6 options)
  - Dark/Light mode toggle (ToggleSwitch)
  - Logout button (at bottom)
- Future phases add more settings (session threshold, language, autostart, etc.)

### Styling Strategy
- For any missing style values (color, font-weight, font-size, background-color, etc.): always check macOS reference app (styleguide + spec) first before making assumptions
- All styleguide hex values are authoritative — use them exactly as specified
- Progress bar colors follow unified thresholds: green (0-50%), yellow (50-75%), orange (75-90%), red (90-100%)
- Progress bar colors differ between Dark/Light mode per styleguide table

### Claude's Discretion
- API endpoint discovery and call strategy (single vs. multiple calls)
- Exact retry backoff timing (exponential, linear, etc.)
- Cache file format and location (within %LOCALAPPDATA%\CCInfoWindows\)
- Settings page exact layout and spacing
- DispatcherTimer vs. PeriodicTimer for countdown ticking
- Warning badge design for API errors (icon choice, position)

</decisions>

<specifics>
## Specific Ideas

- Footer buttons should feel like a compact toolbar — think VS Code status bar icons
- "Aktualisieren" spinner should be subtle — just the icon rotating, not a separate loading overlay
- The app should feel "alive" between API polls thanks to the minute-by-minute countdown ticking
- Cached data on startup: show immediately with a brief "Aktualisiere..." text or spinner, then update when fresh data arrives

</specifics>

<code_context>
## Existing Code Insights

### Reusable Assets
- `MainViewModel.cs`: Already has HttpClient injection, ValidateTokenAsync(), AuthStateChangedMessage handling, Logout/ReLogin commands — extend with polling logic
- `MainView.xaml`: Has InfoBar for session-expired (keep), Grid with 3 rows — replace placeholder StackPanel with dashboard sections
- `ISettingsService` / `SettingsService`: Already persists WindowState to settings.json — extend AppSettings model with RefreshInterval, ColorMode
- `AppSettings.cs`: Currently only has WindowState — add new properties for Phase 2 settings
- `ICredentialService`: GetSessionToken() for API authentication
- `INavigationService`: Frame-based navigation, NavigateTo<T>() — use for Settings page

### Established Patterns
- MVVM with CommunityToolkit.Mvvm source generators ([ObservableProperty], [RelayCommand])
- DI via Microsoft.Extensions.DependencyInjection (HttpClient singleton, services registered in App.xaml.cs)
- WeakReferenceMessenger for cross-ViewModel communication (AuthStateChangedMessage)
- Settings persisted as JSON in %LOCALAPPDATA%\CCInfoWindows\settings.json

### Integration Points
- MainView.xaml Grid.Row="1" (currently placeholder) → replace with ScrollViewer containing dashboard sections
- App.xaml.cs DI container → register new services (API service, timer service)
- App.xaml → add ResourceDictionary merge for AppTheme.xaml
- MainView.xaml Grid.Row="2" (currently Logout button) → replace with horizontal icon footer

</code_context>

<deferred>
## Deferred Ideas

- Always-on-top toggle in Settings — Phase 6 (from Phase 1 context)
- System Tray minimize option — potential v2 feature (from Phase 1 context)

</deferred>

---

*Phase: 02-core-monitoring-dashboard*
*Context gathered: 2026-03-10*
