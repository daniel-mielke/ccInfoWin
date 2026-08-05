# Phase 11: Behavior & Interaction - Context

**Gathered:** 2026-03-19
**Status:** Ready for planning

<domain>
## Phase Boundary

This phase implements behavioral fixes: timer format for ≥24h durations, logout and login button visual upgrades with icons, and smooth refresh animation that always completes a full rotation before stopping. Changes span C# (CountdownFormatter, MainView.xaml.cs) and XAML (SettingsView.xaml, MainView.xaml).

</domain>

<decisions>
## Implementation Decisions

### Timer Format ≥24h (TEXT-01)
- `CountdownFormatter.FormatCountdown()` extended: when `TotalHours >= 24`, return `"{days}d {hours}h"` format
- No localization — always English `d` for days, regardless of system language
- `hours` in the output = `remaining.Hours` (the hours component only, not total hours)
- Existing format stays for <24h: `"{hours}h {minutes}min"` or `"{minutes}min"`
- No ViewModel changes needed — the helper change propagates automatically

### Logout Button (INTER-01)
- Location: `SettingsView.xaml` — Button at Grid.Row="2" with `LogoutCommand`
- Remove: `Style="{ThemeResource AccentButtonStyle}"` (currently blue)
- Add: `Background="{ThemeResource ProgressRedBrush}"` and `Foreground="White"`
- Content: Replace l:Uids.Uid content with a StackPanel containing FontIcon + TextBlock
- Icon: FontIcon Glyph="&#xE8FB;" (ChevronRightEnd/logout-like arrow) left of label text
- Label text: use localized string from Resources.resw (existing key or new key)
- CornerRadius: inherit from Button default

### Login Icon (INTER-02)
- Location: `MainView.xaml` — `ReLoginButton` inside InfoBar.ActionButton (line ~65)
- Button has `l:Uids.Uid="ReLoginButton"` which sets the Content via localization
- Change: Add explicit Content with StackPanel (FontIcon + TextBlock) instead of relying on Uid for content
- Icon: FontIcon Glyph="&#xE77B;" (Sign in icon) left of label text
- Label: keep existing localized text ("Log in" / "Anmelden") — read from Resources.resw

### Refresh Animation Smooth Stop (INTER-03)
- Current behavior: `SpinnerStoryboard.Stop()` called immediately when `IsRefreshing=false` — snaps
- Target: animation always completes current full rotation before stopping
- Implementation in `MainView.xaml.cs`:
  - Add `bool _stopOnComplete` field
  - Wire `SpinnerStoryboard.Completed` event handler once (in page load or constructor)
  - In `OnViewModelPropertyChanged`: when `IsRefreshing=false`, set `_stopOnComplete = true` instead of calling `.Stop()` directly
  - In `Completed` handler: if `_stopOnComplete == true`, call `SpinnerStoryboard.Stop()` and reset `_stopOnComplete = false`
  - When `IsRefreshing=true`: call `SpinnerStoryboard.Begin()` as before; if `_stopOnComplete` was set, clear it
- Storyboard already has `RepeatBehavior="Forever"` and `Duration="0:0:1"` (1 second per rotation) — no XAML change needed

### Claude's Discretion
- Logout button CornerRadius: inherit default (no explicit value)
- Exact StackPanel Spacing for icon+text in buttons: use Spacing="8"
- Orientation: Horizontal for all icon+text StackPanels

</decisions>

<code_context>
## Existing Code Insights

### Reusable Assets
- `CountdownFormatter.cs` — static class with `FormatCountdown(DateTimeOffset?)`, returns string; located at `CCInfoWindows/CCInfoWindows/Helpers/CountdownFormatter.cs`
- `SpinnerStoryboard` — defined in `MainView.xaml` Page.Resources with `RepeatBehavior="Forever"`, targets `RefreshIconTransform.Angle` 0→360, Duration 1s
- `OnViewModelPropertyChanged` in `MainView.xaml.cs` — already handles `IsRefreshing` property change, calls `SpinnerStoryboard.Begin()` / `.Stop()`
- `ProgressRedBrush` — defined in AppTheme.xaml, Dark=#FF453A, Light=#FF3B30

### Established Patterns
- Button content via l:Uids.Uid: sets `Content` property from .resw file — to override with icon+text, must use explicit `Content` child instead
- StackPanel with FontIcon + TextBlock: used in MainView.xaml footer buttons (FontIcon + transparent background buttons)
- `_stopOnComplete` flag pattern: WinUI 3 Storyboard doesn't natively support "stop after current iteration" — flag + Completed handler is the idiomatic approach

### Integration Points
- `CountdownFormatter.cs`: consumed by MainViewModel.cs lines 413, 422, 434, 443, 456, 465, 518-520
- `ReLoginButton` in `MainView.xaml` line ~65: inside `InfoBar.ActionButton` block
- `SettingsLogoutButton` in `SettingsView.xaml` line ~147: inside Grid.Row="2", currently has AccentButtonStyle
- `MainView.xaml.cs` `OnViewModelPropertyChanged`: lines 314-336

</code_context>

<specifics>
## Specific Ideas

- TEXT-01: The ≥24h branch uses integer division: `days = (int)remaining.TotalDays`, `hours = remaining.Hours` (not TotalHours)
- INTER-03: The Completed event fires once per full rotation (since Duration="0:0:1" and RepeatBehavior="Forever" — Completed fires each cycle). Wire the handler defensively (unsubscribe before subscribing to avoid double-registration)
- INTER-01: `ProgressRedBrush` is the correct resource for the error/100% color — consistent with requirement spec

</specifics>

<deferred>
## Deferred Ideas

None — discussion stayed within phase scope.

</deferred>
