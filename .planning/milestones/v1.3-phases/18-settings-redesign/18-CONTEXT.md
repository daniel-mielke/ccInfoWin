# Phase 18: Settings Redesign - Context

**Gathered:** 2026-04-13
**Status:** Ready for planning

<domain>
## Phase Boundary

Replace the flat single-page Settings layout with a Segmented Control at the top (4 tabs: General, Updates, Account, About). Each tab shows its content below the control. Fixed 360px width. All settings reorganized into uniform 40px rows with short time notation. Full localization (DE/EN).

</domain>

<decisions>
## Implementation Decisions

### Tab Structure & Content
- Tab order: General, Updates, Account, About — per macOS Spec FEAT-03a
- General tab: all existing settings (refresh interval, session timeout, theme, language, Sonnet context, autostart) in uniform 40px rows
- Updates tab: app version, pricing source info, last pricing fetch timestamp
- Account tab: token status and logout button
- About tab: app name, version, GitHub link, macOS original credits

### Segmented Control Implementation
- Use `CommunityToolkit.WinUI.Controls.Segmented` — already installed in project
- Content switching via Visibility toggle on 4 StackPanels — no Frame navigation needed
- Colored icon badges per tab (FEAT-03c) — inline DataTemplate with colored FontIcon
- Badge colors: Green (General), Blue (Updates), Red (Account), Orange (About) — new theme brushes in AppTheme.xaml

### Layout & Localization
- Fixed 360px width for settings area
- Uniform 40px height rows: label left-aligned, control right-aligned
- Short time notation: "30s", "1min", "5min" etc. — replaces current "30 Sekunden" long format
- All new tab labels and content use `l:Uids.Uid` for runtime DE/EN switching

</decisions>

<code_context>
## Existing Code Insights

### Reusable Assets
- `SettingsView.xaml` — current flat layout with all settings controls
- `SettingsViewModel.cs` — RefreshOptions, ThresholdIndex, Language, SonnetContext, DarkMode, Autostart, Logout
- `ISettingsService` / `IPricingService` — already injected in ViewModel
- `AppTheme.xaml` — theme resource dictionary for brushes
- `l:Uids.Uid` pattern — established runtime localization

### Established Patterns
- `[ObservableProperty]` for bindable state, `[RelayCommand]` for actions
- `BoolToVisibilityConverter` for conditional UI display
- `RefreshOption` record with Label + Seconds
- ComboBox with `ItemsSource` binding and `SelectedItem`/`SelectedIndex`

### Integration Points
- `SettingsView.xaml` — complete XAML rewrite with Segmented Control
- `SettingsViewModel.cs` — add SelectedTabIndex property, add PricingSourceText/LastFetchText
- `AppTheme.xaml` — add 4 SettingsBadge*Brush theme resources
- `de-DE/Resources.resw` + `en-US/Resources.resw` — new tab and content localization keys

</code_context>

<specifics>
## Specific Ideas

- macOS reference spec `spec/v1.10.0-macOS/spec-release-1.8.3-to-1.10.0.md` FEAT-03a/b/c has detailed tab structure, badge colors, layout diagrams
- Segmented Control should have `SelectedIndex="{x:Bind ViewModel.SelectedTabIndex, Mode=TwoWay}"`
- Badge colors from spec: Green #30D158/#34C759, Blue #0A84FF/#007AFF, Red #FF453A/#FF3B30, Orange #FF9F0A/#FF9500
- Short time format options: "30s", "1min", "2min", "5min", "10min", "Manuell"/"Manual"
- SettingsViewModel already has `PricingSourceText` property — extend with last fetch timestamp

</specifics>

<deferred>
## Deferred Ideas

None — discussion stayed within phase scope.

</deferred>
