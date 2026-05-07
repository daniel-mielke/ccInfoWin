---
phase: 22
slug: ui-polish
status: draft
shadcn_initialized: false
preset: none
created: 2026-05-06
---

# Phase 22 — UI Design Contract

> Surgical visual delta on three disjoint code paths in an existing WinUI 3 view.
> Design system is LOCKED by `spec/v1.7.1-macOS/ccinfo-styleguide.md`.
> No new tokens, no new components, no new visual primitives.

---

## Scope Summary

| Polish | Visual Delta | New XAML Elements |
|--------|--------------|-------------------|
| 1. Anti-flicker refresh spinner | Existing `FontIcon` + rotation storyboard stays visible for >= 250 ms; Button disables while `IsRefreshing` | None — D-01 rejects ProgressRing replacement; existing v1.1 storyboard IS the spinner |
| 2. Inactive-session ComboBox tooltip | Two-line tooltip on inactive items (path + "Inactive for > {N}min"); single-line path on active items | `ToolTipService.ToolTip` on existing `TextBlock` in `ComboBox.ItemTemplate` |
| 3. About-tab pricing-timestamp DispatcherTimer | "X minutes ago" rebinds every 60 s while About tab active | None — existing `TextBlock`, new `DispatcherTimer` in ViewModel |

**Phase 22 introduces ZERO new visual primitives.** All deltas reuse existing styles, brushes, fonts, spacing, and converters. This UI-SPEC documents how the interaction contracts compose against the locked design system — not new design tokens.

---

## Design System

| Property | Value |
|----------|-------|
| Tool | none (manual WinUI 3 ResourceDictionary; locked v1.7.1 styleguide) |
| Preset | not applicable |
| Component library | WinUI 3 (Windows App SDK 1.8) |
| Icon library | Segoe Fluent Icons (system font glyphs) |
| Font | Segoe UI Variable (Windows 11 system font) |
| Source of truth | `spec/v1.7.1-macOS/ccinfo-styleguide.md` |

---

## Spacing Scale

Inherited from styleguide; no Phase 22 additions.

| Token | Value | Usage |
|-------|-------|-------|
| xs | 4px | Inline icon gaps, tight stack spacing |
| sm | 8px | Compact element spacing, button padding, badge corner radius |
| md | 12px | Window vertical padding, section spacing |
| lg | 16px | Window horizontal padding, footer toolbar spacing |
| xl | 24px | Reserved for major section breaks |
| 2xl | 32px | Tab-bar height (Statistics) |

Phase 22 spacing rules:

- Two-line tooltip line-spacing: native `LineBreak` flow (default `LineHeight`) — no custom spacing.
- Footer refresh button hit-target: existing 32px (16px icon + 8px padding all sides). Unchanged.

Exceptions: none.

---

## Typography

Inherited from styleguide §3.2; no Phase 22 additions. Phase 22 reuses three existing roles only:

| Role | Size | Weight | Line Height | Used By |
|------|------|--------|-------------|---------|
| Footer button label | 13px | Regular (400) | default | Refresh button (unchanged) |
| Dropdown text | 14px | Regular (400) | default | Session ComboBox items (unchanged) |
| Tooltip text | system default | Regular (400) | default | New two-line inactive-session tooltip |

Tooltip typography rule:

- The two-line tooltip uses the WinUI 3 `ToolTip` default text style. No custom `FontSize`, no custom `FontWeight`, no custom `LineHeight` overrides.
- Both lines render with identical typography; the only differentiator is line position (line 1 = path, line 2 = threshold message).

About-tab pricing-timestamp typography:

- Existing `TextBlock` style (already in `SettingsView.xaml`, About tab). Unchanged. Only the bound `LastFetchRelativeTime` string content updates on each `DispatcherTimer.Tick`.

---

## Color

Inherited from styleguide §4. No Phase 22 additions. The three polish tweaks introduce ZERO new color tokens.

| Role | Value (Dark) | Value (Light) | Usage |
|------|--------------|---------------|-------|
| Dominant (60%) | `#1E1E1E` | `#F5F5F5` | App background — unchanged |
| Secondary (30%) | `#2C2C2E` | `#FFFFFF` | ComboBox surface, chart container — unchanged |
| Accent (10%) | `#007AFF` | `#007AFF` | ComboBox chevron, selection — unchanged |
| Disabled state | WinUI 3 system default | WinUI 3 system default | Refresh button while `IsRefreshing == true` |

