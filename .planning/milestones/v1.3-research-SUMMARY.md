# Project Research Summary

**Project:** ccInfoWin v1.2 - macOS v1.8.3 Feature Parity
**Domain:** WinUI 3 Desktop App - Claude Code usage monitoring
**Researched:** 2026-04-12
**Confidence:** HIGH

## Executive Summary

ccInfoWin v1.2 ports five improvements from macOS ccInfo v1.8.3 onto the existing WinUI 3 / .NET9 codebase. Core challenge: surgical precision over shared infrastructure. Phases 1 and 2 have a hard ordering dependency; Phases 3-5 are fully independent and low-risk.

Recommended approach: Phase 1 as an atomic commit removing the token-count heuristic and introducing ModelFamily enum and updated method signatures, then Phase 2 to wire the live setting through the data path. Phases 3, 4, 5 land in any order.

Main risks: Phase 1 (stale Opus entries in ContextLimits dictionary; both Utilization call sites must be updated atomically) and Phase 3 (missing UNC path validation causing NTLM hash leak). Phase 5 is essentially already done.
## Key Findings

### Recommended Stack

No stack changes. Entire milestone uses the existing dependency graph: C# 13 / .NET 9 BCL, CommunityToolkit.Mvvm source generators, WinUI3Localizer resw keys, and WeakReferenceMessenger. System.Text.Json handles new AppSettings property via C# default values.

**Core technologies:**
- C# 13 / .NET 9: Enum types, LINQ OrderBy, Directory.Exists - already in use throughout
- CommunityToolkit.Mvvm 8.4.0: [ObservableProperty], ValueChangedMessage<T>, WeakReferenceMessenger - identical patterns already implemented
- WinUI3Localizer 2.3.0: Attached-property key syntax in resw - proven by SessionComboBox.AutomationProperties.Name
- System.Text.Json (bundled): Auto-default for new SonnetContextSize - no migration needed
- Windows App SDK 1.8: ToolTipService.ToolTip, AutomationProperties.Name - supported via l:Uids.Uid

### Expected Features

All five features constitute the complete v1.2 milestone.

**Must have (table stakes):**
- 1M context window support for Opus - showing 200K for Opus is visibly wrong for any Max subscriber
- Stable subagent display order - reordering on every refresh is unusable
- Session list without ghost projects - sessions for deleted directories confuse users
- Tooltips on icon-only footer buttons - Windows design guideline requirement

**Should have (competitive):**
- Sonnet context size picker (200K / 1M) - model-family detection alone insufficient

**Defer (v2+):**
- Per-model arbitrary context override - no real use case
- Auto-detect Sonnet context from API - usage endpoint does not expose context window size
- Win2D chart canvas accessibility tree - requires custom IAccessibleEx
- Sparkle-style Updates settings tab - existing banner covers the use case
### Architecture Approach

All five phases integrate into the existing MVVM layer without new services or DI changes. Change set is bottom-up: ModelContextLimits (helper) first, propagating to ContextWindowData (models), then AppSettings and JsonlService (services), then ViewModels, then XAML views.

**Modified components:**
1. ModelContextLimits (Helpers) - ModelFamily enum, GetModelFamily(), GetMaxContextTokens(modelName, sonnetContextSize), simplified GetEffectiveMaxTokens(maxTokens), flat 20K ShouldWarnAutocompact()
2. JsonlService (Services) - ISettingsService injection, Directory.Exists filter, OrderBy(AgentId)
3. ContextWindowData + SubagentContextData (Models) - two Utilization call sites updated
4. AppSettings (Models) - SonnetContextSize int with 200K default
5. SettingsViewModel / MainViewModel (ViewModels) - picker state and messenger subscription
6. SettingsView.xaml / MainView.xaml (Views) - Sonnet ComboBox and footer tooltip/accessibility
7. Resources.resw both locales - context labels and tooltip keys

**New components:**
1. ModelFamily enum - Opus, Sonnet, Haiku, Unknown
2. SonnetContextChangedMessage - ValueChangedMessage<int>, same shape as RefreshIntervalChangedMessage

### Critical Pitfalls

1. **Stale ContextLimits dictionary with 200K Opus entries** - Update or delete in Phase 1. GetMaxContextTokens must return 1,000,000 for claude-opus-4-6.

2. **GetEffectiveMaxTokens signature change not propagated atomically** - Delete deprecated constants in same commit to force compiler errors. No stubs.

3. **Directory.Exists on unvalidated UNC path (NTLM hash leak)** - Cwd from JSONL is external data. Guard: Path.IsPathRooted(Cwd) AND not-UNC check before Directory.Exists.

4. **SettingsViewModel.Initialize() firing prematurely** - Assign backing field _selectedSonnetContextIndex (not generated property setter) inside Initialize().

5. **ToolTipService.ToolTip as XAML attribute overrides WinUI3Localizer** - Freezes tooltips in one language. Define only in resw. Zero XAML changes needed for Phase 5.
## Implications for Roadmap

