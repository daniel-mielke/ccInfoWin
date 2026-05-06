# Phase 23: Localization Gaps - Context

**Gathered:** 2026-05-06
**Status:** Ready for planning
**Mode:** Auto-generated (autonomous workflow — Roadmap goal + REQUIREMENTS.md are clear enough; downstream agents discover hardcoded-string sites)

<domain>
## Phase Boundary

Six new resource keys (DE + EN) cover previously hardcoded XAML strings (`"Loading"`, `"No data"`, `"Not signed in"`) and the inactive-session tooltip / login-reload button strings; runtime language switch works for all six without app restart.

This phase is purely localization plumbing — no new visible UI components, no behavior changes. The visible delta is exclusively in `Strings/{de-DE,en-US}/Resources.resw` and the XAML files that consume the new keys via `x:Uid` (or `l:Uids.Uid`) bindings.

</domain>

<decisions>
## Implementation Decisions

### Authoritative Resource Keys (D-01 — LOCKED via REQUIREMENTS.md L10N-01)

The phase delivers exactly these six keys, no more, no less:

| Key | EN value | DE value | Used by |
|-----|----------|----------|---------|
| `NotSignedIn.Text` | `Not signed in` | `Nicht angemeldet` | XAML view (TBD by Researcher) — replaces hardcoded `"Not signed in"` |
| `NoData.Text` | `No data` | `Keine Daten` | XAML view (TBD by Researcher) — replaces hardcoded `"No data"` |
| `Loading.Text` | `Loading` | `Wird geladen` | XAML view (TBD by Researcher) — replaces hardcoded `"Loading"` |
| `InactiveSessionTooltip` | `Inactive for > {0}min` | `Inaktiv seit > {0}min` | `MainViewModel.SessionDisplayItem.ComputeTooltipText` (Phase 22) — referenced via Localizer |
| `LoginReloadButton.[using:Microsoft.UI.Xaml.Controls]ToolTipService.ToolTip` | `Reload page` | `Seite neu laden` | `LoginView.xaml` reload button (Phase 20) — bound via `l:Uids.Uid="LoginReloadButton"` |
| `LoginReloadButton.[using:Microsoft.UI.Xaml.Automation]AutomationProperties.Name` | `Reload login page` | `Login-Seite neu laden` | `LoginView.xaml` reload button (Phase 20) — bound via `l:Uids.Uid="LoginReloadButton"` |

### Pre-Existing Keys from Earlier Phases (D-02)