Accent reserved for: ComboBox chevron only. Phase 22 does NOT add accent to the refresh button, the spinner, or the tooltip.

Disabled-state contract for Polish 1:

- Refresh button uses the WinUI 3 default `ButtonDisabledBackgroundThemeBrush` / `ButtonDisabledForegroundThemeBrush` resource — no custom disabled brush.
- The rotating `FontIcon` glyph keeps its `SecondaryTextBrush` foreground while spinning; the `Button.IsEnabled = false` state does NOT recolor the glyph. Visual signal of "refresh in progress" is rotation, not color change.

Tooltip color contract for Polish 2:

- Default WinUI 3 `ToolTip` background and foreground brushes. No custom theming. Both active and inactive tooltips render with identical chrome.

---

## Copywriting Contract

Phase 22 references one new and zero existing localization keys. Strings are authored in Phase 23 (`Strings/de-DE/Resources.resw`, `Strings/en-US/Resources.resw`). Phase 22 references the key by name; if Phase 22 ships before Phase 23, `Localizer.Get().GetLocalizedString("InactiveSessionTooltip")` returns the key name itself — visible degradation, not crash.

| Element | Locale | Copy / Source |
|---------|--------|---------------|
| Refresh button label (existing) | DE | `FooterRefreshButton.Content` resw key — unchanged |
| Refresh button label (existing) | EN | `FooterRefreshButton.Content` resw key — unchanged |
| Refresh button tooltip (existing) | DE/EN | `ToolTipService.ToolTip="Refresh"` literal — unchanged in Phase 22 |
| Active-session tooltip | DE/EN | `SessionInfo.Cwd` (filesystem path, not localized) — unchanged |
| Inactive-session tooltip line 1 | DE/EN | `SessionInfo.Cwd` (filesystem path, not localized) |
| Inactive-session tooltip line 2 | DE | `Inaktiv seit > {0}min` — Phase 23 authors `InactiveSessionTooltip` key |
| Inactive-session tooltip line 2 | EN | `Inactive for > {0}min` — Phase 23 authors `InactiveSessionTooltip` key |
| About-tab pricing timestamp (existing) | DE/EN | Existing relative-time formatter — content rebinds on `DispatcherTimer.Tick`, no new copy |

Localization key reference table (consumed by Phase 22, authored by Phase 23):

| Key | DE | EN | Owner Phase |
|-----|----|----|-------------|
| `InactiveSessionTooltip` | `Inaktiv seit > {0}min` | `Inactive for > {0}min` | Phase 23 (POLISH-04 references it) |

No primary CTA, no empty state, no error state, no destructive confirmation introduced by Phase 22. All existing copy is preserved unchanged.

---

## Component & Interaction Contract

This section is the heart of Phase 22's UI-SPEC. It locks the three interaction contracts referenced by the orchestrator brief.

### Polish 1 — Anti-flicker Refresh Spinner

**Visual primitive choice:** Existing v1.1 `FontIcon Glyph="&#xE895;"` + `RotateTransform` (`RefreshIconTransform`) + `SpinnerStoryboard`. NO `ProgressRing` element. NO new XAML control. Rationale: D-01 in `22-CONTEXT.md` (the existing rotation animation is the spinner per PROJECT.md "Validated" lock; replacement would discard `_stopOnComplete` mechanism).

**Replacement strategy:** None. The contract is "rotating refresh indicator visible during refresh", and the existing `FontIcon` + storyboard already satisfies it. No glyph swap, no simultaneous render with one collapsed.

| Property | Value |
|----------|-------|
| Visual element | Existing `FontIcon` at `MainView.xaml:611` (`Glyph="&#xE895;"`, FontSize 16, Foreground `SecondaryTextBrush`) |
| Rotation source | Existing `SpinnerStoryboard` at `MainView.xaml:18-24` (1s linear, From=0 To=360) |
| Animation control mechanism | Existing `_stopOnComplete` flag in `MainView.xaml.cs:29, 167-192`. Phase 22 does NOT modify. |
| Trigger property | `MainViewModel.IsRefreshing` (existing `[ObservableProperty]`) |
| PropertyChanged listener | Existing `OnViewModelPropertyChanged` in `MainView.xaml.cs:179-192`. Phase 22 does NOT modify. |
| Minimum-display floor | 250 ms (named constant `MinimumSpinnerDisplayMs`) |
| Floor scope | Manual `[RelayCommand] Refresh()` only — auto-poll path is NOT floored (D-02). |
| Floor mechanism | `Task.WhenAll(PollUsageCoreAsync(), Task.Delay(TimeSpan.FromMilliseconds(MinimumSpinnerDisplayMs)))` inside the manual command, after a refactor that extracts `PollUsageCoreAsync` from `PollUsageAsync` (D-03). |
| Disabled-while-refreshing mechanism | `[RelayCommand(CanExecute = nameof(CanRefresh))]` + `[NotifyCanExecuteChangedFor(nameof(RefreshCommand))]` on `_isRefreshing` (D-04 Option A — recommended). XAML unchanged. |
| Disabled visual | WinUI 3 `Button.IsEnabled=false` default styling — no custom override |

