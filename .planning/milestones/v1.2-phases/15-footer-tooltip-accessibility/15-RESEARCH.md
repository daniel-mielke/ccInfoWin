# Phase 15: Footer Tooltip & Accessibility - Research

**Researched:** 2026-04-12
**Domain:** WinUI 3 XAML — ToolTipService, AutomationProperties, WinUI3Localizer Uid pattern
**Confidence:** HIGH

## Summary

Phase 15 adds localized tooltips and `AutomationProperties.Name` to the three footer buttons (Refresh, Settings, Quit). The code investigation reveals the implementation is already complete in the resw files — both `Strings/en-US/Resources.resw` and `Strings/de-DE/Resources.resw` already contain all six required entries for tooltip and automation properties on all three buttons. The XAML already has `l:Uids.Uid` set on each footer button matching these keys.

The WinUI3Localizer reads attached properties from the resw using the long-form namespace syntax: `ControlUid.[using:Namespace]Property.SubProperty = Value`. This is the identical pattern already used for `ExportButton`, `SessionComboBox`, and `SettingsBackButton` — all confirmed working in production. No new pattern needs to be introduced.

**Primary recommendation:** Verify the existing resw entries are correctly wired and the phase is effectively done — the planner needs one verification task: build the project and confirm the tooltip behavior at runtime. No code changes are expected to be needed.

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions
None — discuss phase was skipped (auto-generated trivial phase).

### Claude's Discretion
All implementation choices are at Claude's discretion — trivial phase with clear spec. Use ROADMAP phase goal, success criteria, and codebase conventions to guide decisions.

Tooltip values:
| Button | de-DE | en-US | AutomationProperties.Name |
|--------|-------|-------|--------------------------|
| Refresh | Aktualisieren | Refresh | Refresh |
| Settings | Einstellungen | Settings | Settings |
| Quit | Beenden | Quit | Quit |

Use `l:Uids.Uid` for tooltips (runtime language switch pattern) and `AutomationProperties.Name` directly on the button.

### Deferred Ideas (OUT OF SCOPE)
None.
</user_constraints>

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|------------------|
| ACC-01 | User sees localized tooltip when hovering each footer button (Refresh, Settings, Quit) | Both resw files already contain `FooterRefreshButton.[using:Microsoft.UI.Xaml.Controls]ToolTipService.ToolTip`, `FooterSettingsButton...`, `FooterQuitButton...` entries. XAML already has matching `l:Uids.Uid` on buttons. |
| ACC-02 | User's screen reader announces button purpose via AutomationProperties.Name | Both resw files already contain `FooterRefreshButton.[using:Microsoft.UI.Xaml.Automation]AutomationProperties.Name`, etc. WinUI3Localizer injects these at runtime. |
| ACC-03 | User sees tooltips in the correct language matching the current app language setting | WinUI3Localizer's `l:Uids.Uid` runtime-switches all attached properties when language changes — same mechanism already working for 30+ other strings in the app. |
</phase_requirements>

## Standard Stack

### Core
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| WinUI3Localizer | (project-existing) | `l:Uids.Uid` runtime localization | Already integrated; handles ToolTipService.ToolTip and AutomationProperties.Name via resw property-path syntax |
| WinUI 3 `ToolTipService` | Windows App SDK 1.8 | Hover tooltip display | Built-in attached property; no extra package |
| WinUI 3 `AutomationProperties` | Windows App SDK 1.8 | Screen reader accessibility name | Built-in attached property; UIA standard |

### Supporting
No additional libraries needed.

### Alternatives Considered
| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| `l:Uids.Uid` on button (drives all attached properties from resw) | Hardcoded `ToolTipService.ToolTip="Refresh"` + separate `AutomationProperties.Name="Refresh"` in XAML | Hardcoding bypasses runtime language switch — ACC-03 would fail |
| `l:Uids.Uid` | Separate Uid element wrapping a `ToolTip` control | More complex; unnecessary for simple string tooltips |

## Architecture Patterns

### WinUI3Localizer Uid Property-Path Syntax (CONFIRMED PATTERN)

The project uses WinUI3Localizer's extended property-path syntax in resw files to set attached properties on controls identified by `l:Uids.Uid`. The pattern is:

```
{UidValue}.[using:{Namespace}]{ClassName}.{PropertyName} = {LocalizedValue}
```

**Confirmed working examples already in this codebase:**

For `ToolTipService.ToolTip` (from `en-US/Resources.resw`):
```xml
<!-- resw entry: -->
FooterRefreshButton.[using:Microsoft.UI.Xaml.Controls]ToolTipService.ToolTip = Refresh

<!-- matching XAML: -->
<Button l:Uids.Uid="FooterRefreshButton" ...>
```

