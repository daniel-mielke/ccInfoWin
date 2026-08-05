# Phase 2: Core Monitoring Dashboard - Research

**Researched:** 2026-03-10
**Domain:** WinUI 3 dashboard UI, Claude.ai unofficial API, auto-refresh polling, theme switching
**Confidence:** HIGH

## Summary

Phase 2 transforms the placeholder MainView into a real-time monitoring dashboard. The core technical challenges are: (1) calling the Claude.ai unofficial usage API with proper authentication and org-ID handling, (2) building styled progress bars with dynamic color thresholds matching the styleguide exactly, (3) implementing auto-refresh polling with configurable intervals, (4) adding dark/light theme toggle via WinUI 3 RequestedTheme, and (5) creating a Settings page with frame navigation.

A critical gap exists in Phase 1: the current login flow only extracts `sessionKey` from cookies but NOT `lastActiveOrg` (organization ID). The macOS reference app extracts both. Phase 2 must fix this by also extracting the `lastActiveOrg` cookie during login and storing it alongside the session key, because the usage API endpoint requires the organization ID in its URL path.

**Primary recommendation:** Build an `IClaudeApiService` that calls `GET https://claude.ai/api/organizations/{orgId}/usage` with sessionKey cookie auth, returning a single JSON response containing `five_hour`, `seven_day`, `seven_day_opus`, and `seven_day_sonnet` usage windows. Use `DispatcherQueueTimer` for both polling and countdown ticking.

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions
- Silent retry with backoff (2-3 attempts) on transient errors (429, 5xx, network failure), then show subtle warning badge next to refresh timer -- no popup, no InfoBar for transient errors
- Last valid data remains visible during errors
- On 401 (token expired): immediately stop polling, show existing InfoBar "Session expired" banner with Re-Login button (from Phase 1), no retry
- Organization IDs must be percent-encoded in API URLs (DATA-02)
- Local cache: persist last API response to disk so app shows last-known values immediately on startup with "Updating..." indicator, then refresh from API
- Default mode: Auto-refresh every 60 seconds on first start
- Configurable intervals: 30s, 1min, 2min, 5min, 10min + Manual (6 options total)
- Interval selection persisted in settings.json, restored on startup
- During refresh: "Aktualisieren" icon in footer spins (rotating animation) -- subtle, non-blocking
- Manual refresh via footer "Aktualisieren" button always available regardless of auto-refresh setting
- Reset countdowns tick locally every minute (not every second)
- API poll corrects/resyncs the countdown value
- Format: "2h 14min" (German locale by default, English locale in Phase 6)
- Toggle lives exclusively in the Settings page (not in footer or main dashboard)
- Default: always Dark on first start (SETT-06), regardless of Windows system theme
- User selection persisted in settings.json, restored on startup
- Theme switch: immediate via WinUI 3 RequestedTheme -- no animation, no restart
- All styleguide colors defined as central XAML ThemeResources in a dedicated ResourceDictionary (e.g., AppTheme.xaml) -- Dark and Light variants per ThemeDictionary
- Show only implemented sections -- no placeholders for future features
- Phase 2 sections (top to bottom): 5-STUNDEN-FENSTER, WOCHENLIMIT, SONNET WOCHENLIMIT, Footer
- Sections separated by 1px divider lines per styleguide
- Section headers: UPPERCASE, 11px, Semibold, muted gray
- Footer: horizontal row of icon-only buttons with tooltip on hover (deviation from styleguide)
- Footer icons: Aktualisieren (ArrowSync \uE895), Einstellungen (Settings \uE713), Beenden (PowerButton \uE7E8)
- Remove current Logout button from MainView dashboard, move to Settings page
- Settings page via frame navigation (slide from right), back navigation to dashboard
- Settings contents: Refresh interval selector (ComboBox), Dark/Light mode toggle (ToggleSwitch), Logout button (at bottom)
- Progress bar colors follow unified thresholds: green (0-50%), yellow (50-75%), orange (75-90%), red (90-100%)
- Progress bar colors differ between Dark/Light mode per styleguide table

### Claude's Discretion
- API endpoint discovery and call strategy (single vs. multiple calls)
- Exact retry backoff timing (exponential, linear, etc.)
- Cache file format and location (within %LOCALAPPDATA%\CCInfoWindows\)
- Settings page exact layout and spacing
- DispatcherTimer vs. PeriodicTimer for countdown ticking
- Warning badge design for API errors (icon choice, position)

