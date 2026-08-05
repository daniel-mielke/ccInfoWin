# Phase 9: Layout & Structure - Context

**Gathered:** 2026-03-19
**Status:** Ready for planning

<domain>
## Phase Boundary

This phase restructures the MainView.xaml layout: adds an "Active Session" section header above the dropdown, inserts a separator below the dropdown, moves the Context Window section between Active Session and the 5-Hour Window, moves the footer into the scrollable content area (no longer fixed), and adds a separator between "Models" and "Input" rows in Statistics.
No logic changes — pure XAML structural rearrangement.

</domain>

<decisions>
## Implementation Decisions

### Section Order
- New order: Active Session header + Dropdown → [separator] → Context Window → [separator] → 5-Hour Window → Weekly → Sonnet Weekly → [separator] → Statistics → [separator] → Footer
- Context Window moves UP — currently it appears after Weekly sections, must appear before 5-Hour Window
- Footer moves INTO the ScrollViewer StackPanel (currently in Grid Row="2", fixed)

### Header Styling
- "Active Session" header uses the same style as other section headers: FontSize="11", FontWeight="SemiBold", Foreground="{ThemeResource SectionHeaderBrush}", CharacterSpacing="50"
- Localized via l:Uids.Uid (new key "SectionHeaderActiveSession")

### Separators
- Use existing `<Border Height="1" Background="{ThemeResource DividerBrush}" />` pattern
- New separators needed:
  1. Below Active Session dropdown (before Context Window)
  2. Above footer (inside ScrollViewer, after Statistics section)
  The existing separator between Context Window and Statistics (currently "Divider before STATISTIKEN") stays

### Footer Behavior
- Footer StackPanel moves from Grid Row="2" into the ScrollViewer's StackPanel as the last child
- Add a separator Border above the footer buttons
- Remove Grid Row="2" from the main Grid (collapse to 2-row Grid: Auto,*)
- Padding on the footer StackPanel stays as-is (0,8,0,4)

### Statistics Separator (LAYOUT-06)
- A separator between Row 0 (Modelle) and Row 1 (Eingabe) in the statistics Grid
- Insert as a new row in the Grid between existing Row 0 and Row 1
- All subsequent row indices shift by +1

### Padding (LAYOUT-01)
- Main Grid currently has Padding="16,12,16,12" — vertical padding (12) is less than horizontal (16)
- Change to Padding="16,16,16,16" to equalize all sides

### Claude's Discretion
- Exact separator margin values: use Margin="0,4,0,0" above footer separator to give breathing room
- Statistics grid row separator: use Margin="0,2,0,2" consistent with existing Row 5 divider style

</decisions>

<code_context>
## Existing Code Insights

### Reusable Assets
- `<Border Height="1" Background="{ThemeResource DividerBrush}" />` — existing separator pattern, used in 4 places already
- Section header pattern: `FontSize="11" FontWeight="SemiBold" Foreground="{ThemeResource SectionHeaderBrush}" CharacterSpacing="50"`
- l:Uids.Uid localization pattern — existing keys in Strings/en-us/Resources.resw and de-de/Resources.resw

### Established Patterns
- Grid with RowDefinitions="Auto,*,Auto" for header/content/footer layout — currently 3 rows
- ScrollViewer with VerticalScrollBarVisibility="Auto" wraps all content in Row="1"
- StackPanel Spacing="16" is the main content container
- Context Window section is already implemented (lines 312-409), just needs repositioning

### Integration Points
- Localization files: `CCInfoWindows/CCInfoWindows/Strings/en-us/Resources.resw` and `de-de/Resources.resw`
  — Need new key "SectionHeaderActiveSession" with values "ACTIVE SESSION" (EN) and "AKTIVE SITZUNG" (DE)
- MainView.xaml.cs: no code-behind changes needed (pure XAML)
- Grid RowDefinitions: change from "Auto,*,Auto" to "Auto,*" after footer moves into scroll content

</code_context>

<specifics>
## Specific Ideas

- The "Active Session" label must be localized — add to both .resw files
- Footer separator should visually match the other section dividers
- The Context Window section block (lines 309-409 in current XAML) moves entirely as-is to appear after the Active Session separator and before the 5-Hour Window section

</specifics>

<deferred>
## Deferred Ideas

None — discussion stayed within phase scope.

</deferred>