For `AutomationProperties.Name`:
```xml
<!-- resw entry: -->
FooterRefreshButton.[using:Microsoft.UI.Xaml.Automation]AutomationProperties.Name = Refresh

<!-- matching XAML: -->
<Button l:Uids.Uid="FooterRefreshButton" ...>
```

The same Uid on the button drives both attached properties simultaneously. No additional XAML attributes are needed on the button.

### Current Footer Button State (MainView.xaml lines 584-625)

All three footer buttons already have `l:Uids.Uid` set:

```xml
<Button l:Uids.Uid="FooterRefreshButton"
        Command="{x:Bind ViewModel.RefreshCommand}"
        Background="Transparent" BorderThickness="0"
        Padding="8" CornerRadius="6">

<Button l:Uids.Uid="FooterSettingsButton"
        Command="{x:Bind ViewModel.OpenSettingsCommand}"
        ...>

<Button l:Uids.Uid="FooterQuitButton"
        Command="{x:Bind ViewModel.ExitAppCommand}"
        ...>
```

### Current resw State (ALREADY COMPLETE)

Both `Strings/en-US/Resources.resw` and `Strings/de-DE/Resources.resw` already contain the required entries under the comment `<!-- MainView footer buttons -->`:

**en-US (lines 101-118):**
- `FooterRefreshButton.[using:Microsoft.UI.Xaml.Controls]ToolTipService.ToolTip` = `Refresh`
- `FooterRefreshButton.[using:Microsoft.UI.Xaml.Automation]AutomationProperties.Name` = `Refresh`
- `FooterSettingsButton.[using:Microsoft.UI.Xaml.Controls]ToolTipService.ToolTip` = `Settings`
- `FooterSettingsButton.[using:Microsoft.UI.Xaml.Automation]AutomationProperties.Name` = `Settings`
- `FooterQuitButton.[using:Microsoft.UI.Xaml.Controls]ToolTipService.ToolTip` = `Quit`
- `FooterQuitButton.[using:Microsoft.UI.Xaml.Automation]AutomationProperties.Name` = `Quit`

**de-DE (lines 101-118):**
- Same keys with values: `Aktualisieren`, `Aktualisieren`, `Einstellungen`, `Einstellungen`, `Beenden`, `Beenden`

### Anti-Patterns to Avoid

- **Hardcoding tooltip text in XAML:** `ToolTipService.ToolTip="Refresh"` bypasses localization — ACC-03 fails
- **Using a separate dummy element for Uid:** The button itself already has `l:Uids.Uid`; do not add wrapper elements
- **Adding `AutomationProperties.Name` inline in XAML:** Redundant if resw already drives it via Uid; causes double-declaration confusion

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Localized tooltip text | Custom binding + converter | WinUI3Localizer Uid property-path in resw | Already the project pattern; handles language switching |
| Accessibility name injection | Code-behind setting AutomationProperties | AutomationProperties.Name via resw Uid | Declarative, survives localization reload |

## Common Pitfalls

### Pitfall 1: Spec suggests a different Uid scheme
**What goes wrong:** The spec (lines 325-339) shows a different Uid naming: `FooterRefreshTooltip` (separate tooltip Uid) rather than combining with the button's existing `FooterRefreshButton` Uid.
**Why it happens:** The spec was written before checking actual codebase state.
**How to avoid:** The codebase already uses `FooterRefreshButton` as the Uid on the button elements, and the resw already contains entries under `FooterRefreshButton.*`. Use the existing Uid — do not add a second Uid or rename.
**Warning signs:** If you see `l:Uids.Uid="FooterRefreshTooltip"` appearing in XAML, that's the wrong approach.

### Pitfall 2: Missing namespace in resw property-path
**What goes wrong:** Writing `FooterRefreshButton.ToolTipService.ToolTip` without the `[using:Microsoft.UI.Xaml.Controls]` namespace prefix causes the localizer to silently ignore the entry.
**Why it happens:** Attached properties require the full namespace path.
**How to avoid:** Use the confirmed working pattern from existing entries in the same resw files.
**Warning signs:** Tooltip not appearing despite resw entry being present.

### Pitfall 3: Refresh button has a wrapper Grid
**What goes wrong:** The Refresh button is nested inside a `<Grid>` (to overlay the API error badge ellipse). The `Button` itself is the element with `l:Uids.Uid`, so this is not an issue — but a planner might incorrectly target the outer Grid.
**Why it happens:** The footer is not a flat StackPanel of Buttons; Refresh is `Grid > Button`.
**How to avoid:** Target the `Button` element (line 586), which already has `l:Uids.Uid="FooterRefreshButton"`.