### Deferred Ideas (OUT OF SCOPE)
- Always-on-top toggle in Settings -- Phase 6
- System Tray minimize option -- potential v2 feature
</user_constraints>

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|-----------------|
| 5HUR-01 | Current usage percentage within sliding 5-hour window displayed | Single API call returns `five_hour.utilization` as 0.0-1.0 double |
| 5HUR-02 | Reset countdown shows remaining time (e.g., "2h 14min") | API returns `five_hour.resets_at` as ISO 8601 date; local timer ticks every 60s |
| WEEK-01 | Weekly 7-day quota displayed as percentage with progress bar | API returns `seven_day.utilization` and `seven_day.resets_at` |
| WEEK-02 | Separate Sonnet and Opus weekly usage with individual progress bars | API returns `seven_day_opus` and `seven_day_sonnet` as separate windows |
| WEEK-03 | Reset countdown and reset date/time for each weekly limit | Each window has `resets_at`; format as countdown + localized date |
| DATA-01 | Claude.ai API polled for 5-hour and weekly usage data | `GET /api/organizations/{orgId}/usage` with sessionKey cookie |
| DATA-02 | Organization IDs percent-encoded in API URLs | Use `Uri.EscapeDataString()` on org ID from `lastActiveOrg` cookie |
| UIPF-02 | Opaque background following light/dark color scheme | ThemeDictionaries in AppTheme.xaml: Dark=#1E1E1E, Light=#F5F5F5 |
| UIPF-04 | Unified color thresholds for all progress bars | Helper class with threshold logic + per-theme hex values from styleguide |
| SETT-01 | Configurable refresh interval (manual or 30s-10min) | ComboBox in Settings, persisted to AppSettings.RefreshIntervalSeconds |
| SETT-05 | Manual dark/light mode toggle with immediate application | ToggleSwitch in Settings, sets `(Content as FrameworkElement).RequestedTheme` |
| SETT-06 | Color mode persisted locally, restored on startup (default: dark) | AppSettings.ColorMode = "dark" default, applied in App.OnLaunched |
</phase_requirements>

## Standard Stack

### Core (already in project)
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| Microsoft.WindowsAppSDK | 1.8.260209005 | WinUI 3 framework | Already installed, provides DispatcherQueueTimer, ProgressBar, Frame navigation |
| CommunityToolkit.Mvvm | 8.4.0 | MVVM source generators | Already installed, [ObservableProperty], [RelayCommand], WeakReferenceMessenger |
| Microsoft.Extensions.DependencyInjection | 9.0.0 | IoC container | Already installed, register new services |
| System.Text.Json | (built-in .NET 9) | JSON serialization | Already used for settings, use for API response + cache |

### Supporting (no new packages needed)
| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| System.Net.Http.HttpClient | (built-in) | API calls | Already registered as singleton in DI |
| DispatcherQueueTimer | (WinUI 3 built-in) | Polling + countdown ticking | Two timer instances: one for API polling, one for per-minute countdown |

### No New NuGet Packages Required
Phase 2 does not need any new NuGet packages. Everything is achievable with the existing stack.

## Architecture Patterns

### Recommended Project Structure (new files for Phase 2)
```
CCInfoWindows/CCInfoWindows/
  Models/
    AppSettings.cs          # MODIFY: add RefreshIntervalSeconds, ColorMode
    UsageData.cs            # NEW: UsageResponse, UsageWindow, UsageData (C# records)
  ViewModels/
    MainViewModel.cs        # MODIFY: add polling, usage properties, countdown, footer commands
    SettingsViewModel.cs    # NEW: refresh interval, theme toggle, logout
  Views/
    MainView.xaml           # MODIFY: replace placeholder with dashboard sections
    SettingsView.xaml(.cs)  # NEW: settings page
  Services/
    ClaudeApiService.cs     # NEW: HTTP calls to usage endpoint
    Interfaces/
      IClaudeApiService.cs  # NEW: service contract
  Helpers/
    ColorThresholds.cs      # NEW: progress bar color logic
  Converters/
    PercentageToColorConverter.cs  # NEW: XAML converter for progress bar fill
  Resources/
    AppTheme.xaml           # NEW: ThemeDictionaries with all styleguide colors
  Messages/
    ThemeChangedMessage.cs  # NEW: notify MainWindow to apply theme
```

