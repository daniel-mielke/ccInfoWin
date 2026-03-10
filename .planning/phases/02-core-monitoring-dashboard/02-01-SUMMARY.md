---
phase: 02-core-monitoring-dashboard
plan: 01
subsystem: ui
tags: [winui3, xaml, theme, xunit, system-text-json, mvvm]

# Dependency graph
requires:
  - phase: 01-foundation
    provides: App shell, DI container, ICredentialService, ISettingsService, AppSettings model
provides:
  - UsageResponse and UsageWindow typed models for Claude API JSON
  - IClaudeApiService interface (fetch + cache contract)
  - ColorThresholds helper (utilization-to-brush mapping)
  - CountdownFormatter helper (countdown + German locale dates)
  - PercentageToColorConverter for XAML bindings
  - AppTheme.xaml with Dark/Light ThemeDictionaries (all styleguide colors)
  - ThemeChangedMessage and RefreshIntervalChangedMessage for cross-VM communication
  - Extended AppSettings with RefreshIntervalSeconds and ColorMode
  - Extended ICredentialService with organization ID storage
  - xUnit test project with 22 passing unit tests
affects: [02-02, 02-03, 02-04]

# Tech tracking
tech-stack:
  added: [xunit 2.9.3, xunit.runner.visualstudio 3.0.2, Microsoft.NET.Test.Sdk 17.12.0, Moq 4.20.72]
  patterns: [theme-resource-brush-keys, static-helper-classes, global-usings-for-test, parameterized-theory-tests]

key-files:
  created:
    - CCInfoWindows/CCInfoWindows/Models/UsageData.cs
    - CCInfoWindows/CCInfoWindows/Services/Interfaces/IClaudeApiService.cs
    - CCInfoWindows/CCInfoWindows/Helpers/ColorThresholds.cs
    - CCInfoWindows/CCInfoWindows/Helpers/CountdownFormatter.cs
    - CCInfoWindows/CCInfoWindows/Converters/PercentageToColorConverter.cs
    - CCInfoWindows/CCInfoWindows/Resources/AppTheme.xaml
    - CCInfoWindows/CCInfoWindows/Messages/ThemeChangedMessage.cs
    - CCInfoWindows/CCInfoWindows/Messages/RefreshIntervalChangedMessage.cs
    - CCInfoWindows.Tests/CCInfoWindows.Tests.csproj
    - CCInfoWindows.Tests/Models/UsageDataTests.cs
    - CCInfoWindows.Tests/Models/AppSettingsTests.cs
    - CCInfoWindows.Tests/Helpers/ColorThresholdsTests.cs
    - CCInfoWindows.Tests/Helpers/CountdownFormatterTests.cs
  modified:
    - CCInfoWindows/CCInfoWindows/Models/AppSettings.cs
    - CCInfoWindows/CCInfoWindows/Services/Interfaces/ICredentialService.cs
    - CCInfoWindows/CCInfoWindows/Services/CredentialService.cs
    - CCInfoWindows/CCInfoWindows/App.xaml
    - CCInfoWindows/CCInfoWindows/App.xaml.cs

key-decisions:
  - "CredentialService uses separate credential target 'CCInfoWindows/claude-org' for org ID to avoid breaking existing session storage"
  - "ClearCredentials now cleans both session and org credential entries"
  - "GlobalUsings.cs in test project for Xunit namespace (xunit 2.9.3 requires explicit using)"

patterns-established:
  - "ThemeResource brush keys: use string keys like 'ProgressGreenBrush' resolved from Application.Current.Resources"
  - "Static helper classes for pure logic (ColorThresholds, CountdownFormatter) -- easily testable without XAML runtime"
  - "German locale formatting via CultureInfo('de-DE') for user-facing dates"

requirements-completed: [UIPF-02, UIPF-04, 5HUR-01, 5HUR-02, WEEK-01, WEEK-02, WEEK-03, SETT-06]

# Metrics
duration: 4min
completed: 2026-03-10
---

# Phase 2 Plan 01: Contracts and Foundations Summary

**Typed usage models, color threshold system, German locale formatters, Dark/Light theme resources, and xUnit test scaffold with 22 tests**

## Performance

- **Duration:** 4 min
- **Started:** 2026-03-10T11:21:58Z
- **Completed:** 2026-03-10T11:26:10Z
- **Tasks:** 3
- **Files modified:** 19

## Accomplishments
- UsageResponse model deserializes all 4 Claude API usage windows with nullable opus/sonnet support
- Color threshold system maps utilization to green/yellow/orange/red brush keys matching styleguide exactly
- AppTheme.xaml defines 13 color brushes each for Dark and Light themes with all styleguide hex values
- xUnit test project with 22 passing tests covering all models and helpers

## Task Commits

Each task was committed atomically:

1. **Task 1: Models, interfaces, and messages** - `735e89b` (feat)
2. **Task 2: Helpers, converters, theme resources, and App wiring** - `7d85223` (feat)
3. **Task 3: Test project and unit tests for models and helpers** - `2e3c8ee` (test)

## Files Created/Modified
- `CCInfoWindows/CCInfoWindows/Models/UsageData.cs` - Typed model for Claude API usage response (UsageResponse, UsageWindow)
- `CCInfoWindows/CCInfoWindows/Models/AppSettings.cs` - Extended with RefreshIntervalSeconds (60) and ColorMode ("dark")
- `CCInfoWindows/CCInfoWindows/Services/Interfaces/IClaudeApiService.cs` - API service contract (fetch + cache)
- `CCInfoWindows/CCInfoWindows/Services/Interfaces/ICredentialService.cs` - Extended with org ID methods
- `CCInfoWindows/CCInfoWindows/Services/CredentialService.cs` - Implements org ID storage as separate credential entry
- `CCInfoWindows/CCInfoWindows/Helpers/ColorThresholds.cs` - Utilization-to-brush-key mapping (4 zones)
- `CCInfoWindows/CCInfoWindows/Helpers/CountdownFormatter.cs` - "Xh Ymin" countdown + German locale reset dates
- `CCInfoWindows/CCInfoWindows/Converters/PercentageToColorConverter.cs` - XAML value converter resolving theme brushes
- `CCInfoWindows/CCInfoWindows/Resources/AppTheme.xaml` - Dark+Light ThemeDictionaries with all styleguide colors
- `CCInfoWindows/CCInfoWindows/Messages/ThemeChangedMessage.cs` - Cross-VM theme change notification
- `CCInfoWindows/CCInfoWindows/Messages/RefreshIntervalChangedMessage.cs` - Cross-VM refresh interval notification
- `CCInfoWindows/CCInfoWindows/App.xaml` - Merges AppTheme.xaml, registers PercentageToColorConverter
- `CCInfoWindows/CCInfoWindows/App.xaml.cs` - Applies persisted theme on startup
- `CCInfoWindows.Tests/CCInfoWindows.Tests.csproj` - xUnit test project
- `CCInfoWindows.Tests/Models/UsageDataTests.cs` - JSON deserialization tests (3 tests)
- `CCInfoWindows.Tests/Models/AppSettingsTests.cs` - Default values and roundtrip tests (3 tests)
- `CCInfoWindows.Tests/Helpers/ColorThresholdsTests.cs` - Boundary value parameterized tests (9 tests)
- `CCInfoWindows.Tests/Helpers/CountdownFormatterTests.cs` - Formatting logic tests (7 tests)

## Decisions Made
- CredentialService uses separate credential target "CCInfoWindows/claude-org" for org ID to avoid breaking existing session storage
- ClearCredentials extended to clean both session and org credential entries
- GlobalUsings.cs added in test project because xunit 2.9.3 requires explicit `using Xunit` (not included in ImplicitUsings)

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] CredentialService missing org ID implementation**
- **Found during:** Task 1 (Models, interfaces, and messages)
- **Issue:** ICredentialService was extended with SaveOrganizationId/GetOrganizationId but CredentialService didn't implement them, causing build failure
- **Fix:** Added implementation using separate credential target "CCInfoWindows/claude-org", also extended ClearCredentials to clean org entry
- **Files modified:** CCInfoWindows/CCInfoWindows/Services/CredentialService.cs
- **Verification:** dotnet build succeeds
- **Committed in:** 735e89b (Task 1 commit)

**2. [Rule 3 - Blocking] xUnit attributes not resolved without explicit using**
- **Found during:** Task 3 (Test project and unit tests)
- **Issue:** xunit 2.9.3 requires explicit `using Xunit;` -- not included by ImplicitUsings
- **Fix:** Added GlobalUsings.cs with `global using Xunit;`
- **Files modified:** CCInfoWindows.Tests/GlobalUsings.cs
- **Verification:** All 22 tests pass
- **Committed in:** 2e3c8ee (Task 3 commit)

---

**Total deviations:** 2 auto-fixed (2 blocking)
**Impact on plan:** Both fixes necessary for compilation. No scope creep.

## Issues Encountered
None beyond the auto-fixed deviations above.

## User Setup Required
None - no external service configuration required.

## Next Phase Readiness
- All typed contracts defined for Plans 02-04 to implement against
- IClaudeApiService ready for Plan 02 (API service implementation)
- ColorThresholds and CountdownFormatter ready for Plan 03 (MainViewModel)
- ThemeChangedMessage and RefreshIntervalChangedMessage ready for Plan 04 (SettingsViewModel)
- AppTheme.xaml color system ready for all UI components
- Test infrastructure established for future test additions

## Self-Check: PASSED

All 13 created files verified on disk. All 3 task commits (735e89b, 7d85223, 2e3c8ee) verified in git log.

---
*Phase: 02-core-monitoring-dashboard*
*Completed: 2026-03-10*
