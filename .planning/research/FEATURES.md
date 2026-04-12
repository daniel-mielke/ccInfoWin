# Feature Research

**Domain:** Desktop LLM usage monitoring — macOS v1.8.3 feature parity delta for ccInfoWin v1.2
**Researched:** 2026-04-12
**Confidence:** HIGH (spec fully defined, reference implementation known, codebase inspected)

---

## Scope

This document covers ONLY the five new features being added in v1.2. The existing feature set
(authentication, charts, weekly limits, context window, token stats, cost, export, settings,
auto-update, localization) is already implemented and NOT re-analyzed here.

---

## Feature Landscape

### Table Stakes (Users Expect These)

| Feature | Why Expected | Complexity | Existing Code Impact |
|---------|--------------|------------|----------------------|
| Accurate context limit for Opus (1M) | Opus has had 1M context since launch. Displaying 200K for Opus is visibly wrong for any Max subscriber. Trust breaks immediately. | MEDIUM | `ModelContextLimits.cs` dict + `GetMaxContextTokens()` + `GetEffectiveMaxTokens()` + `ShouldWarnAutocompact()` must all change. Propagates to `ContextWindowData.Utilization` and `SubagentContextData.Utilization`. |
| Stable subagent order | If the subagent list reorders on every refresh, users can't track individual agents visually. Any monitoring tool should have stable display order. | LOW | `BuildSubagentContext()` in `JsonlService.cs` — single `.OrderBy()` addition. No model or view changes. |
| Session list without ghost projects | Showing sessions for deleted directories is confusing (user clicks session, gets no data). Users expect the dropdown to reflect reality. | LOW | `RebuildSessionsList()` in `JsonlService.cs` — single `.Where(Directory.Exists)` filter addition. Edge: selected session cleared if its directory is deleted. |
| Tooltips on icon-only buttons | Icon-only buttons without tooltips fail basic discoverability. Any desktop app with icon-only controls must have hover tooltips — this is a Windows design guideline requirement. | LOW | `MainView.xaml` — add `ToolTipService.ToolTip` and `AutomationProperties.Name` to three existing footer buttons. Localization strings for both locales needed. |

### Differentiators (Competitive Advantage)

| Feature | Value Proposition | Complexity | Notes |
|---------|-------------------|------------|-------|
| Sonnet context size picker (200K / 1M) | Sonnet on Max plan supports 1M context. Letting users configure which size they have makes the context bar accurate for Max subscribers — no other Windows tool offers this. | MEDIUM | New setting in `AppSettings`, new ComboBox row in `SettingsView.xaml`, `SettingsViewModel` property, messenger trigger for live refresh. Depends on Phase 1 model-based detection being in place first. |
| Model-family-based context detection | Detecting context size from model family (Opus/Sonnet/Haiku) rather than a token-count heuristic is more reliable and future-proof. Prevents false "1M context" display when a Sonnet session grows past 180K tokens. | MEDIUM | Core logic change in `ModelContextLimits.cs`. Introduces `ModelFamily` enum. Unified 33K flat autocompact buffer replaces the two-tier 33K/165K system. Warning threshold changes from percentage-based to flat 20K-remaining. |

### Anti-Features (Explicitly Out of Scope)