### Pattern 1: API Service with Retry and Caching
**What:** ClaudeApiService encapsulates all HTTP communication with claude.ai, including retry logic, caching, and error classification.
**When to use:** Every API poll goes through this service.
**Example:**
```csharp
// Source: Derived from macOS reference app ClaudeAPIClient.swift
public class ClaudeApiService : IClaudeApiService
{
    private readonly HttpClient _httpClient;
    private readonly ICredentialService _credentialService;
    private const string BaseUrl = "https://claude.ai/api";

    public async Task<UsageData?> FetchUsageAsync(CancellationToken ct = default)
    {
        var (sessionKey, orgId) = _credentialService.GetCredentials();
        if (sessionKey is null || orgId is null) return null;

        var encodedOrgId = Uri.EscapeDataString(orgId);
        var url = $"{BaseUrl}/organizations/{encodedOrgId}/usage";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("Cookie", $"sessionKey={sessionKey}");
        request.Headers.Add("anthropic-client-platform", "web_claude_ai");

        var response = await _httpClient.SendAsync(request, ct);
        // Handle 401, 429, 5xx per locked decisions...
    }
}
```

### Pattern 2: DispatcherQueueTimer for Polling and Countdown
**What:** Two separate timers -- one for API polling (configurable 30s-10min), one for countdown ticking (fixed 60s).
**When to use:** MainViewModel creates both timers on initialization.
**Example:**
```csharp
// DispatcherQueueTimer fires on UI thread -- no marshaling needed
private DispatcherQueueTimer _pollTimer;
private DispatcherQueueTimer _countdownTimer;

private void StartTimers()
{
    var dispatcherQueue = DispatcherQueue.GetForCurrentThread();

    _pollTimer = dispatcherQueue.CreateTimer();
    _pollTimer.Interval = TimeSpan.FromSeconds(_refreshIntervalSeconds);
    _pollTimer.Tick += async (s, e) => await PollUsageAsync();
    _pollTimer.Start();

    _countdownTimer = dispatcherQueue.CreateTimer();
    _countdownTimer.Interval = TimeSpan.FromMinutes(1);
    _countdownTimer.Tick += (s, e) => UpdateCountdowns();
    _countdownTimer.Start();
}
```

### Pattern 3: Theme Switching via RequestedTheme
**What:** Set `(Content as FrameworkElement).RequestedTheme` on the Window root element for immediate theme switch.
**When to use:** On startup (restore persisted theme) and on Settings toggle change.
**Example:**
```csharp
// In MainWindow or via a ThemeService
public static void ApplyTheme(Window window, string colorMode)
{
    if (window.Content is FrameworkElement fe)
    {
        fe.RequestedTheme = colorMode == "dark"
            ? ElementTheme.Dark
            : ElementTheme.Light;
    }
}
```

### Pattern 4: ThemeDictionaries for Custom Colors
**What:** Central ResourceDictionary with ThemeDictionaries defining Dark and Light color variants.
**When to use:** All styleguide-specific colors (background, text, progress bar, dividers).
**Example:**
```xml
<!-- AppTheme.xaml -->
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
    <ResourceDictionary.ThemeDictionaries>
        <ResourceDictionary x:Key="Dark">
            <SolidColorBrush x:Key="AppBackgroundBrush" Color="#1E1E1E" />
            <SolidColorBrush x:Key="SectionHeaderBrush" Color="#8E8E93" />
            <SolidColorBrush x:Key="DividerBrush" Color="#3A3A3A" />
            <SolidColorBrush x:Key="ProgressTrackBrush" Color="#38383A" />
            <SolidColorBrush x:Key="PrimaryTextBrush" Color="#FFFFFF" />
            <SolidColorBrush x:Key="SecondaryTextBrush" Color="#8E8E93" />
            <SolidColorBrush x:Key="TertiaryTextBrush" Color="#636366" />
            <SolidColorBrush x:Key="ProgressGreenBrush" Color="#30D158" />
            <SolidColorBrush x:Key="ProgressYellowBrush" Color="#FFD60A" />
            <SolidColorBrush x:Key="ProgressOrangeBrush" Color="#FF9F0A" />
            <SolidColorBrush x:Key="ProgressRedBrush" Color="#FF453A" />
        </ResourceDictionary>
        <ResourceDictionary x:Key="Light">
            <SolidColorBrush x:Key="AppBackgroundBrush" Color="#F5F5F5" />
            <SolidColorBrush x:Key="SectionHeaderBrush" Color="#6E6E73" />
            <SolidColorBrush x:Key="DividerBrush" Color="#D0D0D0" />
            <SolidColorBrush x:Key="ProgressTrackBrush" Color="#D1D1D6" />
            <SolidColorBrush x:Key="PrimaryTextBrush" Color="#1C1C1E" />
            <SolidColorBrush x:Key="SecondaryTextBrush" Color="#6E6E73" />
            <SolidColorBrush x:Key="TertiaryTextBrush" Color="#8E8E93" />
            <SolidColorBrush x:Key="ProgressGreenBrush" Color="#34C759" />
            <SolidColorBrush x:Key="ProgressYellowBrush" Color="#FFCC00" />
            <SolidColorBrush x:Key="ProgressOrangeBrush" Color="#FF9500" />
            <SolidColorBrush x:Key="ProgressRedBrush" Color="#FF3B30" />
        </ResourceDictionary>
    </ResourceDictionary.ThemeDictionaries>
</ResourceDictionary>
```