**Visible UI delta:**

- Manual click on a sub-100 ms cached refresh: spinner now visibly rotates for >= 250 ms instead of flashing once and stopping.
- During refresh: button shows pressed/disabled state (system default); second click is suppressed by `Command.CanExecute`.
- Auto-poll (every 30 s): unchanged — spinner duration matches actual API latency, no artificial floor.

**Out of scope:**

- Replacing `FontIcon` with `ProgressRing` (D-01 — REJECTED, FEAT-09c rejected).
- Recoloring the spinner glyph (uses `SecondaryTextBrush` unchanged).
- Adding a tooltip to communicate the disabled state (existing `ToolTipService.ToolTip="Refresh"` is sufficient).

### Polish 2 — Inactive-Session Tooltip

**Tooltip composition strategy:** `TextBlock` with explicit `LineBreak` inline element, attached via `ToolTipService.ToolTip` to the existing `TextBlock` inside `ComboBox.ItemTemplate` (`MainView.xaml:104-107`).

Rationale: a single `TextBlock` with `\n` in the bound string OR a `LineBreak` inline produces the cleanest two-line layout without introducing a `Grid`, a nested `StackPanel`, or new style resources. The `TooltipText` property on `SessionDisplayItem` (D-05) carries the pre-composed `"{cwd}\n{template}"` string; the `TextBlock`'s default behavior renders `\n` as a line break.

**Decision: use `\n` in `TooltipText`, render with default `TextBlock.TextWrapping="Wrap"` and `LineHeight` default.** No `Grid`, no second `TextBlock`, no inline `LineBreak`.

| Property | Value |
|----------|-------|
| Active-session tooltip content | `SessionInfo.Cwd` only (single line, plain string) |
| Inactive-session tooltip content | `$"{Cwd}\n{string.Format(InactiveSessionTooltip, threshold)}"` (two-line, embedded `\n`) |
| Tooltip carrier property | `SessionDisplayItem.TooltipText` (new `required string`, computed by `MainViewModel.ComputeTooltipText`) |
| XAML attachment point | `ToolTipService.ToolTip="{Binding TooltipText}"` on the existing `TextBlock` inside `ComboBox.ItemTemplate` |
| Tooltip styling | WinUI 3 default `ToolTip` chrome — no `Style` resource, no custom brushes |
| Threshold value source | `_settingsService.LoadSettings().SessionTimeoutMinutes` at `SortedSessions` rebuild time |
| Recompute trigger | `SessionTimeoutChangedMessage` (new) sent from `SettingsViewModel`, received by `MainViewModel.Receive(...)` → `RefreshSessionsAsync()` |
| Inactive-item visibility | `.Where(s => s.IsActive(threshold))` filter is REMOVED from `RefreshSessionsAsync` (D-06) — inactive sessions now appear in ComboBox |
| Ordering | Existing `OrderByDescending(s => s.LastActivity)` — active sessions stay at top, inactive trail. No secondary `ThenBy`. |
| `IsActive` per-item | Computed from `s.IsActive(threshold)` per item (replaces hardcoded `IsActive = true` bug) |

**XAML delta (single line addition):**

```xml
<DataTemplate x:DataType="viewmodels:SessionDisplayItem">
    <TextBlock Text="{x:Bind DisplayName}"
               ToolTipService.ToolTip="{x:Bind TooltipText}"
               VerticalAlignment="Center" />
</DataTemplate>
```

**Visible UI delta:**

- ComboBox now contains both active and inactive sessions (was active-only).
- Hovering an active item: tooltip shows path on a single line (existing behavior preserved when active items had no tooltip — Phase 22 introduces single-line path tooltip on active items per POLISH-05).
- Hovering an inactive item: tooltip shows path on line 1, "Inactive for > {N}min" on line 2.
- Changing `SessionTimeoutMinutes` in Settings: next ComboBox open shows updated threshold without app restart.

