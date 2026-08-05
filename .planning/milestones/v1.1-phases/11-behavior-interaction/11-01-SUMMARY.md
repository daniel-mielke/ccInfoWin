---
phase: 11-behavior-interaction
plan: 01
subsystem: ui
tags: [winui3, xaml, countdown, formatting, localization, icons, fontic]

# Dependency graph
requires:
  - phase: 10-visual-styles
    provides: ProgressRedBrush and AppTheme.xaml resource brushes used by logout button
provides:
  - CountdownFormatter >=24h branch returning "Xd Yh" format
  - Red logout button with FontIcon E8FB in SettingsView
  - Login icon FontIcon E77B on ReLoginButton in MainView
affects: [future phases using CountdownFormatter, button icon patterns]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "TDD for pure C# helper methods: RED (failing tests) then GREEN (implementation)"
    - "Icon+label buttons: StackPanel with FontIcon + TextBlock inside Button Content"
    - "Localization for icon+label buttons: l:Uids.Uid on TextBlock (not Button), resw key uses .Text property"

key-files:
  created: []
  modified:
    - CCInfoWindows/CCInfoWindows/Helpers/CountdownFormatter.cs
    - CCInfoWindows.Tests/Helpers/CountdownFormatterTests.cs
    - CCInfoWindows/CCInfoWindows/Views/SettingsView.xaml
    - CCInfoWindows/CCInfoWindows/Views/MainView.xaml
    - CCInfoWindows/CCInfoWindows/Strings/en-US/Resources.resw
    - CCInfoWindows/CCInfoWindows/Strings/de-DE/Resources.resw

key-decisions:
  - "CountdownFormatter uses remaining.Hours (component, 0-23) not TotalHours for the hours part of Xd Yh display"
  - "Icon+label buttons use l:Uids.Uid on inner TextBlock with .Text resw key, not on Button with .Content key"
  - "Old .Content resw keys replaced with .Text keys — cleaner, no orphaned keys"

patterns-established:
  - "Icon+label button pattern: <Button><StackPanel Orientation=Horizontal Spacing=8><FontIcon/><TextBlock l:Uids.Uid/></StackPanel></Button>"
  - "l:Uids.Uid on TextBlock resolves .Text property in resw; on Button resolves .Content"

requirements-completed: [TEXT-01, INTER-01, INTER-02]

# Metrics
duration: 18min
completed: 2026-03-20
---

# Phase 11 Plan 01: Behavior-Interaction Timer Format and Button Icons Summary

**CountdownFormatter extended with >=24h "Xd Yh" branch (TDD), logout button styled red with E8FB icon, ReLogin button decorated with E77B login icon**

## Performance

- **Duration:** ~18 min
- **Started:** 2026-03-20T00:00:00Z
- **Completed:** 2026-03-20T00:18:00Z
- **Tasks:** 3
- **Files modified:** 6

## Accomplishments
- `CountdownFormatter.FormatCountdown` now returns "3d 22h" style for durations >= 24 hours, with 5 new passing TDD tests covering boundary cases
- Logout button in SettingsView styled red (`ProgressRedBrush`) with white text and FontIcon E8FB (sign-out) left of localized label
- ReLogin button in MainView decorated with FontIcon E77B (sign-in) left of localized label text

## Task Commits

Each task was committed atomically:

1. **Task 1: Add >=24h branch to CountdownFormatter with TDD tests** - `fc1f395` (feat)
2. **Task 2: Style logout button red with icon in SettingsView** - `3dc25d0` (feat)
3. **Task 3: Add login icon to ReLoginButton in MainView** - `4e8a891` (feat)

_Note: Task 1 used TDD — RED phase (failing tests) then GREEN phase (implementation) in a single commit._

## Files Created/Modified
- `CCInfoWindows/CCInfoWindows/Helpers/CountdownFormatter.cs` - Added >=24h branch using TotalDays/remaining.Hours
- `CCInfoWindows.Tests/Helpers/CountdownFormatterTests.cs` - 5 new tests for days format and boundary
- `CCInfoWindows/CCInfoWindows/Views/SettingsView.xaml` - Logout button replaced with red ProgressRedBrush + E8FB icon
- `CCInfoWindows/CCInfoWindows/Views/MainView.xaml` - ReLoginButton wrapped in StackPanel with E77B FontIcon
- `CCInfoWindows/CCInfoWindows/Strings/en-US/Resources.resw` - Updated .Content to .Text keys for both buttons
- `CCInfoWindows/CCInfoWindows/Strings/de-DE/Resources.resw` - Updated .Content to .Text keys for both buttons

## Decisions Made
- Used `remaining.Hours` (TimeSpan component property, 0-23) NOT `remaining.TotalHours` for the hours part of the "Xd Yh" format — otherwise "1d 23h" would display as "1d 47h"
- Moved `l:Uids.Uid` from Button element to inner TextBlock because TextBlock resolves `.Text` from resw while Button resolves `.Content`; matching property name is critical for WinUI3Localizer
- Replaced old `.Content` resw keys with `.Text` keys (no orphaned unused entries)

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered

None.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness
- All behavioral changes for plan 11-01 complete
- Plan 11-02 (INTER-03 refresh animation Storyboard) can proceed
- CountdownFormatter pattern established for any future timer display changes

---
*Phase: 11-behavior-interaction*
*Completed: 2026-03-20*