### Pattern 5: Local Cache for Startup
**What:** Persist last API response as JSON to `%LOCALAPPDATA%\CCInfoWindows\usage_cache.json`. On startup, load cache immediately, show data with "Aktualisiere..." indicator, then poll fresh data.
**When to use:** Every app startup, every successful API response.

### Anti-Patterns to Avoid
- **Fire-and-forget async in timer tick:** Always `await` the polling method. Use `async void` only for event handlers, and catch all exceptions within.
- **Blocking UI thread with HTTP calls:** Never use `.Result` or `.Wait()` -- always `await`.
- **Storing organization ID in Credential Manager as separate entry:** Store it alongside sessionKey (extend the credential model) to keep auth data atomic.
- **Using `Application.RequestedTheme` for runtime toggle:** This throws at runtime. Use `FrameworkElement.RequestedTheme` on the root content element instead.
- **Custom ProgressBar template for color changes:** Don't retemplate -- just set `Foreground` directly. WinUI 3 ProgressBar accepts `Foreground` as a `SolidColorBrush`.

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Progress bar color thresholds | Manual if/else in each XAML binding | Centralized `ColorThresholds` helper + IValueConverter | Single source of truth, testable, reusable across all bars |
| Countdown formatting | Custom string concatenation | `TimeSpan` formatting or custom method returning "Xh Ymin" | Consistent format, handles edge cases (0h, negative time) |
| JSON deserialization | Manual JsonDocument parsing | `System.Text.Json` with typed models + `JsonSerializerOptions` | Type-safe, handles nulls, ISO 8601 dates out of box |
| Timer on UI thread | `System.Threading.Timer` + DispatcherQueue.TryEnqueue | `DispatcherQueueTimer` | Already runs on UI thread, no marshaling overhead |
| Retry with backoff | Polly or hand-rolled while loop | Simple for-loop with `Task.Delay` (2-3 attempts only) | Polly is overkill for 2-3 retries; simple loop is clear |
| Percent encoding | Manual string replacement | `Uri.EscapeDataString()` | RFC 3986 compliant, handles all special chars |

## Common Pitfalls

### Pitfall 1: Missing Organization ID from Login
**What goes wrong:** The current LoginViewModel only extracts `sessionKey` cookie. The usage API requires `{orgId}` in the URL path.
**Why it happens:** Phase 1 didn't need org ID -- it only validated tokens via `/api/organizations`.
**How to avoid:** Modify cookie extraction in LoginViewModel to also grab `lastActiveOrg` cookie. Store both values. The `lastActiveOrg` cookie is set by claude.ai after login and contains the current organization UUID.
**Warning signs:** API calls return 404 because org ID is null/empty.

### Pitfall 2: DispatcherQueueTimer vs DispatcherTimer
**What goes wrong:** Using the wrong timer class or mixing WPF/UWP timer APIs.
**Why it happens:** WinUI 3 has `Microsoft.UI.Xaml.DispatcherQueueTimer` (correct) and `Windows.UI.Xaml.DispatcherTimer` (legacy UWP). Documentation often mixes these.
**How to avoid:** Use `DispatcherQueue.GetForCurrentThread().CreateTimer()` to get a `DispatcherQueueTimer`. This is the recommended approach for WinUI 3 / Windows App SDK.
**Warning signs:** Compilation errors, timer not firing, or UI thread access violations.