## Code Examples

### Existing working pattern — ToolTipService via Uid (ExportButton, already shipping)

resw entry (en-US):
```xml
<data name="ExportButton.[using:Microsoft.UI.Xaml.Controls]ToolTipService.ToolTip" xml:space="preserve">
  <value>Export chart</value>
</data>
```

XAML:
```xml
<Button l:Uids.Uid="ExportButton" ...>
```

This is the identical pattern used for the footer buttons. No differences.

### Existing working pattern — AutomationProperties.Name via Uid (SessionComboBox, already shipping)

resw entry (en-US):
```xml
<data name="SessionComboBox.[using:Microsoft.UI.Xaml.Automation]AutomationProperties.Name" xml:space="preserve">
  <value>Select session</value>
</data>
```

XAML:
```xml
<ComboBox l:Uids.Uid="SessionComboBox" ...>
```

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| Hardcoded `ToolTipService.ToolTip="..."` in XAML | WinUI3Localizer Uid property-path in resw | Phase 6 (ExportButton established this pattern) | Runtime language switch works without XAML reload |

## Open Questions

1. **Is this phase actually already complete?**
   - What we know: XAML has correct `l:Uids.Uid` on all three footer buttons. Both resw files have all 6 required entries with correct values.
   - What's unclear: Whether the entries were added as part of a prior plan that ran but was never verified, or were pre-populated during project setup.
   - Recommendation: The plan should have a single verification task — build the project, hover over each button, confirm tooltip appears, and verify via Narrator/Accessibility Insights that `AutomationProperties.Name` is set. If all passes, the phase gate is met with zero code changes.

## Environment Availability

Step 2.6: SKIPPED (no external dependencies — purely XAML/resw changes with no new tools or services required).

## Validation Architecture

### Test Framework
| Property | Value |
|----------|-------|
| Framework | xUnit (existing) |
| Config file | CCInfoWindows.Tests project |
| Quick run command | `dotnet test CCInfoWindows/CCInfoWindows.Tests/CCInfoWindows.Tests.csproj` |
| Full suite command | `dotnet test CCInfoWindows/CCInfoWindows.Tests/CCInfoWindows.Tests.csproj` |

### Phase Requirements -> Test Map
| Req ID | Behavior | Test Type | Automated Command | Notes |
|--------|----------|-----------|-------------------|-------|
| ACC-01 | Tooltip appears on hover | manual-only | N/A | WinUI 3 tooltip rendering is not unit-testable; requires live UI |
| ACC-02 | Screen reader announces button name | manual-only | N/A | UIA/Narrator interaction requires live app; use Accessibility Insights for Windows |
| ACC-03 | Tooltip language matches app language | manual-only | N/A | Runtime language switch requires live UI |

### Sampling Rate
- **Per task commit:** N/A (no code changes expected)
- **Per wave merge:** `dotnet build CCInfoWindows/CCInfoWindows/CCInfoWindows.csproj` — confirm clean build
- **Phase gate:** Manual hover test on all three buttons in both en-US and de-DE; Accessibility Insights confirms AutomationProperties.Name

### Wave 0 Gaps
None — existing test infrastructure covers all unit-testable code; ACC-01/02/03 are inherently manual UI validations.

## Sources

### Primary (HIGH confidence)
- Direct code inspection: `CCInfoWindows/CCInfoWindows/Views/MainView.xaml` (lines 584-625) — footer button Uid values confirmed
- Direct code inspection: `CCInfoWindows/CCInfoWindows/Strings/en-US/Resources.resw` (lines 100-118) — all 6 entries present
- Direct code inspection: `CCInfoWindows/CCInfoWindows/Strings/de-DE/Resources.resw` (lines 100-118) — all 6 entries present
- Pattern cross-reference: ExportButton (line 172 en-US resw) — identical ToolTipService.ToolTip Uid pattern, already shipping

### Secondary (MEDIUM confidence)
- `spec-release-from-1.7.1-to-1.8.3.md` Phase 5 (lines 298-348) — spec requirements; note: spec proposes a different Uid naming scheme that does not match actual codebase (see Pitfall 1)

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH — code inspection of actual working files
- Architecture: HIGH — confirmed pattern from multiple existing working examples in the same codebase
- Pitfalls: HIGH — discovered by comparing spec naming vs actual codebase state

**Research date:** 2026-04-12
**Valid until:** Stable — WinUI 3 attached property / WinUI3Localizer pattern does not change between patch releases
