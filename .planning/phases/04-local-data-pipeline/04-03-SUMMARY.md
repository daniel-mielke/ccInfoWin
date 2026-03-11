---
phase: 04-local-data-pipeline
plan: 03
subsystem: ui
tags: [winui3, mvvm, jsonl, xaml, collectionsviewsource, itemsrepeater]

requires:
  - phase: 04-02
    provides: IJsonlService with Sessions, GetContextWindow, GetTokenSummary, DataUpdated event

provides:
  - Session ComboBox with Aktiv/Inaktiv grouping via CollectionViewSource
  - KONTEXTFENSTER section with context progress bar, model badge, autocompact warning, subagent bars
  - TOKENS section with Input/Output counters and K/M formatting
  - InvertedBoolToVisibilityConverter for 'Keine aktive Session' placeholder
  - Session activity threshold in Settings (15/30/60/120 min)

affects:
  - phase 05 (any phase using MainViewModel or MainView)
  - UI layout (SettingsView additions)

tech-stack:
  added: []
  patterns:
    - CollectionViewSource with IGrouping<string, SessionInfo> for grouped WinUI 3 ComboBox
    - SessionGroup class implementing IGrouping backed by List<T>
    - SubagentDisplayData inner view model class for ItemsRepeater x:DataType binding
    - GroupedSessions property triggers CollectionViewSource.Source update via PropertyChanged in code-behind
    - RefreshSessionList respects SESS-04 (no auto-switch) by preserving session ID on DataUpdated

key-files:
  created:
    - CCInfoWindows/CCInfoWindows/Converters/InvertedBoolToVisibilityConverter.cs
  modified:
    - CCInfoWindows/CCInfoWindows/ViewModels/MainViewModel.cs
    - CCInfoWindows/CCInfoWindows/Views/MainView.xaml
    - CCInfoWindows/CCInfoWindows/Views/MainView.xaml.cs
    - CCInfoWindows/CCInfoWindows/Views/SettingsView.xaml
    - CCInfoWindows/CCInfoWindows/ViewModels/SettingsViewModel.cs
    - CCInfoWindows/CCInfoWindows/App.xaml

key-decisions:
  - "SessionGroup implements IGrouping<string, SessionInfo> backed by List<T> to satisfy CollectionViewSource IsSourceGrouped=true"
  - "GroupedSessions property re-evaluated on DataUpdated; code-behind updates CollectionViewSource.Source on GroupedSessions PropertyChanged"
  - "SubagentDisplayData as public class (not inner) so x:DataType in DataTemplate resolves without namespace issues"
  - "CollectionViewSource x:Name in Page.Resources, source set in code-behind OnLoaded — avoids x:Bind limitation with CollectionViewSource Source binding"
  - "SelectedThresholdIndex maps 0=15, 1=30, 2=60, 3=120 minutes with default=1 (30 min)"

patterns-established:
  - "Grouped ComboBox: CollectionViewSource in Page.Resources + IGrouping data in ViewModel + GroupStyle.HeaderTemplate binding to Key"
  - "InvertedBoolToVisibilityConverter for 'empty state' placeholders (false=Visible)"
  - "SubagentDisplayData: pre-computed display properties in ViewModel, x:DataType in ItemsRepeater DataTemplate"

requirements-completed:
  - SESS-01
  - SESS-02
  - SESS-03
  - SESS-04
  - SESS-05
  - CTXW-01
  - CTXW-02
  - CTXW-03
  - CTXW-04
  - CTXW-05
  - TOKS-01
  - SETT-03

duration: 30min
completed: 2026-03-11
---

# Phase 4 Plan 3: UI Pipeline Wiring Summary

**Session dropdown with Aktiv/Inaktiv grouping, context window progress bar with model badge + subagent bars, token counters, and configurable session timeout threshold wired to JSONL data pipeline**

## Performance

- **Duration:** ~30 min
- **Started:** 2026-03-11T17:40:00Z
- **Completed:** 2026-03-11T18:10:00Z
- **Tasks:** 2 of 3 (Task 3 is human-verify checkpoint, not yet verified)
- **Files modified:** 7

## Accomplishments

- MainViewModel extended with IJsonlService injection, 20+ new observable properties for sessions, context window, tokens
- Session ComboBox with live Aktiv/Inaktiv grouping using CollectionViewSource backed by IGrouping<string, SessionInfo>
- KONTEXTFENSTER section with progress bar, percentage text, model badge chip, autocompact warning, dynamic subagent ItemsRepeater
- TOKENS section with Input/Output counters using K/M compact formatting
- InvertedBoolToVisibilityConverter for "Keine aktive Session" empty-state placeholder
- SettingsView extended with session timeout ComboBox (15/30/60/120 min) bound to SettingsViewModel.SelectedThresholdIndex
- SESS-04 respected: RefreshSessionList preserves current session ID on DataUpdated, never auto-switches away

## Task Commits

Each task was committed atomically:

1. **Task 1: Extend MainViewModel** - `644ac11` (feat)
2. **Task 2: Update MainView.xaml, SettingsView, InvertedBoolToVisibilityConverter** - `5cca06c` (feat)
3. **Task 3: Human-verify checkpoint** - awaiting verification

## Files Created/Modified

- `CCInfoWindows/CCInfoWindows/Converters/InvertedBoolToVisibilityConverter.cs` - New converter (true=Collapsed, false=Visible)
- `CCInfoWindows/CCInfoWindows/ViewModels/MainViewModel.cs` - IJsonlService injection, session/context/token properties, SessionGroup class, SubagentDisplayData class
- `CCInfoWindows/CCInfoWindows/Views/MainView.xaml` - Session ComboBox with GroupStyle, KONTEXTFENSTER, TOKENS sections, CollectionViewSource resource
- `CCInfoWindows/CCInfoWindows/Views/MainView.xaml.cs` - CollectionViewSource.Source wiring in OnLoaded and PropertyChanged handler
- `CCInfoWindows/CCInfoWindows/Views/SettingsView.xaml` - Sitzungs-Timeout ComboBox with 15/30/60/120 min options
- `CCInfoWindows/CCInfoWindows/ViewModels/SettingsViewModel.cs` - SelectedThresholdIndex with persistence and min/index mapping
- `CCInfoWindows/CCInfoWindows/App.xaml` - InvertedBoolToVisibilityConverter registered as StaticResource

## Decisions Made

- CollectionViewSource requires setting Source from code-behind rather than x:Bind because x:Bind with CollectionViewSource Source in Page.Resources has WinUI 3 limitations. Set in OnLoaded and on GroupedSessions PropertyChanged.
- SessionGroup implements IGrouping backed by List<T> rather than custom collection — simplest approach satisfying CollectionViewSource grouped binding.
- SubagentDisplayData declared as public class (not nested) so `x:DataType="viewmodels:SubagentDisplayData"` resolves correctly in the DataTemplate.
- SettingsView gets a separate divider before the dark mode toggle to visually separate threshold config.

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered

None - build succeeded on first attempt, all 145 tests passed unchanged.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- All Phase 4 JSONL pipeline UI features implemented
- Session dropdown, context window, and token counters ready for runtime verification
- Task 3 checkpoint: human must verify all 11 dashboard sections visually before Phase 4 is complete
- After verification, Phase 5 (cost analytics or remaining features) can proceed

---
*Phase: 04-local-data-pipeline*
*Completed: 2026-03-11*