### Pitfall 3: Theme Not Applying to Window Background
**What goes wrong:** Setting `RequestedTheme` on the root `FrameworkElement` updates control colors but the actual window background (title bar area, chrome) stays unchanged.
**Why it happens:** WinUI 3 window chrome is separate from XAML content.
**How to avoid:** Set the Page/Grid `Background` explicitly via ThemeResource binding: `Background="{ThemeResource AppBackgroundBrush}"`. The window itself doesn't participate in XAML theming -- only the content does.
**Warning signs:** Controls switch theme but background color stays the same.

### Pitfall 4: API Response Structure Assumptions
**What goes wrong:** Assuming all fields are always present. The `seven_day_opus` and `seven_day_sonnet` fields may be null for some subscription plans.
**Why it happens:** The API is unofficial and undocumented.
**How to avoid:** Make all weekly model-specific fields nullable in the C# model. Handle null gracefully by hiding the section or showing "N/A".
**Warning signs:** `NullReferenceException` on deserialization or display.

### Pitfall 5: Credential Service Extension Breaking Change
**What goes wrong:** Changing `ICredentialService` to store org ID in addition to session key may break the saved credential format.
**Why it happens:** Phase 1 stored only a password string. Now we need session key + org ID.
**How to avoid:** Store as JSON object in the Credential Manager password field (both sessionKey and orgId), or use a separate credential entry. Migrate existing single-value credentials gracefully (re-login needed if org ID is missing).
**Warning signs:** Existing users after Phase 1 get null org ID until they re-login.

### Pitfall 6: Timer Not Stopped on Navigation Away
**What goes wrong:** Polling timer keeps running when user navigates to Settings page, causing unnecessary API calls.
**Why it happens:** Timer lifecycle not tied to page visibility.
**How to avoid:** Stop timers when navigating away from MainView, restart when returning. Use `Page.Loaded`/`Page.Unloaded` events or ViewModel lifecycle methods.
**Warning signs:** Multiple simultaneous poll requests, unexpected API errors in logs.

### Pitfall 7: anthropic-client-platform Header
**What goes wrong:** API calls fail or return different data without the `anthropic-client-platform: web_claude_ai` header.
**Why it happens:** The macOS reference app includes this header; the API may require it for proper routing.
**How to avoid:** Always include `anthropic-client-platform: web_claude_ai` header in all API requests.
**Warning signs:** Unexpected 403 or different response format.

## Code Examples

### API Response JSON Structure (from macOS reference app)
```json
// Source: Reverse-engineered from stefanlange/ccInfo UsageData.swift
// GET https://claude.ai/api/organizations/{orgId}/usage
{
  "five_hour": {
    "utilization": 0.49,
    "resets_at": "2026-03-10T15:30:00Z"
  },
  "seven_day": {
    "utilization": 0.16,
    "resets_at": "2026-03-14T10:00:00Z"
  },
  "seven_day_opus": {
    "utilization": 0.24,
    "resets_at": "2026-03-14T10:00:00Z"
  },
  "seven_day_sonnet": {
    "utilization": 0.08,
    "resets_at": "2026-03-14T10:00:00Z"
  }
}
```

### Color Threshold Helper
```csharp
// Source: Styleguide ccinfo-styleguide.md section 4.1
public static class ColorThresholds
{
    public static string GetThresholdKey(double utilization)
    {
        return utilization switch
        {
            < 0.50 => "ProgressGreenBrush",
            < 0.75 => "ProgressYellowBrush",
            < 0.90 => "ProgressOrangeBrush",
            _ => "ProgressRedBrush"
        };
    }
}
```

### Countdown Formatting
```csharp
// Source: macOS reference UsageData.swift WindowUsage.formattedTimeUntilReset
public static string FormatCountdown(DateTimeOffset? resetsAt)
{
    if (resetsAt is null || resetsAt <= DateTimeOffset.UtcNow) return "--";
    var remaining = resetsAt.Value - DateTimeOffset.UtcNow;
    if (remaining.TotalHours >= 1)
        return $"{(int)remaining.TotalHours}h {remaining.Minutes}min";
    return $"{remaining.Minutes}min";
}
```