The two `LoginReloadButton.*` keys were authored by **Plan 20-01** as part of the Phase 20 Auth Flow Stability self-containment (per RESEARCH Open Question #1). Phase 23 must:
1. Verify both keys exist in both resw files with the expected values.
2. NOT re-author them — duplicate `<data>` entries in resw cause runtime resource lookup failures.
3. Treat the keys as already-satisfying L10N-01 (count them as 2 of the 6).

The remaining 4 keys (`NotSignedIn.Text`, `NoData.Text`, `Loading.Text`, `InactiveSessionTooltip`) are NEW in this phase.

### Hardcoded String Discovery (D-03 — Researcher's task)

Researcher must locate every XAML file with hardcoded `"Loading"`, `"No data"`, `"Not signed in"` strings. Suspected sites (not exhaustive — Researcher confirms):
- `MainView.xaml` — likely candidate for "Not signed in" (initial state) and possibly "Loading" / "No data"
- `SettingsView.xaml` — possibly "No data" in some sub-panel
- Other Views — Researcher greps the entire `Views/**/*.xaml`

Acceptance grep at the end (per L10N-02): `grep -rE '"(Loading|No data|Not signed in)"' CCInfoWindows/CCInfoWindows/Views/**/*.xaml` returns 0 matches.

### Migration Strategy (D-04)

For each hardcoded string site, replace the `Text="..."` literal with `x:Uid="<KeyPrefix>"` (without the `.Text` suffix). The WinUI 3 resource framework auto-binds `<KeyPrefix>.Text` to the `Text` property of the element. Example:

```xaml
<!-- Before -->
<TextBlock Text="Not signed in" />

<!-- After -->
<TextBlock x:Uid="NotSignedIn" />
```

Note: For tooltip/automation properties, the `[using:...]` namespace prefix is required in the resw key (see existing `LoginReloadButton.*` and `FooterRefreshButton.*` patterns).

### `InactiveSessionTooltip` — Format String (D-05)

The key value uses positional placeholder `{0}` for the threshold integer. The runtime formatter substitutes the current `SessionTimeoutMinutes` value. Phase 22 already calls `Localizer.Get().GetLocalizedString("InactiveSessionTooltip")` and applies `string.Format`. Phase 23 ships the localized format string; Phase 22's defensive try/catch (per RESEARCH [A1]) gracefully handles the pre-Phase-23 missing-key scenario by falling back to `"Inactive for > {0}min"` inline literal.

### Runtime Language Switch (D-06 — non-blocking smoke)

The existing `LanguageRefreshService` (or equivalent — Researcher confirms) already handles dynamic resource re-resolution on language change. Phase 23 does NOT introduce new switch infrastructure — it just relies on the existing mechanism. L10N-03 verification is a manual smoke test: change language in Settings → confirm all 6 strings update without restart.

### `\n` in `InactiveSessionTooltip` (D-07)

Phase 22 composes the tooltip as `path + "\n" + localizedThreshold`. The `InactiveSessionTooltip` resw VALUE itself does NOT include `\n` — it's just the second-line text (`"Inactive for > {0}min"`). Phase 22 owns the multi-line composition.

### Format-String Test Coverage (D-08)

Add 2 xUnit tests verifying that loading the new resw keys via the existing `Localizer` returns non-empty strings for both `en-US` and `de-DE`. This validates that the resource files are well-formed XML and discoverable by the Localizer pipeline. No need to test every key — sample 1-2 per locale.

### Claude's Discretion

- Exact wording of DE translations (current proposals use natural German per project memory note "Communication with user: German"). Translator/native-speaker review optional, not required for Phase 23 ship.
- Insertion order of new `<data>` entries in resw files — group with related keys for readability (e.g., `LoginReloadButton.*` keys near `FooterRefreshButton.*`, `NotSignedIn.Text` near other top-level state strings).
- Whether to use `x:Uid` (built-in WinUI 3) or `l:Uids.Uid` (WinUI3Localizer extension) — follow existing project convention. Codebase uses both; Researcher determines which is correct for each site.

</decisions>

<canonical_refs>
## Canonical References

**Downstream agents MUST read these before planning or implementing.**

### Spec & Roadmap
- `.planning/milestones/v1.4-REQUIREMENTS.md` — L10N-01..L10N-03 (canonical scope)
- `.planning/milestones/v1.4-ROADMAP.md` — Phase 23 section (goal + success criteria)

### Existing localization infrastructure
- `CCInfoWindows/CCInfoWindows/Strings/en-US/Resources.resw` — existing 130+ keys; pattern reference
- `CCInfoWindows/CCInfoWindows/Strings/de-DE/Resources.resw` — existing 130+ keys; pattern reference
- WinUI3Localizer NuGet package (existing dependency) — runtime resource resolution + language switch

### Phase-20 / Phase-22 dependencies
- `.planning/phases/20-auth-flow-stability/20-01-SUMMARY.md` — confirms `LoginReloadButton.*` keys already authored
- `.planning/phases/22-ui-polish/22-02-SUMMARY.md` — confirms `InactiveSessionTooltip` reference site (`SessionDisplayItem.ComputeTooltipText`)

</canonical_refs>

<specifics>
## Specific Ideas

- **Insertion grouping in resw:** keep alphabetical or grouped-by-feature, mirror existing convention.
- **Manual smoke for L10N-03:** Settings → switch language → confirm Login button tooltip + Settings panels update live.
- **Defensive fallback already in place:** Phase 22's `ComputeTooltipText` has try/catch around localizer lookup. If Phase 23 ships AFTER Phase 22 in deployment order, Phase 22 users get an inline-literal fallback for the brief window. No coordination needed.

</specifics>

<deferred>
## Deferred Ideas

- **Pluralization** for `InactiveSessionTooltip` (e.g., "1 minute" vs "5 minutes") — current scope uses simple format string `> {0}min`. Pluralization is a v1.5+ concern.
- **`LastFetchMinutesAgo` / `LastFetchNever` keys** flagged by Phase 22 RESEARCH [A2] — Phase 22 ships with English-only inline literals. If a future phase wants to localize that specific timestamp, NEW keys are needed (not part of the 6 in L10N-01).
- **Locale-aware date/number formatting** beyond simple substitution — out of scope.
- **Cross-language resource validation** (e.g., test that all keys exist in both locales) — beyond the 2 sample-per-locale tests in D-08; could become a CI check in v1.5+.

</deferred>

---

*Phase: 23-localization-gaps*
*Context gathered: 2026-05-06 via autonomous workflow (clear scope from REQUIREMENTS + ROADMAP — Researcher discovers hardcoded sites)*