**Out of scope:**

- Custom tooltip popup chrome (background, border, shadow). WinUI 3 default only.
- Visual differentiation of inactive items in the ComboBox list itself (e.g. greyed-out text). Tooltip is the ONLY signal in Phase 22.
- Tooltip on the ComboBox header (`SelectedItem` display). Phase 22 covers `ItemTemplate` only.

### Polish 3 — About-Tab Pricing-Timestamp DispatcherTimer

**Lifecycle hook strategy:** Three view-lifecycle event handlers in `SettingsView.xaml.cs` route to two ViewModel methods (`StartAboutTimestampTimer` / `StopAboutTimestampTimer`). The timer is owned by `SettingsViewModel` (D-09); `SettingsView.xaml.cs` only routes events.

| Event | Handler | ViewModel Action | Rationale |
|-------|---------|------------------|-----------|
| `Page.Loaded` | `OnLoaded` (existing — extend) | If `TabsSegmented.SelectedIndex == AboutTabIndex` then `StartAboutTimestampTimer()` else no-op | Covers the rare case where Settings opens with About pre-selected (e.g. via persistence) |
| `Segmented.SelectionChanged` | `OnSegmentedSelectionChanged` (NEW) | If new index == `AboutTabIndex` then `Start`; else `Stop` | Timer only ticks while About is the active tab (POLISH-07) |
| `Page.Unloaded` | `OnUnloaded` (NEW) | Always `StopAboutTimestampTimer()` | Belt-and-suspenders against memory leak (POLISH-08) |

| Property | Value |
|----------|-------|
| Timer type | `Microsoft.UI.Xaml.DispatcherTimer` (UI-thread-bound by default) |
| Timer interval | `TimeSpan.FromMinutes(1)` |
| Timer field | `_aboutTimestampTimer` (private, nullable) on `SettingsViewModel` |
| Tick handler | `(_, _) => OnPropertyChanged(nameof(LastFetchRelativeTime))` |
| Bound property | `LastFetchRelativeTime` (computed `string`, no backing field, no `[ObservableProperty]`) |
| Source data | `_pricingService.LastFetch` (existing or planner-added read-only property) |
| Format helper | Existing relative-time formatter if present; otherwise new `RelativeTimeFormatter.Format(DateTimeOffset)` returning localized "X minutes ago" / "vor X Minuten" |
| `AboutTabIndex` constant | `3` (named constant in `SettingsView.xaml.cs` Code-Behind; tab order: 0=General, 1=Updates, 2=Account, 3=About) |
| XAML wiring | `Page.Unloaded="OnUnloaded"`, `Segmented.SelectionChanged="OnSegmentedSelectionChanged"` on existing `TabsSegmented` |

**Visible UI delta:**

- About tab open, "X minutes ago" text now updates live every 60 s without manual refresh or tab toggle.
- Switching to General/Updates/Account tab: timer stops; About-tab text remains frozen at last tick (next visit re-starts timer).
- Closing Settings page: timer stops, no orphaned ticks.

**Out of scope:**

- Sub-minute granularity (60 s interval is the contract).
- Animating the timestamp text on tick (no fade/slide).
- Restarting the timer on app focus / window activation. Tab activation is the only trigger.

---

## Reused Converters & Resources

Phase 22 explicitly REUSES (does not introduce):

| Asset | Path | Phase 22 Use |
|-------|------|--------------|
| `BoolToVisibilityConverter` | `Converters/BoolToVisibilityConverter.cs` | Existing — no new consumer in Phase 22 |
| `InvertedBoolToVisibilityConverter` | `Converters/InvertedBoolToVisibilityConverter.cs` | Existing — no new consumer in Phase 22 |
| `SectionHeaderBrush` / `SecondaryTextBrush` / `PrimaryTextBrush` | `Themes/Colors.xaml` | Existing — referenced by all touched XAML elements |
| `SegmentedBackgroundBrush` | `Themes/Colors.xaml` | Existing — ComboBox background |
| `SpinnerStoryboard` + `RefreshIconTransform` | `Views/MainView.xaml:18-24, 615` | Existing v1.1 — Phase 22 preserves untouched |
| WinUI 3 default `ToolTip` style | system | Default tooltip chrome |
| WinUI 3 default `ButtonDisabled*` brushes | system | Disabled-state visual |