### Reset Date Formatting
```csharp
// German locale format: "Fr. 27.02., 10:00"
public static string FormatResetDate(DateTimeOffset? resetsAt)
{
    if (resetsAt is null) return "--";
    return resetsAt.Value.LocalDateTime.ToString("ddd dd.MM., HH:mm", new CultureInfo("de-DE"));
}
```

### Cookie Extraction (Organization ID)
```csharp
// Source: macOS reference AuthWebView.swift extractCredentials()
// In LoginViewModel.TryExtractSessionCookieAsync:
var orgCookie = cookies.FirstOrDefault(c =>
    string.Equals(c.Name, "lastActiveOrg", StringComparison.Ordinal));

if (sessionCookie is not null && orgCookie is not null)
{
    _credentialService.SaveCredentials(sessionCookie.Value, orgCookie.Value);
    // ...
}
```

### Progress Bar XAML with Dynamic Color
```xml
<!-- Use Foreground binding with converter for dynamic color -->
<ProgressBar
    Value="{x:Bind ViewModel.FiveHourPercentage, Mode=OneWay}"
    Maximum="100"
    Foreground="{x:Bind ViewModel.FiveHourPercentage, Mode=OneWay, Converter={StaticResource PercentageToColorConverter}}"
    Background="{ThemeResource ProgressTrackBrush}"
    Height="6"
    CornerRadius="3" />
```

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| `DispatcherTimer` (UWP) | `DispatcherQueueTimer` (WinUI 3) | Windows App SDK 1.0+ | Use `DispatcherQueue.CreateTimer()` instead |
| `Application.RequestedTheme` set once | `FrameworkElement.RequestedTheme` at runtime | Always been this way in WinUI 3 | Theme toggle works without restart |
| Separate credential entries | Single JSON blob in Credential Manager | Phase 2 change | Atomic credential storage |

## Open Questions

1. **API endpoint stability**
   - What we know: The macOS reference app uses `GET /api/organizations/{orgId}/usage` and it returns a flat JSON with `five_hour`, `seven_day`, `seven_day_opus`, `seven_day_sonnet` windows.
   - What's unclear: Since this is an unofficial API, it may change without notice. Response fields could vary by subscription plan (Pro vs Max).
   - Recommendation: Code defensively with all fields nullable. Log unexpected response shapes for debugging.

2. **Utilization value range**
   - What we know: macOS app treats `utilization` as a 0.0-1.0 double (49% = 0.49).
   - What's unclear: Whether it can exceed 1.0 (e.g., during rate-limited state).
   - Recommendation: Clamp display to 0-100% but accept values > 1.0 gracefully (show as 100%+).

3. **Organization ID migration from Phase 1**
   - What we know: Phase 1 only stores sessionKey. Phase 2 needs orgId too.
   - What's unclear: Best migration path -- force re-login or try to fetch orgId from `/api/organizations` endpoint using existing sessionKey.
   - Recommendation: On startup, if orgId is missing but sessionKey exists, call `/api/organizations` to fetch org list and extract the first org's UUID. Only force re-login if this also fails.

## Validation Architecture

### Test Framework
| Property | Value |
|----------|-------|
| Framework | No test project exists yet |
| Config file | none -- see Wave 0 |
| Quick run command | `dotnet test CCInfoWindows.Tests/CCInfoWindows.Tests.csproj --filter "Category!=Integration"` |
| Full suite command | `dotnet test CCInfoWindows.Tests/CCInfoWindows.Tests.csproj` |

