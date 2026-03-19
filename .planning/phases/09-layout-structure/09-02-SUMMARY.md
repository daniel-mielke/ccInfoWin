---
phase: 09-layout-structure
plan: 02
subsystem: ui
tags: [winui3, xaml, layout, scrollviewer, grid, separator]

# Dependency graph
requires:
  - phase: 09-layout-structure/09-01
    provides: Active Session header, Context Window repositioned, equal padding structure
provides:
  - Footer relocated into ScrollViewer (scrolls with content, not fixed)
  - Separator above footer (DividerBrush, Margin="0,4,0,0")
  - Separator between Models and Input rows in Statistics grid (DividerBrush, Grid.Row="1")
  - Root Grid collapsed from 3 rows to 2 rows (Auto,*)
  - Statistics Grid expanded from 8 to 9 RowDefinitions
affects: [10-visual-styles, 11-behavior]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Footer inside ScrollViewer StackPanel — content scrolls together, no fixed bottom bar"
    - "Separator Border with ThemeResource DividerBrush for consistent divider styling"
    - "InfoBar Visibility bound to IsOpen to prevent phantom layout height when closed"

key-files:
  created: []
  modified:
    - CCInfoWindows/CCInfoWindows/Views/MainView.xaml

key-decisions:
  - "Footer moved into ScrollViewer content — removes sticky bottom bar, aligns with macOS ccInfo scroll behavior"
  - "Root Grid RowDefinitions changed from Auto,*,Auto to Auto,* — third row no longer needed"
  - "Padding reorganized during checkpoint: root Grid padding removed, ScrollViewer content gets 16px padding and InfoBar row gets its own padding for full-height scroll track"
  - "InfoBar Visibility bound to IsOpen property to eliminate phantom height when banner is hidden"

patterns-established:
  - "Separator pattern: <Border Height=\"1\" Background=\"{ThemeResource DividerBrush}\" /> for all dividers"
  - "Scrollable layout: all content including footer goes inside ScrollViewer StackPanel"

requirements-completed: [LAYOUT-05, LAYOUT-06]

# Metrics
duration: ~45min
completed: 2026-03-19
---

# Phase 9 Plan 02: Footer Scroll & Statistics Separator Summary

**Footer relocated into ScrollViewer (scrolls with content), Statistics grid gains Models/Input separator — completing all 6 LAYOUT requirements from Plans 01 and 02**

## Performance

- **Duration:** ~45 min
- **Started:** 2026-03-19T14:00:00Z
- **Completed:** 2026-03-19T15:00:00Z
- **Tasks:** 3 (including checkpoint)
- **Files modified:** 1

## Accomplishments

- Footer StackPanel moved from fixed Grid.Row="2" into ScrollViewer content area — users can now scroll to reach it
- Root Grid collapsed from 3 rows (Auto,\*,Auto) to 2 rows (Auto,\*) — clean layout structure
- Separator added above footer using DividerBrush — clear visual break before action buttons
- Separator inserted between Models and Input rows in Statistics grid — improves readability of token breakdown
- Statistics Grid expanded from 8 to 9 RowDefinitions with correct row index shifting (21 elements updated)
- All 6 LAYOUT requirements (LAYOUT-01 through LAYOUT-06) visually confirmed by user

## Task Commits

Each task was committed atomically:

1. **Task 1: Move footer into ScrollViewer and collapse Grid rows** - `eb2796c` (feat)
2. **Task 2: Insert separator between Models and Input rows in Statistics grid** - `73f55cb` (feat)
3. **Task 3: Visual verification checkpoint — approved by user** - (checkpoint, no code commit)

**Additional fixes during checkpoint review:**
- `2867857` - fix: remove root Grid padding, move to InfoBar row and ScrollViewer content
- `180a111` - fix: remove WinUI 3 Page default padding, remove InfoBar row padding
- `55bcaf5` - fix: bind InfoBar Visibility to IsOpen to prevent layout height when closed
- `0a83dda` - fix: increase top padding of scroll content to 16px to match horizontal padding

## Files Created/Modified

- `CCInfoWindows/CCInfoWindows/Views/MainView.xaml` - Footer moved into ScrollViewer, Statistics separator added, padding restructured, InfoBar Visibility binding fixed

## Decisions Made

- Footer inside ScrollViewer: matches macOS ccInfo v1.7.1 scroll behavior, eliminates fixed bottom bar
- Padding reorganized during visual review: root Grid padding removed so ScrollViewer can fill full height; ScrollViewer inner StackPanel gets Padding="16,16,16,16" for symmetric content spacing
- InfoBar Visibility bound to IsOpen: WinUI 3 InfoBar reserves layout height even when hidden — binding eliminates phantom space

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Root Grid padding caused ScrollViewer to not fill full height**
- **Found during:** Task 3 (visual verification)
- **Issue:** Padding on root Grid created insets preventing full-height scroll track
- **Fix:** Removed root Grid Padding, moved padding to InfoBar row and ScrollViewer content StackPanel
- **Files modified:** CCInfoWindows/CCInfoWindows/Views/MainView.xaml
- **Verification:** Visual inspection confirmed full-height scroll track
- **Committed in:** 2867857

**2. [Rule 1 - Bug] WinUI 3 Page default padding added unwanted insets**
- **Found during:** Task 3 (visual verification)
- **Issue:** Page element has default padding in WinUI 3 that stacked with Grid padding
- **Fix:** Explicitly set Padding="0" on Page, removed redundant InfoBar row padding
- **Files modified:** CCInfoWindows/CCInfoWindows/Views/MainView.xaml
- **Verification:** Visual inspection confirmed clean edges
- **Committed in:** 180a111

**3. [Rule 2 - Missing Critical] InfoBar height not suppressed when hidden**
- **Found during:** Task 3 (visual verification)
- **Issue:** InfoBar with Visibility="Collapsed" still reserved layout height due to WinUI 3 behavior
- **Fix:** Bound Visibility to IsOpen property so element collapses when not shown
- **Files modified:** CCInfoWindows/CCInfoWindows/Views/MainView.xaml
- **Verification:** InfoBar row takes zero height when closed
- **Committed in:** 55bcaf5

**4. [Rule 1 - Bug] Top padding of scroll content was 8px instead of 16px**
- **Found during:** Task 3 (visual verification)
- **Issue:** After padding reorganization, top padding was set to 8px — asymmetric with 16px sides
- **Fix:** Changed top padding to 16px for symmetric Padding="16,16,16,16"
- **Files modified:** CCInfoWindows/CCInfoWindows/Views/MainView.xaml
- **Verification:** Visual inspection confirmed symmetric padding all sides
- **Committed in:** 0a83dda

---

**Total deviations:** 4 auto-fixed (3 bugs, 1 missing critical)
**Impact on plan:** All fixes arose from visual verification — padding and InfoBar behavior issues specific to WinUI 3. No scope creep.

## Issues Encountered

- WinUI 3 Page element has implicit default padding that must be explicitly zeroed out — not documented in plan
- WinUI 3 InfoBar reserves layout height even with Visibility="Collapsed" — requires IsOpen binding to suppress

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- All 6 LAYOUT requirements (LAYOUT-01 through LAYOUT-06) complete
- MainView.xaml structure is stable — ready for Phase 10 visual styles
- Phase 10 will modify AppTheme.xaml for STYLE-01 through STYLE-05 and TEXT-02/03/04

---
*Phase: 09-layout-structure*
*Completed: 2026-03-19*