Phase 22 does NOT introduce:

- New brushes
- New converters (D-04 Option A removes the need for `InvertedBooleanConverter`)
- New `Style` resources
- New `ResourceDictionary` entries
- New icon glyphs
- New animation primitives

---

## Registry Safety

Not applicable. WinUI 3 / Windows App SDK 1.8 / no shadcn / no third-party UI registries. All controls used by Phase 22 (`Button`, `FontIcon`, `ComboBox`, `TextBlock`, `ToolTipService`, `DispatcherTimer`, `Segmented`) are first-party Microsoft surfaces.

| Registry | Blocks Used | Safety Gate |
|----------|-------------|-------------|
| WinUI 3 / Windows App SDK 1.8 | Button, FontIcon, ComboBox, TextBlock, ToolTipService, DispatcherTimer | not required (first-party) |
| CommunityToolkit.WinUI.Controls (Segmented) | Segmented (existing from Phase 18) | not required (existing dependency, no new block) |
| CommunityToolkit.Mvvm 8.4 | `[RelayCommand(CanExecute=...)]`, `[NotifyCanExecuteChangedFor]`, `WeakReferenceMessenger` | not required (existing dependency) |
| WinUI3Localizer | `Localizer.Get().GetLocalizedString("InactiveSessionTooltip")` | not required (existing dependency, key authored by Phase 23) |

---

## Acceptance Mapping

| Requirement | Contract Section | Verification |
|-------------|------------------|--------------|
| POLISH-01 | Polish 1 — Anti-flicker Refresh Spinner | D-01 reinterprets "ProgressRing in place of glyph" as the existing rotating `FontIcon`; visual delta = zero |
| POLISH-02 | Polish 1 — minimum-display floor | Manual `Refresh()` wraps work in `Task.WhenAll(work, Task.Delay(250ms))` |
| POLISH-03 | Polish 1 — disabled-while-refreshing | `[RelayCommand(CanExecute)]` + `[NotifyCanExecuteChangedFor]` |
| POLISH-04 | Polish 2 — two-line inactive tooltip | `TooltipText` = `$"{Cwd}\n{template}"`, `\n` rendered by default `TextBlock` |
| POLISH-05 | Polish 2 — single-line active tooltip | `TooltipText` = `Cwd` only when `IsActive == true` |
| POLISH-06 | Polish 2 — recompute on threshold change | `SessionTimeoutChangedMessage` triggers `RefreshSessionsAsync()` |
| POLISH-07 | Polish 3 — DispatcherTimer ticks every minute on About tab | `_aboutTimestampTimer.Interval = TimeSpan.FromMinutes(1)`, started by `OnSegmentedSelectionChanged` when index == 3 |
| POLISH-08 | Polish 3 — timer stops on tab switch and Unloaded | `OnSegmentedSelectionChanged` stops on non-About index; `OnUnloaded` always stops |

---

## Pre-Populated From

| Source | Decisions Used |
|--------|----------------|
| `22-CONTEXT.md` Decisions D-01..D-11 | All eleven implementation decisions used as authoritative |
| `spec/v1.7.1-macOS/ccinfo-styleguide.md` | All design tokens (color, typography, spacing) inherited unchanged |
| `.planning/milestones/v1.4-REQUIREMENTS.md` POLISH-01..POLISH-08 | All eight acceptance criteria mapped |
| `.planning/ROADMAP.md` Phase 22 success criteria | Five success criteria mapped |
| Existing `MainView.xaml` (lines 18-24, 96-109, 600-627) | Reused; not redesigned |
| Existing `MainViewModel.cs` (`IsRefreshing`, `PollUsageAsync`, `RefreshCommand`, `SessionDisplayItem`) | Reused per D-03..D-08 |
| Existing `SettingsView.xaml.cs` `OnLoaded` + `ApplyTabTooltips` pattern | Reused per D-10 |

User input during this session: zero new questions. Orchestrator brief delivered all locked decisions.

---

## Checker Sign-Off

- [ ] Dimension 1 Copywriting: PASS
- [ ] Dimension 2 Visuals: PASS
- [ ] Dimension 3 Color: PASS
- [ ] Dimension 4 Typography: PASS
- [ ] Dimension 5 Spacing: PASS
- [ ] Dimension 6 Registry Safety: PASS

**Approval:** pending