### Phase Requirements -> Test Map
| Req ID | Behavior | Test Type | Automated Command | File Exists? |
|--------|----------|-----------|-------------------|-------------|
| 5HUR-01 | Usage percentage parsing from API response | unit | `dotnet test --filter "FullyQualifiedName~UsageDataTests"` | No - Wave 0 |
| 5HUR-02 | Countdown formatting logic | unit | `dotnet test --filter "FullyQualifiedName~CountdownFormatterTests"` | No - Wave 0 |
| WEEK-01 | Weekly quota parsing | unit | `dotnet test --filter "FullyQualifiedName~UsageDataTests"` | No - Wave 0 |
| WEEK-02 | Separate Sonnet/Opus parsing | unit | `dotnet test --filter "FullyQualifiedName~UsageDataTests"` | No - Wave 0 |
| WEEK-03 | Reset date formatting (German locale) | unit | `dotnet test --filter "FullyQualifiedName~DateFormatterTests"` | No - Wave 0 |
| DATA-01 | API call construction and header setup | unit | `dotnet test --filter "FullyQualifiedName~ClaudeApiServiceTests"` | No - Wave 0 |
| DATA-02 | Org ID percent-encoding | unit | `dotnet test --filter "FullyQualifiedName~ClaudeApiServiceTests"` | No - Wave 0 |
| UIPF-04 | Color threshold logic | unit | `dotnet test --filter "FullyQualifiedName~ColorThresholdsTests"` | No - Wave 0 |
| SETT-01 | Refresh interval persistence | unit | `dotnet test --filter "FullyQualifiedName~SettingsTests"` | No - Wave 0 |
| SETT-05 | Theme toggle | manual-only | Manual: toggle switch and verify visual change | N/A |
| SETT-06 | Default dark mode on first start | unit | `dotnet test --filter "FullyQualifiedName~SettingsTests"` | No - Wave 0 |
| UIPF-02 | Opaque background theme | manual-only | Manual: visual inspection in both themes | N/A |

### Sampling Rate
- **Per task commit:** `dotnet test CCInfoWindows.Tests/CCInfoWindows.Tests.csproj --filter "Category!=Integration" -x`
- **Per wave merge:** `dotnet test CCInfoWindows.Tests/CCInfoWindows.Tests.csproj`
- **Phase gate:** Full suite green before `/gsd:verify-work`

### Wave 0 Gaps
- [ ] `CCInfoWindows.Tests/CCInfoWindows.Tests.csproj` -- new test project (xUnit + Moq)
- [ ] `CCInfoWindows.Tests/Models/UsageDataTests.cs` -- covers 5HUR-01, WEEK-01, WEEK-02
- [ ] `CCInfoWindows.Tests/Helpers/ColorThresholdsTests.cs` -- covers UIPF-04
- [ ] `CCInfoWindows.Tests/Helpers/CountdownFormatterTests.cs` -- covers 5HUR-02, WEEK-03
- [ ] `CCInfoWindows.Tests/Services/ClaudeApiServiceTests.cs` -- covers DATA-01, DATA-02
- [ ] `CCInfoWindows.Tests/Models/AppSettingsTests.cs` -- covers SETT-01, SETT-06

## Sources

### Primary (HIGH confidence)
- stefanlange/ccInfo `ClaudeAPIClient.swift` -- API endpoint URL, headers, auth pattern (GitHub source)
- stefanlange/ccInfo `UsageData.swift` -- JSON response structure, field names, data types (GitHub source)
- stefanlange/ccInfo `AuthWebView.swift` -- `lastActiveOrg` cookie extraction pattern (GitHub source)
- ccinfo-styleguide.md (project spec) -- all color hex values, typography, spacing, progress bar geometry
- ccinfo-spec.md (project spec) -- functional requirements (FA-020 through FA-095)
- ccinfo-tech-spec.md (project spec) -- architecture, navigation, threading model

### Secondary (MEDIUM confidence)
- [WinUI 3 FrameworkElement.RequestedTheme](https://learn.microsoft.com/en-us/uwp/api/windows.ui.xaml.frameworkelement.requestedtheme) -- runtime theme toggling
- [WinUI 3 DispatcherQueueTimer](https://github.com/microsoft/WindowsAppSDK/discussions/4770) -- recommended over DispatcherTimer
- [WinUI 3 ProgressBar](https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.progressbar) -- Foreground property for color override

### Tertiary (LOW confidence)
- Claude.ai API stability -- unofficial API, may change without notice
- `anthropic-client-platform` header requirement -- observed in reference app, not officially documented

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH -- no new packages needed, existing stack covers everything
- Architecture: HIGH -- patterns directly derived from working macOS reference app + established WinUI 3 patterns
- API endpoints: HIGH -- verified from macOS reference app source code (ClaudeAPIClient.swift + UsageData.swift)
- Pitfalls: HIGH -- identified from code review comparing Phase 1 implementation against macOS reference
- Color/styling: HIGH -- all values from authoritative styleguide document

**Research date:** 2026-03-10
**Valid until:** 2026-04-10 (API endpoints may change; styleguide/framework are stable)
