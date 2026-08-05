# Phase 15: Footer Tooltip & Accessibility - Context

**Gathered:** 2026-04-12
**Status:** Ready for planning
**Mode:** Auto-generated (trivial UI phase — spec-driven)

<domain>
## Phase Boundary

Add localized tooltips and AutomationProperties.Name to all three footer buttons (Refresh, Settings, Quit). No layout changes, no new controls — just XAML attributes and localization strings.

</domain>

<decisions>
## Implementation Decisions

### Claude's Discretion
All implementation choices are at Claude's discretion — trivial phase with clear spec. Use ROADMAP phase goal, success criteria, and codebase conventions to guide decisions.

Key spec reference: `spec-release-from-1.7.1-to-1.8.3.md` Phase 5 (lines 298-348)

Tooltip values:
| Button | de-DE | en-US | AutomationProperties.Name |
|--------|-------|-------|--------------------------|
| Refresh | Aktualisieren | Refresh | Refresh |
| Settings | Einstellungen | Settings | Settings |
| Quit | Beenden | Quit | Quit |

Use `l:Uids.Uid` for tooltips (runtime language switch pattern) and `AutomationProperties.Name` directly on the button.

</decisions>

<code_context>
## Existing Code Insights

Codebase context will be gathered during plan-phase research.

</code_context>

<specifics>
## Specific Ideas

- Footer buttons are in `MainView.xaml` in a horizontal StackPanel
- Use `ToolTipService.ToolTip` with `l:Uids.Uid` for localized text
- Check existing localization pattern for correct Uid property path syntax

</specifics>

<deferred>
## Deferred Ideas

None — discuss phase skipped.

</deferred>