### Phase 1: ModelContextLimits Core Rewrite
**Rationale:** Foundation for everything. Phase 2 cannot function without updated GetMaxContextTokens(modelName, sonnetContextSize). Must be atomic.
**Delivers:** Correct 1M context display for Opus. Architectural basis for Sonnet context config. Removal of fragile token-count heuristic.
**Addresses:** 1M context for Opus (table stakes), model-family-based detection (differentiator)
**Avoids:** Stale ContextLimits dictionary, GetEffectiveMaxTokens signature mismatch, deprecated constants surviving

### Phase 2: Sonnet Context Window Setting (End-to-End)
**Rationale:** Depends directly on Phase 1. Injects ISettingsService into JsonlService. Wires full data path from Settings UI to context bar.
**Delivers:** User-configurable Sonnet context (200K / 1M). Live refresh on setting change. Settings persistence.
**Uses:** CommunityToolkit.Mvvm WeakReferenceMessenger, [ObservableProperty], WinUI3Localizer resw keys
**Implements:** SonnetContextChangedMessage -> MainViewModel subscription -> UpdateSessionData
**Avoids:** Initialize() premature fire, ISettingsService disk read under _sessionsLock

### Phase 3: Session Filtering (Directory Existence)
**Rationale:** Independent. Single filter clause in RebuildSessionsList(). UNC path validation mandatory.
**Delivers:** Clean session dropdown. Auto-fallback to next valid session on directory deletion.
**Avoids:** Directory.Exists on unvalidated UNC path (NTLM hash leak), selected-session removal without recovery

### Phase 4: Subagent Sorting Stabilization
**Rationale:** Trivial one-liner, fully independent. Can batch with Phase 3.
**Delivers:** Stable alphabetical subagent order across all FSW-triggered refreshes.
**Avoids:** Sort at call site instead of inside BuildSubagentContext

### Phase 5: Footer Tooltip and Accessibility
**Rationale:** Purely additive, zero regression risk. resw entries confirmed present.
**Delivers:** Windows design guideline compliance, screen reader support, runtime language switching for tooltips.
**Avoids:** ToolTipService.ToolTip as XAML attribute, missing de-DE resw entries

### Phase Ordering Rationale

- Phase 1 must be first: compile-time dependency for Phase 2
- Phase 2 must follow Phase 1: passes live sonnetContextSize to updated signature
- Phases 3, 4, 5 fully independent after Phase 1
- Natural batching: Phase 3 + Phase 4 as single JsonlService polish commit
- Phase 5 last: purely additive, zero regression risk

### Research Flags

All phases use standard patterns - no /gsd:research-phase deep dive required:
- **Phase 1:** Pure C# refactor of static helper. Patterns documented by direct codebase inspection.
- **Phase 2:** Identical to existing language picker and refresh interval patterns.
- **Phase 3:** BCL Directory.Exists with two-condition guard. No novel patterns.
- **Phase 4:** Single LINQ OrderBy inside existing method. Trivial.
- **Phase 5:** resw additions only. WinUI3Localizer syntax proven by existing SessionComboBox entry.

## Confidence Assessment

| Area | Confidence | Notes |
|------|------------|-------|
| Stack | HIGH | Direct codebase inspection. Zero uncertainty about available APIs. |
| Features | HIGH | Authoritative spec document. Reference implementation known. |
| Architecture | HIGH | Component boundaries from direct code reading. Data flow verified. |
| Pitfalls | HIGH | Direct code analysis. Specific line numbers referenced. |

**Overall confidence:** HIGH

### Gaps to Address

- **ISettingsService injection into JsonlService:** Verify before Phase 2. If not present, DI registration in App.xaml.cs (line 141) needs updating.
- **ISettingsService.LoadSettings() caching:** Verify in-memory cache vs disk I/O. If disk I/O, read into local variable before _sessionsLock.
- **de-DE resw completeness for Phase 5:** en-US entries confirmed. de-DE must be verified before marking done.
- **ModelContextLimits dictionary decision:** Decide at Phase 1 start: update all Opus entries to 1,000,000 OR delete the dictionary entirely.

## Sources

### Primary (HIGH confidence)
- spec-release-from-1.7.1-to-1.8.3.md - Authoritative implementation spec
- CCInfoWindows/CCInfoWindows/Helpers/ModelContextLimits.cs
- CCInfoWindows/CCInfoWindows/Models/ContextWindowData.cs
- CCInfoWindows/CCInfoWindows/Services/JsonlService.cs (lines 640-779)
- CCInfoWindows/CCInfoWindows/Models/AppSettings.cs
- CCInfoWindows/CCInfoWindows/ViewModels/SettingsViewModel.cs (lines 85-100)
- CCInfoWindows/CCInfoWindows/Views/MainView.xaml (lines 579-625)
- CCInfoWindows/CCInfoWindows/Strings/en-US/Resources.resw (lines 100-118)
- CCInfoWindows/CCInfoWindows/Strings/de-DE/Resources.resw
- .planning/PROJECT.md

### Secondary (MEDIUM confidence)
- .planning/research/STACK.md, FEATURES.md, ARCHITECTURE.md, PITFALLS.md (synthesized above)

---
*Research completed: 2026-04-12*
*Ready for roadmap: yes*
