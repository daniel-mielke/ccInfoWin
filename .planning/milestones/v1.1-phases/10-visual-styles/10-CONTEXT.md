# Phase 10: Visual Styles - Context

**Gathered:** 2026-03-19
**Status:** Ready for planning

<domain>
## Phase Boundary

This phase applies uniform visual styling across the app: progress bar height and track color, ComboBox rounded background matching the Segmented tab bar, model badge pill shapes, chart axis label color matching timer text, and Statistics section label consistency (color, weight, spacing). All changes are XAML-only — no C# logic changes.

</domain>

<decisions>
## Implementation Decisions

### Progress Bar Track
- `ProgressTrackBrush` in AppTheme.xaml updated to `#72808080` (hex approximation of rgba(128,128,128,0.45)) for both Dark and Light themes
- All ProgressBars (including Subagent-Context bars) set to `Height="6"` — uniform height per STYLE-01
- `CornerRadius` stays inline per bar (no global style) — existing values preserved
- `ProgressTrackBrush` change is global in AppTheme.xaml — all ProgressBars inherit automatically

### ComboBox & Segmented Background
- ComboBox `Background="{ThemeResource SegmentedBackgroundBrush}"` — shares same brush as Segmented tab bar
- `CornerRadius="8"` set inline on the ComboBox element — minimal invasive change
- Segmented inline ResourceDictionary overrides remain unchanged (already correct values)

### Statistics Label Styling
- "Total" (StatsTotal): `Foreground="{ThemeResource SecondaryTextBrush}"` and `FontWeight="Normal"` — matches Cache Read/Write visual weight
- "Cost (API equiv.)" (StatsCost): `Foreground="{ThemeResource SecondaryTextBrush}"` and `FontWeight="Normal"` — matches Cache Read/Write
- `Margin="0,8,0,0"` added to StatsTotal TextBlock for consistent vertical spacing before Total row (TEXT-04)

### Model Badge Corner Radius
- All model badge `Border` elements: `CornerRadius="999"` (fully rounded pill shape) per STYLE-04
- Applies to: main context badge (line ~153), subagent badges (line ~196)

### Chart Axis Label Color
- 5-hour chart axis labels rendered via Win2D Canvas in code-behind (UsageChart_Draw)
- Must use same color as timer text — `SecondaryTextBrush` from the current RequestedTheme
- Implementation: read `ActualTheme` in draw method and use theme-appropriate hex values

### Claude's Discretion
- Light theme `ProgressTrackBrush` approximation: `#72808080` is used for both themes (same base color, semi-transparent) — works on both dark/light backgrounds
- Chart color reading: use `CanvasControl.RequestedTheme` or parent `ActualTheme` to select correct color at draw time

</decisions>

<code_context>
## Existing Code Insights

### Reusable Assets
- `AppTheme.xaml` — `ProgressTrackBrush`, `SegmentedBackgroundBrush`, `SecondaryTextBrush` already defined; only values need updating
- `UsageChart_Draw` in MainView.xaml.cs — existing chart draw handler for Win2D canvas, handles axis labels
- Model badge `Border` elements at lines ~151-158 (main context) and ~194-203 (subagent) — both need CornerRadius change

### Established Patterns
- AppTheme.xaml ThemeDictionaries: separate `Dark` and `Light` keys — both must be updated for any color change
- Inline XAML attribute for CornerRadius: used throughout (no centralized style for per-control corner radii)
- `{ThemeResource XxxBrush}` binding pattern — referenced in XAML, resolved at runtime per theme

### Integration Points
- `AppTheme.xaml`: update `ProgressTrackBrush` in both Dark and Light theme sections
- `MainView.xaml`: ComboBox CornerRadius + Background, model badge CornerRadius, Statistics label Foreground/FontWeight/Margin
- `MainView.xaml.cs` (or Views/MainView.xaml code-behind): `UsageChart_Draw` for axis label color

</code_context>

<specifics>
## Specific Ideas

- STYLE-02: rgba(128,128,128,0.45) in hex = `#72808080` — Alpha 0x72 = 114 decimal ≈ 0.45 * 255
- STYLE-03: CornerRadius minimum 8px for both ComboBox and Segmented — use CornerRadius="8" inline on ComboBox
- STYLE-04: CornerRadius=999 is the WinUI 3 convention for fully rounded pills
- STYLE-05: Axis labels must match clock icon / FiveHourCountdown text color (`SecondaryTextBrush`)
- TEXT-02/03: Both Total and Cost move from PrimaryTextBrush/SemiBold to SecondaryTextBrush/Normal

</specifics>

<deferred>
## Deferred Ideas

None — discussion stayed within phase scope.

</deferred>