| Feature | Why Requested | Why Problematic | Alternative |
|---------|---------------|-----------------|-------------|
| Per-model context size override (arbitrary number input) | Power users want full control | Adds validation complexity, UI noise, edge cases (e.g. user sets 500K which doesn't exist). Claude models only come in 200K or 1M. Two-option ComboBox is sufficient and correct. | 200K/1M ComboBox for Sonnet — covers 100% of real-world cases |
| Auto-detect Sonnet context from API response | Seems elegant — let the API tell us | The API does not expose context window size in the usage endpoint. Would require separate undocumented API call; brittle. | Manual setting with sensible 200K default |
| Filter sessions by date / age threshold | Related to ghost-project filtering | Different problem domain — old-but-valid sessions should remain visible. Age filtering conflates "stale" with "orphaned". | Directory-existence check only (Phase 3) |
| Accessibility tree for chart canvas | Full screen reader support for Win2D canvas | Win2D canvas has no accessibility element tree — implementing this would require custom IAccessibleEx implementation, massively complex. | `AutomationProperties.Name` on all interactive controls (Phase 5 covers this for footer buttons) |
| Sparkle-style in-app update UI (Settings tab) | macOS v1.8.0 added a dedicated Updates settings tab | The macOS version is Sparkle-specific (XPC, EdDSA appcast). Our GitHub Releases poller + InfoBar banner already covers the use case without a dedicated tab. | Existing `UpdateService` + banner (already shipped) |

---

## Feature Dependencies

```
Phase 1: Model-based context detection (ModelContextLimits.cs refactor)
    └──required by──> Phase 2: Sonnet context window setting
                          (GetMaxContextTokens must accept sonnet size parameter
                           before Settings UI can influence context display)

Phase 3: Session filtering          ← independent, no Phase 1/2 dependency
Phase 4: Subagent sorting           ← independent, no Phase 1/2 dependency
Phase 5: Footer tooltips            ← independent, no Phase 1/2 dependency
```

### Dependency Notes

- **Phase 1 blocks Phase 2:** The `GetMaxContextTokens()` signature must accept a `sonnetContextSize` parameter (or read from injected settings) before Phase 2 can wire the UI control to live context recalculation. If Phase 2 is implemented first, Sonnet context changes would have no effect.
- **Phases 3, 4, 5 are fully independent:** Can be implemented in any order or in parallel after Phase 1+2.
- **`ContextWindowData.Utilization` and `SubagentContextData.Utilization`** both call `GetEffectiveMaxTokens()` directly — Phase 1's signature simplification (`(long maxTokens)` instead of `(long currentTokens, long maxTokens)`) propagates to both records automatically.
- **`ShouldWarnAutocompact()` behavioral change** (percentage → flat 20K remaining) affects `GetContextWindow()` in `JsonlService.cs` — no view changes, the existing `ShouldWarnAutocompact` bool property on `ContextWindowData` propagates through unchanged.

---

## Implementation Complexity Assessment

| Phase | Effort | Files Changed | Risk |
|-------|--------|---------------|------|
| Phase 1: 1M context window + model detection | MEDIUM | `ModelContextLimits.cs`, `ContextWindowData.cs`, `JsonlService.cs`, `MainViewModel.cs` (verify) | LOW — pure logic, no new infrastructure, comprehensive existing test surface for `JsonlServiceTests` |
| Phase 2: Sonnet context setting | MEDIUM | `AppSettings.cs`, `SettingsView.xaml`, `SettingsViewModel.cs`, `JsonlService.cs`, 2x `.resw` files | LOW — follows established pattern (Language picker is identical pattern already implemented) |
| Phase 3: Session filtering | LOW | `JsonlService.cs` — one filter clause in `RebuildSessionsList()` | LOW — `Directory.Exists()` is a standard .NET API. Edge case: selected session invalidation handled by existing "no active session" fallback in `MainViewModel` |
| Phase 4: Subagent sorting | TRIVIAL | `JsonlService.cs` — one `.OrderBy()` call in `BuildSubagentContext()` | NEGLIGIBLE |
| Phase 5: Footer tooltips | LOW | `MainView.xaml`, 2x `.resw` files | LOW — `ToolTipService.ToolTip` via `l:Uids.Uid` needs verification: WinUI3Localizer property path syntax for attached properties. Existing localization pattern (`l:Uids.Uid`) may require the property segment `[using:Microsoft.UI.Xaml.Controls]ToolTipService.ToolTip` in the Uid name — verify against existing `l:Uids.Uid` usage before assuming standard WinUI Uid pattern works here. |

---

## Pitfall Flags for Phase Implementation

| Phase | Key Pitfall |
|-------|-------------|
| Phase 1 | `GetMaxContextTokens()` currently has no parameter for `sonnetContextSize` — the signature change must be designed so `JsonlService` can supply the configured value without creating a circular dependency (service reads settings, settings are loaded by `SettingsService`). Inject `ISettingsService` into `JsonlService` constructor, or pass the value as a method parameter. |
| Phase 1 | The existing `ExtendedContextDetectionThreshold = 180_000` heuristic must be fully removed — not just unused. Leaving it in code creates future confusion about which path is active. |
| Phase 2 | `settings.json` deserialization: `System.Text.Json` will use the default value (200,000) if `sonnetContextSize` key is missing. No migration code needed — this is the correct zero-effort migration path. |
| Phase 3 | The `Cwd` field can be an empty string for sessions parsed before v1.0 (pre-`Cwd` JSONL format). The filter must guard: `!string.IsNullOrEmpty(s.Cwd) && Directory.Exists(s.Cwd)`. Sessions with empty Cwd are effectively unvalidatable orphans and should also be excluded. |
| Phase 5 | `AutomationProperties.Name` should be the English string (screen readers expect non-localized control names in most assistive tech conventions). The tooltip can be localized; the automation name should stay English and be hardcoded, not run through the localizer. |

---

## MVP Definition for This Milestone

### Ship Together (v1.2)

All five phases constitute the complete milestone. There is no partial-ship that makes sense:

- [ ] Phase 1 (1M context + model detection) — correctness fix, not optional
- [ ] Phase 2 (Sonnet picker) — requires Phase 1, low additional effort
- [ ] Phase 3 (session filtering) — independent, small scope
- [ ] Phase 4 (subagent sorting) — independent, trivial
- [ ] Phase 5 (footer tooltips) — independent, Windows design guideline compliance

### Defer to Future Milestones

- Accessibility tree for Win2D chart canvas — complex, niche
- Updates Settings tab — macOS-specific pattern, existing banner sufficient
- Per-model context size override beyond Sonnet — no real-world use case exists

---

## Sources

- `spec-release-from-1.7.1-to-1.8.3.md` — Primary specification document (HIGH confidence)
- `.planning/PROJECT.md` — Project context and constraints (HIGH confidence)
- `CCInfoWindows/CCInfoWindows/Helpers/ModelContextLimits.cs` — Current implementation (HIGH confidence)
- `CCInfoWindows/CCInfoWindows/Models/AppSettings.cs` — Current settings model (HIGH confidence)
- `CCInfoWindows/CCInfoWindows/Models/ContextWindowData.cs` — Utilization calculation chain (HIGH confidence)
- `CCInfoWindows/CCInfoWindows/Services/JsonlService.cs` — `BuildSubagentContext()`, `RebuildSessionsList()` (HIGH confidence)
- `CCInfoWindows/CCInfoWindows/Views/MainView.xaml` — Footer button structure (HIGH confidence)
- `CCInfoWindows/CCInfoWindows/Views/SettingsView.xaml` — Existing ComboBox pattern for Language picker (HIGH confidence)

---
*Feature research for: ccInfoWin v1.2 macOS v1.8.3 parity delta*
*Researched: 2026-04-12*
